using Asp.Versioning;
using Microsoft.AspNetCore.Authorization; // Added
using Microsoft.AspNetCore.Mvc;
using Modules.Products.Contracts;
using Modules.Products.Interfaces;
using Shared.Controllers;
using Shared.Http;

namespace Modules.Products.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Authorize] // Enforces that all endpoints require a valid JWT (Customer or Admin)
public class ProductsController : BaseController
{
    private readonly IProductService _productService;

    public ProductsController(IProductService productService)
    {
        _productService = productService;
    }

    [HttpPost("CreateProduct")]
    [Authorize(Roles = "Admin")] 
    public async Task<IActionResult> CreateProduct([FromForm] CreateProductRequest request)
    {
        var response = await _productService.CreateProductAsync(request);
        return CreatedAtAction(nameof(GetProduct), new { id = response.Id }, response);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> UpdateProduct(Guid id, [FromForm] UpdateProductRequest request)
    {
        var response = await _productService.UpdateProductAsync(id, request);
        if (response == null) return NotFound(new { Message = "Product not found." });
        return Ok(response);
    }

    [HttpGet("{id}")]
    [ETag] // HTTP caching: ETag + 304 Not Modified
    public async Task<IActionResult> GetProduct(Guid id)
    {
        var response = await _productService.GetProductAsync(id);
        if (response == null) return NotFound(new { Message = "Product not found." });
        return Ok(response);
    }

    [HttpGet]
    [ETag]
    public async Task<IActionResult> GetProducts([FromQuery] string? category, [FromQuery] int page = 1, [FromQuery] int pageSize = 10)
    {
        var response = await _productService.GetProductsAsync(category, page, pageSize);
        return Ok(response);
    }

    [HttpPost("search")]
    public async Task<IActionResult> SearchProducts([FromForm] string? category, [FromForm] int page = 1, [FromForm] int pageSize = 10)
    {
        var response = await _productService.GetProductsAsync(category, page, pageSize);
        return Ok(response);
    }
}