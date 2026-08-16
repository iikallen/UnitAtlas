using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using UnitAtlas.Application.Integrations;
using UnitAtlas.Application.Tenancy;
using UnitAtlas.Contracts;
using UnitAtlas.Domain;
using UnitAtlas.Infrastructure.Persistence;

namespace UnitAtlas.Infrastructure.Integrations;

public sealed class IntegrationDispatcher(
    IServiceScopeFactory scopeFactory,
    IEnumerable<IIntegrationAdapter> adapters,
    IConfiguration configuration,
    ILogger<IntegrationDispatcher> logger) : BackgroundService
{
    private readonly IReadOnlyDictionary<string, IIntegrationAdapter> adapters = adapters
        .ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase);
    private readonly int maxAttempts = configuration.GetValue("Integrations:MaxAttempts", 8);
    private readonly TimeSpan leaseDuration = TimeSpan.FromSeconds(configuration.GetValue("Integrations:LeaseSeconds", 30));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await DispatchCycleAsync(stoppingToken); }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(exception, "Integration dispatch cycle failed");
            }
            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
        }
    }

    private async Task DispatchCycleAsync(CancellationToken cancellationToken)
    {
        Guid[] tenants;
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<UnitAtlasDb>();
            tenants = await db.Tenants.AsNoTracking().Select(x => x.Id).ToArrayAsync(cancellationToken);
        }

        foreach (var tenantId in tenants)
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
            tenant.Initialize(tenantId, "integration-dispatcher");
            var db = scope.ServiceProvider.GetRequiredService<UnitAtlasDb>();
            await EnsureFanoutAsync(db, cancellationToken);
            while (await LeaseAndDeliverAsync(db, cancellationToken)) { }
        }
    }

    private static Task<int> EnsureFanoutAsync(UnitAtlasDb db, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO integration_deliveries
                ("Id", "TenantId", "OutboxMessageId", "IntegrationEndpointId", "Status", "AttemptCount",
                 "NextAttemptAt", "CreatedAt")
            SELECT gen_random_uuid(), o."TenantId", o."Id", e."Id", 'Pending', 0, now(), now()
            FROM outbox_messages o
            JOIN integration_endpoints e ON e."TenantId" = o."TenantId"
            WHERE e."Enabled" AND o."CreatedAt" >= e."CreatedAt"
            ON CONFLICT ("TenantId", "OutboxMessageId", "IntegrationEndpointId") DO NOTHING
            """, cancellationToken);

    private async Task<bool> LeaseAndDeliverAsync(UnitAtlasDb db, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var token = Guid.NewGuid();
        var leased = await db.Database.ExecuteSqlInterpolatedAsync($$"""
            WITH candidate AS (
                SELECT "Id" FROM integration_deliveries
                WHERE (("Status" IN ('Pending', 'Retry') AND "NextAttemptAt" <= {{now}})
                    OR ("Status" = 'Delivering' AND "LeaseUntil" < {{now}}))
                ORDER BY "NextAttemptAt", "CreatedAt"
                FOR UPDATE SKIP LOCKED LIMIT 1
            )
            UPDATE integration_deliveries d
            SET "Status" = 'Delivering', "AttemptCount" = d."AttemptCount" + 1,
                "LastAttemptAt" = {{now}}, "LeaseUntil" = {{now.Add(leaseDuration)}}, "LeaseToken" = {{token}}
            FROM candidate WHERE d."Id" = candidate."Id"
            """, cancellationToken);
        if (leased == 0) return false;

        var delivery = await db.IntegrationDeliveries.AsNoTracking().SingleAsync(x => x.LeaseToken == token, cancellationToken);
        var message = await db.OutboxMessages.AsNoTracking().SingleAsync(x => x.Id == delivery.OutboxMessageId, cancellationToken);
        var endpoint = await db.IntegrationEndpoints.AsNoTracking().SingleAsync(x => x.Id == delivery.IntegrationEndpointId, cancellationToken);

        IntegrationSendResult result;
        if (!adapters.TryGetValue(endpoint.Adapter, out var adapter))
            result = new(false, false, "ADAPTER_NOT_FOUND");
        else
        {
            using var payload = JsonDocument.Parse(message.PayloadJson);
            var envelope = new WebhookEnvelope(
                "1.0", message.Id, message.CorrelationId, message.Source, message.Type, message.CreatedAt,
                new WebhookSubject(message.SubjectType, message.SubjectId), payload.RootElement.Clone());
            result = await adapter.SendAsync(
                new(endpoint.Adapter, endpoint.BaseAddress, endpoint.SecretRef, endpoint.SettingsJson), envelope, cancellationToken);
        }

        await CompleteAsync(db, delivery, token, result, cancellationToken);
        return true;
    }

    private async Task CompleteAsync(UnitAtlasDb db, IntegrationDelivery delivery, Guid token,
        IntegrationSendResult result, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var status = result.Delivered
            ? IntegrationDeliveryStatus.Delivered
            : result.Retryable && delivery.AttemptCount < maxAttempts
                ? IntegrationDeliveryStatus.Retry
                : IntegrationDeliveryStatus.DeadLetter;
        var retryAt = result.RetryAt ?? now.Add(Backoff(delivery.AttemptCount));

        await db.IntegrationDeliveries.Where(x => x.Id == delivery.Id && x.LeaseToken == token)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, status)
                .SetProperty(x => x.NextAttemptAt, retryAt)
                .SetProperty(x => x.DeliveredAt, result.Delivered ? now : null)
                .SetProperty(x => x.LeaseUntil, (DateTimeOffset?)null)
                .SetProperty(x => x.LeaseToken, (Guid?)null)
                .SetProperty(x => x.LastErrorCode, result.ErrorCode), cancellationToken);
    }

    private static TimeSpan Backoff(int attempt)
    {
        var seconds = Math.Min(300, Math.Pow(2, Math.Min(attempt, 8)));
        return TimeSpan.FromSeconds(seconds + Random.Shared.NextDouble() * Math.Min(10, seconds / 4));
    }
}
