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
                new VehicleModel { Code = "ACC", Name = "Hyundai Accent", Variant = "1.4 AT Đặc biệt", ListPrice = 567_000_000, Color = "Trắng", ModelYear = 2024, FuelType = "Xăng", Seats = 5, Segment = "B-Sedan" },
                new VehicleModel { Code = "TUC", Name = "Hyundai Tucson", Variant = "2.0 Xăng đặc biệt", ListPrice = 940_000_000, Color = "Đen", ModelYear = 2024, FuelType = "Xăng", Seats = 5, Segment = "C-SUV" },
                new VehicleModel { Code = "SANTA", Name = "Hyundai Santa Fe", Variant = "2.5 Xăng cao cấp", ListPrice = 1_365_000_000, Color = "Bạc", ModelYear = 2024, FuelType = "Xăng", Seats = 7, Segment = "D-SUV" },
                new VehicleModel { Code = "CRETA", Name = "Hyundai Creta", Variant = "1.5 Đặc biệt", ListPrice = 640_000_000, Color = "Đỏ", ModelYear = 2024, FuelType = "Xăng", Seats = 5, Segment = "B-SUV" });
            await db.SaveChangesAsync();
        }
        if (!await db.Leads.AnyAsync())
        {
            var models = await db.Models.ToListAsync();
            VehicleModel M(string c) => models.First(m => m.Code == c);
            int n = 0;
            Lead L(string name, string phone, LeadSource src, string modelCode, LeadStage stage, string sale, string? id = null, string? addr = null)
            {
                n++;
                return new Lead { Code = $"LD{DateTime.Now:yyMM}{n:D4}", Name = name, Phone = phone, Source = src,
                    ModelId = M(modelCode).Id, Stage = stage, SalesPerson = sale, IdentityNo = id, Address = addr,
                    CreatedAt = DateTime.Now.AddDays(-n) };
            }
            var leads = new List<Lead>
            {
                L("Nguyễn Văn An", "0901111111", LeadSource.Facebook, "ACC", LeadStage.TestDriven, "Sale Hoa", "001090111111", "Hà Nội"),
                L("Trần Thị Bình", "0902222222", LeadSource.Hotline, "TUC", LeadStage.Quoted, "Sale Nam", "001092222222", "Hải Phòng"),
                L("Lê Hoàng Cường", "0903333333", LeadSource.WalkIn, "SANTA", LeadStage.Deposited, "Sale Hoa", "001093333333", "Bắc Ninh"),
                L("Phạm Thu Dung", "0904444444", LeadSource.Website, "CRETA", LeadStage.New, "Sale Nam"),
                L("Vũ Minh Đức", "0905555555", LeadSource.Referral, "ACC", LeadStage.Contacted, "Sale Hoa"),
                L("Đỗ Quốc Huy", "0906666666", LeadSource.WalkIn, "TUC", LeadStage.Delivered, "Sale Nam", "001096666666", "Hà Nội"),
            };
            db.Leads.AddRange(leads);
            await db.SaveChangesAsync();

            // Thương vụ mẫu — kèm phí lăn bánh (VAT 10%, trước bạ ~10%, biển số, bảo hiểm) + xe đã giao có VIN.
            int d = 0;
            Deal MakeDeal(Lead lead, string modelCode, DealStatus st)
            {
                d++;
                var m = M(modelCode);
                var price = m.ListPrice;
                var deal = new Deal
                {
                    Code = $"DL{DateTime.Now:yyMM}-{d:D3}", LeadId = lead.Id, ModelId = m.Id,
                    Price = price, Discount = 10_000_000, DepositAmount = st >= DealStatus.Deposited ? 50_000_000 : 0,
                    VatAmount = Math.Round(price * 0.10m, 0), RegistrationFee = Math.Round(price * 0.10m, 0),
                    PlateFee = 20_000_000, InsuranceAmount = 12_000_000, AccessoriesAmount = 8_000_000,
                    PaymentMethod = PayMethod.Cash, Status = st,
                    BuyerName = lead.Name, BuyerIdNo = lead.IdentityNo, BuyerPhone = lead.Phone, BuyerAddress = lead.Address, SalesPerson = lead.SalesPerson,
                    ExpectedDelivery = DateTime.Today.AddDays(7),
                };
                if (st >= DealStatus.Deposited) deal.DepositAt = DateTime.Now.AddDays(-2);
                if (st == DealStatus.Delivered)
                {
                    deal.DeliveredAt = DateTime.Now.AddDays(-1);
                    deal.Vin = "RLHXXXXXXXX" + (100000 + d);
                    deal.EngineNo = "G4FG" + (200000 + d);
                    deal.ChassisNo = "RLHKF48" + (300000 + d);
                    deal.Color = m.Color; deal.LicensePlate = $"30K-{d:D3}.{d * 11:D2}";
                }
                return deal;
            }
            db.Deals.AddRange(
                MakeDeal(leads[1], "TUC", DealStatus.Quoted),
                MakeDeal(leads[2], "SANTA", DealStatus.Deposited),
                MakeDeal(leads[5], "TUC", DealStatus.Delivered));
            await db.SaveChangesAsync();
        }
    }

    private static async Task MigratePostgresAsync(AppDbContext db)
    {
        if (!db.Database.IsNpgsql()) return;
        var def = TenantContext.DefaultOrgId;
        const string S = "minishowroom";
        var sql = new List<string>
        {
            $"CREATE TABLE IF NOT EXISTS {S}.\"Orgs\" (\"Id\" uuid PRIMARY KEY, \"Name\" text NOT NULL DEFAULT '', \"ApiKey\" text NOT NULL DEFAULT '', \"CreatedAt\" timestamp NOT NULL DEFAULT now())",
            $"CREATE UNIQUE INDEX IF NOT EXISTS \"IX_Orgs_ApiKey\" ON {S}.\"Orgs\" (\"ApiKey\")",
        };
        foreach (var t in new[] { "Models", "Leads", "TestDrives", "Deals" })
            sql.Add($"ALTER TABLE {S}.\"{t}\" ADD COLUMN IF NOT EXISTS \"OrgId\" uuid NOT NULL DEFAULT '{def}'");

        // Cột nghiệp vụ bổ sung (đợt nâng cấp production template) — DB cloud cũ cần ALTER vì EnsureCreated không sửa bảng đã có.
        void Add(string t, string col, string type) => sql.Add($"ALTER TABLE {S}.\"{t}\" ADD COLUMN IF NOT EXISTS \"{col}\" {type}");
        Add("Models", "ModelYear", "integer"); Add("Models", "FuelType", "text"); Add("Models", "Seats", "integer");
        Add("Models", "Segment", "text"); Add("Models", "WarrantyMonths", "integer NOT NULL DEFAULT 36");
        Add("Leads", "IdentityNo", "text"); Add("Leads", "Address", "text");
        foreach (var c in new[] { "VatAmount", "RegistrationFee", "PlateFee", "InsuranceAmount", "AccessoriesAmount", "LoanAmount" })
            Add("Deals", c, "numeric(18,2) NOT NULL DEFAULT 0");
        Add("Deals", "PaymentMethod", "integer NOT NULL DEFAULT 0");
        foreach (var c in new[] { "BankName", "Vin", "EngineNo", "ChassisNo", "Color", "LicensePlate", "BuyerName", "BuyerIdNo", "BuyerPhone", "BuyerAddress", "SalesPerson" })
            Add("Deals", c, "text");
        Add("Deals", "ExpectedDelivery", "timestamp");
        Add("Deals", "InsurancePolicyCode", "text");   // tích hợp MiniInsurance

        foreach (var s in sql) try { await db.Database.ExecuteSqlRawAsync(s); } catch { }
    }
}
