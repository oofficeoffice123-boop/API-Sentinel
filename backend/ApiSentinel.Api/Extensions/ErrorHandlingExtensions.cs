using System.Text.Json;

namespace ApiSentinel.Api.Extensions;

public static class ErrorHandlingExtensions
{
    public static IApplicationBuilder UseSentinelErrors(this IApplicationBuilder app) => app.Use(async (context, next) =>
    {
        try { await next(context); }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
        catch (Exception)
        {
            var traceId = context.TraceIdentifier;
            context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("ApiSentinel.Errors")
                .LogError("Unhandled request failure. TraceId: {TraceId}", traceId);
            if (context.Response.HasStarted) throw;
            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new { title = "An unexpected error occurred.", status = 500, traceId }));
        }
    });
}