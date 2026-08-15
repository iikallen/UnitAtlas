namespace UnitAtlas.Domain;

public sealed class Site
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
}

public sealed class Location
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid SiteId { get; set; }
    public Guid? ParentLocationId { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public required string Type { get; set; }
}

public sealed class Lot
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ProductId { get; set; }
    public required string Code { get; set; }
    public DateTimeOffset? ManufacturedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
}

public sealed class ProductIdentifier
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ProductId { get; set; }
    public required string Type { get; set; }
    public required string Value { get; set; }
}

public sealed class UnitIdentifier
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid UnitId { get; set; }
    public required string Type { get; set; }
    public required string Value { get; set; }
}

public sealed class IdempotencyRecord
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public required string Key { get; set; }
    public required string Operation { get; set; }
    public required string RequestHash { get; set; }
    public Guid ResourceId { get; set; }
    public int ResponseStatus { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}

public sealed class AuditEntry
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public required string ActorSubject { get; set; }
    public required string Action { get; set; }
    public required string EntityType { get; set; }
    public Guid? EntityId { get; set; }
    public required string DataJson { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class PublicPassportConfig
{
    public Guid UnitId { get; set; }
    public Guid TenantId { get; set; }
    public required string PublicId { get; set; }
    public bool IsPublished { get; set; }
}

public sealed class OutboxMessage
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public required string Type { get; set; }
    public required string PayloadJson { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
}

public sealed class ExternalReference
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public required string System { get; set; }
    public required string EntityType { get; set; }
    public Guid EntityId { get; set; }
    public required string Value { get; set; }
}
