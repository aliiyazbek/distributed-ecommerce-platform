using Microsoft.EntityFrameworkCore;
using Modules.Products.Entities;
using Modules.Products.Interfaces;

namespace Modules.Products.Persistence.Repositories;

public class ProductRepository : IProductRepository
{
    private readonly ProductsDbContext _context;

    public ProductRepository(ProductsDbContext context)
    {
        _context = context;
    }

    public async Task<Product?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Products
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
    }

    public async Task<ProductPage> GetPageAsync(string? category, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = _context.Products.AsNoTracking();

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
}