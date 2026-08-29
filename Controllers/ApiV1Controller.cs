using Microsoft.AspNetCore.Mvc;
using MiniShowroom.Data;
using MiniShowroom.Models;
using MiniShowroom.Services;

namespace MiniShowroom.Controllers;

/// <summary>
/// API JSON cho SPA React (client-side). DTO phẳng (tránh vòng lặp navigation khi serialize).
/// Dashboard cache Redis 30s theo tenant. Mọi enum trả kèm text tiếng Việt để UI khỏi map lại.
/// </summary>
[ApiController]
[Route("api/v1")]
[Produces("application/json")]
public class ApiV1Controller(IShowroomService svc, ICache cache, ITenantContext tenant) : ControllerBase
{
    // ───────────────────────── Dashboard ─────────────────────────
    [HttpGet("dashboard")]
    public async Task<IActionResult> Dashboard()
    {
        var key = $"show:dash:{tenant.OrgId}";
        var cached = await cache.GetAsync<DashboardDto>(key);
        if (cached != null) { Response.Headers["X-Cache"] = "HIT"; return Ok(cached); }

        var d = await svc.DashboardAsync();
        var dto = new DashboardDto(d.Leads, d.Active, d.TestDrivesWeek, d.DealsOpen, d.RevenueMonth,
            d.Funnel.Select(f => new FunnelDto((int)f.Stage, Ui.Stage(f.Stage).text, f.Count)).ToList());
        await cache.SetAsync(key, dto, TimeSpan.FromSeconds(30));
        Response.Headers["X-Cache"] = "MISS";
        return Ok(dto);
    }

    // ───────────────────────── Models ─────────────────────────
    [HttpGet("models")]
    public async Task<IActionResult> Models([FromQuery] bool activeOnly = false)
        => Ok((await svc.ModelsAsync(activeOnly)).Select(ToModelDto));

    [HttpPost("models")]
    public async Task<IActionResult> CreateModel([FromBody] ModelReq r)
    {
        if (string.IsNullOrWhiteSpace(r.Name)) return BadRequest(new { error = "Cần tên mẫu xe." });
        var id = await svc.CreateModelAsync(new VehicleModel
        {
            Name = r.Name.Trim(), Code = r.Code ?? "", Variant = r.Variant, ListPrice = r.ListPrice, Color = r.Color,
            ModelYear = r.ModelYear, FuelType = r.FuelType, Seats = r.Seats, Segment = r.Segment,
            WarrantyMonths = r.WarrantyMonths ?? 36
        });
        return Ok(new { id });
    }

    // ───────────────────────── Leads ─────────────────────────
    [HttpGet("leads")]
    public async Task<IActionResult> Leads([FromQuery] LeadStage? stage, [FromQuery] string? q)
        => Ok((await svc.LeadsAsync(stage, q)).Select(ToLeadDto));

    [HttpGet("leads/{id:int}")]
    public async Task<IActionResult> Lead(int id)
    {
        var l = await svc.GetLeadAsync(id);
        if (l == null) return NotFound(new { error = "Không tìm thấy lead." });
        return Ok(new LeadDetailDto(
            ToLeadDto(l),
            l.TestDrives.OrderByDescending(t => t.ScheduledAt).Select(t => new TestDriveDto(
                t.Id, t.ModelId, t.Model?.Name ?? "", t.ScheduledAt, (int)t.Status, Ui.TD(t.Status).text, t.Note)).ToList(),
            l.Deals.OrderByDescending(d => d.CreatedAt).Select(ToDealDto).ToList()));
    }

    [HttpPost("leads")]
    public async Task<IActionResult> CreateLead([FromBody] LeadReq r)
    {
        if (string.IsNullOrWhiteSpace(r.Name) || string.IsNullOrWhiteSpace(r.Phone))
            return BadRequest(new { error = "Cần tên và SĐT." });
        var id = await svc.CreateLeadAsync(new Lead
        {
            Name = r.Name.Trim(), Phone = r.Phone.Trim(), Email = r.Email, IdentityNo = r.IdentityNo, Address = r.Address,
            Source = (LeadSource)r.Source, ModelId = r.ModelId, SalesPerson = r.SalesPerson, Note = r.Note
        });
        return Ok(new { id });
    }

    [HttpPost("leads/{id:int}/advance")]
    public async Task<IActionResult> Advance(int id, [FromBody] AdvanceReq r)
    {
        var to = (LeadStage)r.To;
        await svc.AdvanceAsync(id, to);
        return Ok(new { ok = true, stage = r.To, stageText = Ui.Stage(to).text });
    }

    // ───────────────────────── Test drives ─────────────────────────
    [HttpPost("testdrives")]
    public async Task<IActionResult> BookTestDrive([FromBody] TestDriveReq r)
    {
        var id = await svc.BookTestDriveAsync(new TestDrive
        {
            LeadId = r.LeadId, ModelId = r.ModelId,
            ScheduledAt = r.ScheduledAt == default ? DateTime.Now.AddDays(1) : r.ScheduledAt, Note = r.Note
        });
        return Ok(new { id });
    }

    [HttpPost("testdrives/{id:int}/status")]
    public async Task<IActionResult> SetTdStatus(int id, [FromBody] TdStatusReq r)
    {
        await svc.SetTestDriveStatusAsync(id, (TestDriveStatus)r.Status);
        return Ok(new { ok = true });
    }

    // ───────────────────────── Deals ─────────────────────────
    [HttpGet("deals")]
    public async Task<IActionResult> Deals([FromQuery] DealStatus? status)
        => Ok((await svc.DealsAsync(status)).Select(ToDealDto));

    [HttpGet("deals/{id:int}")]
    public async Task<IActionResult> Deal(int id)
    {
        var d = await svc.GetDealAsync(id);
        return d == null ? NotFound(new { error = "Không tìm thấy thương vụ." }) : Ok(ToDealDto(d));
    }

    [HttpPost("deals")]
    public async Task<IActionResult> CreateDeal([FromBody] DealReq r)
    {
        var m = (await svc.ModelsAsync()).FirstOrDefault(x => x.Id == r.ModelId);
        if (m == null) return BadRequest(new { error = "Mẫu xe không hợp lệ." });
        var lead = await svc.GetLeadAsync(r.LeadId);
        if (lead == null) return BadRequest(new { error = "Lead không hợp lệ." });

        var price = r.Price > 0 ? r.Price : m.ListPrice;
        // Nếu client không truyền phí → tự tính giá lăn bánh chuẩn (VAT 10%, trước bạ 10%, biển số 20tr, BH 1.5%).
        var vat = r.VatAmount ?? Math.Round(price * 0.10m, 0);
        var reg = r.RegistrationFee ?? Math.Round(price * 0.10m, 0);
        var ins = r.InsuranceAmount ?? Math.Round(price * 0.015m, 0);
        var id = await svc.CreateDealAsync(new Deal
        {
            LeadId = r.LeadId, ModelId = r.ModelId, Price = price, Discount = r.Discount, DepositAmount = r.DepositAmount,
            VatAmount = vat, RegistrationFee = reg, PlateFee = r.PlateFee ?? 20_000_000, InsuranceAmount = ins,
            AccessoriesAmount = r.AccessoriesAmount, PaymentMethod = (PayMethod)r.PaymentMethod, BankName = r.BankName, LoanAmount = r.LoanAmount,
            ExpectedDelivery = r.ExpectedDelivery, SalesPerson = r.SalesPerson ?? lead.SalesPerson,
            BuyerName = lead.Name, BuyerIdNo = lead.IdentityNo, BuyerPhone = lead.Phone, BuyerAddress = lead.Address
        });
        return Ok(new { id });
    }

    [HttpPost("deals/{id:int}/action")]
    public async Task<IActionResult> DealAction(int id, [FromBody] DealActionReq r)
    {
        var (ok, msg) = await svc.DealActionAsync(id, (DealStatus)r.To);
        return ok ? Ok(new { ok, msg }) : BadRequest(new { ok, error = msg });
    }

    [HttpPost("deals/{id:int}/assign-vehicle")]
    public async Task<IActionResult> AssignVehicle(int id, [FromBody] AssignVehicleReq r)
    {
        var (ok, msg) = await svc.AssignVehicleAsync(id, r.Vin, r.EngineNo, r.ChassisNo, r.Color, r.LicensePlate);
        return ok ? Ok(new { ok, msg }) : BadRequest(new { ok, error = msg });
    }

    // ───────────────────────── Mappers ─────────────────────────
    private static ModelDto ToModelDto(VehicleModel m) => new(
        m.Id, m.Code, m.Name, m.Variant, m.ListPrice, m.Color, m.ModelYear, m.FuelType, m.Seats, m.Segment, m.WarrantyMonths, m.IsActive);

    private static LeadDto ToLeadDto(Lead l) => new(
        l.Id, l.Code, l.Name, l.Phone, l.Email, l.IdentityNo, l.Address,
        (int)l.Source, Ui.Source(l.Source), l.ModelId, l.Model?.Name,
        (int)l.Stage, Ui.Stage(l.Stage).text, Ui.Stage(l.Stage).css, l.SalesPerson, l.Note, l.CreatedAt);

    private static DealDto ToDealDto(Deal d) => new(
        d.Id, d.Code, d.LeadId, d.Lead?.Name, d.ModelId, d.Model?.Name ?? "",
        d.Price, d.Discount, d.FinalPrice, d.DepositAmount,
        d.VatAmount, d.RegistrationFee, d.PlateFee, d.InsuranceAmount, d.AccessoriesAmount, d.TotalPayable, d.Remaining,
        (int)d.PaymentMethod, d.PaymentMethod.ToString(), d.BankName, d.LoanAmount,
        d.Vin, d.EngineNo, d.ChassisNo, d.Color, d.LicensePlate,
        d.BuyerName, d.BuyerIdNo, d.BuyerPhone, d.BuyerAddress, d.SalesPerson,
        (int)d.Status, Ui.Deal(d.Status).text, Ui.Deal(d.Status).css, d.CreatedAt, d.DepositAt, d.ExpectedDelivery, d.DeliveredAt,
        d.InsurancePolicyCode, d.WarrantyStampCode);
}

// ───────────────────────── DTOs & request records ─────────────────────────
public record DashboardDto(int Leads, int Active, int TestDrivesWeek, int DealsOpen, decimal RevenueMonth, List<FunnelDto> Funnel);
public record FunnelDto(int Stage, string StageText, int Count);
public record ModelDto(int Id, string Code, string Name, string? Variant, decimal ListPrice, string? Color,
    int? ModelYear, string? FuelType, int? Seats, string? Segment, int WarrantyMonths, bool IsActive);
public record LeadDto(int Id, string Code, string Name, string Phone, string? Email, string? IdentityNo, string? Address,
    int Source, string SourceText, int? ModelId, string? ModelName,
    int Stage, string StageText, string StageCss, string? SalesPerson, string? Note, DateTime CreatedAt);
public record TestDriveDto(int Id, int ModelId, string ModelName, DateTime ScheduledAt, int Status, string StatusText, string? Note);
public record DealDto(int Id, string Code, int LeadId, string? LeadName, int ModelId, string ModelName,
    decimal Price, decimal Discount, decimal FinalPrice, decimal DepositAmount,
    decimal VatAmount, decimal RegistrationFee, decimal PlateFee, decimal InsuranceAmount, decimal AccessoriesAmount, decimal TotalPayable, decimal Remaining,
    int PaymentMethod, string PaymentMethodText, string? BankName, decimal LoanAmount,
    string? Vin, string? EngineNo, string? ChassisNo, string? Color, string? LicensePlate,
    string? BuyerName, string? BuyerIdNo, string? BuyerPhone, string? BuyerAddress, string? SalesPerson,
    int Status, string StatusText, string StatusCss, DateTime CreatedAt, DateTime? DepositAt, DateTime? ExpectedDelivery, DateTime? DeliveredAt,
    string? InsurancePolicyCode, string? WarrantyStampCode);
public record LeadDetailDto(LeadDto Lead, List<TestDriveDto> TestDrives, List<DealDto> Deals);

// Request DTO = class có get/set (System.Text.Json bind ổn định hơn positional record cho body phức tạp).
// Enum nhận dạng int trong body → cast trong controller.
public class ModelReq
{
    public string Name { get; set; } = ""; public string? Code { get; set; } public string? Variant { get; set; }
    public decimal ListPrice { get; set; } public string? Color { get; set; }
    public int? ModelYear { get; set; } public string? FuelType { get; set; } public int? Seats { get; set; }
    public string? Segment { get; set; } public int? WarrantyMonths { get; set; }
}
public class LeadReq
{
    public string Name { get; set; } = ""; public string Phone { get; set; } = ""; public string? Email { get; set; }
    public string? IdentityNo { get; set; } public string? Address { get; set; }
    public int Source { get; set; } public int? ModelId { get; set; } public string? SalesPerson { get; set; } public string? Note { get; set; }
}
public class AdvanceReq { public int To { get; set; } }
public class TestDriveReq { public int LeadId { get; set; } public int ModelId { get; set; } public DateTime ScheduledAt { get; set; } public string? Note { get; set; } }
public class TdStatusReq { public int Status { get; set; } }
public class DealReq
{
    public int LeadId { get; set; } public int ModelId { get; set; }
    public decimal Price { get; set; } public decimal Discount { get; set; } public decimal DepositAmount { get; set; }
    public decimal? VatAmount { get; set; } public decimal? RegistrationFee { get; set; } public decimal? PlateFee { get; set; }
    public decimal? InsuranceAmount { get; set; } public decimal AccessoriesAmount { get; set; }
    public int PaymentMethod { get; set; } public string? BankName { get; set; } public decimal LoanAmount { get; set; }
    public DateTime? ExpectedDelivery { get; set; } public string? SalesPerson { get; set; }
}
public class DealActionReq { public int To { get; set; } }
public class AssignVehicleReq
{
    public string? Vin { get; set; } public string? EngineNo { get; set; } public string? ChassisNo { get; set; }
    public string? Color { get; set; } public string? LicensePlate { get; set; }
}
