using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Modules.Products.Contracts;
using Modules.Products.Entities;
using Modules.Products.Interfaces;
using Modules.Products.Persistence;

namespace Modules.Products.Services;

public class ProductService : IProductService
{
    private readonly ProductsDbContext _context;
    private readonly IWebHostEnvironment _env;

    public ProductService(ProductsDbContext context, IWebHostEnvironment env)
    {
        _context = context;
        _env = env;
    }

    public async Task<ProductResponse> CreateProductAsync(CreateProductRequest request)
    {
        string imageUrl = string.Empty;
        if (request.Image != null && request.Image.Length > 0)
        {
            var webRoot = _env.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
            var uploadsFolder = Path.Combine(webRoot, "images");
            Directory.CreateDirectory(uploadsFolder);
            var uniqueFileName = $"{Guid.NewGuid()}_{request.Image.FileName}";
            var filePath = Path.Combine(uploadsFolder, uniqueFileName);
            using var fileStream = new FileStream(filePath, FileMode.Create);
            await request.Image.CopyToAsync(fileStream);
            imageUrl = $"/images/{uniqueFileName}";
        }

        var product = new Product
        {
            Name = request.Name,
            Description = request.Description,
            Price = request.Price,
            Category = request.Category,
            AvailableQuantity = request.AvailableQuantity,
            ImageUrl = imageUrl
        };

        _context.Products.Add(product);
        await _context.SaveChangesAsync();

        return MapToResponse(product);
    }

    public async Task<ProductResponse?> GetProductAsync(Guid id)
    {
        var product = await _context.Products.FindAsync(id);
        return product == null ? null : MapToResponse(product);
    }

    public async Task<PaginatedProductResponse> GetProductsAsync(string? category, int page, int pageSize)
    {
        var query = _context.Products.AsQueryable();
        if (!string.IsNullOrWhiteSpace(category))
        {
            query = query.Where(p => p.Category.ToLower() == category.ToLower());
        }

        var totalItems = await query.CountAsync();
        var products = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        
        return new PaginatedProductResponse(
            totalItems, page, pageSize, 
            (int)Math.Ceiling(totalItems / (double)pageSize), 
            products.Select(MapToResponse)
        );
    }

    private static ProductResponse MapToResponse(Product product) => 
        new(product.Id, product.Name, product.Description, product.Price, product.Category, product.ImageUrl, product.AvailableQuantity, product.CreatedAt);
}