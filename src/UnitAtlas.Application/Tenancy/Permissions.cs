using UnitAtlas.Domain;

namespace UnitAtlas.Application.Tenancy;

public static class Permissions
{
    public const string UnitsRead = "units.read";
    public const string UnitsCreate = "units.create";
    public const string EventsRecord = "events.record";
    public const string ProductsManage = "products.manage";
    public const string PackagingRead = "packaging.read";
    public const string PackagingManage = "packaging.manage";
    public const string IntegrationsRead = "integrations.read";
    public const string IntegrationsManage = "integrations.manage";
    public const string PrintingRead = "printing.read";
    public const string PrintingManage = "printing.manage";
    public const string TenantManage = "tenant.manage";

    public static readonly string[] All = [UnitsRead, UnitsCreate, EventsRecord, ProductsManage, PackagingRead, PackagingManage, IntegrationsRead, IntegrationsManage, PrintingRead, PrintingManage, TenantManage];

    public static IReadOnlyCollection<string> For(TenantRole role) => role switch
    {
        TenantRole.Owner or TenantRole.Admin => All,
        TenantRole.ProductionManager => [UnitsRead, UnitsCreate, EventsRecord, ProductsManage, PackagingRead, PackagingManage, IntegrationsRead, PrintingRead, PrintingManage],
        TenantRole.ProductionOperator => [UnitsRead, UnitsCreate, EventsRecord, PackagingRead, PackagingManage, PrintingRead, PrintingManage],
        TenantRole.QualityManager or TenantRole.QualityOperator => [UnitsRead, EventsRecord, PackagingRead],
        TenantRole.WarehouseManager => [UnitsRead, EventsRecord, PackagingRead, PackagingManage, IntegrationsRead, PrintingRead, PrintingManage],
        TenantRole.WarehouseOperator => [UnitsRead, EventsRecord, PackagingRead, PackagingManage, PrintingRead, PrintingManage],
        _ => [UnitsRead, PackagingRead, PrintingRead]
    };
}
