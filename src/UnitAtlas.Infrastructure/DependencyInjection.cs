using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UnitAtlas.Infrastructure.Persistence;

namespace UnitAtlas.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<UnitAtlasDb>(options =>
            options.UseNpgsql(configuration.GetConnectionString("Default")));
        return services;
    }
}
