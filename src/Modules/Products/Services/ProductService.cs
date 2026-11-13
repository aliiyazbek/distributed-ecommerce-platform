using Grpc.Core;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Modules.Products.Contracts;
using Modules.Products.Entities;
using Modules.Products.Interfaces;
using Shared.Events;
using Modules.Orders.Integration.Protos;

namespace Modules.Products.Services;

public class ProductService : IProductService
{
    private readonly IProductRepository _repository;
    private readonly IWebHostEnvironment _env;
    private readonly IMediator _mediator;
    private readonly InventoryService.InventoryServiceClient _inventoryClient;

    public ProductService(
        IProductRepository repository,
        IWebHostEnvironment env,
        IMediator mediator,
        InventoryService.InventoryServiceClient inventoryClient)
    {
        _repository = repository;
        _env = env;
        _mediator = mediator;
        _inventoryClient = inventoryClient;
    }

    public async Task<ProductResponse> CreateProductAsync(CreateProductRequest request)
    {
        var product = new Product
        {
            Name = request.Name,
            Description = request.Description,
            Price = request.Price,
            Category = request.Category,
            CreatedAt = DateTime.UtcNow,
            ImageUrl = await SaveImageAsync(request.Image) ?? string.Empty
        };

        // 1. Commit to the local database
        await _repository.AddAsync(product);

        // 2. Initialize stock in the inventory service (2-second deadline)
        var callOptions = new CallOptions().WithDeadline(DateTime.UtcNow.AddMilliseconds(2000));
        try
        {
            var initResponse = await _inventoryClient.InitializeStockAsync(new InitializeRequest
            {
                ProductId = product.Id.ToString(),
                InitialQuantity = request.AvailableQuantity
            }, callOptions);

            if (!initResponse.Success)
            {
                // SAGA ROLLBACK: inventory rejected the stock, remove the product locally
                await _repository.RemoveAsync(product);
                throw new Exception($"Failed to initialize inventory: {initResponse.Message}");
            }
        }
        catch (RpcException ex)
        {
            // SAGA ROLLBACK: inventory is offline or timed out
            await _repository.RemoveAsync(product);
            throw new Exception($"Inventory service unavailable. Product creation aborted. Status: {ex.Status.StatusCode}");
        }

        // 3. Notify other modules
        await _mediator.Publish(new ProductCreatedIntegrationEvent(product.Id, product.Name, product.Category, product.Price));

        return MapToResponse(product, request.AvailableQuantity);
    }

    public async Task<ProductResponse?> UpdateProductAsync(Guid id, UpdateProductRequest request)
    {
        var product = await _repository.GetByIdForUpdateAsync(id);
        if (product == null) return null;

        product.Name = request.Name;
        product.Description = request.Description;
        product.Price = request.Price;
        product.Category = request.Category;

        var newImageUrl = await SaveImageAsync(request.Image);
        if (newImageUrl != null) product.ImageUrl = newImageUrl;

        await _repository.UpdateAsync(product);

        return MapToResponse(product, await FetchQuantityFromInventoryAsync(id));
    }

    public async Task<ProductResponse?> GetProductAsync(Guid id)
    {
        var product = await _repository.GetByIdAsync(id);
        if (product == null) return null;

        // Stock is never cached: always read live from the inventory service
        return MapToResponse(product, await FetchQuantityFromInventoryAsync(id));
    }

    public async Task<PaginatedProductResponse> GetProductsAsync(string? category, int page, int pageSize)
    {
        var result = await _repository.GetPageAsync(category, page, pageSize);

        // Live stock for the whole page in ONE gRPC call (not one call per product: avoids N+1)
        var quantities = await FetchQuantitiesFromInventoryAsync(result.Items.Select(p => p.Id));
        var items = result.Items
            .Select(p => MapToResponse(p, quantities.GetValueOrDefault(p.Id)))
            .ToList();

        return new PaginatedProductResponse(
            result.TotalItems, page, pageSize,
            (int)Math.Ceiling(result.TotalItems / (double)pageSize),
            items);
    }

    // Saves an uploaded image to wwwroot/images and returns its public URL, or null if there is no image
    private async Task<string?> SaveImageAsync(IFormFile? image)
    {
        if (image == null || image.Length == 0) return null;

        var webRoot = _env.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
        var uploadsFolder = Path.Combine(webRoot, "images");
        Directory.CreateDirectory(uploadsFolder);

        var uniqueFileName = $"{Guid.NewGuid()}_{Path.GetFileName(image.FileName)}";
        await using var fileStream = new FileStream(Path.Combine(uploadsFolder, uniqueFileName), FileMode.Create);
        await image.CopyToAsync(fileStream);

        return $"/images/{uniqueFileName}";
    }

    private async Task<int> FetchQuantityFromInventoryAsync(Guid productId)
    {
        try
        {
            var callOptions = new CallOptions().WithDeadline(DateTime.UtcNow.AddMilliseconds(1000));
            var response = await _inventoryClient.CheckQuantityAsync(
                new CheckQuantityRequest { ProductId = productId.ToString() }, callOptions);
            return response.AvailableQuantity;
        }
        catch (RpcException)
        {
            // Inventory down: show 0 rather than failing the whole page
            return 0;
        }
    }

    // Batched version for lists: one round trip for any number of products.
    // Missing products (or inventory down) map to 0, like the single-product lookup.
    private async Task<IReadOnlyDictionary<Guid, int>> FetchQuantitiesFromInventoryAsync(IEnumerable<Guid> productIds)
    {
        var request = new CheckQuantitiesRequest();
        request.ProductIds.AddRange(productIds.Select(id => id.ToString()));
        if (request.ProductIds.Count == 0) return new Dictionary<Guid, int>();

        try
        {
            var callOptions = new CallOptions().WithDeadline(DateTime.UtcNow.AddMilliseconds(1000));
            var response = await _inventoryClient.CheckQuantitiesAsync(request, callOptions);
            return response.Items.ToDictionary(i => Guid.Parse(i.ProductId), i => i.AvailableQuantity);
        }
        catch (RpcException)
        {
            // Inventory down: show 0 rather than failing the whole page
            return new Dictionary<Guid, int>();
        }
    }

    private static ProductResponse MapToResponse(Product product, int quantity) =>
        new(product.Id, product.Name, product.Description, product.Price, product.Category,
            quantity, product.ImageUrl, product.CreatedAt);
}