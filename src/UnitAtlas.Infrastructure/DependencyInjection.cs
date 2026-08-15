using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UnitAtlas.Application.Tenancy;
using UnitAtlas.Infrastructure.Persistence;

namespace UnitAtlas.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ITenantContext, TenantContext>();
        services.AddScoped<TenantConnectionInterceptor>();
        services.AddDbContext<UnitAtlasDb>((provider, options) => options
            .UseNpgsql(configuration.GetConnectionString("Default"))
            .AddInterceptors(provider.GetRequiredService<TenantConnectionInterceptor>()));
        return services;
    }
}
