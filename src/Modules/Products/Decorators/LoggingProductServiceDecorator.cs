using Microsoft.Extensions.Logging;
using Modules.Products.Contracts;
using Modules.Products.Interfaces;
using System.Diagnostics;

namespace Modules.Products.Decorators;

public class LoggingProductServiceDecorator : IProductService
{
    private readonly IProductService _inner;
    private readonly ILogger<LoggingProductServiceDecorator> _logger;

    public LoggingProductServiceDecorator(IProductService inner, ILogger<LoggingProductServiceDecorator> logger)
    {
        _inner = inner;
        _logger = logger;
    }

    public async Task<ProductResponse> CreateProductAsync(CreateProductRequest request)
    {
        _logger.LogInformation("Executing CreateProductAsync for product '{ProductName}'", request.Name);
        var stopwatch = Stopwatch.StartNew();
        
        var result = await _inner.CreateProductAsync(request);
        
        stopwatch.Stop();
        _logger.LogInformation("CreateProductAsync executed successfully in {ElapsedMilliseconds}ms. New Product ID: {Id}", stopwatch.ElapsedMilliseconds, result.Id);
        
        return result;
    }

    public async Task<ProductResponse?> GetProductAsync(Guid id)
    {
        _logger.LogInformation("Fetching product with ID {ProductId}", id);
        return await _inner.GetProductAsync(id);
    }

    public async Task<PaginatedProductResponse> GetProductsAsync(string? category, int page, int pageSize)
    {
        _logger.LogInformation("Fetching products (Category: {Category}, Page: {Page}, PageSize: {PageSize})", category ?? "All", page, pageSize);
        return await _inner.GetProductsAsync(category, page, pageSize);
    }
}