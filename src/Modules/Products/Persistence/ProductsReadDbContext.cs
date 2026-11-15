using Microsoft.EntityFrameworkCore;

namespace Modules.Products.Persistence;

// Same model as ProductsDbContext, connected to the READ REPLICA (Requirement 3).
// Read-only by design: the replica rejects writes anyway, and this fails fast in code.
// Never used for migrations; use ProductsDbContext for those.
public sealed class ProductsReadDbContext : ProductsDbContext
{
    public ProductsReadDbContext(DbContextOptions<ProductsReadDbContext> options) : base(options) { }

    public override int SaveChanges(bool acceptAllChangesOnSuccess) =>
        throw new InvalidOperationException("ProductsReadDbContext is read-only (read replica). Write through ProductsDbContext.");

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("ProductsReadDbContext is read-only (read replica). Write through ProductsDbContext.");
}
