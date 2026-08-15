using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using UnitAtlas.Application.Tenancy;
using UnitAtlas.Infrastructure.Persistence;

namespace UnitAtlas.Api.Auth;

public sealed class TenantContextMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext httpContext, ITenantContext tenantContext, UnitAtlasDb db)
    {
        if (!httpContext.Request.Path.StartsWithSegments("/api") || httpContext.User.Identity?.IsAuthenticated != true)
        {
            await next(httpContext);
            return;
        }

        var subject = httpContext.User.FindFirstValue("sub") ?? httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var tenantValue = httpContext.User.FindFirstValue("tenant_id");
        if (string.IsNullOrWhiteSpace(subject) || !Guid.TryParse(tenantValue, out var tenantId))
        {
            await next(httpContext);
            return;
        }

        tenantContext.Initialize(tenantId, subject);
        var membership = await db.TenantMemberships.AsNoTracking()
            .SingleOrDefaultAsync(x => x.UserSubject == subject, httpContext.RequestAborted);
        if (membership is null)
        {
            tenantContext.Clear();
            await next(httpContext);
            return;
        }

        tenantContext.Initialize(tenantId, subject, membership.Role);
        if (httpContext.User.Identity is ClaimsIdentity identity)
        {
            identity.AddClaim(new Claim("role", membership.Role.ToString()));
            identity.AddClaims(tenantContext.GrantedPermissions.Select(permission => new Claim("permission", permission)));
        }

        try
        {
            await next(httpContext);
        }
        finally
        {
            tenantContext.Clear();
        }
    }
}
