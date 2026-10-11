using System.Diagnostics;

namespace Sale.Api.Logging;

public sealed class RequestLoggingMiddleware(
    RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        using var scope = logger.BeginScope(
            "Service={Service} TenantId={TenantId} TraceId={TraceId}",
            "Sale.Api",
            context.Request.RouteValues["tenantId"]?.ToString() ?? "(none)",
            Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier);

        var started = Stopwatch.GetTimestamp();
        var failed = false;
        try
        {
            await next(context);
        }
        catch
        {
            failed = true;
            throw;
        }
        finally
        {
            logger.LogInformation(new EventId(1000, "HttpRequestCompleted"),
                "HTTP {Method} {Path} returned {StatusCode} in {DurationMs} ms",
                context.Request.Method, context.Request.Path.Value,
                failed ? StatusCodes.Status500InternalServerError : context.Response.StatusCode,
                Math.Round(Stopwatch.GetElapsedTime(started).TotalMilliseconds, 2));
        }
    }
}
