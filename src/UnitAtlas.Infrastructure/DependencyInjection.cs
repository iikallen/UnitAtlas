using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UnitAtlas.Application.Tenancy;
using UnitAtlas.Application.Integrations;
using UnitAtlas.Infrastructure.Integrations;
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
        services.AddHttpClient<WebhookIntegrationAdapter>(client =>
            client.Timeout = TimeSpan.FromSeconds(configuration.GetValue("Integrations:HttpTimeoutSeconds", 10)));
        services.AddTransient<IIntegrationAdapter>(provider => provider.GetRequiredService<WebhookIntegrationAdapter>());
        services.AddHostedService<IntegrationDispatcher>();
        return services;
    }
}
