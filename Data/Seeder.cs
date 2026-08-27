using Microsoft.EntityFrameworkCore;
using MiniReconcile.Models;
namespace MiniReconcile.Data;

public static class Seeder
{
    public static async Task SeedAsync(AppDbContext db)
    {
        await db.Database.EnsureCreatedAsync();
        await MigratePostgresAsync(db);
        if (!await db.Orgs.AnyAsync(o => o.Id == TenantContext.DefaultOrgId))
        { db.Orgs.Add(new Org { Id = TenantContext.DefaultOrgId, Name = "Demo Đối soát", ApiKey = TenantContext.DefaultApiKey }); await db.SaveChangesAsync(); }

        if (!await db.Partners.AnyAsync())
        {
            var t = DateTime.Today;
            var p1 = new Partner { Code = "DL001", Name = "Đại lý Đông Đô", Phone = "0911000001", OpeningBalance = 50_000_000 };
            var p2 = new Partner { Code = "DL002", Name = "Đại lý Miền Nam", Phone = "0911000002", OpeningBalance = 0 };
            var p3 = new Partner { Code = "DL003", Name = "Đại lý Tây Bắc", Phone = "0911000003", OpeningBalance = 0 };
            db.Partners.AddRange(p1, p2, p3);
            await db.SaveChangesAsync();

            db.Ledger.AddRange(
                // Đông Đô: nợ cũ + hóa đơn quá hạn + thanh toán một phần
                new LedgerEntry { PartnerId = p1.Id, EntryDate = t.AddDays(-120), DocNo = "HD-1001", Type = LedgerType.Debit, Amount = 80_000_000, DueDate = t.AddDays(-90), Note = "Nhập lô xe Q1" },
                new LedgerEntry { PartnerId = p1.Id, EntryDate = t.AddDays(-40), DocNo = "PT-5001", Type = LedgerType.Credit, Amount = 60_000_000, Note = "Chuyển khoản" },
                new LedgerEntry { PartnerId = p1.Id, EntryDate = t.AddDays(-20), DocNo = "HD-1042", Type = LedgerType.Debit, Amount = 30_000_000, DueDate = t.AddDays(10), Note = "Phụ tùng" },
                // Miền Nam: mua và trả đủ
                new LedgerEntry { PartnerId = p2.Id, EntryDate = t.AddDays(-60), DocNo = "HD-2001", Type = LedgerType.Debit, Amount = 45_000_000, DueDate = t.AddDays(-30), Note = "Nhập xe" },
                new LedgerEntry { PartnerId = p2.Id, EntryDate = t.AddDays(-25), DocNo = "PT-6001", Type = LedgerType.Credit, Amount = 45_000_000, Note = "Tất toán" },
                // Tây Bắc: hóa đơn còn trong hạn
                new LedgerEntry { PartnerId = p3.Id, EntryDate = t.AddDays(-10), DocNo = "HD-3001", Type = LedgerType.Debit, Amount = 20_000_000, DueDate = t.AddDays(20), Note = "Nhập phụ tùng" });
            await db.SaveChangesAsync();
        }
    }

    private static async Task MigratePostgresAsync(AppDbContext db)
    {
        if (!db.Database.IsNpgsql()) return;
        var def = TenantContext.DefaultOrgId;
        var tables = new[] { "Partners", "Ledger", "Statements" };
        var sql = new List<string> {
            "CREATE TABLE IF NOT EXISTS minirec.\"Orgs\" (\"Id\" uuid PRIMARY KEY, \"Name\" text NOT NULL DEFAULT '', \"ApiKey\" text NOT NULL DEFAULT '', \"CreatedAt\" timestamp NOT NULL DEFAULT now())",
            "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_Orgs_ApiKey\" ON minirec.\"Orgs\" (\"ApiKey\")" };
        foreach (var t in tables) sql.Add($"ALTER TABLE minirec.\"{t}\" ADD COLUMN IF NOT EXISTS \"OrgId\" uuid NOT NULL DEFAULT '{def}'");
        foreach (var s in sql) try { await db.Database.ExecuteSqlRawAsync(s); } catch { }
    }
}
