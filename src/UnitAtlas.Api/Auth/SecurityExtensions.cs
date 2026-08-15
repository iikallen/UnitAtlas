using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using UnitAtlas.Application.Tenancy;

namespace UnitAtlas.Api.Auth;

public static class SecurityExtensions
{
    public static IServiceCollection AddUnitAtlasSecurity(
        this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        if (environment.IsDevelopment() && configuration.GetValue<bool>("Authentication:DemoMode"))
        {
            services.AddAuthentication(DemoAuthenticationHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, DemoAuthenticationHandler>(DemoAuthenticationHandler.SchemeName, _ => { });
        }
        else
        {
            var authority = configuration["Authentication:Authority"]
                ?? throw new InvalidOperationException("Authentication:Authority is required outside development demo mode.");
            var audience = configuration["Authentication:Audience"]
                ?? throw new InvalidOperationException("Authentication:Audience is required outside development demo mode.");
            services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(options =>
                {
                    options.Authority = authority;
                    options.Audience = audience;
                    options.MapInboundClaims = false;
                    options.RequireHttpsMetadata = true;
                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        NameClaimType = "sub",
                        RoleClaimType = "role"
                    };
                });
        }

        var authorization = services.AddAuthorizationBuilder();
        foreach (var permission in Permissions.All)
            authorization.AddPolicy(permission, policy => policy.RequireAuthenticatedUser().RequireClaim("permission", permission));
        return services;
    }
}
