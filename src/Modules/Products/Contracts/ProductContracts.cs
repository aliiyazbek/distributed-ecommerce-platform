using Microsoft.AspNetCore.Http;

namespace Modules.Products.Contracts;

public record CreateProductRequest(string Name, string Description, decimal Price, string Category, int AvailableQuantity, IFormFile? Image);

public record ProductResponse(Guid Id, string Name, string Description, decimal Price, string Category, string ImageUrl, int AvailableQuantity, DateTime CreatedAt);

public record PaginatedProductResponse(int TotalItems, int Page, int PageSize, int TotalPages, IEnumerable<ProductResponse> Items);