using UnitAtlas.Domain;

namespace UnitAtlas.Application.Tenancy;

public interface ITenantContext
{
    bool IsAvailable { get; }
    Guid TenantId { get; }
    string UserSubject { get; }
    TenantRole Role { get; }
    IReadOnlyCollection<string> GrantedPermissions { get; }
    void Initialize(Guid tenantId, string userSubject, TenantRole role = TenantRole.Viewer);
    void Clear();
}

public sealed class TenantContext : ITenantContext
{
    public bool IsAvailable { get; private set; }
    public Guid TenantId { get; private set; }
    public string UserSubject { get; private set; } = "";
    public TenantRole Role { get; private set; }
    public IReadOnlyCollection<string> GrantedPermissions { get; private set; } = [];

    public void Initialize(Guid tenantId, string userSubject, TenantRole role = TenantRole.Viewer)
    {
        TenantId = tenantId;
        UserSubject = userSubject;
        Role = role;
        GrantedPermissions = Permissions.For(role);
        IsAvailable = true;
    }

    public void Clear()
    {
        TenantId = Guid.Empty;
        UserSubject = "";
        GrantedPermissions = [];
        IsAvailable = false;
    }
}
