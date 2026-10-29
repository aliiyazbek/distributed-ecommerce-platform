using System.Security.Claims;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Modules.Orders.Contracts;
using Modules.Orders.Interfaces;
using Shared.Controllers;
namespace Modules.Orders.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Authorize]
public class OrdersController : BaseController
{
    private readonly IOrderService _orderService;

    public OrdersController(IOrderService orderService)
    {
        _orderService = orderService;
    }

    [HttpPost]
    public async Task<IActionResult> CreateOrder([FromBody] CreateOrderRequest request)
    {
        var (success, data, error) = await _orderService.CreateOrderAsync(GetUserId(), request);

        if (!success)
            return BadRequest(new { Message = error });

        return CreatedAtAction(nameof(GetOrder), new { id = data!.Id }, data);
    }

    [HttpGet]
    public async Task<IActionResult> GetOrders()
    {
        var orders = await _orderService.GetOrdersAsync(User.IsInRole("Admin") ? null : GetUserId());
        return Ok(orders);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetOrder(Guid id)
    {
        var (success, data, error) = await _orderService.GetOrderAsync(id);

        if (!success)
            return NotFound(new { Message = error });

        if (data!.UserId != GetUserId() && !User.IsInRole("Admin"))
            return StatusCode(StatusCodes.Status403Forbidden, new { Message = "You can only view your own orders." });

        return Ok(data);
    }

    [HttpPatch("{id}/status")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> UpdateOrderStatus(Guid id, [FromBody] UpdateOrderStatusRequest request)
    {
        var (success, data, error) = await _orderService.UpdateOrderStatusAsync(id, request);

        if (!success)
            return NotFound(new { Message = error });

        return Ok(data);
    }

    private Guid GetUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}
