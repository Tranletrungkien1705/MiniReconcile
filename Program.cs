using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using MiniReconcile.Data;
using MiniReconcile.Models;
using MiniReconcile.Services;
using Serilog;

JwtSecurityTokenHandler.DefaultMapInboundClaims = false;   // giữ claim gốc từ MiniSSO
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
FleetObs.ConfigureLogger("minireconcile");

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog();
builder.WebHost.UseUrls($"http://0.0.0.0:{Environment.GetEnvironmentVariable("PORT") ?? "8080"}");

var conn = Environment.GetEnvironmentVariable("CONNECTION_STRING")
    ?? builder.Configuration.GetConnectionString("DefaultConnection") ?? "Data Source=minirec.db";
builder.Services.AddDbContext<AppDbContext>(o =>
{
    if (DbUtil.IsPostgres(conn)) o.UseNpgsql(DbUtil.ToNpgsql(conn));
    else o.UseSqlite(conn);
});
builder.Services.AddScoped<ITenantContext, TenantContext>();
builder.Services.AddScoped<IReconService, ReconService>();
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
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));
builder.Services.AddFleetObs();
builder.Services.AddControllersWithViews();

var app = builder.Build();
using (var scope = app.Services.CreateScope())
    await Seeder.SeedAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>());

app.UseFleetObs();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

// SSO chung: endpoint xác thực bằng token MiniSSO.
app.MapGet("/api/whoami", (ClaimsPrincipal u) => Results.Ok(new
{
    app = "minireconcile",
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
app.MapGet("/api/summary", async (IReconService svc) =>
{
    var d = await svc.DashboardAsync();
    return Results.Ok(new { receivable = d.TotalReceivable, partners = d.PartnerCount, overdue = d.OverdueCount, pendingStatements = d.PendingStatements });
});

// API công khai: cổng đại lý (iDealer) tra công nợ + tuổi nợ theo mã đại lý.
app.MapGet("/api/partner-balance", async (string code, IReconService svc, AppDbContext db) =>
{
    var p = await db.Partners.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Code == code.Trim());
    if (p == null) return Results.NotFound(new { code, found = false });
    var bal = await svc.BalanceOfAsync(p.Id);
    var aging = await svc.AgingForAsync(p.Id, DateTime.Today);
    var entries = await db.Ledger.IgnoreQueryFilters().Where(e => e.PartnerId == p.Id)
        .OrderByDescending(e => e.EntryDate).Take(10).ToListAsync();
    return Results.Ok(new
    {
        found = true, p.Code, p.Name, balance = bal, overdue = aging.Over90 + aging.B61_90 + aging.B31_60,
        aging = new { current = aging.Current, d1_30 = aging.B1_30, d31_60 = aging.B31_60, d61_90 = aging.B61_90, over90 = aging.Over90 },
        recent = entries.Select(e => new { date = e.EntryDate.ToString("yyyy-MM-dd"), e.DocNo, type = e.Type == LedgerType.Debit ? "Nợ" : "Có", e.Amount, e.Note })
    });
});

// API tích hợp: hệ bán hàng (MiniDMS...) đẩy công nợ vào sổ đối soát tự động.
// type: 0 = ghi nợ (đại lý nợ tiền hàng), 1 = ghi có (đại lý thanh toán).
app.MapPost("/api/ext/ledger", async (ExtLedgerDto dto, IReconService svc, AppDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(dto.PartnerCode)) return Results.BadRequest(new { error = "Cần PartnerCode." });
    if (dto.Amount <= 0) return Results.BadRequest(new { error = "Số tiền phải > 0." });
    var p = await db.Partners.FirstOrDefaultAsync(x => x.Code == dto.PartnerCode);
    if (p == null)
    {
        await svc.CreatePartnerAsync(new Partner { Code = dto.PartnerCode.Trim(), Name = dto.PartnerName ?? dto.PartnerCode.Trim() });
        p = await db.Partners.FirstOrDefaultAsync(x => x.Code == dto.PartnerCode);
    }
    var date = DateTime.TryParse(dto.Date, out var dt) ? dt : DateTime.Today;
    var (ok, msg) = await svc.AddEntryAsync(new LedgerEntry
    {
        PartnerId = p!.Id, Type = (LedgerType)dto.Type, Amount = dto.Amount, DocNo = dto.RefNo ?? "",
        EntryDate = date, DueDate = dto.Type == 0 ? date.AddDays(30) : null, Note = dto.Note
    });
    var bal = await svc.BalanceOfAsync(p.Id);
    return ok ? Results.Ok(new { ok, msg, partnerCode = p.Code, balance = bal }) : Results.BadRequest(new { ok, error = msg });
});

app.MapPost("/api/orgs/register", async (RegisterOrgDto dto, AppDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(dto.Name)) return Results.BadRequest(new { error = "Cần Name." });
    var org = new Org { Name = dto.Name.Trim(), ApiKey = "rec_" + Guid.NewGuid().ToString("N") };
    db.Orgs.Add(org); await db.SaveChangesAsync();
    return Results.Ok(new { orgId = org.Id, apiKey = org.ApiKey });
});

app.MapControllerRoute(name: "default", pattern: "{controller=Home}/{action=Index}/{id?}");
app.Run();

record RegisterOrgDto(string Name);
record ExtLedgerDto(string PartnerCode, string? PartnerName, int Type, decimal Amount, string? RefNo, string? Date, string? Note);
