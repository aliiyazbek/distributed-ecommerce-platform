using Microsoft.AspNetCore.Http;
using Shared.Data;

namespace Shared.Http;

// Writes the X-Db-Read response header (REPLICA | PRIMARY) from the per-request ReadSourceStatus,
// so tests and JMeter can see which database answered. No header when no routed read happened.
public sealed class ReadSourceHeaderMiddleware
{
    private readonly RequestDelegate _next;

    public ReadSourceHeaderMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, ReadSourceStatus readSource)
    {
        context.Response.OnStarting(() =>
        {
            if (readSource.Source != ReadSource.None)
                context.Response.Headers["X-Db-Read"] = readSource.Source.ToString().ToUpperInvariant();

            return Task.CompletedTask;
        });

        await _next(context);
    }
}
