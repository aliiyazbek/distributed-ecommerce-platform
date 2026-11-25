using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Shared.Http;

public sealed class RedisAuthRateLimiterMiddleware(
    RequestDelegate next,
    IConnectionMultiplexer redis,
    ILogger<RedisAuthRateLimiterMiddleware> logger)
{
    private const int PermitLimit = 5;
    private const int WindowMs = 10_000;

    // Atomic fixed-window counter: increments the key and sets TTL on the first request
    private const string FixedWindowLua = """
        local current = redis.call('INCR', KEYS[1])
        if current == 1 then
            redis.call('PEXPIRE', KEYS[1], ARGV[1])
        end
        local ttl = redis.call('PTTL', KEYS[1])
        return {current, ttl}
        """;

    public async Task InvokeAsync(HttpContext context)
    {
        if (!IsAuthRateLimitedEndpoint(context.Request))
        {
            await next(context);
            return;
        }

        var clientIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown_ip";
        var key = $"ecommerce:ratelimit:auth:{clientIp}";

        try
        {
            var db = redis.GetDatabase();
            var result = (RedisResult[]?)await db.ScriptEvaluateAsync(
                FixedWindowLua,
                new RedisKey[] { key },
                new RedisValue[] { WindowMs });

            if (result is { Length: 2 })
            {
                var count = (long)result[0];
                var ttlMs = Math.Max(1000L, (long)result[1]);

                if (count > PermitLimit)
                {
                    var retryAfterSeconds = (int)Math.Ceiling(ttlMs / 1000.0);
                    context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                    context.Response.Headers.RetryAfter = retryAfterSeconds.ToString();
                    context.Response.ContentType = "application/problem+json";

                    var problem = new ProblemDetails
                    {
                        Status = StatusCodes.Status429TooManyRequests,
                        Title = "Too Many Requests",
                        Detail = $"Rate limit exceeded ({PermitLimit} requests per {WindowMs / 1000}s). Retry after {retryAfterSeconds}s."
                    };

                    await context.Response.WriteAsync(JsonSerializer.Serialize(problem));
                    return;
                }
            }
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            logger.LogWarning(ex, "Redis rate limiter unavailable; falling back to per-instance rate limiter");
        }

        await next(context);
    }

    private static bool IsAuthRateLimitedEndpoint(HttpRequest request)
    {
        if (!HttpMethods.IsPost(request.Method))
            return false;

        var path = request.Path.Value ?? string.Empty;
        return path.EndsWith("/api/users/login", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith("/api/users/register", StringComparison.OrdinalIgnoreCase);
    }
}
