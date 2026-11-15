using Microsoft.AspNetCore.Http;
using Shared.Caching;

namespace Shared.Http;

// Writes the X-Cache response header from the per-request CacheStatus.
public sealed class CacheStatusHeaderMiddleware
{
    private readonly RequestDelegate _next;

    public CacheStatusHeaderMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, CacheStatus cacheStatus)
    {
        context.Response.OnStarting(() =>
        {
            if (cacheStatus.Outcome != CacheOutcome.None)
                context.Response.Headers["X-Cache"] = cacheStatus.Outcome.ToString().ToUpperInvariant();

            return Task.CompletedTask;
        });

        await _next(context);
    }
}