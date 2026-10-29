using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Modules.Orders.Contracts;
using Modules.Orders.Interfaces;

namespace Modules.Orders.Decorators;

public class LoggingOrderServiceDecorator : IOrderService
{
    private readonly IOrderService _innerService;
    private readonly ILogger<LoggingOrderServiceDecorator> _logger;

    public LoggingOrderServiceDecorator(
        IOrderService innerService,
        ILogger<LoggingOrderServiceDecorator> logger)
    {
        _innerService = innerService;
        _logger = logger;
    }

    public async Task<(bool Success, OrderResponse? Data, string? Error)> CreateOrderAsync(Guid userId, CreateOrderRequest request)
    {
        _logger.LogInformation("Creating order for user {UserId} with {ItemCount} items", userId, request.Items.Count);
        var stopwatch = Stopwatch.StartNew();

        var result = await _innerService.CreateOrderAsync(userId, request);

        stopwatch.Stop();

        if (result.Success)
            _logger.LogInformation("Created order {OrderId} for user {UserId} in {ElapsedMilliseconds}ms", result.Data?.Id, userId, stopwatch.ElapsedMilliseconds);
        else
            _logger.LogWarning("Failed to create order for user {UserId}. Reason: {Error}", userId, result.Error);

        return result;
    }

    public async Task<(bool Success, OrderResponse? Data, string? Error)> GetOrderAsync(Guid orderId)
    {
        _logger.LogInformation("Fetching order {OrderId}", orderId);
        return await _innerService.GetOrderAsync(orderId);
    }

    public async Task<List<OrderResponse>> GetOrdersAsync(Guid? userId)
    {
        _logger.LogInformation("Fetching orders for {UserId}", userId?.ToString() ?? "all users");
        return await _innerService.GetOrdersAsync(userId);
    }

    public async Task<(bool Success, OrderResponse? Data, string? Error)> UpdateOrderStatusAsync(Guid orderId, UpdateOrderStatusRequest request)
    {
        _logger.LogInformation("Changing status of order {OrderId} to {Status}", orderId, request.Status);

        var result = await _innerService.UpdateOrderStatusAsync(orderId, request);

        if (!result.Success)
            _logger.LogWarning("Failed to change status of order {OrderId}. Reason: {Error}", orderId, result.Error);

        return result;
    }
}
