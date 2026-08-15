using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace UnitAtlas.Api.Auth;

public sealed class DemoAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IConfiguration configuration) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "DevelopmentDemo";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var subject = Request.Headers["X-Demo-Subject"].FirstOrDefault() ?? "demo.operator";
        var tenantId = Request.Headers["X-Demo-Tenant"].FirstOrDefault()
            ?? configuration["Authentication:DemoTenantId"]
            ?? "11111111-1111-1111-1111-111111111111";
        var identity = new ClaimsIdentity([
            new Claim("sub", subject),
            new Claim("tenant_id", tenantId)
        ], SchemeName, "sub", "role");
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}
