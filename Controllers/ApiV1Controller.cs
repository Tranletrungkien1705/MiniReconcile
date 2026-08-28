using Microsoft.AspNetCore.Mvc;
using MiniReconcile.Data;
using MiniReconcile.Models;
using MiniReconcile.Services;

namespace MiniReconcile.Controllers;

/// <summary>
/// API JSON cho SPA React. DTO phẳng. Dashboard cache Redis 30s theo tenant (X-Cache).
/// Đối soát công nợ: sổ cái Nợ/Có → dư nợ; phân tích tuổi nợ (aging); bảng đối soát Draft→Sent→Confirmed/Disputed→Closed (đóng băng bút toán).
/// </summary>
[ApiController]
[Route("api/v1")]
[Produces("application/json")]
public class ApiV1Controller(IReconService svc, ICache cache, ITenantContext tenant) : ControllerBase
{
    [HttpGet("dashboard")]
    public async Task<IActionResult> Dashboard()
    {
        var key = $"recon:dash:{tenant.OrgId}";
        var hit = await cache.GetAsync<DashDto>(key);
        if (hit != null) { Response.Headers["X-Cache"] = "HIT"; return Ok(hit); }
        var d = await svc.DashboardAsync();
        var dto = new DashDto(d.TotalReceivable, d.PartnerCount, d.OverdueCount, d.PendingStatements,
            AgingDto.From(d.Aging), d.Top.Select(t => new TopDto(t.Partner.Name, t.Balance, t.Overdue)).ToList());
        await cache.SetAsync(key, dto, TimeSpan.FromSeconds(30));
        Response.Headers["X-Cache"] = "MISS";
        return Ok(dto);
    }

    [HttpGet("partners")]
    public async Task<IActionResult> Partners([FromQuery] string? q)
        => Ok((await svc.PartnerBalancesAsync())
            .Where(b => string.IsNullOrWhiteSpace(q) || b.Partner.Name.Contains(q!) || b.Partner.Code.Contains(q!))
            .Select(b => new { b.Partner.Id, b.Partner.Code, b.Partner.Name, b.Partner.Phone, balance = b.Balance, overdue = b.Overdue }));

    [HttpGet("partners/{id:int}")]
    public async Task<IActionResult> Partner(int id)
    {
        var p = await svc.GetPartnerAsync(id);
        if (p == null) return NotFound(new { error = "Không tìm thấy đối tác." });
        var ledger = await svc.LedgerAsync(id);
        var balance = await svc.BalanceOfAsync(id);
        var aging = await svc.AgingForAsync(id, DateTime.Today);
        return Ok(new
        {
            p.Id, p.Code, p.Name, p.Phone, p.Email, p.OpeningBalance, balance, aging = AgingDto.From(aging),
            ledger = ledger.Select(e => new
            {
                e.Id, e.EntryDate, e.DocNo, type = (int)e.Type, typeText = Ui.Ledger(e.Type), e.Amount, signed = e.Signed,
                e.DueDate, e.Note, frozen = e.StatementId != null
            })
        });
    }

    [HttpPost("partners")]
    public async Task<IActionResult> CreatePartner([FromBody] PartnerReq r)
    {
        if (string.IsNullOrWhiteSpace(r.Name)) return BadRequest(new { error = "Cần tên đối tác." });
        var id = await svc.CreatePartnerAsync(new Partner { Name = r.Name.Trim(), Code = r.Code ?? "", Phone = r.Phone, Email = r.Email, OpeningBalance = r.OpeningBalance });
        return Ok(new { id });
    }

    [HttpPost("partners/{id:int}/entries")]
    public async Task<IActionResult> AddEntry(int id, [FromBody] EntryReq r)
    {
        var (ok, msg) = await svc.AddEntryAsync(new LedgerEntry
        {
            PartnerId = id, EntryDate = r.EntryDate == default ? DateTime.Today : r.EntryDate,
            DocNo = r.DocNo ?? "", Type = (LedgerType)r.Type, Amount = r.Amount,
            DueDate = r.DueDate, Note = r.Note
        });
        return ok ? Ok(new { ok, msg }) : BadRequest(new { ok, error = msg });
    }

    [HttpGet("statements")]
    public async Task<IActionResult> Statements([FromQuery] StatementStatus? status)
        => Ok((await svc.StatementsAsync(status)).Select(s => new
        {
            s.Id, s.No, partner = s.Partner?.Name, s.FromDate, s.ToDate, s.OpeningBalance, s.TotalDebit, s.TotalCredit, s.ClosingBalance,
            status = (int)s.Status, statusText = Ui.Stmt(s.Status).text, statusCss = Ui.Stmt(s.Status).css, s.ConfirmedAt
        }));

    [HttpGet("statements/{id:int}")]
    public async Task<IActionResult> Statement(int id)
    {
        var s = await svc.GetStatementAsync(id);
        if (s == null) return NotFound(new { error = "Không tìm thấy bảng đối soát." });
        var lines = await svc.StatementLinesAsync(id);
        return Ok(new
        {
            s.Id, s.No, partner = s.Partner?.Name, s.FromDate, s.ToDate, s.OpeningBalance, s.TotalDebit, s.TotalCredit, s.ClosingBalance,
            status = (int)s.Status, statusText = Ui.Stmt(s.Status).text, s.DisputeNote, s.ConfirmedAt,
            lines = lines.Select(e => new { e.EntryDate, e.DocNo, type = (int)e.Type, typeText = Ui.Ledger(e.Type), e.Amount, signed = e.Signed })
        });
    }

    [HttpPost("statements")]
    public async Task<IActionResult> CreateStatement([FromBody] StatementReq r)
    {
        var (ok, msg, id) = await svc.CreateStatementAsync(r.PartnerId, r.From == default ? DateTime.Today.AddMonths(-1) : r.From, r.To == default ? DateTime.Today : r.To);
        return ok ? Ok(new { id }) : BadRequest(new { error = msg });
    }

    [HttpPost("statements/{id:int}/status")]
    public async Task<IActionResult> SetStatus(int id, [FromBody] StmtStatusReq r)
    {
        var (ok, msg) = await svc.SetStatusAsync(id, (StatementStatus)r.Status, r.Note);
        return ok ? Ok(new { ok, msg }) : BadRequest(new { ok, error = msg });
    }
}

public record AgingDto(decimal Current, decimal B1_30, decimal B31_60, decimal B61_90, decimal Over90)
{
    public static AgingDto From(Aging a) => new(a.Current, a.B1_30, a.B31_60, a.B61_90, a.Over90);
}
public record DashDto(decimal TotalReceivable, int PartnerCount, int OverdueCount, int PendingStatements, AgingDto Aging, List<TopDto> Top);
public record TopDto(string Partner, decimal Balance, decimal Overdue);

public class PartnerReq { public string Name { get; set; } = ""; public string? Code { get; set; } public string? Phone { get; set; } public string? Email { get; set; } public decimal OpeningBalance { get; set; } }
public class EntryReq { public DateTime EntryDate { get; set; } public string? DocNo { get; set; } public int Type { get; set; } public decimal Amount { get; set; } public DateTime? DueDate { get; set; } public string? Note { get; set; } }
public class StatementReq { public int PartnerId { get; set; } public DateTime From { get; set; } public DateTime To { get; set; } }
public class StmtStatusReq { public int Status { get; set; } public string? Note { get; set; } }
