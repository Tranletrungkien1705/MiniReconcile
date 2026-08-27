using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MiniReconcile.Data;
using MiniReconcile.Models;
using MiniReconcile.Services;

namespace MiniReconcile.Controllers;

public class HomeController(IReconService svc) : Controller
{
    public async Task<IActionResult> Index() { ViewBag.Dash = await svc.DashboardAsync(); return View(); }
}

public class PartnerController(IReconService svc) : Controller
{
    public async Task<IActionResult> Index(string? q)
    {
        ViewBag.Q = q;
        return View(await svc.PartnerBalancesAsync() is var all && !string.IsNullOrWhiteSpace(q)
            ? all.Where(b => b.Partner.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || b.Partner.Code.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList()
            : all);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string name, string? code, string? phone, string? email, decimal openingBalance)
    {
        if (string.IsNullOrWhiteSpace(name)) { TempData["Error"] = "Cần tên đối tác."; return RedirectToAction(nameof(Index)); }
        await svc.CreatePartnerAsync(new Partner { Name = name.Trim(), Code = code ?? "", Phone = phone, Email = email, OpeningBalance = openingBalance });
        TempData["Success"] = "Đã thêm đối tác."; return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Detail(int id)
    {
        var p = await svc.GetPartnerAsync(id);
        if (p == null) return NotFound();
        ViewBag.Ledger = await svc.LedgerAsync(id);
        ViewBag.Balance = await svc.BalanceOfAsync(id);
        ViewBag.Aging = await svc.AgingForAsync(id, DateTime.Today);
        return View(p);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddEntry(int id, LedgerType type, DateTime entryDate, string docNo, decimal amount, DateTime? dueDate, string? note)
    {
        var (ok, msg) = await svc.AddEntryAsync(new LedgerEntry
        {
            PartnerId = id, Type = type, EntryDate = entryDate == default ? DateTime.Today : entryDate,
            DocNo = docNo ?? "", Amount = amount, DueDate = type == LedgerType.Debit ? dueDate : null, Note = note
        });
        TempData[ok ? "Success" : "Error"] = msg; return RedirectToAction(nameof(Detail), new { id });
    }
}

public class StatementController(IReconService svc) : Controller
{
    public async Task<IActionResult> Index(StatementStatus? status) { ViewBag.Status = status; return View(await svc.StatementsAsync(status)); }

    public async Task<IActionResult> Create(int? partnerId)
    {
        ViewBag.Partners = await svc.PartnersAsync(null);
        ViewBag.PartnerId = partnerId;
        return View();
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(int partnerId, DateTime fromDate, DateTime toDate)
    {
        var (ok, msg, id) = await svc.CreateStatementAsync(partnerId, fromDate, toDate);
        TempData[ok ? "Success" : "Error"] = msg;
        return ok ? RedirectToAction(nameof(Detail), new { id }) : RedirectToAction(nameof(Create));
    }

    public async Task<IActionResult> Detail(int id)
    {
        var s = await svc.GetStatementAsync(id);
        if (s == null) return NotFound();
        ViewBag.Lines = await svc.StatementLinesAsync(id);
        return View(s);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SetStatus(int id, StatementStatus status, string? note)
    {
        var (ok, msg) = await svc.SetStatusAsync(id, status, note);
        TempData[ok ? "Success" : "Error"] = msg; return RedirectToAction(nameof(Detail), new { id });
    }
}

public class AgingController(IReconService svc) : Controller
{
    public async Task<IActionResult> Index()
    {
        ViewBag.Total = await svc.AgingAllAsync(DateTime.Today);
        return View(await svc.PartnerBalancesAsync());
    }
}

public class OrgController(AppDbContext db) : Controller
{
    public async Task<IActionResult> Index()
    {
        Request.Cookies.TryGetValue(TenantContext.CookieName, out var curKey);
        ViewBag.CurrentKey = curKey ?? TenantContext.DefaultApiKey;
        return View(await db.Orgs.IgnoreQueryFilters().OrderBy(o => o.CreatedAt).ToListAsync());
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) { TempData["Error"] = "Cần tên tổ chức."; return RedirectToAction(nameof(Index)); }
        var org = new Org { Name = name.Trim(), ApiKey = "rec_" + Guid.NewGuid().ToString("N") };
        db.Orgs.Add(org); await db.SaveChangesAsync();
        SetCookies(org.ApiKey, org.Name);
        TempData["Success"] = $"Đã tạo & chuyển sang \"{org.Name}\"."; return RedirectToAction("Index", "Home");
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Switch(string apiKey)
    {
        var org = await db.Orgs.IgnoreQueryFilters().FirstOrDefaultAsync(o => o.ApiKey == apiKey);
        if (org == null) { TempData["Error"] = "Không tìm thấy."; return RedirectToAction(nameof(Index)); }
        SetCookies(org.ApiKey, org.Name); return RedirectToAction("Index", "Home");
    }
    private void SetCookies(string k, string n)
    {
        var o = new CookieOptions { IsEssential = true, Expires = DateTimeOffset.UtcNow.AddDays(30) };
        Response.Cookies.Append(TenantContext.CookieName, k, o); Response.Cookies.Append("org_name", n, o);
    }
}
