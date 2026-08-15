using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using UnitAtlas.Application.Tenancy;

namespace UnitAtlas.Infrastructure.Persistence;

public sealed class TenantConnectionInterceptor(ITenantContext tenantContext) : DbConnectionInterceptor
{
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        if (!tenantContext.IsAvailable)
            return;

        using var command = CreateCommand(connection);
        command.ExecuteNonQuery();
    }

    public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        if (!tenantContext.IsAvailable)
            return;

        await using var command = CreateCommand(connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private DbCommand CreateCommand(DbConnection connection)
    {
        var command = connection.CreateCommand();
        command.CommandText = "select set_config('app.current_tenant', @tenant_id, false)";
        command.Parameters.Add(new NpgsqlParameter("tenant_id", tenantContext.TenantId.ToString()));
        return command;
    }
}
