using Grpc.Core; // Required for gRPC CallOptions and RpcException
using MediatR;
using Microsoft.EntityFrameworkCore;
using Modules.Orders.Contracts;
using Modules.Orders.Entities;
using Modules.Orders.Interfaces;
using Modules.Orders.Persistence;
using Shared.Events;
using Shared.Queries;
// using Shared.Commands; // (You can likely remove this if ReserveStockCommand was the only command)

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

        // 1. VALIDATION PHASE: Ensure products exist in the C# catalog
        // NOTE: We no longer check AvailableQuantity here, as C# no longer owns that data!
        foreach (var item in request.Items)
        {
            if (!productsById.TryGetValue(item.ProductId, out var product))
                return (false, null, $"Product {item.ProductId} was not found in catalog.");
        }

        // 2. RESERVATION PHASE (gRPC): Reserve stock in Node.js
        var callOptions = new CallOptions().WithDeadline(DateTime.UtcNow.AddMilliseconds(2000));
        var successfullyReservedItems = new List<(Guid ProductId, int Quantity)>();

        foreach (var item in request.Items)
        {
            try
            {
                var reserveRequest = new ReserveRequest
                {
                    ProductId = item.ProductId.ToString(),
                    RequestedQuantity = item.Quantity
                };
                
                var reserveResponse = await _inventoryClient.ReserveQuantityAsync(reserveRequest, callOptions);
                
                if (!reserveResponse.Success)
                {
                    // Node.js rejected the reservation (e.g., out of stock)
                    // We must rollback any items in this order that we ALREADY reserved in previous loop iterations
                    await RollbackReservationsAsync(successfullyReservedItems);
                    return (false, null, reserveResponse.ErrorMessage ?? $"Insufficient stock for '{productsById[item.ProductId].Name}'.");
                }

                // Track successful reservations for potential Saga rollback
                successfullyReservedItems.Add((item.ProductId, item.Quantity));
            }
            catch (RpcException ex)
            {
                // Network failure or timeout. Rollback previously reserved items.
                await RollbackReservationsAsync(successfullyReservedItems);
                return (false, null, $"Inventory service unavailable: {ex.Status.StatusCode}");
            }
        }

        // 3. CREATION PHASE: Local Commit and Saga Rollback
        try
        {
            var items = request.Items
                .Select(i => new OrderItem(i.ProductId, productsById[i.ProductId].Name, productsById[i.ProductId].Price, i.Quantity))
                .ToList();

            var order = new Order(userId, items);
            _dbContext.Orders.Add(order);
            await _dbContext.SaveChangesAsync();

            return (true, MapToResponse(order), null);
        }
        catch (Exception)
        {
            // The C# database failed to save the order (e.g. constraint violation, DB crash).
            // We MUST refund ALL reserved stock back to the Node.js vault.
            await RollbackReservationsAsync(successfullyReservedItems);
            return (false, null, "Order creation failed due to database error. Reserved inventory was safely released.");
        }
    }

    // --- SAGA ROLLBACK HELPER ---
    private async Task RollbackReservationsAsync(List<(Guid ProductId, int Quantity)> itemsToRollback)
    {
        if (!itemsToRollback.Any()) return;

        var rollbackOptions = new CallOptions().WithDeadline(DateTime.UtcNow.AddMilliseconds(2000));
        
        foreach (var item in itemsToRollback)
        {
            try 
            {
                await _inventoryClient.ReleaseQuantityAsync(new ReleaseRequest 
                { 
                    ProductId = item.ProductId.ToString(), 
                    ReleaseQuantity = item.Quantity 
                }, rollbackOptions); 
            }
            catch (RpcException)
            {
                // Extreme Edge Case: C# failed to save, AND Node.js crashed during the rollback.
                // In production, this requires an ILogger.LogCritical call to trigger a manual reconciliation alert.
            }
        }
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