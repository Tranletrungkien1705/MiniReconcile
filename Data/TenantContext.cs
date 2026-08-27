namespace MiniReconcile.Data;
public interface ITenantContext { Guid OrgId { get; set; } }
public sealed class TenantContext : ITenantContext
{
    public static readonly Guid DefaultOrgId = new("ffffffff-ffff-ffff-ffff-ffffffffffff");
    public const string DefaultApiKey = "demo-rec";
    public const string CookieName = "org_key";
    public Guid OrgId { get; set; } = DefaultOrgId;
}
