namespace MiniShowroom.Data;

public interface ITenantContext { Guid OrgId { get; set; } }

public sealed class TenantContext : ITenantContext
{
    public static readonly Guid DefaultOrgId = new("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    public const string DefaultApiKey = "demo-showroom";
    public const string CookieName = "org_key";
    public Guid OrgId { get; set; } = DefaultOrgId;
}
