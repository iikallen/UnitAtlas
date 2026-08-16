namespace UnitAtlas.Contracts;

public sealed record PrintProfileRequest(string? Code, string? IdentifierMode, string? Gs1CompanyPrefix);
public sealed record PrinterRequest(string? Code, string? Name, string? Transport, string? Endpoint);
public sealed record PrintJobRequest(
    Guid TemplateId,
    Guid ProfileId,
    Guid PrinterId,
    string? EntityType,
    string? Code,
    int Copies,
    string? IdempotencyKey);
public sealed record PrintAttemptRequest(string? Status, string? Error);
