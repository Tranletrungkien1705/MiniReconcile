using Microsoft.EntityFrameworkCore;
using MiniReconcile.Models;

namespace MiniReconcile.Data;

public class AppDbContext : DbContext
{
    private readonly Guid _orgId;
    public AppDbContext(DbContextOptions<AppDbContext> options, ITenantContext tenant) : base(options) => _orgId = tenant.OrgId;

    public DbSet<Org> Orgs => Set<Org>();
    public DbSet<Partner> Partners => Set<Partner>();
    public DbSet<LedgerEntry> Ledger => Set<LedgerEntry>();
    public DbSet<Statement> Statements => Set<Statement>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        if (Database.IsNpgsql()) b.HasDefaultSchema("minirec");
        b.Entity<Org>().HasIndex(x => x.ApiKey).IsUnique();
        b.Entity<Partner>(e =>
        {
            e.HasIndex(x => new { x.OrgId, x.Code }).IsUnique();
            e.Property(x => x.OpeningBalance).HasPrecision(18, 2);
            e.HasQueryFilter(x => x.OrgId == _orgId);
        });
        b.Entity<LedgerEntry>(e =>
        {
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.Ignore(x => x.Signed);
            e.HasIndex(x => new { x.OrgId, x.PartnerId });
            e.HasOne(x => x.Partner).WithMany(x => x.Entries).HasForeignKey(x => x.PartnerId);
            e.HasQueryFilter(x => x.OrgId == _orgId);
        });
        b.Entity<Statement>(e =>
        {
            e.HasIndex(x => new { x.OrgId, x.No }).IsUnique();
            e.Property(x => x.OpeningBalance).HasPrecision(18, 2);
            e.Property(x => x.TotalDebit).HasPrecision(18, 2);
            e.Property(x => x.TotalCredit).HasPrecision(18, 2);
            e.Property(x => x.ClosingBalance).HasPrecision(18, 2);
            e.HasOne(x => x.Partner).WithMany().HasForeignKey(x => x.PartnerId);
            e.HasQueryFilter(x => x.OrgId == _orgId);
        });
    }

    public override int SaveChanges() { StampOrg(); return base.SaveChanges(); }
    public override Task<int> SaveChangesAsync(CancellationToken ct = default) { StampOrg(); return base.SaveChangesAsync(ct); }
    private void StampOrg()
    {
        foreach (var e in ChangeTracker.Entries<IOrgOwned>())
            if (e.State == EntityState.Added && e.Entity.OrgId == Guid.Empty) e.Entity.OrgId = _orgId;
    }
}
