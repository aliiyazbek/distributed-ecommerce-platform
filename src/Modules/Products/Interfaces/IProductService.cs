using Modules.Products.Contracts;

namespace Modules.Products.Interfaces;

public interface IProductService
{
    Task<ProductResponse> CreateProductAsync(CreateProductRequest request);
    Task<ProductResponse?> GetProductAsync(Guid id);
    Task<PaginatedProductResponse> GetProductsAsync(string? category, int page, int pageSize);
}