using MediatR;
using Microsoft.EntityFrameworkCore;
using Modules.Orders.Contracts;
using Modules.Orders.Entities;
using Modules.Orders.Interfaces;
using Modules.Orders.Persistence;
using Shared.Events;
using Shared.Queries;
using Shared.Commands;

using Modules.Orders.Integration.Protos;
namespace Modules.Orders.Services;

public class OrderService : IOrderService
{
    private readonly OrdersDbContext _dbContext;
    private readonly ISender _sender;
    private readonly IPublisher _publisher;
    private readonly InventoryService.InventoryServiceClient _inventoryClient;

    public OrderService(OrdersDbContext dbContext, ISender sender, IPublisher publisher, InventoryService.InventoryServiceClient inventoryClient)
    {
        _dbContext = dbContext;
        _sender = sender;
        _publisher = publisher;
        _inventoryClient = inventoryClient;
    }

   public async Task<(bool Success, OrderResponse? Data, string? Error)> CreateOrderAsync(Guid userId, CreateOrderRequest request)
    {
        var productIds = request.Items.Select(i => i.ProductId).ToList();
        var products = await _sender.Send(new GetProductsByIdsQuery(productIds));
        var productsById = products.ToDictionary(p => p.Id);

        // 1. VALIDATION PHASE: Ensure products exist and have sufficient stock
        foreach (var item in request.Items)
        {
            if (!productsById.TryGetValue(item.ProductId, out var product))
                return (false, null, $"Product {item.ProductId} was not found.");

            // Ensure your Product response DTO includes 'AvailableQuantity' for this to work
            if (product.AvailableQuantity < item.Quantity)
                return (false, null, $"Insufficient stock for '{product.Name}'. Requested: {item.Quantity}, Available: {product.AvailableQuantity}.");
        }

        // 2. RESERVATION PHASE: Tell the Products module to deduct the stock
        // We create a dictionary of ProductId -> Quantity to deduct
        var stockToReserve = request.Items.ToDictionary(i => i.ProductId, i => i.Quantity);
        
        var reserveResult = await _sender.Send(new ReserveStockCommand(stockToReserve));
        if (!reserveResult.Success)
            return (false, null, reserveResult.Error); // Rollback if deduction fails

        // 3. CREATION PHASE: Create and save the order
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
