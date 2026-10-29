using MediatR;
using Microsoft.EntityFrameworkCore;
using Modules.Orders.Contracts;
using Modules.Orders.Entities;
using Modules.Orders.Interfaces;
using Modules.Orders.Persistence;
using Shared.Events;
using Shared.Queries;

namespace Modules.Orders.Services;

public class OrderService : IOrderService
{
    private readonly OrdersDbContext _dbContext;
    private readonly ISender _sender;
    private readonly IPublisher _publisher;

    public OrderService(OrdersDbContext dbContext, ISender sender, IPublisher publisher)
    {
        _dbContext = dbContext;
        _sender = sender;
        _publisher = publisher;
    }

    public async Task<(bool Success, OrderResponse? Data, string? Error)> CreateOrderAsync(Guid userId, CreateOrderRequest request)
    {
        var productIds = request.Items.Select(i => i.ProductId).ToList();
        var products = await _sender.Send(new GetProductsByIdsQuery(productIds));
        var productsById = products.ToDictionary(p => p.Id);

        foreach (var productId in productIds)
        {
            if (!productsById.ContainsKey(productId))
                return (false, null, $"Product {productId} was not found.");
        }

        var items = request.Items
            .Select(i => new OrderItem(i.ProductId, productsById[i.ProductId].Name, productsById[i.ProductId].Price, i.Quantity))
            .ToList();

        var order = new Order(userId, items);
        _dbContext.Orders.Add(order);
        await _dbContext.SaveChangesAsync();

        return (true, MapToResponse(order), null);
    }

    public async Task<(bool Success, OrderResponse? Data, string? Error)> GetOrderAsync(Guid orderId)
    {
        var order = await _dbContext.Orders
            .AsNoTracking()
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == orderId);

        if (order == null)
            return (false, null, "Order not found.");

        return (true, MapToResponse(order), null);
    }

    public async Task<List<OrderResponse>> GetOrdersAsync(Guid? userId)
    {
        IQueryable<Order> query = _dbContext.Orders
            .AsNoTracking()
            .Include(o => o.Items);

        if (userId.HasValue)
            query = query.Where(o => o.UserId == userId.Value);

        var orders = await query.OrderByDescending(o => o.CreatedAt).ToListAsync();
        return orders.Select(MapToResponse).ToList();
    }

    public async Task<(bool Success, OrderResponse? Data, string? Error)> UpdateOrderStatusAsync(Guid orderId, UpdateOrderStatusRequest request)
    {
        var order = await _dbContext.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == orderId);

        if (order == null)
            return (false, null, "Order not found.");

        order.ChangeStatus(Enum.Parse<OrderStatus>(request.Status, ignoreCase: true));
        await _dbContext.SaveChangesAsync();

        await _publisher.Publish(new OrderStatusChangedIntegrationEvent(order.Id, order.UserId, order.Status.ToString()));

        return (true, MapToResponse(order), null);
    }

    private static OrderResponse MapToResponse(Order order) =>
        new(order.Id, order.UserId, order.Status.ToString(), order.TotalPrice, order.CreatedAt, order.UpdatedAt,
            order.Items.Select(i => new OrderItemResponse(i.ProductId, i.ProductName, i.UnitPrice, i.Quantity)).ToList());
}
