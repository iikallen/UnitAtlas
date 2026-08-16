namespace UnitAtlas.Domain;

public enum IntegrationDeliveryStatus
{
    Pending,
    Delivering,
    Retry,
    Delivered,
    DeadLetter
}

public sealed class IntegrationEndpoint
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public required string System { get; set; }
    public required string Adapter { get; set; }
    public required string BaseAddress { get; set; }
    public string? SecretRef { get; set; }
    public required string SettingsJson { get; set; }
    public bool Enabled { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class IntegrationDelivery
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid OutboxMessageId { get; set; }
    public Guid IntegrationEndpointId { get; set; }
    public IntegrationDeliveryStatus Status { get; set; }
    public int AttemptCount { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }
    public DateTimeOffset? LastAttemptAt { get; set; }
    public DateTimeOffset? DeliveredAt { get; set; }
    public DateTimeOffset? LeaseUntil { get; set; }
    public Guid? LeaseToken { get; set; }
    public string? LastErrorCode { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class InboxMessage
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid IntegrationEndpointId { get; set; }
    public required string SourceSystem { get; set; }
    public required string ExternalMessageId { get; set; }
    public required string PayloadHash { get; set; }
    public required string PayloadJson { get; set; }
    public required string ResultJson { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
}
