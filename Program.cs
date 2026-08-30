using Microsoft.EntityFrameworkCore;
using MiniReconcile.Data;
using MiniReconcile.Models;
using MiniReconcile.Services;
using Serilog;

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
builder.Services.AddFleetObs();
builder.Services.AddControllersWithViews();

var app = builder.Build();
using (var scope = app.Services.CreateScope())
    await Seeder.SeedAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>());

app.UseFleetObs();

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
