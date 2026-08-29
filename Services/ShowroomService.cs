using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using MiniShowroom.Data;
using MiniShowroom.Models;

namespace MiniShowroom.Services;

public record ShowDash(int Leads, int Active, int TestDrivesWeek, int DealsOpen, decimal RevenueMonth,
    List<(LeadStage Stage, int Count)> Funnel);

public interface IShowroomService
{
    Task<List<VehicleModel>> ModelsAsync(bool activeOnly = false);
    Task<int> CreateModelAsync(VehicleModel m);
    Task<List<Lead>> LeadsAsync(LeadStage? stage, string? q);
    Task<Lead?> GetLeadAsync(int id);
    Task<int> CreateLeadAsync(Lead l);
    Task AdvanceAsync(int leadId, LeadStage to);
    Task<int> BookTestDriveAsync(TestDrive td);
    Task SetTestDriveStatusAsync(int id, TestDriveStatus st);
    Task<int> CreateDealAsync(Deal d);
    Task<(bool ok, string msg)> DealActionAsync(int dealId, DealStatus to);
    Task<(bool ok, string msg)> AssignVehicleAsync(int dealId, string? vin, string? engineNo, string? chassisNo, string? color, string? plate);
    Task<List<Deal>> DealsAsync(DealStatus? status);
    Task<Deal?> GetDealAsync(int id);
    Task<ShowDash> DashboardAsync();
}

public class ShowroomService(AppDbContext db, IHttpClientFactory httpFactory) : IShowroomService
{
    // Tích hợp fleet: khi giao xe → tự lập BH TNDS (MiniInsurance) + gửi thông báo (MiniNotify). Best-effort, không chặn giao xe.
    private static string InsuranceUrl => Environment.GetEnvironmentVariable("MINIINSURANCE_URL") ?? "https://miniinsurance.onrender.com";
    private static string NotifyUrl => Environment.GetEnvironmentVariable("MININOTIFY_URL") ?? "https://mininotify.onrender.com";
    private static string StampUrl => Environment.GetEnvironmentVariable("MINISTAMP_URL") ?? "https://ministamp.onrender.com";

    private async Task OnDeliveredAsync(Deal d)
    {
        var http = httpFactory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(12);
        try
        {
            var res = await http.PostAsJsonAsync($"{InsuranceUrl}/api/ext/auto-policy", new
            {
                plate = d.LicensePlate ?? d.Vin ?? d.Code, vehicleModel = d.Model?.Name,
                customerName = d.BuyerName, customerPhone = d.BuyerPhone, sumInsured = d.Price
            });
            if (res.IsSuccessStatusCode)
            {
                var body = await res.Content.ReadFromJsonAsync<AutoPolicyResult>();
                if (body?.code is { } code) { d.InsurancePolicyCode = code; await db.SaveChangesAsync(); }
            }
        }
        catch { /* best-effort */ }
        try
        {
            var res = await http.PostAsJsonAsync($"{StampUrl}/api/ext/vehicle-stamp", new
            {
                vehicleModel = d.Model?.Name, vin = d.Vin, plate = d.LicensePlate, buyerPhone = d.BuyerPhone
            });
            if (res.IsSuccessStatusCode)
            {
                var body = await res.Content.ReadFromJsonAsync<VehicleStampResult>();
                if (body?.qrId is { } qr) { d.WarrantyStampCode = qr; await db.SaveChangesAsync(); }
            }
        }
        catch { /* best-effort */ }
        try
        {
            await http.PostAsJsonAsync($"{NotifyUrl}/api/send", new
            {
                channel = "Sms", to = d.BuyerPhone ?? "", subject = "",
                body = $"Chuc mung {d.BuyerName} da nhan xe {d.Model?.Name} (HD {d.Code}). BH TNDS: {d.InsurancePolicyCode ?? "dang lap"}."
            });
        }
        catch { /* best-effort */ }
    }

    private sealed record AutoPolicyResult(int policyId, string code, string insurer, decimal premium);
    private sealed record VehicleStampResult(string qrId, string pin, string product, string? warrantyEnd, string? verifyUrl);

    public Task<List<VehicleModel>> ModelsAsync(bool activeOnly = false) =>
        (activeOnly ? db.Models.Where(m => m.IsActive) : db.Models).OrderBy(m => m.Name).ToListAsync();

    public async Task<int> CreateModelAsync(VehicleModel m)
    {
        if (string.IsNullOrWhiteSpace(m.Code)) m.Code = $"MD{await db.Models.CountAsync() + 1:D3}";
        db.Models.Add(m); await db.SaveChangesAsync(); return m.Id;
    }

    public async Task<List<Lead>> LeadsAsync(LeadStage? stage, string? q)
    {
        var query = db.Leads.Include(l => l.Model).AsQueryable();
        if (stage.HasValue) query = query.Where(l => l.Stage == stage.Value);
        if (!string.IsNullOrWhiteSpace(q)) query = query.Where(l => l.Name.Contains(q) || l.Phone.Contains(q) || l.Code.Contains(q));
        var list = await query.ToListAsync();
        return list.OrderByDescending(l => l.CreatedAt).ToList();
    }

    public Task<Lead?> GetLeadAsync(int id) =>
        db.Leads.Include(l => l.Model).Include(l => l.TestDrives).ThenInclude(t => t.Model)
          .Include(l => l.Deals).ThenInclude(d => d.Model).FirstOrDefaultAsync(l => l.Id == id);

    public async Task<int> CreateLeadAsync(Lead l)
    {
        l.Code = $"LD{DateTime.Now:yyMM}{await db.Leads.CountAsync() + 1:D4}";
        l.Stage = LeadStage.New;
        db.Leads.Add(l); await db.SaveChangesAsync(); return l.Id;
    }

    public async Task AdvanceAsync(int leadId, LeadStage to)
    {
        var l = await db.Leads.FirstOrDefaultAsync(x => x.Id == leadId) ?? throw new KeyNotFoundException();
        l.Stage = to;
        if (to == LeadStage.Lost) l.LostAt = DateTime.Now;
        await db.SaveChangesAsync();
    }

    // Nâng giai đoạn TIẾN (không lùi), bỏ qua nếu đã Lost/Delivered.
    private static LeadStage Max(LeadStage a, LeadStage b) => (LeadStage)Math.Max((int)a, (int)b);
    private static void Bump(Lead l, LeadStage minStage)
    {
        if (l.Stage is LeadStage.Lost or LeadStage.Delivered) return;
        l.Stage = Max(l.Stage, minStage);
    }

    public async Task<int> BookTestDriveAsync(TestDrive td)
    {
        var l = await db.Leads.FirstAsync(x => x.Id == td.LeadId);
        db.TestDrives.Add(td);
        Bump(l, LeadStage.TestDriven);
        await db.SaveChangesAsync();
        return td.Id;
    }

    public async Task SetTestDriveStatusAsync(int id, TestDriveStatus st)
    {
        var td = await db.TestDrives.FirstOrDefaultAsync(x => x.Id == id) ?? throw new KeyNotFoundException();
        td.Status = st; await db.SaveChangesAsync();
    }

    public async Task<int> CreateDealAsync(Deal d)
    {
        var l = await db.Leads.FirstAsync(x => x.Id == d.LeadId);
        d.Code = $"DL{DateTime.Now:yyMM}-{await db.Deals.CountAsync() + 1:D3}";
        d.Status = DealStatus.Quoted;
        db.Deals.Add(d);
        Bump(l, LeadStage.Quoted);
        await db.SaveChangesAsync();
        return d.Id;
    }

    public async Task<(bool ok, string msg)> DealActionAsync(int dealId, DealStatus to)
    {
        var d = await db.Deals.Include(x => x.Lead).Include(x => x.Model).FirstOrDefaultAsync(x => x.Id == dealId);
        if (d == null) return (false, "Không tìm thấy thương vụ.");
        // luồng: Quoted → Deposited → Delivered; hủy bất kỳ khi chưa giao.
        bool ok = to switch
        {
            DealStatus.Deposited => d.Status == DealStatus.Quoted,
            DealStatus.Delivered => d.Status == DealStatus.Deposited,
            DealStatus.Cancelled => d.Status is DealStatus.Quoted or DealStatus.Deposited,
            _ => false
        };
        if (!ok) return (false, "Chuyển trạng thái không hợp lệ.");
        // Nghiệp vụ: KHÔNG giao xe khi chưa gán định danh xe (VIN) — hồ sơ giao xe phải có số khung/số máy.
        if (to == DealStatus.Delivered && string.IsNullOrWhiteSpace(d.Vin))
            return (false, "Chưa gán xe (VIN) cho thương vụ — không thể giao.");
        d.Status = to;
        if (to == DealStatus.Deposited) { d.DepositAt = DateTime.Now; Bump(d.Lead, LeadStage.Deposited); }
        if (to == DealStatus.Delivered) { d.DeliveredAt = DateTime.Now; d.Lead.Stage = LeadStage.Delivered; }
        await db.SaveChangesAsync();
        if (to == DealStatus.Delivered) await OnDeliveredAsync(d);   // tích hợp fleet: lập BH + thông báo (best-effort)
        return (true, to switch { DealStatus.Deposited => "Đã ghi nhận đặt cọc.", DealStatus.Delivered => "Đã giao xe — chốt deal!", _ => "Đã hủy thương vụ." });
    }

    // Gán định danh xe được giao (VIN/số máy/số khung/biển số) — chỉ khi đã cọc trở đi, chưa giao.
    public async Task<(bool ok, string msg)> AssignVehicleAsync(int dealId, string? vin, string? engineNo, string? chassisNo, string? color, string? plate)
    {
        var d = await db.Deals.FirstOrDefaultAsync(x => x.Id == dealId);
        if (d == null) return (false, "Không tìm thấy thương vụ.");
        if (d.Status is DealStatus.Cancelled) return (false, "Thương vụ đã hủy.");
        if (string.IsNullOrWhiteSpace(vin)) return (false, "Cần số VIN.");
        d.Vin = vin.Trim(); d.EngineNo = engineNo; d.ChassisNo = chassisNo;
        if (!string.IsNullOrWhiteSpace(color)) d.Color = color;
        if (!string.IsNullOrWhiteSpace(plate)) d.LicensePlate = plate;
        await db.SaveChangesAsync();
        return (true, "Đã gán xe cho thương vụ.");
    }

    public async Task<List<Deal>> DealsAsync(DealStatus? status)
    {
        var q = db.Deals.Include(d => d.Model).Include(d => d.Lead).AsQueryable();
        if (status.HasValue) q = q.Where(d => d.Status == status.Value);
        var list = await q.ToListAsync();
        return list.OrderByDescending(d => d.CreatedAt).ToList();
    }

    public Task<Deal?> GetDealAsync(int id) =>
        db.Deals.Include(d => d.Model).Include(d => d.Lead).FirstOrDefaultAsync(d => d.Id == id);

    public async Task<ShowDash> DashboardAsync()
    {
        var leads = await db.Leads.ToListAsync();
        var deals = await db.Deals.ToListAsync();
        var weekAgo = DateTime.Now.AddDays(-7);
        var monthStart = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        var funnel = new List<(LeadStage, int)>();
        foreach (LeadStage s in Enum.GetValues(typeof(LeadStage)))
            funnel.Add((s, leads.Count(l => l.Stage == s)));
        return new ShowDash(
            leads.Count,
            leads.Count(l => l.Stage is not (LeadStage.Delivered or LeadStage.Lost)),
            await db.TestDrives.CountAsync(t => t.ScheduledAt >= weekAgo),
            deals.Count(d => d.Status is DealStatus.Quoted or DealStatus.Deposited),
            deals.Where(d => d.Status == DealStatus.Delivered && d.DeliveredAt >= monthStart).Sum(d => d.TotalPayable),
            funnel);
    }
}
