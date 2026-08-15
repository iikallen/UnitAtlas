using Microsoft.AspNetCore.Diagnostics;
using UnitAtlas.Api.Observability;

namespace UnitAtlas.Api;

internal sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        Telemetry.Exceptions.Add(1, new KeyValuePair<string, object?>("exception.type", exception.GetType().Name));
        logger.LogError(exception, "Unhandled API exception");
        await Results.Problem(
            statusCode: StatusCodes.Status500InternalServerError,
            title: "An unexpected error occurred.",
            extensions: new Dictionary<string, object?> { ["code"] = "INTERNAL_ERROR" })
            .ExecuteAsync(context);
        return true;
    }
}
