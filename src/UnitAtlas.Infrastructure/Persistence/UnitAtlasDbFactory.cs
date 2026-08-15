using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using UnitAtlas.Application.Tenancy;

namespace UnitAtlas.Infrastructure.Persistence;

public sealed class UnitAtlasDbFactory : IDesignTimeDbContextFactory<UnitAtlasDb>
{
    public UnitAtlasDb CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Default")
            ?? "Host=localhost;Database=unitatlas;Username=unitatlas;Password=unitatlas_dev";
        return new UnitAtlasDb(new DbContextOptionsBuilder<UnitAtlasDb>().UseNpgsql(connectionString).Options, new TenantContext());
    }
}
