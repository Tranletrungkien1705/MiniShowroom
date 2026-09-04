using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using MiniShowroom.Data;
using MiniShowroom.Models;
using MiniShowroom.Services;
using Serilog;

JwtSecurityTokenHandler.DefaultMapInboundClaims = false;   // giữ claim gốc từ MiniSSO
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
FleetObs.ConfigureLogger("minishowroom");

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog();
builder.WebHost.UseUrls($"http://0.0.0.0:{Environment.GetEnvironmentVariable("PORT") ?? "8080"}");

var conn = Environment.GetEnvironmentVariable("CONNECTION_STRING")
    ?? builder.Configuration.GetConnectionString("DefaultConnection") ?? "Data Source=minishowroom.db";
builder.Services.AddDbContext<AppDbContext>(o =>
{
    if (DbUtil.IsPostgres(conn)) o.UseNpgsql(DbUtil.ToNpgsql(conn));
    else o.UseSqlite(conn);
});
builder.Services.AddScoped<ITenantContext, TenantContext>();
builder.Services.AddScoped<IShowroomService, ShowroomService>();
builder.Services.AddHttpClient();   // gọi MiniInsurance/MiniNotify khi giao xe
// SSO chung: tin token MiniSSO (OIDC RS256).
var ssoAuthority = Environment.GetEnvironmentVariable("SSO_AUTHORITY") ?? "https://minisso.onrender.com";
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
{
    o.Authority = ssoAuthority;
    o.RequireHttpsMetadata = ssoAuthority.StartsWith("https");
    o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true, ValidIssuer = ssoAuthority,
        ValidateAudience = false, ValidateLifetime = true, NameClaimType = "name", RoleClaimType = "role"
    };
});
builder.Services.AddAuthorization();
builder.Services.AddFleetObs();
builder.Services.AddControllersWithViews();

var app = builder.Build();
using (var scope = app.Services.CreateScope())
    await Seeder.SeedAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>());

app.UseFleetObs();
FleetObs.ReportLicense(ssoAuthority, "minishowroom");
app.UseAuthentication();
app.UseAuthorization();

// SSO chung: endpoint xác thực bằng token MiniSSO.
app.MapGet("/api/whoami", (ClaimsPrincipal u) => Results.Ok(new
{
    app = "minishowroom",
    sub = u.FindFirst("sub")?.Value, name = u.Identity?.Name ?? u.FindFirst("name")?.Value,
    email = u.FindFirst("email")?.Value, tenant = u.FindFirst("tenant")?.Value,
    roles = u.FindAll("role").Select(c => c.Value)
})).RequireAuthorization();

app.Use(async (ctx, next) =>
{
    var key = ctx.Request.Headers["X-Api-Key"].FirstOrDefault();
    if (string.IsNullOrWhiteSpace(key)) ctx.Request.Cookies.TryGetValue(TenantContext.CookieName, out key);
    if (!string.IsNullOrWhiteSpace(key))
    {
        using var lookup = app.Services.CreateScope();
        var ldb = lookup.ServiceProvider.GetRequiredService<AppDbContext>();
        var org = await ldb.Orgs.FirstOrDefaultAsync(o => o.ApiKey == key);
        if (org != null) ctx.RequestServices.GetRequiredService<ITenantContext>().OrgId = org.Id;
    }
    await next();
});

app.UseStaticFiles();
app.MapGet("/healthz", () => "ok");

// API tiếp nhận lead từ web/landing page (form khách để lại thông tin)
app.MapPost("/api/leads", async (LeadDto dto, IShowroomService svc) =>
{
    if (string.IsNullOrWhiteSpace(dto.Name) || string.IsNullOrWhiteSpace(dto.Phone))
        return Results.BadRequest(new { error = "Cần Name và Phone." });
    var models = await svc.ModelsAsync();
    var mid = models.FirstOrDefault(m => m.Code == dto.ModelCode)?.Id;
    var id = await svc.CreateLeadAsync(new Lead { Name = dto.Name.Trim(), Phone = dto.Phone.Trim(), Email = dto.Email, Source = LeadSource.Website, ModelId = mid, Note = dto.Note });
    var l = await svc.GetLeadAsync(id);
    return Results.Ok(new { id, code = l!.Code, stage = Ui.Stage(l.Stage).text });
});

app.MapPost("/api/orgs/register", async (RegisterOrgDto dto, AppDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(dto.Name)) return Results.BadRequest(new { error = "Cần Name." });
    var org = new Org { Name = dto.Name.Trim(), ApiKey = "shw_" + Guid.NewGuid().ToString("N") };
    db.Orgs.Add(org); await db.SaveChangesAsync();
    return Results.Ok(new { orgId = org.Id, apiKey = org.ApiKey });
});

// Import hàng loạt model xe thật từ Mst_CarModel (SQL nguồn 2010.HTC). Dedupe theo Code.
app.MapPost("/api/import/models", async (List<ImportModelDto> rows, AppDbContext db) =>
{
    if (rows is null || rows.Count == 0) return Results.BadRequest(new { error = "Không có dữ liệu import." });
    int added = 0, skipped = 0;
    foreach (var r in rows)
    {
        if (string.IsNullOrWhiteSpace(r.ModelCode) || string.IsNullOrWhiteSpace(r.ModelName)) { skipped++; continue; }
        var code = r.ModelCode.Trim();
        if (await db.Models.AnyAsync(m => m.Code == code)) { skipped++; continue; }
        db.Models.Add(new VehicleModel
        {
            Code = code, Name = r.ModelName.Trim(), Segment = r.SegmentType,
            IsActive = r.FlagActive != "0", WarrantyMonths = 36
        });
        added++;
    }
    await db.SaveChangesAsync();
    return Results.Ok(new { added, skipped, total = rows.Count });
});

app.MapControllerRoute(name: "default", pattern: "{controller=Home}/{action=Index}/{id?}");
app.Run();

record RegisterOrgDto(string Name);
record ImportModelDto(string? ModelCode, string? ModelName, string? SegmentType, string? FlagActive);
record LeadDto(string Name, string Phone, string? Email, string? ModelCode, string? Note);
