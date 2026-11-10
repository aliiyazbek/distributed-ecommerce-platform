using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Modules.Products.Caching;

/// <summary>
/// Cache-aside on Redis (Requirement 3).
///
/// WHAT is cached:   product catalog data (name, description, price, category, image).
///                   It changes rarely and is read far more often than written.
/// WHAT is NOT cached: AvailableQuantity. It changes with every order and lives in the
///                   inventory service, so caching it would show customers wrong stock.
///                   It is fetched live and merged into the cached product.
/// Never cached:     user data, tokens, orders (private / sensitive / fast-changing).
///
/// EXPIRY:           absolute TTL (safety net) + explicit invalidation on create/update.
/// LIST INVALIDATION: list pages are keyed with a version token; bumping the token makes every
///                   old list key unreachable (they simply expire) - no key scanning needed.
/// FAILURE:          Redis down => log and fall back to the database (the cache is never a
///                   single point of failure).
/// </summary>
public sealed class ProductCache : IProductCache
{
    private const string ListVersionKey = "products:list:version";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly IDistributedCache _cache;
    private readonly IHttpContextAccessor _http;
    private readonly ILogger<ProductCache> _logger;
    private readonly bool _enabled;
    private readonly TimeSpan _productTtl;
    private readonly TimeSpan _listTtl;

    public ProductCache(IDistributedCache cache, IHttpContextAccessor http, IConfiguration config, ILogger<ProductCache> logger)
    {
        _cache = cache;
        _http = http;
        _logger = logger;
        _enabled = config.GetValue("Caching:Enabled", true);
        _productTtl = TimeSpan.FromSeconds(config.GetValue("Caching:ProductTtlSeconds", 300));
        _listTtl = TimeSpan.FromSeconds(config.GetValue("Caching:ProductListTtlSeconds", 60));
    }

    private static string ProductKey(Guid id) => $"product:{id}";

    public async Task<CachedProduct?> GetProductAsync(Guid id) =>
        await GetAsync<CachedProduct>(ProductKey(id));

    public Task SetProductAsync(CachedProduct product) =>
        SetAsync(ProductKey(product.Id), product, _productTtl);

    public async Task<CachedProductPage?> GetPageAsync(string? category, int page, int pageSize) =>
        await GetAsync<CachedProductPage>(await PageKeyAsync(category, page, pageSize));

    public async Task SetPageAsync(string? category, int page, int pageSize, CachedProductPage value) =>
        await SetAsync(await PageKeyAsync(category, page, pageSize), value, _listTtl);

    public async Task InvalidateAsync(Guid productId)
    {
        if (!_enabled) return;
        try
        {
            await _cache.RemoveAsync(ProductKey(productId));
            await _cache.SetStringAsync(ListVersionKey, Guid.NewGuid().ToString("N"),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(1) });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis invalidation failed for product {ProductId}; entries will expire by TTL", productId);
        }
    }

    private async Task<string> PageKeyAsync(string? category, int page, int pageSize)
    {
        string version = "0";
        try { version = await _cache.GetStringAsync(ListVersionKey) ?? "0"; } catch { /* fail open */ }
        return $"products:list:{version}:{category?.Trim().ToLowerInvariant() ?? "all"}:{page}:{pageSize}";
    }

    private async Task<T?> GetAsync<T>(string key) where T : class
    {
        if (!_enabled) { SetHeader("DISABLED"); return null; }
        try
        {
            var bytes = await _cache.GetAsync(key);
            SetHeader(bytes is null ? "MISS" : "HIT");
            return bytes is null ? null : JsonSerializer.Deserialize<T>(bytes, Json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis read failed for {Key}; falling back to database", key);
            SetHeader("BYPASS");
            return null;
        }
    }

    private async Task SetAsync<T>(string key, T value, TimeSpan ttl)
    {
        if (!_enabled) return;
        try
        {
            await _cache.SetAsync(key, JsonSerializer.SerializeToUtf8Bytes(value, Json),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis write failed for {Key}", key);
        }
    }

    // Debug header used by the load test / demo to show whether the DB was hit.
    private void SetHeader(string value)
    {
        var ctx = _http.HttpContext;
        if (ctx is { Response.HasStarted: false })
            ctx.Response.Headers["X-Cache"] = _enabled ? value : "DISABLED";
    }
}
