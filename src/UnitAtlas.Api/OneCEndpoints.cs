using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using UnitAtlas.Application.Integrations;
using UnitAtlas.Application.Printing;
using UnitAtlas.Application.Tenancy;
using UnitAtlas.Application.Traceability;
using UnitAtlas.Api.Observability;
using UnitAtlas.Domain;
using UnitAtlas.Infrastructure.Persistence;

namespace UnitAtlas.Api;

public static class OneCEndpoints
{
    public static WebApplication MapOneCEndpoints(this WebApplication app)
    {
        app.MapPost("/api/v1/integration-inbox/{system}/1c", ReceiveAsync)
            .RequireAuthorization(Permissions.IntegrationsManage);
        return app;
    }

    private static async Task<IResult> ReceiveAsync(
        string system, HttpRequest request, UnitAtlasDb db, ITenantContext tenant)
    {
        var source = system.Trim().ToUpperInvariant();
        var endpoint = await db.IntegrationEndpoints.AsNoTracking()
            .SingleOrDefaultAsync(x => x.System == source && x.Adapter == "ONE_C" && x.Enabled);
        if (endpoint is null) return Problem("INTEGRATION_ENDPOINT_NOT_FOUND", "Enabled ONE_C endpoint not found.", 404);

        var externalMessageId = (request.Headers["X-External-Message-Id"].FirstOrDefault()
            ?? request.Headers["Idempotency-Key"].FirstOrDefault() ?? "").Trim();
        if (!Valid(externalMessageId, 200))
            return Problem("EXTERNAL_MESSAGE_ID_REQUIRED", "X-External-Message-Id is required.", 400);

        JsonDocument document;
        try { document = await JsonDocument.ParseAsync(request.Body, cancellationToken: request.HttpContext.RequestAborted); }
        catch (JsonException) { return Problem("INVALID_JSON", "Request body must be valid JSON.", 400); }
        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !String(root, "type", out var type)
                || !root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
                return Problem("INVALID_ONE_C_MESSAGE", "type and object data are required.", 400);

            var payload = root.GetRawText();
            var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
            var existing = await db.InboxMessages.AsNoTracking()
                .SingleOrDefaultAsync(x => x.SourceSystem == source && x.ExternalMessageId == externalMessageId);
            if (existing is not null) return Existing(existing, hash);

            await using var transaction = await db.Database.BeginTransactionAsync();
            try
            {
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT pg_advisory_xact_lock(hashtextextended({tenant.TenantId + ":" + source}, 3))");
                existing = await db.InboxMessages.AsNoTracking()
                    .SingleOrDefaultAsync(x => x.SourceSystem == source && x.ExternalMessageId == externalMessageId);
                if (existing is not null)
                {
                    await transaction.RollbackAsync();
                    return Existing(existing, hash);
                }

                var inboxId = Guid.CreateVersion7();
                var outcome = type.ToLowerInvariant() switch
                {
                    "product.upsert" => await UpsertProductAsync(data, source, tenant, db),
                    "production.completed" => await CompleteProductionAsync(data, source, tenant, db, inboxId),
                    "production_order.completed" when OneCPilotProfile.IsSelected(endpoint.SettingsJson)
                        => await CompleteProductionOrderAsync(data, source, tenant, db, inboxId, hash),
                    "production_order.completed" => Operation.Error("ONE_C_PROFILE_REQUIRED", $"Endpoint profile must be {OneCPilotProfile.Code}.", 409),
                    "shipment.recorded" => await RecordMovementAsync(data, source, tenant, db, inboxId, "SHIPPED"),
                    "receipt.recorded" => await RecordMovementAsync(data, source, tenant, db, inboxId, "RECEIVED"),
                    _ => Operation.Error("ONE_C_MESSAGE_NOT_SUPPORTED", "Supported types: product.upsert, production.completed, production_order.completed, shipment.recorded, receipt.recorded.", 400)
                };
                if (outcome.ErrorCode is not null)
                {
                    await transaction.RollbackAsync();
                    return Problem(outcome.ErrorCode, outcome.Detail!, outcome.Status);
                }

                var resultJson = JsonSerializer.Serialize(outcome.Value);
                db.InboxMessages.Add(new InboxMessage
                {
                    Id = inboxId, TenantId = tenant.TenantId, IntegrationEndpointId = endpoint.Id,
                    SourceSystem = source, ExternalMessageId = externalMessageId, PayloadHash = hash,
                    PayloadJson = payload, ResultJson = resultJson, ReceivedAt = DateTimeOffset.UtcNow
                });
                await db.SaveChangesAsync();
                await transaction.CommitAsync();
                return Results.Json(JsonSerializer.Deserialize<JsonElement>(resultJson), statusCode: 201);
            }
            catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                await transaction.RollbackAsync();
                return Problem("ONE_C_REFERENCE_CONFLICT", "A 1C reference, SKU, GTIN or serial is already assigned.", 409);
            }
        }
    }

    private static async Task<Operation> UpsertProductAsync(
        JsonElement data, string source, ITenantContext tenant, UnitAtlasDb db)
    {
        if (!Fields(data, out var externalId, out var sku, out var name, out var gtin,
                "externalId", "sku", "name", "gtin")
            || !Valid(externalId, 200) || !Valid(sku, 100) || !Valid(name, 200)
            || gtin.Length is < 8 or > 14 || !gtin.All(char.IsDigit))
            return Operation.Error("INVALID_ONE_C_PRODUCT", "externalId, sku, name and an 8-14 digit GTIN are required.", 400);

        var reference = await db.ExternalReferences.SingleOrDefaultAsync(
            x => x.System == source && x.EntityType == "Product" && x.Value == externalId);
        var product = reference is null ? null : await db.Products.SingleOrDefaultAsync(x => x.Id == reference.EntityId);
        if (reference is not null && product is null)
            return Operation.Error("ONE_C_REFERENCE_BROKEN", "The product external reference is invalid.", 409);

        var created = product is null;
        if (product is null)
        {
            product = new Product
            {
                Id = Guid.CreateVersion7(), TenantId = tenant.TenantId, Sku = sku, Name = name,
                Gtin = gtin, CreatedAt = DateTimeOffset.UtcNow
            };
            db.AddRange(product,
                new ProductIdentifier { Id = Guid.CreateVersion7(), TenantId = tenant.TenantId, ProductId = product.Id, Type = "SKU", Value = sku },
                new ProductIdentifier { Id = Guid.CreateVersion7(), TenantId = tenant.TenantId, ProductId = product.Id, Type = "GTIN", Value = gtin },
                new ExternalReference { Id = Guid.CreateVersion7(), TenantId = tenant.TenantId, System = source, EntityType = "Product", EntityId = product.Id, Value = externalId });
        }
        else
        {
            product.Sku = sku;
            product.Name = name;
            product.Gtin = gtin;
            var identifiers = await db.ProductIdentifiers.Where(x => x.ProductId == product.Id && (x.Type == "SKU" || x.Type == "GTIN")).ToListAsync();
            identifiers.Single(x => x.Type == "SKU").Value = sku;
            identifiers.Single(x => x.Type == "GTIN").Value = gtin;
        }

        db.AuditEntries.Add(Audit(tenant, "one_c.product.upserted", "Product", product.Id,
            new { source, externalId, sku, gtin, created }));
        return Operation.Ok(new { entityType = "Product", entityId = product.Id, externalId, created });
    }

    private static async Task<Operation> CompleteProductionAsync(
        JsonElement data, string source, ITenantContext tenant, UnitAtlasDb db, Guid correlationId)
    {
        if (!Fields(data, out var externalId, out var productExternalId, out var serial, out var lotCode,
                "externalId", "productExternalId", "serial", "lot")
            || !Valid(externalId, 200) || !Valid(productExternalId, 200)
            || !Valid(serial, 200) || !Valid(lotCode, 120))
            return Operation.Error("INVALID_ONE_C_PRODUCTION", "externalId, productExternalId, serial and lot are required.", 400);
        if (!OccurredAt(data, out var occurredAt))
            return Operation.Error("INVALID_ONE_C_PRODUCTION", "occurredAt must be an ISO-8601 timestamp.", 400);

        var existing = await db.ExternalReferences.AsNoTracking().SingleOrDefaultAsync(
            x => x.System == source && x.EntityType == "TrackedUnit" && x.Value == externalId);
        if (existing is not null)
        {
            var prior = await db.Units.AsNoTracking().SingleAsync(x => x.Id == existing.EntityId);
            return Operation.Ok(new { entityType = "TrackedUnit", entityId = prior.Id, atlasId = prior.AtlasId, externalId, created = false });
        }
        var productReference = await db.ExternalReferences.AsNoTracking().SingleOrDefaultAsync(
            x => x.System == source && x.EntityType == "Product" && x.Value == productExternalId);
        if (productReference is null)
            return Operation.Error("ONE_C_PRODUCT_NOT_FOUND", "productExternalId was not found.", 404);
        var product = await db.Products.SingleAsync(x => x.Id == productReference.EntityId);
        var now = DateTimeOffset.UtcNow;
        var lot = await db.Lots.SingleOrDefaultAsync(x => x.ProductId == product.Id && x.Code == lotCode);
        if (lot is null)
        {
            lot = new Lot { Id = Guid.CreateVersion7(), TenantId = tenant.TenantId, ProductId = product.Id, Code = lotCode, ManufacturedAt = occurredAt };
            db.Lots.Add(lot);
        }
        var unit = new TrackedUnit
        {
            Id = Guid.CreateVersion7(), TenantId = tenant.TenantId, ProductId = product.Id,
            AtlasId = $"UA-KZ-{now:yyyy}-{Random.Shared.NextInt64(0, 10_000_000_000):D10}",
            Serial = serial, Lot = lotCode, LotId = lot.Id, ManufacturedAt = occurredAt, CreatedAt = now
        };
        var trace = Trace(unit, "MANUFACTURED", source, occurredAt, 1, $"1c:{source}:{externalId}:manufactured", correlationId);
        trace.SourceSystem = source;
        db.AddRange(unit, trace,
            new UnitState { UnitId = unit.Id, TenantId = tenant.TenantId, Status = "Manufactured", Location = source,
                LastEventId = trace.Id, CurrentOccurredAt = occurredAt, CurrentSequence = 1, UpdatedAt = now },
            new UnitIdentifier { Id = Guid.CreateVersion7(), TenantId = tenant.TenantId, UnitId = unit.Id, Type = "ATLAS_ID", Value = unit.AtlasId },
            new UnitIdentifier { Id = Guid.CreateVersion7(), TenantId = tenant.TenantId, UnitId = unit.Id, Type = "SERIAL", Value = serial },
            new PublicPassportConfig { UnitId = unit.Id, TenantId = tenant.TenantId, PublicId = Guid.NewGuid().ToString("N"), IsPublished = false },
            new ExternalReference { Id = Guid.CreateVersion7(), TenantId = tenant.TenantId, System = source, EntityType = "TrackedUnit", EntityId = unit.Id, Value = externalId },
            Audit(tenant, "one_c.production.completed", "TrackedUnit", unit.Id, new { source, externalId, productExternalId, unit.AtlasId, serial, lot = lotCode }),
            Outbox(tenant.TenantId, correlationId, "unit.created", "TrackedUnit", unit.Id,
                new { unit.Id, unit.AtlasId, unit.ProductId, unit.Serial, unit.Lot, unit.ManufacturedAt }),
            Outbox(tenant.TenantId, correlationId, "trace_event.recorded", "TraceEvent", trace.Id,
                new { trace.Id, unit.AtlasId, trace.EventType, trace.Location, trace.OccurredAt, trace.SourceSystem }));
        return Operation.Ok(new { entityType = "TrackedUnit", entityId = unit.Id, atlasId = unit.AtlasId, externalId, created = true });
    }

    private static async Task<Operation> CompleteProductionOrderAsync(
        JsonElement data, string source, ITenantContext tenant, UnitAtlasDb db, Guid correlationId, string requestHash)
    {
        if (!Fields(data, out var externalId, out var productExternalId, out var lotCode, out var serialPrefix,
                "externalId", "productExternalId", "lot", "serialPrefix")
            || !Valid(externalId, 200) || !Valid(productExternalId, 200) || !Valid(lotCode, 20)
            || !Valid(serialPrefix, 15) || !Integer(data, "quantity", out var quantity) || quantity is < 1 or > 1000)
            return Operation.Error("INVALID_ONE_C_PRODUCTION_ORDER", "externalId, productExternalId, lot (max 20), serialPrefix (max 15) and quantity 1-1000 are required.", 400);
        if (!OccurredAt(data, out var occurredAt))
            return Operation.Error("INVALID_ONE_C_PRODUCTION_ORDER", "occurredAt must be an ISO-8601 timestamp.", 400);
        if (!data.TryGetProperty("label", out var label) || label.ValueKind != JsonValueKind.Object
            || !GuidField(label, "templateId", out var templateId)
            || !GuidField(label, "profileId", out var profileId)
            || !GuidField(label, "printerId", out var printerId))
            return Operation.Error("INVALID_ONE_C_PRODUCTION_ORDER", "label.templateId, label.profileId and label.printerId are required.", 400);

        var existingOrder = await db.ExternalReferences.AsNoTracking().SingleOrDefaultAsync(
            x => x.System == source && x.EntityType == "Lot" && x.Value == externalId);
        if (existingOrder is not null)
        {
            var priorUnits = await db.Units.AsNoTracking().Where(x => x.LotId == existingOrder.EntityId).OrderBy(x => x.Serial).ToListAsync();
            var priorJob = await db.PrintJobs.AsNoTracking().SingleOrDefaultAsync(x => x.IdempotencyKey == $"1c:{source}:production-order:{externalId}:labels");
            if (priorJob is null)
                return Operation.Error("ONE_C_REFERENCE_BROKEN", "The production order external reference is invalid.", 409);
            if (!CryptographicOperations.FixedTimeEquals(
                    Convert.FromHexString(priorJob.RequestHash), Convert.FromHexString(requestHash)))
                return Operation.Error("ONE_C_PRODUCTION_ORDER_CONFLICT", "Production Order was already completed with a different payload.", 409);
            return Operation.Ok(new
            {
                entityType = "ProductionOrder", productionBatchId = existingOrder.EntityId, externalId,
                quantity = priorUnits.Count, printJobId = priorJob.Id,
                units = priorUnits.Select(x => new { id = x.Id, atlasId = x.AtlasId, serial = x.Serial }), created = false
            });
        }

        var productReference = await db.ExternalReferences.AsNoTracking().SingleOrDefaultAsync(
            x => x.System == source && x.EntityType == "Product" && x.Value == productExternalId);
        if (productReference is null)
            return Operation.Error("ONE_C_PRODUCT_NOT_FOUND", "productExternalId was not found.", 404);
        var product = await db.Products.SingleAsync(x => x.Id == productReference.EntityId);
        var template = await db.LabelTemplates.SingleOrDefaultAsync(x => x.Id == templateId);
        var profile = await db.PrintProfiles.SingleOrDefaultAsync(x => x.Id == profileId);
        var printer = await db.Printers.SingleOrDefaultAsync(x => x.Id == printerId);
        if (template is null || profile is null || printer is null)
            return Operation.Error("PRINT_SETUP_NOT_FOUND", "Template, profile or printer was not found.", 404);
        if (!printer.IsEnabled || template.EntityType != "UNIT" || template.Symbology != "GS1_DATA_MATRIX"
            || template.IdentifierMode != "GS1" || profile.IdentifierMode != "GS1")
            return Operation.Error("PILOT_PRINT_SETUP_MISMATCH", "The pilot requires an enabled printer and a GS1 Data Matrix unit template/profile.", 409);

        var lot = await db.Lots.SingleOrDefaultAsync(x => x.ProductId == product.Id && x.Code == lotCode);
        if (lot is null)
        {
            lot = new Lot
            {
                Id = Guid.CreateVersion7(), TenantId = tenant.TenantId, ProductId = product.Id,
                Code = lotCode, ManufacturedAt = occurredAt
            };
            db.Lots.Add(lot);
        }

        var now = DateTimeOffset.UtcNow;
        var printJob = new PrintJob
        {
            Id = Guid.CreateVersion7(), TenantId = tenant.TenantId, TemplateId = template.Id,
            ProfileId = profile.Id, PrinterId = printer.Id, Status = "PENDING",
            IdempotencyKey = $"1c:{source}:production-order:{externalId}:labels", RequestHash = requestHash,
            CreatedBy = tenant.UserSubject, CreatedAt = now
        };
        var units = new List<TrackedUnit>(quantity);
        var entities = new List<object>(quantity * 9 + 5) { printJob };
        for (var index = 1; index <= quantity; index++)
        {
            var serial = $"{serialPrefix}-{index:D4}";
            if (!LabelPayloads.TryGs1Unit(product.Gtin, lotCode, serial, profile.Gs1CompanyPrefix!, out var labelPayload))
                return Operation.Error("GS1_IDENTIFIER_INVALID", $"Unit {index} cannot be encoded with the configured GS1 Company Prefix.", 422);
            var unit = new TrackedUnit
            {
                Id = Guid.CreateVersion7(), TenantId = tenant.TenantId, ProductId = product.Id,
                AtlasId = $"UA-KZ-{now:yyyy}-{Guid.NewGuid():N}", Serial = serial, Lot = lotCode,
                LotId = lot.Id, ManufacturedAt = occurredAt, CreatedAt = now
            };
            var trace = Trace(unit, "MANUFACTURED", source, occurredAt, 1,
                $"1c:{source}:{externalId}:{index}:manufactured", correlationId);
            trace.SourceSystem = source;
            units.Add(unit);
            entities.AddRange([
                unit, trace,
                new UnitState { UnitId = unit.Id, TenantId = tenant.TenantId, Status = "Manufactured", Location = source,
                    LastEventId = trace.Id, CurrentOccurredAt = occurredAt, CurrentSequence = 1, UpdatedAt = now },
                new UnitIdentifier { Id = Guid.CreateVersion7(), TenantId = tenant.TenantId, UnitId = unit.Id, Type = "ATLAS_ID", Value = unit.AtlasId },
                new UnitIdentifier { Id = Guid.CreateVersion7(), TenantId = tenant.TenantId, UnitId = unit.Id, Type = "SERIAL", Value = serial },
                new PublicPassportConfig { UnitId = unit.Id, TenantId = tenant.TenantId, PublicId = Guid.NewGuid().ToString("N"), IsPublished = false },
                new ExternalReference { Id = Guid.CreateVersion7(), TenantId = tenant.TenantId, System = source, EntityType = "TrackedUnit", EntityId = unit.Id, Value = $"{externalId}:{index:D4}" },
                new PrintJobItem { Id = Guid.CreateVersion7(), TenantId = tenant.TenantId, PrintJobId = printJob.Id,
                    EntityType = "UNIT", EntityId = unit.Id, Code = unit.AtlasId, Payload = labelPayload!.Encoded,
                    HumanReadable = labelPayload.HumanReadable, Copies = 1 },
                Outbox(tenant.TenantId, correlationId, "unit.created", "TrackedUnit", unit.Id,
                    new { unit.Id, unit.AtlasId, unit.ProductId, unit.Serial, unit.Lot, unit.ManufacturedAt }),
                Outbox(tenant.TenantId, correlationId, "trace_event.recorded", "TraceEvent", trace.Id,
                    new { trace.Id, unit.AtlasId, trace.EventType, trace.Location, trace.OccurredAt, trace.SourceSystem })
            ]);
        }
        entities.AddRange([
            new ExternalReference { Id = Guid.CreateVersion7(), TenantId = tenant.TenantId, System = source, EntityType = "Lot", EntityId = lot.Id, Value = externalId },
            Audit(tenant, "one_c.production_order.completed", "Lot", lot.Id,
                new { source, externalId, productExternalId, lot = lot.Code, quantity, printJobId = printJob.Id }),
            Outbox(tenant.TenantId, correlationId, "production_order.completed", "Lot", lot.Id,
                new { productionOrderExternalId = externalId, productionBatchId = lot.Id, quantity, printJobId = printJob.Id }),
            Outbox(tenant.TenantId, printJob.Id, "print_job.created", "PrintJob", printJob.Id,
                new { printJobId = printJob.Id, itemCount = quantity })
        ]);
        db.AddRange(entities);
        return Operation.Ok(new
        {
            entityType = "ProductionOrder", productionBatchId = lot.Id, externalId, quantity,
            printJobId = printJob.Id, units = units.Select(x => new { id = x.Id, atlasId = x.AtlasId, serial = x.Serial }), created = true
        });
    }

    private static async Task<Operation> RecordMovementAsync(
        JsonElement data, string source, ITenantContext tenant, UnitAtlasDb db, Guid correlationId, string eventType)
    {
        if (!Fields(data, out var externalId, out var unitExternalId, "externalId", "unitExternalId")
            || !Valid(externalId, 200) || !Valid(unitExternalId, 200))
            return Operation.Error("INVALID_ONE_C_MOVEMENT", "externalId and unitExternalId are required.", 400);
        if (!OccurredAt(data, out var occurredAt))
            return Operation.Error("INVALID_ONE_C_MOVEMENT", "occurredAt must be an ISO-8601 timestamp.", 400);
        var eventReference = await db.ExternalReferences.AsNoTracking().SingleOrDefaultAsync(
            x => x.System == source && x.EntityType == "TraceEvent" && x.Value == externalId);
        if (eventReference is not null)
            return Operation.Ok(new { entityType = "TraceEvent", entityId = eventReference.EntityId, externalId, created = false });
        var unitReference = await db.ExternalReferences.AsNoTracking().SingleOrDefaultAsync(
            x => x.System == source && x.EntityType == "TrackedUnit" && x.Value == unitExternalId);
        if (unitReference is null)
            return Operation.Error("ONE_C_UNIT_NOT_FOUND", "unitExternalId was not found.", 404);
        var unit = await db.Units.SingleAsync(x => x.Id == unitReference.EntityId);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM units WHERE \"Id\" = {unit.Id} FOR UPDATE");
        var sequence = (await db.TraceEvents.Where(x => x.UnitId == unit.Id).MaxAsync(x => (long?)x.Sequence) ?? 0) + 1;
        var location = String(data, "location", out var requestedLocation) && Valid(requestedLocation, 200) ? requestedLocation : source;
        var trace = Trace(unit, eventType, location, occurredAt, sequence, $"1c:{source}:{externalId}", correlationId);
        trace.SourceSystem = source;
        var state = await db.UnitStates.SingleAsync(x => x.UnitId == unit.Id);
        TraceEventProjection.TryGetStatus(eventType, out var status);
        TraceEventProjection.Apply(state, trace, status);
        db.AddRange(trace,
            new ExternalReference { Id = Guid.CreateVersion7(), TenantId = tenant.TenantId, System = source, EntityType = "TraceEvent", EntityId = trace.Id, Value = externalId },
            Audit(tenant, $"one_c.{eventType.ToLowerInvariant()}", "TraceEvent", trace.Id, new { source, externalId, unitExternalId, unit.AtlasId, eventType, occurredAt }),
            Outbox(tenant.TenantId, correlationId, "trace_event.recorded", "TraceEvent", trace.Id,
                new { trace.Id, unit.AtlasId, trace.EventType, trace.Location, trace.OccurredAt, trace.SourceSystem }),
            Outbox(tenant.TenantId, correlationId, eventType == "SHIPPED" ? "shipment.recorded" : "receipt.recorded",
                "TrackedUnit", unit.Id,
                new { traceId = trace.Id, unitId = unit.Id, unit.AtlasId, externalId, unitExternalId, trace.Location, trace.OccurredAt, trace.SourceSystem }));
        return Operation.Ok(new { entityType = "TraceEvent", entityId = trace.Id, externalId, created = true });
    }

    private static TraceEvent Trace(TrackedUnit unit, string eventType, string location,
        DateTimeOffset occurredAt, long sequence, string idempotencyKey, Guid correlationId) => new()
    {
        Id = Guid.CreateVersion7(), TenantId = unit.TenantId, UnitId = unit.Id, EventType = eventType,
        OccurredAt = occurredAt, RecordedAt = DateTimeOffset.UtcNow, Sequence = sequence, Location = location,
        Actor = "1c", ActorSubject = "integration", SourceSystem = "1c", IdempotencyKey = idempotencyKey,
        CorrelationId = correlationId
    };

    private static AuditEntry Audit(ITenantContext tenant, string action, string entityType, Guid entityId, object data) => new()
    {
        Id = Guid.CreateVersion7(), TenantId = tenant.TenantId, ActorSubject = tenant.UserSubject,
        Action = action, EntityType = entityType, EntityId = entityId,
        DataJson = JsonSerializer.Serialize(data), CreatedAt = DateTimeOffset.UtcNow
    };

    private static OutboxMessage Outbox(Guid tenantId, Guid correlationId, string type, string subjectType, Guid subjectId, object payload) => new()
    {
        Id = Guid.CreateVersion7(), TenantId = tenantId, CorrelationId = correlationId, Source = "unitatlas",
        Type = type, SubjectType = subjectType, SubjectId = subjectId.ToString(),
        PayloadJson = JsonSerializer.Serialize(payload), CreatedAt = DateTimeOffset.UtcNow
    };

    private static bool OccurredAt(JsonElement data, out DateTimeOffset value)
    {
        value = DateTimeOffset.UtcNow;
        return !data.TryGetProperty("occurredAt", out var property)
            || property.ValueKind == JsonValueKind.Null
            || property.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(property.GetString(), out value);
    }

    private static bool Fields(JsonElement data, out string a, out string b, params string[] names)
    {
        a = b = "";
        return names.Length == 2 && String(data, names[0], out a) && String(data, names[1], out b);
    }

    private static bool Fields(JsonElement data, out string a, out string b, out string c, out string d, params string[] names)
    {
        a = b = c = d = "";
        return names.Length == 4 && String(data, names[0], out a) && String(data, names[1], out b)
            && String(data, names[2], out c) && String(data, names[3], out d);
    }

    private static bool String(JsonElement data, string name, out string value)
    {
        value = "";
        if (!data.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.String) return false;
        value = property.GetString()?.Trim() ?? "";
        return value.Length > 0;
    }

    private static bool Valid(string value, int max) => value.Length is > 0 && value.Length <= max;

    private static bool Integer(JsonElement data, string name, out int value)
    {
        value = 0;
        return data.TryGetProperty(name, out var property) && property.TryGetInt32(out value);
    }

    private static bool GuidField(JsonElement data, string name, out Guid value)
    {
        value = Guid.Empty;
        return data.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            && Guid.TryParse(property.GetString(), out value);
    }

    private static IResult Existing(InboxMessage existing, string hash)
    {
        Telemetry.InboxDuplicates.Add(1, new KeyValuePair<string, object?>("integration.system", existing.SourceSystem));
        return existing.PayloadHash == hash
            ? Results.Json(JsonSerializer.Deserialize<JsonElement>(existing.ResultJson), statusCode: 200)
            : Problem("INBOX_IDEMPOTENCY_CONFLICT", "ExternalMessageId was already used with a different payload.", 409);
    }

    private static IResult Problem(string code, string detail, int status) => Results.Problem(
        statusCode: status, title: code, detail: detail, extensions: new Dictionary<string, object?> { ["code"] = code });

    private sealed record Operation(object? Value, string? ErrorCode, string? Detail, int Status)
    {
        public static Operation Ok(object value) => new(value, null, null, 201);
        public static Operation Error(string code, string detail, int status) => new(null, code, detail, status);
    }
}
