using Microsoft.EntityFrameworkCore;
using Modules.Products.Entities;
using Modules.Products.Interfaces;
using Shared.Data;

namespace Modules.Products.Persistence.Repositories;

public class ProductRepository : IProductRepository
{
    private static readonly string[] CatalogScope = { ProductsDbContext.CatalogScope };

    private readonly ProductsDbContext _context;        // primary: writes + reads that must be authoritative
    private readonly ProductsReadDbContext _readContext; // read replica
    private readonly ReadRouter _router;

    public ProductRepository(ProductsDbContext context, ProductsReadDbContext readContext, ReadRouter router)
    {
        _context = context;
        _readContext = readContext;
        _router = router;
    }

    // Reads: served by the replica when it already has the latest catalog write, else by the primary.
    public Task<Product?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        _router.ReadAsync<ProductsDbContext, Product?>(_context, _readContext,
            (db, ct) => QueryById(db, id, ct), CatalogScope, cancellationToken);

    public Task<ProductPage> GetPageAsync(string? category, int page, int pageSize, CancellationToken cancellationToken = default) =>
        _router.ReadAsync<ProductsDbContext, ProductPage>(_context, _readContext,
            (db, ct) => QueryPage(db, category, page, pageSize, ct), CatalogScope, cancellationToken);

    // Read-before-write: always the primary, so an update never starts from a lagging copy.
    public Task<Product?> GetByIdForUpdateAsync(Guid id, CancellationToken cancellationToken = default) =>
        QueryById(_context, id, cancellationToken);

    public async Task AddAsync(Product product, CancellationToken cancellationToken = default)
    {
        _context.Products.Add(product);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(Product product, CancellationToken cancellationToken = default)
    {
        _context.Products.Update(product);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveAsync(Product product, CancellationToken cancellationToken = default)
    {
        _context.Products.Remove(product);
        await _context.SaveChangesAsync(cancellationToken);
    }

    // ---------- Queries (run against whichever context the router picks) ----------

    private static Task<Product?> QueryById(ProductsDbContext db, Guid id, CancellationToken cancellationToken) =>
        db.Products
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    private static async Task<ProductPage> QueryPage(ProductsDbContext db, string? category, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = db.Products.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(category))
        {
            var normalized = category.Trim().ToLower();
            query = query.Where(p => p.Category.ToLower() == normalized);
        }

        var totalItems = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderBy(p => p.CreatedAt).ThenBy(p => p.Id) // stable order: same page always returns the same rows
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new ProductPage(items, totalItems);
    }
}
