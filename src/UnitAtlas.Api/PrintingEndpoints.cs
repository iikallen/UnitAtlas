using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using UnitAtlas.Application.Printing;
using UnitAtlas.Application.Tenancy;
using UnitAtlas.Contracts;
using UnitAtlas.Domain;
using UnitAtlas.Infrastructure.Persistence;

namespace UnitAtlas.Api;

public static class PrintingEndpoints
{
    public static WebApplication MapPrintingEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1").RequireAuthorization();
        api.MapGet("/print-setup", Setup).RequireAuthorization(Permissions.PrintingRead);
        api.MapPost("/print-profiles", CreateProfile).RequireAuthorization(Permissions.PrintingManage);
        api.MapPost("/printers", CreatePrinter).RequireAuthorization(Permissions.PrintingManage);
        api.MapGet("/print-jobs", ListJobs).RequireAuthorization(Permissions.PrintingRead);
        api.MapPost("/print-jobs", CreateJob).RequireAuthorization(Permissions.PrintingManage);
        api.MapGet("/print-jobs/{id:guid}", GetJob).RequireAuthorization(Permissions.PrintingRead);
        api.MapPost("/print-jobs/{id:guid}/attempts", RecordAttempt).RequireAuthorization(Permissions.PrintingManage);
        return app;
    }

    private static async Task<IResult> Setup(UnitAtlasDb db) => Results.Ok(new
    {
        templates = await db.LabelTemplates.AsNoTracking().OrderBy(x => x.Code)
            .Select(x => new { x.Id, x.Code, x.EntityType, x.IdentifierMode, x.Symbology }).ToListAsync(),
        profiles = await db.PrintProfiles.AsNoTracking().OrderBy(x => x.Code)
            .Select(x => new { x.Id, x.Code, x.IdentifierMode, x.Gs1CompanyPrefix }).ToListAsync(),
        printers = await db.Printers.AsNoTracking().OrderBy(x => x.Code)
            .Select(x => new { x.Id, x.Code, x.Name, x.Transport, x.Endpoint, x.IsEnabled }).ToListAsync()
    });

    private static async Task<IResult> CreateProfile(PrintProfileRequest request, UnitAtlasDb db, ITenantContext tenant)
    {
        var code = request.Code?.Trim();
        var mode = request.IdentifierMode?.Trim().ToUpperInvariant();
        var prefix = request.Gs1CompanyPrefix?.Trim();
        if (string.IsNullOrWhiteSpace(code) || !LabelPayloads.IdentifierModes.Contains(mode, StringComparer.Ordinal))
            return Validation("profile", "Code and identifierMode INTERNAL or GS1 are required.");
        if (mode == "GS1" && !LabelPayloads.ValidGs1Prefix(prefix))
            return Validation("gs1CompanyPrefix", "GS1 mode requires a licensed 6-12 digit GS1 Company Prefix.");
        if (mode == "INTERNAL" && !string.IsNullOrWhiteSpace(prefix))
            return Validation("gs1CompanyPrefix", "INTERNAL mode must not contain a GS1 Company Prefix.");

        var now = DateTimeOffset.UtcNow;
        var profile = new PrintProfile
        {
            Id = Guid.CreateVersion7(), TenantId = tenant.TenantId, Code = code, IdentifierMode = mode!,
            Gs1CompanyPrefix = mode == "GS1" ? prefix : null, CreatedAt = now
        };
        db.AddRange(profile, Audit(tenant, "print_profile.created", "PrintProfile", profile.Id, new { profile.Code, profile.IdentifierMode }, now));
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException exception) when (Unique(exception))
        { return Problem("PRINT_PROFILE_EXISTS", "Print profile already exists.", 409); }
        return Results.Created($"/api/v1/print-profiles/{profile.Id}", new { profile.Id, profile.Code, profile.IdentifierMode, profile.Gs1CompanyPrefix });
    }

    private static async Task<IResult> CreatePrinter(PrinterRequest request, UnitAtlasDb db, ITenantContext tenant)
    {
        var code = request.Code?.Trim();
        var name = request.Name?.Trim();
        var transport = request.Transport?.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(transport))
            return Validation("printer", "Code, name and transport are required.");
        var now = DateTimeOffset.UtcNow;
        var printer = new Printer
        {
            Id = Guid.CreateVersion7(), TenantId = tenant.TenantId, Code = code, Name = name,
            Transport = transport, Endpoint = request.Endpoint?.Trim(), IsEnabled = true, CreatedAt = now
        };
        db.AddRange(printer, Audit(tenant, "printer.created", "Printer", printer.Id, new { printer.Code, printer.Transport }, now));
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException exception) when (Unique(exception))
        { return Problem("PRINTER_EXISTS", "Printer already exists.", 409); }
        return Results.Created($"/api/v1/printers/{printer.Id}", new { printer.Id, printer.Code, printer.Name, printer.Transport, printer.Endpoint, printer.IsEnabled });
    }

    private static async Task<IResult> ListJobs(string? status, Guid? printerId, int? limit, UnitAtlasDb db)
    {
        var take = Math.Clamp(limit ?? 50, 1, 100);
        var normalizedStatus = status?.Trim().ToUpperInvariant();
        var query = db.PrintJobs.AsNoTracking()
            .Where(x => (normalizedStatus == null || x.Status == normalizedStatus) && (printerId == null || x.PrinterId == printerId));
        return Results.Ok(await query.OrderBy(x => x.CreatedAt).Take(take)
            .Select(x => new { x.Id, x.TemplateId, x.ProfileId, x.PrinterId, x.Status, x.CreatedAt, x.DispatchedAt, x.PrintedAt })
            .ToListAsync());
    }

    private static async Task<IResult> CreateJob(PrintJobRequest request, UnitAtlasDb db, ITenantContext tenant)
    {
        var entityType = request.EntityType?.Trim().ToUpperInvariant();
        var code = request.Code?.Trim();
        var key = request.IdempotencyKey?.Trim();
        if (!LabelPayloads.EntityTypes.Contains(entityType, StringComparer.Ordinal) || string.IsNullOrWhiteSpace(code)
            || string.IsNullOrWhiteSpace(key) || request.Copies is < 1 or > 100)
            return Validation("printJob", "entityType UNIT or LOGISTIC_UNIT, code, idempotencyKey and 1-100 copies are required.");

        var hash = LabelPayloads.RequestHash(request);
        var existing = await db.PrintJobs.AsNoTracking().SingleOrDefaultAsync(x => x.IdempotencyKey == key);
        if (existing is not null) return Replay(existing, hash);

        var template = await db.LabelTemplates.SingleOrDefaultAsync(x => x.Id == request.TemplateId);
        var profile = await db.PrintProfiles.SingleOrDefaultAsync(x => x.Id == request.ProfileId);
        var printer = await db.Printers.SingleOrDefaultAsync(x => x.Id == request.PrinterId);
        if (template is null || profile is null || printer is null)
            return Problem("PRINT_SETUP_NOT_FOUND", "Template, profile or printer was not found.", 404);
        if (!printer.IsEnabled) return Problem("PRINTER_DISABLED", "Printer is disabled.", 409);
        if (template.EntityType != entityType || template.IdentifierMode != profile.IdentifierMode)
            return Problem("PRINT_SETUP_MISMATCH", "Template, profile and entity type do not match.", 409);

        Guid entityId;
        LabelPayload? payload;
        if (entityType == "UNIT")
        {
            var unit = await db.Units.Include(x => x.Product).SingleOrDefaultAsync(x => x.AtlasId == code);
            if (unit is null) return Problem("UNIT_NOT_FOUND", "Unit not found.", 404);
            entityId = unit.Id;
            payload = profile.IdentifierMode == "INTERNAL"
                ? LabelPayloads.Internal(entityType, unit.AtlasId)
                : LabelPayloads.TryGs1Unit(unit.Product.Gtin, unit.Lot, unit.Serial, profile.Gs1CompanyPrefix!, out var gs1) ? gs1 : null;
        }
        else
        {
            var logistic = await db.LogisticUnits.SingleOrDefaultAsync(x => x.Code == code);
            if (logistic is null) return Problem("LOGISTIC_UNIT_NOT_FOUND", "Logistic unit not found.", 404);
            entityId = logistic.Id;
            payload = profile.IdentifierMode == "INTERNAL"
                ? LabelPayloads.Internal(entityType, logistic.Code)
                : LabelPayloads.TryGs1Logistic(logistic.Sscc, profile.Gs1CompanyPrefix!, out var gs1) ? gs1 : null;
        }
        if (payload is null)
            return Problem("GS1_IDENTIFIER_INVALID", "The entity identifier is not valid for the configured GS1 Company Prefix.", 422);

        var now = DateTimeOffset.UtcNow;
        var job = new PrintJob
        {
            Id = Guid.CreateVersion7(), TenantId = tenant.TenantId, TemplateId = template.Id, ProfileId = profile.Id,
            PrinterId = printer.Id, Status = "PENDING", IdempotencyKey = key, RequestHash = hash,
            CreatedBy = tenant.UserSubject, CreatedAt = now
        };
        var item = new PrintJobItem
        {
            Id = Guid.CreateVersion7(), TenantId = tenant.TenantId, PrintJobId = job.Id, EntityType = entityType!,
            EntityId = entityId, Code = code, Payload = payload.Encoded, HumanReadable = payload.HumanReadable, Copies = request.Copies
        };
        db.AddRange(job, item,
            Audit(tenant, "print_job.created", "PrintJob", job.Id, new { item.EntityType, item.Code, item.Copies, templateCode = template.Code, profile.IdentifierMode, printerCode = printer.Code }, now),
            Outbox(tenant, job.Id, "print_job.created", new { printJobId = job.Id, item.EntityType, item.EntityId, item.Code, item.Copies }, now));
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException exception) when (Unique(exception))
        {
            db.ChangeTracker.Clear();
            var winner = await db.PrintJobs.AsNoTracking().SingleAsync(x => x.IdempotencyKey == key);
            return Replay(winner, hash);
        }
        return Results.Created($"/api/v1/print-jobs/{job.Id}", new { job.Id, job.Status, duplicate = false });
    }

    private static async Task<IResult> GetJob(Guid id, UnitAtlasDb db)
    {
        var job = await db.PrintJobs.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
        if (job is null) return Problem("PRINT_JOB_NOT_FOUND", "Print job not found.", 404);
        var items = await db.PrintJobItems.AsNoTracking().Where(x => x.PrintJobId == id)
            .Select(x => new { x.Id, x.EntityType, x.EntityId, x.Code, x.Payload, x.HumanReadable, x.Copies }).ToListAsync();
        var attempts = await db.PrintAttempts.AsNoTracking().Where(x => x.PrintJobId == id).OrderBy(x => x.CreatedAt)
            .Select(x => new { x.Id, x.Status, x.Error, x.CreatedAt }).ToListAsync();
        return Results.Ok(new { job.Id, job.TemplateId, job.ProfileId, job.PrinterId, job.Status, job.CreatedBy, job.CreatedAt, job.DispatchedAt, job.PrintedAt, items, attempts });
    }

    private static async Task<IResult> RecordAttempt(Guid id, PrintAttemptRequest request, UnitAtlasDb db, ITenantContext tenant)
    {
        var status = request.Status?.Trim().ToUpperInvariant();
        if (!LabelPayloads.AttemptStatuses.Contains(status, StringComparer.Ordinal))
            return Validation("status", "Allowed: DISPATCHED, PRINTED, FAILED.");
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM print_jobs WHERE \"Id\" = {id} FOR UPDATE");
        var job = await db.PrintJobs.SingleOrDefaultAsync(x => x.Id == id);
        if (job is null) { await transaction.RollbackAsync(); return Problem("PRINT_JOB_NOT_FOUND", "Print job not found.", 404); }
        var valid = status == "DISPATCHED" ? job.Status is "PENDING" or "FAILED" : job.Status == "DISPATCHED";
        if (!valid) { await transaction.RollbackAsync(); return Problem("PRINT_STATUS_CONFLICT", $"Cannot move print job from {job.Status} to {status}.", 409); }

        var now = DateTimeOffset.UtcNow;
        job.Status = status!;
        if (status == "DISPATCHED") job.DispatchedAt = now;
        if (status == "PRINTED") job.PrintedAt = now;
        var attempt = new PrintAttempt
        {
            Id = Guid.CreateVersion7(), TenantId = tenant.TenantId, PrintJobId = job.Id,
            Status = status!, Error = status == "FAILED" ? request.Error?.Trim() : null, CreatedAt = now
        };
        db.AddRange(attempt, Audit(tenant, $"print_job.{status!.ToLowerInvariant()}", "PrintJob", job.Id, new { attempt.Status, attempt.Error }, now));
        if (status is "PRINTED" or "FAILED") db.OutboxMessages.Add(Outbox(tenant, job.Id, $"print_job.{status.ToLowerInvariant()}", new { printJobId = job.Id, attempt.Status, attempt.Error }, now));
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return Results.Created($"/api/v1/print-jobs/{job.Id}", new { attemptId = attempt.Id, printJobId = job.Id, job.Status });
    }

    private static AuditEntry Audit(ITenantContext tenant, string action, string type, Guid id, object data, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(), TenantId = tenant.TenantId, ActorSubject = tenant.UserSubject, Action = action,
        EntityType = type, EntityId = id, DataJson = JsonSerializer.Serialize(data), CreatedAt = now
    };

    private static OutboxMessage Outbox(ITenantContext tenant, Guid id, string type, object payload, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(), TenantId = tenant.TenantId, CorrelationId = id, Source = "unitatlas", Type = type,
        SubjectType = "PrintJob", SubjectId = id.ToString(), PayloadJson = JsonSerializer.Serialize(payload), CreatedAt = now
    };

    private static IResult Replay(PrintJob job, string hash) => job.RequestHash == hash
        ? Results.Json(new { job.Id, job.Status, duplicate = true }, statusCode: 201)
        : Problem("IDEMPOTENCY_KEY_REUSED", "Idempotency key reused with a different print request.", 409);
    private static bool Unique(DbUpdateException exception) => exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
    private static IResult Validation(string key, string message) => Results.ValidationProblem(new Dictionary<string, string[]> { [key] = [message] });
    private static IResult Problem(string code, string title, int status) => Results.Problem(statusCode: status, title: title, extensions: new Dictionary<string, object?> { ["code"] = code });
}
