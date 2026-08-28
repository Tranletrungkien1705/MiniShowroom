using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MiniShowroom.Data;
using MiniShowroom.Models;
using MiniShowroom.Services;
using Xunit;

namespace MiniShowroom.Tests;

/// <summary>
/// Test nghiệp vụ showroom trên SQLite in-memory: phễu bán hàng (forward-only), state machine thương vụ,
/// guard giao xe cần VIN, và các bước tự tiến giai đoạn lead.
/// </summary>
public class ShowroomServiceTests
{
    // Mỗi test 1 SQLite in-memory riêng (giữ connection mở để DB tồn tại suốt test).
    private static (AppDbContext db, IShowroomService svc, SqliteConnection conn) NewSvc()
    {
        var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        var opt = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(conn).Options;
        var tenant = new TenantContext { OrgId = TenantContext.DefaultOrgId };
        var db = new AppDbContext(opt, tenant);
        db.Database.EnsureCreated();
        return (db, new ShowroomService(db), conn);
    }

    private static async Task<(int leadId, int modelId)> SeedLeadModel(AppDbContext db, IShowroomService svc)
    {
        var mid = await svc.CreateModelAsync(new VehicleModel { Name = "Tucson", Code = "TUC", ListPrice = 940_000_000 });
        var lid = await svc.CreateLeadAsync(new Lead { Name = "Khách A", Phone = "0900000000", ModelId = mid });
        return (lid, mid);
    }

    [Fact]
    public async Task CreateLead_StartsAtNew_WithGeneratedCode()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var id = await svc.CreateLeadAsync(new Lead { Name = "A", Phone = "090" });
            var l = await svc.GetLeadAsync(id);
            Assert.NotNull(l);
            Assert.Equal(LeadStage.New, l!.Stage);
            Assert.StartsWith("LD", l.Code);
        }
    }

    [Fact]
    public async Task BookTestDrive_BumpsLeadToTestDriven()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var (lid, mid) = await SeedLeadModel(db, svc);
            await svc.BookTestDriveAsync(new TestDrive { LeadId = lid, ModelId = mid });
            var l = await svc.GetLeadAsync(lid);
            Assert.Equal(LeadStage.TestDriven, l!.Stage);
        }
    }

    [Fact]
    public async Task CreateDeal_BumpsLeadToQuoted()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var (lid, mid) = await SeedLeadModel(db, svc);
            await svc.CreateDealAsync(new Deal { LeadId = lid, ModelId = mid, Price = 940_000_000 });
            var l = await svc.GetLeadAsync(lid);
            Assert.Equal(LeadStage.Quoted, l!.Stage);
        }
    }

    [Fact]
    public async Task Bump_IsForwardOnly_DoesNotRegress()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var (lid, mid) = await SeedLeadModel(db, svc);
            await svc.AdvanceAsync(lid, LeadStage.Deposited);         // đẩy lên Deposited
            await svc.BookTestDriveAsync(new TestDrive { LeadId = lid, ModelId = mid }); // Bump(TestDriven) < Deposited
            var l = await svc.GetLeadAsync(lid);
            Assert.Equal(LeadStage.Deposited, l!.Stage);              // KHÔNG lùi về TestDriven
        }
    }

    [Fact]
    public async Task DealFlow_Quoted_To_Deposited_BumpsLead()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var (lid, mid) = await SeedLeadModel(db, svc);
            var did = await svc.CreateDealAsync(new Deal { LeadId = lid, ModelId = mid, Price = 940_000_000 });
            var (ok, _) = await svc.DealActionAsync(did, DealStatus.Deposited);
            Assert.True(ok);
            Assert.Equal(LeadStage.Deposited, (await svc.GetLeadAsync(lid))!.Stage);
        }
    }

    [Fact]
    public async Task DealFlow_CannotDeliver_FromQuoted()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var (lid, mid) = await SeedLeadModel(db, svc);
            var did = await svc.CreateDealAsync(new Deal { LeadId = lid, ModelId = mid, Price = 940_000_000 });
            var (ok, msg) = await svc.DealActionAsync(did, DealStatus.Delivered);  // bỏ qua bước cọc
            Assert.False(ok);
            Assert.Contains("không hợp lệ", msg);
        }
    }

    [Fact]
    public async Task Deliver_Blocked_WithoutVin()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var (lid, mid) = await SeedLeadModel(db, svc);
            var did = await svc.CreateDealAsync(new Deal { LeadId = lid, ModelId = mid, Price = 940_000_000 });
            await svc.DealActionAsync(did, DealStatus.Deposited);
            var (ok, msg) = await svc.DealActionAsync(did, DealStatus.Delivered);   // chưa gán VIN
            Assert.False(ok);
            Assert.Contains("VIN", msg);
        }
    }

    [Fact]
    public async Task Deliver_Succeeds_AfterVinAssigned_AndSetsLeadDelivered()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var (lid, mid) = await SeedLeadModel(db, svc);
            var did = await svc.CreateDealAsync(new Deal { LeadId = lid, ModelId = mid, Price = 940_000_000 });
            await svc.DealActionAsync(did, DealStatus.Deposited);
            var (aok, _) = await svc.AssignVehicleAsync(did, "VINX123", "E1", "C1", "Đen", "30A-1");
            Assert.True(aok);
            var (ok, _) = await svc.DealActionAsync(did, DealStatus.Delivered);
            Assert.True(ok);
            var d = await svc.GetDealAsync(did);
            Assert.Equal(DealStatus.Delivered, d!.Status);
            Assert.NotNull(d.DeliveredAt);
            Assert.Equal(LeadStage.Delivered, (await svc.GetLeadAsync(lid))!.Stage);
        }
    }

    [Fact]
    public async Task AssignVehicle_RequiresVin()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var (lid, mid) = await SeedLeadModel(db, svc);
            var did = await svc.CreateDealAsync(new Deal { LeadId = lid, ModelId = mid, Price = 940_000_000 });
            var (ok, msg) = await svc.AssignVehicleAsync(did, "", null, null, null, null);
            Assert.False(ok);
            Assert.Contains("VIN", msg);
        }
    }

    [Fact]
    public async Task TotalPayable_And_Remaining_ComputedCorrectly()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var (lid, mid) = await SeedLeadModel(db, svc);
            var did = await svc.CreateDealAsync(new Deal
            {
                LeadId = lid, ModelId = mid, Price = 900_000_000, Discount = 10_000_000, DepositAmount = 50_000_000,
                VatAmount = 90_000_000, RegistrationFee = 90_000_000, PlateFee = 20_000_000, InsuranceAmount = 12_000_000, AccessoriesAmount = 8_000_000
            });
            var d = await svc.GetDealAsync(did);
            // final = 900 - 10 = 890; total = 890 + 90 + 90 + 20 + 12 + 8 = 1110 (triệu)
            Assert.Equal(890_000_000, d!.FinalPrice);
            Assert.Equal(1_110_000_000, d.TotalPayable);
            Assert.Equal(1_060_000_000, d.Remaining);   // 1110 - 50 cọc (tiền mặt, không vay)
        }
    }

    [Fact]
    public async Task Deal_Cancel_FromQuotedOrDeposited()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var (lid, mid) = await SeedLeadModel(db, svc);
            var did = await svc.CreateDealAsync(new Deal { LeadId = lid, ModelId = mid, Price = 940_000_000 });
            var (ok, _) = await svc.DealActionAsync(did, DealStatus.Cancelled);
            Assert.True(ok);
            Assert.Equal(DealStatus.Cancelled, (await svc.GetDealAsync(did))!.Status);
        }
    }
}
