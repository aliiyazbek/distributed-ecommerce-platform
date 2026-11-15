using Modules.Products.Entities;

namespace Modules.Products.Interfaces;

// One page of products plus the total count (needed for TotalPages).
public record ProductPage(IReadOnlyList<Product> Items, int TotalItems);

public interface IProductRepository
{
    // Reads: these are what the caching decorator will cache.
    Task<Product?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ProductPage> GetPageAsync(string? category, int page, int pageSize, CancellationToken cancellationToken = default);

    // Read-before-write (e.g. an update): always the primary database, never cached or replicated.
    Task<Product?> GetByIdForUpdateAsync(Guid id, CancellationToken cancellationToken = default);

    // Writes: each one saves immediately, so a decorator can invalidate right after a confirmed commit.
    Task AddAsync(Product product, CancellationToken cancellationToken = default);
    Task UpdateAsync(Product product, CancellationToken cancellationToken = default);
    Task RemoveAsync(Product product, CancellationToken cancellationToken = default);
}