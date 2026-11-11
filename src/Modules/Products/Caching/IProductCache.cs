using Modules.Products.Contracts;

namespace Modules.Products.Caching;

/// <summary>What we store in Redis: product data WITHOUT stock quantity (see ProductCache).</summary>
public record CachedProduct(Guid Id, string Name, string Description, decimal Price, string Category, string ImageUrl, DateTime CreatedAt);

public record CachedProductPage(int TotalItems, int Page, int PageSize, int TotalPages, List<CachedProduct> Items);

public interface IProductCache
{
    Task<CachedProduct?> GetProductAsync(Guid id);
    Task SetProductAsync(CachedProduct product);

    Task<CachedProductPage?> GetPageAsync(string? category, int page, int pageSize);
    Task SetPageAsync(string? category, int page, int pageSize, CachedProductPage value);

    /// <summary>Called on every write: drops the product entry and invalidates all cached list pages.</summary>
    Task InvalidateAsync(Guid productId);
}
