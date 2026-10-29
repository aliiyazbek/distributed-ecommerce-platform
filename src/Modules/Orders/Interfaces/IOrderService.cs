using Modules.Orders.Contracts;

namespace Modules.Orders.Interfaces;

public interface IOrderService
{
    Task<(bool Success, OrderResponse? Data, string? Error)> CreateOrderAsync(Guid userId, CreateOrderRequest request);
    Task<(bool Success, OrderResponse? Data, string? Error)> GetOrderAsync(Guid orderId);
    Task<List<OrderResponse>> GetOrdersAsync(Guid? userId);
    Task<(bool Success, OrderResponse? Data, string? Error)> UpdateOrderStatusAsync(Guid orderId, UpdateOrderStatusRequest request);
}
