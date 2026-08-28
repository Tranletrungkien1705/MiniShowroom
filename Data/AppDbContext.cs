using Microsoft.EntityFrameworkCore;
using MiniShowroom.Models;

namespace MiniShowroom.Data;

public class AppDbContext : DbContext
{
    private readonly Guid _orgId;
    public AppDbContext(DbContextOptions<AppDbContext> options, ITenantContext tenant) : base(options) => _orgId = tenant.OrgId;

    public DbSet<Org> Orgs => Set<Org>();
    public DbSet<VehicleModel> Models => Set<VehicleModel>();
    public DbSet<Lead> Leads => Set<Lead>();
    public DbSet<TestDrive> TestDrives => Set<TestDrive>();
    public DbSet<Deal> Deals => Set<Deal>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        if (Database.IsNpgsql()) b.HasDefaultSchema("minishowroom");
        b.Entity<Org>().HasIndex(x => x.ApiKey).IsUnique();
        b.Entity<VehicleModel>(e =>
        {
            e.HasIndex(x => new { x.OrgId, x.Code }).IsUnique();
            e.Property(x => x.ListPrice).HasPrecision(18, 2);
            e.HasQueryFilter(x => x.OrgId == _orgId);
        });
        b.Entity<Lead>(e =>
        {
            e.HasIndex(x => new { x.OrgId, x.Code }).IsUnique();
            e.HasOne(x => x.Model).WithMany().HasForeignKey(x => x.ModelId);
            e.HasQueryFilter(x => x.OrgId == _orgId);
        });
        b.Entity<TestDrive>(e =>
        {
            e.HasOne(x => x.Lead).WithMany(x => x.TestDrives).HasForeignKey(x => x.LeadId);
            e.HasOne(x => x.Model).WithMany().HasForeignKey(x => x.ModelId).OnDelete(DeleteBehavior.Restrict);
            e.HasQueryFilter(x => x.OrgId == _orgId);
        });
        b.Entity<Deal>(e =>
        {
            e.HasIndex(x => new { x.OrgId, x.Code }).IsUnique();
            foreach (var p in new[] { nameof(Deal.Price), nameof(Deal.Discount), nameof(Deal.DepositAmount),
                nameof(Deal.VatAmount), nameof(Deal.RegistrationFee), nameof(Deal.PlateFee),
                nameof(Deal.InsuranceAmount), nameof(Deal.AccessoriesAmount), nameof(Deal.LoanAmount) })
                e.Property(p).HasPrecision(18, 2);
            e.Ignore(x => x.FinalPrice); e.Ignore(x => x.Remaining); e.Ignore(x => x.TotalPayable);
            e.HasOne(x => x.Lead).WithMany(x => x.Deals).HasForeignKey(x => x.LeadId);
            e.HasOne(x => x.Model).WithMany().HasForeignKey(x => x.ModelId).OnDelete(DeleteBehavior.Restrict);
            e.HasQueryFilter(x => x.OrgId == _orgId);
        });
    }

    public override int SaveChanges() { StampOrg(); return base.SaveChanges(); }
    public override Task<int> SaveChangesAsync(CancellationToken ct = default) { StampOrg(); return base.SaveChangesAsync(ct); }
    private void StampOrg()
    {
        foreach (var e in ChangeTracker.Entries<IOrgOwned>())
            if (e.State == EntityState.Added && e.Entity.OrgId == Guid.Empty) e.Entity.OrgId = _orgId;
    }
}
