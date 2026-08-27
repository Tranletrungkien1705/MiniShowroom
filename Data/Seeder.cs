using Microsoft.EntityFrameworkCore;
using MiniShowroom.Models;

namespace MiniShowroom.Data;

public static class Seeder
{
    public static async Task SeedAsync(AppDbContext db)
    {
        await db.Database.EnsureCreatedAsync();
        await MigratePostgresAsync(db);

        if (!await db.Orgs.AnyAsync(o => o.Id == TenantContext.DefaultOrgId))
        {
            db.Orgs.Add(new Org { Id = TenantContext.DefaultOrgId, Name = "Demo Showroom", ApiKey = TenantContext.DefaultApiKey });
            await db.SaveChangesAsync();
        }
        if (!await db.Models.AnyAsync())
        {
            db.Models.AddRange(
                new VehicleModel { Code = "ACC", Name = "Hyundai Accent", Variant = "1.4 AT Đặc biệt", ListPrice = 567_000_000, Color = "Trắng" },
                new VehicleModel { Code = "TUC", Name = "Hyundai Tucson", Variant = "2.0 Xăng đặc biệt", ListPrice = 940_000_000, Color = "Đen" },
                new VehicleModel { Code = "SANTA", Name = "Hyundai Santa Fe", Variant = "2.5 Xăng cao cấp", ListPrice = 1_365_000_000, Color = "Bạc" },
                new VehicleModel { Code = "CRETA", Name = "Hyundai Creta", Variant = "1.5 Đặc biệt", ListPrice = 640_000_000, Color = "Đỏ" });
            await db.SaveChangesAsync();
        }
        if (!await db.Leads.AnyAsync())
        {
            var models = await db.Models.ToListAsync();
            int MId(string c) => models.First(m => m.Code == c).Id;
            int n = 0;
            Lead L(string name, string phone, LeadSource src, string modelCode, LeadStage stage, string sale)
            {
                n++;
                return new Lead { Code = $"LD{DateTime.Now:yyMM}{n:D4}", Name = name, Phone = phone, Source = src,
                    ModelId = MId(modelCode), Stage = stage, SalesPerson = sale, CreatedAt = DateTime.Now.AddDays(-n) };
            }
            db.Leads.AddRange(
                L("Nguyễn Văn An", "0901111111", LeadSource.Facebook, "ACC", LeadStage.TestDriven, "Sale Hoa"),
                L("Trần Thị Bình", "0902222222", LeadSource.Hotline, "TUC", LeadStage.Quoted, "Sale Nam"),
                L("Lê Hoàng Cường", "0903333333", LeadSource.WalkIn, "SANTA", LeadStage.Deposited, "Sale Hoa"),
                L("Phạm Thu Dung", "0904444444", LeadSource.Website, "CRETA", LeadStage.New, "Sale Nam"),
                L("Vũ Minh Đức", "0905555555", LeadSource.Referral, "ACC", LeadStage.Contacted, "Sale Hoa"));
            await db.SaveChangesAsync();
        }
    }

    private static async Task MigratePostgresAsync(AppDbContext db)
    {
        if (!db.Database.IsNpgsql()) return;
        var def = TenantContext.DefaultOrgId;
        var tables = new[] { "Models", "Leads", "TestDrives", "Deals" };
        var sql = new List<string>
        {
            "CREATE TABLE IF NOT EXISTS minishowroom.\"Orgs\" (\"Id\" uuid PRIMARY KEY, \"Name\" text NOT NULL DEFAULT '', \"ApiKey\" text NOT NULL DEFAULT '', \"CreatedAt\" timestamp NOT NULL DEFAULT now())",
            "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_Orgs_ApiKey\" ON minishowroom.\"Orgs\" (\"ApiKey\")",
        };
        foreach (var t in tables) sql.Add($"ALTER TABLE minishowroom.\"{t}\" ADD COLUMN IF NOT EXISTS \"OrgId\" uuid NOT NULL DEFAULT '{def}'");
        foreach (var s in sql) try { await db.Database.ExecuteSqlRawAsync(s); } catch { }
    }
}
