using Microsoft.EntityFrameworkCore;
using MiniReconcile.Data;
using MiniReconcile.Models;

namespace MiniReconcile.Services;

public record PartnerBalance(Partner Partner, decimal Balance, decimal Overdue, Aging Aging);
public record AgingBucket(string Label, decimal Amount);
public record Aging(decimal Current, decimal B1_30, decimal B31_60, decimal B61_90, decimal Over90)
{
    public decimal Total => Current + B1_30 + B31_60 + B61_90 + Over90;
    public List<AgingBucket> Buckets() => new()
    {
        new("Trong hạn", Current), new("1–30 ngày", B1_30), new("31–60 ngày", B31_60),
        new("61–90 ngày", B61_90), new(">90 ngày", Over90)
    };
}
public record ReconDash(decimal TotalReceivable, int PartnerCount, int OverdueCount, int PendingStatements, Aging Aging, List<PartnerBalance> Top);

public interface IReconService
{
    Task<List<Partner>> PartnersAsync(string? q);
    Task<List<PartnerBalance>> PartnerBalancesAsync();
    Task<Partner?> GetPartnerAsync(int id);
    Task<int> CreatePartnerAsync(Partner p);
    Task<decimal> BalanceOfAsync(int partnerId, DateTime? asOf = null);
    Task<List<LedgerEntry>> LedgerAsync(int partnerId);
    Task<(bool ok, string msg)> AddEntryAsync(LedgerEntry e);
    Task<Aging> AgingForAsync(int partnerId, DateTime asOf);
    Task<Aging> AgingAllAsync(DateTime asOf);
    Task<List<Statement>> StatementsAsync(StatementStatus? status);
    Task<Statement?> GetStatementAsync(int id);
    Task<List<LedgerEntry>> StatementLinesAsync(int statementId);
    Task<(bool ok, string msg, int id)> CreateStatementAsync(int partnerId, DateTime from, DateTime to);
    Task<(bool ok, string msg)> SetStatusAsync(int id, StatementStatus status, string? note);
    Task<ReconDash> DashboardAsync();
}

public class ReconService(AppDbContext db) : IReconService
{
    public Task<List<Partner>> PartnersAsync(string? q)
    {
        var query = db.Partners.AsQueryable();
        if (!string.IsNullOrWhiteSpace(q)) query = query.Where(p => p.Name.Contains(q) || p.Code.Contains(q));
        return query.OrderBy(p => p.Code).ToListAsync();
    }

    public async Task<List<PartnerBalance>> PartnerBalancesAsync()
    {
        var today = DateTime.Today;
        var partners = await db.Partners.OrderBy(p => p.Code).ToListAsync();
        var entries = await db.Ledger.ToListAsync();
        var result = new List<PartnerBalance>();
        foreach (var p in partners)
        {
            var es = entries.Where(e => e.PartnerId == p.Id).ToList();
            var bal = p.OpeningBalance + es.Sum(e => e.Signed);
            var aging = ComputeAging(p, es, today);
            result.Add(new PartnerBalance(p, bal, aging.Total - aging.Current, aging));
        }
        return result;
    }

    public Task<Partner?> GetPartnerAsync(int id) => db.Partners.FirstOrDefaultAsync(p => p.Id == id);

    public async Task<int> CreatePartnerAsync(Partner p)
    {
        if (string.IsNullOrWhiteSpace(p.Code)) p.Code = $"KH{await db.Partners.CountAsync() + 1:D4}";
        db.Partners.Add(p); await db.SaveChangesAsync(); return p.Id;
    }

    public async Task<decimal> BalanceOfAsync(int partnerId, DateTime? asOf = null)
    {
        var p = await db.Partners.FirstOrDefaultAsync(x => x.Id == partnerId);
        if (p == null) return 0;
        var q = db.Ledger.Where(e => e.PartnerId == partnerId);
        if (asOf.HasValue) q = q.Where(e => e.EntryDate <= asOf.Value);
        var es = await q.ToListAsync();
        return p.OpeningBalance + es.Sum(e => e.Signed);
    }

    public Task<List<LedgerEntry>> LedgerAsync(int partnerId) =>
        db.Ledger.Where(e => e.PartnerId == partnerId).OrderBy(e => e.EntryDate).ThenBy(e => e.Id).ToListAsync();

    public async Task<(bool ok, string msg)> AddEntryAsync(LedgerEntry e)
    {
        if (e.Amount <= 0) return (false, "Số tiền phải > 0.");
        if (!await db.Partners.AnyAsync(p => p.Id == e.PartnerId)) return (false, "Không tìm thấy đối tác.");
        db.Ledger.Add(e); await db.SaveChangesAsync();
        return (true, e.Type == LedgerType.Debit ? "Đã ghi công nợ (ghi nợ)." : "Đã ghi nhận thanh toán.");
    }

    public async Task<Aging> AgingForAsync(int partnerId, DateTime asOf)
    {
        var p = await db.Partners.FirstOrDefaultAsync(x => x.Id == partnerId);
        if (p == null) return new Aging(0, 0, 0, 0, 0);
        var es = await db.Ledger.Where(e => e.PartnerId == partnerId).ToListAsync();
        return ComputeAging(p, es, asOf);
    }

    public async Task<Aging> AgingAllAsync(DateTime asOf)
    {
        var partners = await db.Partners.ToListAsync();
        var entries = await db.Ledger.ToListAsync();
        decimal c = 0, b1 = 0, b2 = 0, b3 = 0, b4 = 0;
        foreach (var p in partners)
        {
            var a = ComputeAging(p, entries.Where(e => e.PartnerId == p.Id).ToList(), asOf);
            c += a.Current; b1 += a.B1_30; b2 += a.B31_60; b3 += a.B61_90; b4 += a.Over90;
        }
        return new Aging(c, b1, b2, b3, b4);
    }

    // FIFO: phân bổ các khoản thanh toán (credit) vào các khoản ghi nợ cũ nhất trước,
    // phần ghi nợ chưa được cấn trừ được xếp tuổi nợ theo hạn thanh toán (hoặc ngày chứng từ).
    private static Aging ComputeAging(Partner p, List<LedgerEntry> entries, DateTime asOf)
    {
        var debits = new List<(DateTime age, decimal remain)>();
        // Dư đầu kỳ coi như khoản ghi nợ cũ nhất, đã tới hạn.
        if (p.OpeningBalance > 0) debits.Add((DateTime.MinValue, p.OpeningBalance));

        foreach (var e in entries.Where(e => e.Type == LedgerType.Debit).OrderBy(e => e.EntryDate).ThenBy(e => e.Id))
            debits.Add((e.DueDate ?? e.EntryDate, e.Amount));

        var credits = entries.Where(e => e.Type == LedgerType.Credit).Sum(e => e.Amount)
                      + (p.OpeningBalance < 0 ? -p.OpeningBalance : 0);   // dư có đầu kỳ

        // Cấn trừ FIFO
        for (int i = 0; i < debits.Count && credits > 0; i++)
        {
            var pay = Math.Min(credits, debits[i].remain);
            debits[i] = (debits[i].age, debits[i].remain - pay);
            credits -= pay;
        }

        decimal c = 0, b1 = 0, b2 = 0, b3 = 0, b4 = 0;
        foreach (var (age, remain) in debits)
        {
            if (remain <= 0) continue;
            var days = age == DateTime.MinValue ? int.MaxValue : (asOf.Date - age.Date).Days;
            if (days <= 0) c += remain;
            else if (days <= 30) b1 += remain;
            else if (days <= 60) b2 += remain;
            else if (days <= 90) b3 += remain;
            else b4 += remain;
        }
        return new Aging(c, b1, b2, b3, b4);
    }

    public Task<List<Statement>> StatementsAsync(StatementStatus? status)
    {
        var q = db.Statements.Include(s => s.Partner).AsQueryable();
        if (status.HasValue) q = q.Where(s => s.Status == status.Value);
        return q.OrderByDescending(s => s.Id).ToListAsync();
    }

    public Task<Statement?> GetStatementAsync(int id) =>
        db.Statements.Include(s => s.Partner).FirstOrDefaultAsync(s => s.Id == id);

    public Task<List<LedgerEntry>> StatementLinesAsync(int statementId) =>
        db.Ledger.Where(e => e.StatementId == statementId).OrderBy(e => e.EntryDate).ThenBy(e => e.Id).ToListAsync();

    public async Task<(bool ok, string msg, int id)> CreateStatementAsync(int partnerId, DateTime from, DateTime to)
    {
        var p = await db.Partners.FirstOrDefaultAsync(x => x.Id == partnerId);
        if (p == null) return (false, "Không tìm thấy đối tác.", 0);
        if (to < from) return (false, "Khoảng thời gian không hợp lệ.", 0);

        // Chỉ đóng băng các dòng chưa thuộc bảng đối soát nào.
        var inPeriod = await db.Ledger.Where(e => e.PartnerId == partnerId && e.StatementId == null
                            && e.EntryDate >= from && e.EntryDate <= to)
                            .OrderBy(e => e.EntryDate).ThenBy(e => e.Id).ToListAsync();
        if (inPeriod.Count == 0) return (false, "Không có phát sinh chưa đối soát trong kỳ.", 0);

        // Dư đầu kỳ = số dư ngay trước ngày 'from' (chỉ tính dòng phát sinh trước from).
        var before = await db.Ledger.Where(e => e.PartnerId == partnerId && e.EntryDate < from).ToListAsync();
        var opening = p.OpeningBalance + before.Sum(e => e.Signed);

        var debit = inPeriod.Where(e => e.Type == LedgerType.Debit).Sum(e => e.Amount);
        var credit = inPeriod.Where(e => e.Type == LedgerType.Credit).Sum(e => e.Amount);

        var st = new Statement
        {
            No = $"DS{DateTime.Today:yyyyMM}-{p.Code}-{await db.Statements.CountAsync() + 1:D3}",
            PartnerId = partnerId, FromDate = from, ToDate = to,
            OpeningBalance = opening, TotalDebit = debit, TotalCredit = credit,
            ClosingBalance = opening + debit - credit, Status = StatementStatus.Draft
        };
        db.Statements.Add(st);
        await db.SaveChangesAsync();

        foreach (var e in inPeriod) e.StatementId = st.Id;
        await db.SaveChangesAsync();
        return (true, $"Đã lập bảng đối soát {st.No} ({inPeriod.Count} dòng).", st.Id);
    }

    private static readonly Dictionary<StatementStatus, StatementStatus[]> _allowed = new()
    {
        [StatementStatus.Draft] = new[] { StatementStatus.Sent },
        [StatementStatus.Sent] = new[] { StatementStatus.Confirmed, StatementStatus.Disputed },
        [StatementStatus.Disputed] = new[] { StatementStatus.Sent, StatementStatus.Closed },
        [StatementStatus.Confirmed] = new[] { StatementStatus.Closed },
    };

    public async Task<(bool ok, string msg)> SetStatusAsync(int id, StatementStatus status, string? note)
    {
        var st = await db.Statements.FirstOrDefaultAsync(s => s.Id == id);
        if (st == null) return (false, "Không tìm thấy.");
        if (!_allowed.TryGetValue(st.Status, out var next) || !next.Contains(status))
            return (false, $"Không thể chuyển {st.Status} → {status}.");
        st.Status = status;
        if (status == StatementStatus.Confirmed) st.ConfirmedAt = DateTime.UtcNow;
        if (status == StatementStatus.Disputed) st.DisputeNote = note;
        await db.SaveChangesAsync();
        return (true, $"Đã cập nhật trạng thái: {status}.");
    }

    public async Task<ReconDash> DashboardAsync()
    {
        var balances = await PartnerBalancesAsync();
        var aging = await AgingAllAsync(DateTime.Today);
        var pending = await db.Statements.CountAsync(s => s.Status == StatementStatus.Sent || s.Status == StatementStatus.Disputed);
        return new ReconDash(
            balances.Sum(b => b.Balance),
            balances.Count,
            balances.Count(b => b.Overdue > 0),
            pending,
            aging,
            balances.Where(b => b.Balance != 0).OrderByDescending(b => b.Balance).Take(6).ToList());
    }
}
