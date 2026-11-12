using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Modules.Products.Caching;
using Modules.Products.Entities;
using Modules.Products.Interfaces;
using Shared.Caching;

namespace Modules.Products.Decorators;

// Cache-aside over Redis for product catalog data (Requirement 3).
// Reads: try Redis, fall back to the inner repository, then store the result.
// Writes: let the inner repository commit first, then invalidate.
// Stock is never here: the repository only returns Product rows; quantity is fetched live by ProductService.
public sealed class CachingProductRepositoryDecorator : IProductRepository
{
    private const string ListVersionKey = "products:list:version";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly IProductRepository _inner;
    private readonly IDistributedCache _cache;
    private readonly CacheStatus _status;
    private readonly CacheCircuitBreaker _breaker;
    private readonly ProductCacheOptions _options;
    private readonly ILogger<CachingProductRepositoryDecorator> _logger;

    public CachingProductRepositoryDecorator(
        IProductRepository inner,
        IDistributedCache cache,
        CacheStatus status,
        CacheCircuitBreaker breaker,
        IOptions<ProductCacheOptions> options,
        ILogger<CachingProductRepositoryDecorator> logger)
    {
        _inner = inner;
        _cache = cache;
        _status = status;
        _breaker = breaker;
        _options = options.Value;
        _logger = logger;
    }

    // ---------- Reads ----------

    public async Task<Product?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var key = ProductKey(id);

        var cached = await TryGetAsync<Product>(key, cancellationToken);
        if (cached is not null) return cached;

        var product = await _inner.GetByIdAsync(id, cancellationToken);
        if (product is not null)
            await TrySetAsync(key, product, TimeSpan.FromSeconds(_options.ProductTtlSeconds), cancellationToken);

        return product;
    }

    public async Task<ProductPage> GetPageAsync(string? category, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var key = await TryGetPageKeyAsync(category, page, pageSize, cancellationToken);

        if (key is not null)
        {
            var cached = await TryGetAsync<ProductPage>(key, cancellationToken);
            if (cached is not null) return cached;
        }

        var result = await _inner.GetPageAsync(category, page, pageSize, cancellationToken);

        if (key is not null)
            await TrySetAsync(key, result, TimeSpan.FromSeconds(_options.ProductListTtlSeconds), cancellationToken);

        return result;
    }

    // ---------- Writes: commit first, then invalidate ----------

    public async Task AddAsync(Product product, CancellationToken cancellationToken = default)
    {
        await _inner.AddAsync(product, cancellationToken);
        await InvalidateAsync(product.Id);
    }

    public async Task UpdateAsync(Product product, CancellationToken cancellationToken = default)
    {
        await _inner.UpdateAsync(product, cancellationToken);
        await InvalidateAsync(product.Id);
    }

    public async Task RemoveAsync(Product product, CancellationToken cancellationToken = default)
    {
        await _inner.RemoveAsync(product, cancellationToken);
        await InvalidateAsync(product.Id);
    }

    // ---------- Helpers ----------

    private static string ProductKey(Guid id) => $"product:{id}";

    // List keys contain a version token. Changing the token makes every old list key unreachable.
    private async Task<string?> TryGetPageKeyAsync(string? category, int page, int pageSize, CancellationToken cancellationToken)
    {
        if (_breaker.IsOpen)
        {
            _status.Record(CacheOutcome.Bypass);
            return null;
        }

        try
        {
            var version = await _cache.GetStringAsync(ListVersionKey, cancellationToken) ?? "0";
            var categoryPart = string.IsNullOrWhiteSpace(category) ? "all" : category.Trim().ToLowerInvariant();
            return $"products:list:{version}:{categoryPart}:{page}:{pageSize}";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            OnRedisFailure(ex, ListVersionKey);
            return null;
        }
    }

    private async Task<T?> TryGetAsync<T>(string key, CancellationToken cancellationToken) where T : class
    {
        if (_breaker.IsOpen)
        {
            _status.Record(CacheOutcome.Bypass);
            return null;
        }

        try
        {
            var bytes = await _cache.GetAsync(key, cancellationToken);
            if (bytes is null)
            {
                _status.Record(CacheOutcome.Miss);
                return null;
            }

            _status.Record(CacheOutcome.Hit);
            return JsonSerializer.Deserialize<T>(bytes, Json);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            OnRedisFailure(ex, key);
            return null;
        }
    }

    private async Task TrySetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken cancellationToken)
    {
        if (_breaker.IsOpen) return;

        try
        {
            await _cache.SetAsync(key, JsonSerializer.SerializeToUtf8Bytes(value, Json),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl }, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            OnRedisFailure(ex, key);
        }
    }

    // Always attempted, even when the breaker is open, and never cancelled:
    // the database has already changed, so skipping this could leave stale data in Redis.
    private async Task InvalidateAsync(Guid productId)
    {
        try
        {
            await _cache.RemoveAsync(ProductKey(productId));
            await _cache.SetStringAsync(ListVersionKey, Guid.NewGuid().ToString("N"),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(1) });
        }
        catch (Exception ex)
        {
            _breaker.ReportFailure();
            _logger.LogWarning(ex, "Cache invalidation failed for product {ProductId}; stale entries will expire by TTL", productId);
        }
    }

    private void OnRedisFailure(Exception ex, string key)
    {
        _status.Record(CacheOutcome.Bypass);
        _breaker.ReportFailure();
        _logger.LogWarning(ex, "Redis unavailable (key {Key}); reading from the database", key);
    }
}
