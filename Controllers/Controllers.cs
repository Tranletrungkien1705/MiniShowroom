using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MiniShowroom.Data;
using MiniShowroom.Models;
using MiniShowroom.Services;

namespace MiniShowroom.Controllers;

public class HomeController : Controller
{
    // SPA React là mặc định ở "/" (client-side). Màn Razor cũ vẫn còn tại /Legacy để đối chiếu.
    public IActionResult Index() => Redirect("/index.html");
}

public class LegacyController(IShowroomService svc) : Controller
{
    public async Task<IActionResult> Index() { ViewBag.Dash = await svc.DashboardAsync(); return View("~/Views/Home/Index.cshtml"); }
}

public class ModelController(IShowroomService svc) : Controller
{
    public async Task<IActionResult> Index() => View(await svc.ModelsAsync());
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string name, string? code, string? variant, decimal listPrice, string? color)
    {
        if (string.IsNullOrWhiteSpace(name)) { TempData["Error"] = "Cần tên mẫu xe."; return RedirectToAction(nameof(Index)); }
        await svc.CreateModelAsync(new VehicleModel { Name = name.Trim(), Code = code ?? "", Variant = variant, ListPrice = listPrice, Color = color });
        TempData["Success"] = "Đã thêm mẫu xe.";
        return RedirectToAction(nameof(Index));
    }
}

public class LeadController(IShowroomService svc) : Controller
{
    public async Task<IActionResult> Index(LeadStage? stage, string? q)
    {
        ViewBag.Stage = stage; ViewBag.Q = q;
        return View(await svc.LeadsAsync(stage, q));
    }

    public async Task<IActionResult> Create()
    {
        ViewBag.Models = await svc.ModelsAsync(true);
        return View();
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string name, string phone, string? email, LeadSource source, int? modelId, string? salesPerson, string? note)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(phone))
        { TempData["Error"] = "Cần tên và SĐT."; ViewBag.Models = await svc.ModelsAsync(true); return View(); }
        var id = await svc.CreateLeadAsync(new Lead { Name = name.Trim(), Phone = phone.Trim(), Email = email, Source = source, ModelId = modelId, SalesPerson = salesPerson, Note = note });
        TempData["Success"] = "Đã tạo lead.";
        return RedirectToAction(nameof(Detail), new { id });
    }

    public async Task<IActionResult> Detail(int id)
    {
        var lead = await svc.GetLeadAsync(id);
        if (lead == null) return NotFound();
        ViewBag.Models = await svc.ModelsAsync(true);
        return View(lead);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Advance(int id, LeadStage to)
    {
        await svc.AdvanceAsync(id, to);
        TempData["Success"] = $"Đã chuyển giai đoạn: {Ui.Stage(to).text}.";
        return RedirectToAction(nameof(Detail), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> BookTestDrive(int id, int modelId, DateTime scheduledAt, string? note)
    {
        await svc.BookTestDriveAsync(new TestDrive { LeadId = id, ModelId = modelId, ScheduledAt = scheduledAt == default ? DateTime.Now.AddDays(1) : scheduledAt, Note = note });
        TempData["Success"] = "Đã đặt lịch lái thử.";
        return RedirectToAction(nameof(Detail), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SetTd(int id, int tdId, TestDriveStatus status)
    {
        await svc.SetTestDriveStatusAsync(tdId, status);
        return RedirectToAction(nameof(Detail), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateDeal(int id, int modelId, decimal price, decimal discount, decimal depositAmount)
    {
        await svc.CreateDealAsync(new Deal { LeadId = id, ModelId = modelId, Price = price, Discount = discount, DepositAmount = depositAmount });
        TempData["Success"] = "Đã tạo báo giá.";
        return RedirectToAction(nameof(Detail), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DealAction(int id, int dealId, DealStatus to)
    {
        var (ok, msg) = await svc.DealActionAsync(dealId, to);
        TempData[ok ? "Success" : "Error"] = msg;
        return RedirectToAction(nameof(Detail), new { id });
    }
}

public class DealController(IShowroomService svc) : Controller
{
    public async Task<IActionResult> Index(DealStatus? status) { ViewBag.Status = status; return View(await svc.DealsAsync(status)); }
}

public class OrgController(AppDbContext db) : Controller
{
    public async Task<IActionResult> Index()
    {
        var orgs = await db.Orgs.IgnoreQueryFilters().OrderBy(o => o.CreatedAt).ToListAsync();
        Request.Cookies.TryGetValue(TenantContext.CookieName, out var curKey);
        ViewBag.CurrentKey = curKey ?? TenantContext.DefaultApiKey;
        return View(orgs);
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) { TempData["Error"] = "Cần tên tổ chức."; return RedirectToAction(nameof(Index)); }
        var org = new Org { Name = name.Trim(), ApiKey = "shw_" + Guid.NewGuid().ToString("N") };
        db.Orgs.Add(org); await db.SaveChangesAsync();
        SetCookies(org.ApiKey, org.Name);
        TempData["Success"] = $"Đã tạo & chuyển sang \"{org.Name}\".";
        return RedirectToAction("Index", "Home");
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Switch(string apiKey)
    {
        var org = await db.Orgs.IgnoreQueryFilters().FirstOrDefaultAsync(o => o.ApiKey == apiKey);
        if (org == null) { TempData["Error"] = "Không tìm thấy."; return RedirectToAction(nameof(Index)); }
        SetCookies(org.ApiKey, org.Name);
        return RedirectToAction("Index", "Home");
    }
    public IActionResult Reset()
    {
        Response.Cookies.Delete(TenantContext.CookieName); Response.Cookies.Delete("org_name");
        return RedirectToAction("Index", "Home");
    }
    private void SetCookies(string k, string n)
    {
        var o = new CookieOptions { IsEssential = true, Expires = DateTimeOffset.UtcNow.AddDays(30) };
        Response.Cookies.Append(TenantContext.CookieName, k, o); Response.Cookies.Append("org_name", n, o);
    }
}
