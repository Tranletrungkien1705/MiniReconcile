using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MiniReconcile.Data;
using MiniReconcile.Models;
using MiniReconcile.Services;
using Xunit;

namespace MiniReconcile.Tests;

/// <summary>Test đối soát công nợ: dư nợ = đầu kỳ + nợ − có, lập bảng đóng băng bút toán, closing balance, vòng trạng thái.</summary>
public class ReconServiceTests
{
    private static (AppDbContext db, IReconService svc, SqliteConnection conn) NewSvc()
    {
        var conn = new SqliteConnection("DataSource=:memory:"); conn.Open();
        var opt = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(conn).Options;
        var db = new AppDbContext(opt, new TenantContext { OrgId = TenantContext.DefaultOrgId });
        db.Database.EnsureCreated();
        return (db, new ReconService(db), conn);
    }

    private static async Task<int> NewPartner(IReconService svc, decimal opening = 0)
        => await svc.CreatePartnerAsync(new Partner { Code = "DL1", Name = "Đại lý 1", OpeningBalance = opening });

    [Fact]
    public async Task Balance_OpeningPlusDebitMinusCredit()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var pid = await NewPartner(svc, 1_000_000);
            await svc.AddEntryAsync(new LedgerEntry { PartnerId = pid, Type = LedgerType.Debit, Amount = 5_000_000, DocNo = "HD1" });
            await svc.AddEntryAsync(new LedgerEntry { PartnerId = pid, Type = LedgerType.Credit, Amount = 2_000_000, DocNo = "PT1" });
            var bal = await svc.BalanceOfAsync(pid);
            Assert.Equal(4_000_000, bal);   // 1 + 5 − 2 (triệu)
        }
    }

    [Fact]
    public async Task CreateStatement_FreezesEntries_AndComputesClosing()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var pid = await NewPartner(svc, 1_000_000);
            await svc.AddEntryAsync(new LedgerEntry { PartnerId = pid, Type = LedgerType.Debit, Amount = 3_000_000, DocNo = "HD1", EntryDate = DateTime.Today.AddDays(-5) });
            await svc.AddEntryAsync(new LedgerEntry { PartnerId = pid, Type = LedgerType.Credit, Amount = 1_000_000, DocNo = "PT1", EntryDate = DateTime.Today.AddDays(-3) });
            var (ok, _, sid) = await svc.CreateStatementAsync(pid, DateTime.Today.AddDays(-10), DateTime.Today);
            Assert.True(ok);
            var s = await svc.GetStatementAsync(sid);
            Assert.Equal(1_000_000, s!.OpeningBalance);
            Assert.Equal(3_000_000, s.TotalDebit);
            Assert.Equal(1_000_000, s.TotalCredit);
            Assert.Equal(3_000_000, s.ClosingBalance);  // 1 + 3 − 1
            var lines = await svc.StatementLinesAsync(sid);
            Assert.All(lines, l => Assert.Equal(sid, l.StatementId));  // đóng băng
        }
    }

    [Fact]
    public async Task Statement_StatusFlow_SentConfirmed()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var pid = await NewPartner(svc);
            await svc.AddEntryAsync(new LedgerEntry { PartnerId = pid, Type = LedgerType.Debit, Amount = 1_000_000, DocNo = "HD" });
            var (_, _, sid) = await svc.CreateStatementAsync(pid, DateTime.Today.AddDays(-10), DateTime.Today);
            Assert.True((await svc.SetStatusAsync(sid, StatementStatus.Sent, null)).ok);
            Assert.True((await svc.SetStatusAsync(sid, StatementStatus.Confirmed, null)).ok);
            Assert.Equal(StatementStatus.Confirmed, (await svc.GetStatementAsync(sid))!.Status);
        }
    }

    [Fact]
    public async Task Statement_Dispute_WithNote()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var pid = await NewPartner(svc);
            await svc.AddEntryAsync(new LedgerEntry { PartnerId = pid, Type = LedgerType.Debit, Amount = 500_000, DocNo = "HD" });
            var (_, _, sid) = await svc.CreateStatementAsync(pid, DateTime.Today.AddDays(-5), DateTime.Today);
            await svc.SetStatusAsync(sid, StatementStatus.Sent, null);
            await svc.SetStatusAsync(sid, StatementStatus.Disputed, "Sai số tiền");
            var s = await svc.GetStatementAsync(sid);
            Assert.Equal(StatementStatus.Disputed, s!.Status);
            Assert.Equal("Sai số tiền", s.DisputeNote);
        }
    }

    [Fact]
    public async Task Dashboard_TotalReceivable()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var pid = await NewPartner(svc);
            await svc.AddEntryAsync(new LedgerEntry { PartnerId = pid, Type = LedgerType.Debit, Amount = 7_000_000, DocNo = "HD" });
            var d = await svc.DashboardAsync();
            Assert.Equal(7_000_000, d.TotalReceivable);
            Assert.Equal(1, d.PartnerCount);
        }
    }
}
