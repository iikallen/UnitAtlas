namespace UnitAtlas.Domain;

public sealed class LabelTemplate
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public required string Code { get; set; }
    public required string EntityType { get; set; }
    public required string IdentifierMode { get; set; }
    public required string Symbology { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class PrintProfile
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public required string Code { get; set; }
    public required string IdentifierMode { get; set; }
    public string? Gs1CompanyPrefix { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class Printer
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public required string Transport { get; set; }
    public string? Endpoint { get; set; }
    public bool IsEnabled { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class PrintJob
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid TemplateId { get; set; }
    public Guid ProfileId { get; set; }
    public Guid PrinterId { get; set; }
    public required string Status { get; set; }
    public required string IdempotencyKey { get; set; }
    public required string RequestHash { get; set; }
    public required string CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? DispatchedAt { get; set; }
    public DateTimeOffset? PrintedAt { get; set; }
}

public sealed class PrintJobItem
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid PrintJobId { get; set; }
    public required string EntityType { get; set; }
    public Guid EntityId { get; set; }
    public required string Code { get; set; }
    public required string Payload { get; set; }
    public required string HumanReadable { get; set; }
    public int Copies { get; set; }
}

public sealed class PrintAttempt
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid PrintJobId { get; set; }
    public required string Status { get; set; }
    public string? Error { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
