using Grpc.Core;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Modules.Orders.Contracts;
using Modules.Orders.Entities;
using Modules.Orders.Interfaces;
using Modules.Orders.Persistence;
using Shared.Events;
using Shared.Queries;

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

        // 1. VALIDATION PHASE: Ensure products exist in the catalog
        foreach (var item in request.Items)
        {
            if (!productsById.TryGetValue(item.ProductId, out var product))
                return (false, null, $"Product {item.ProductId} was not found.");
        }

        // We pre-generate the order to establish the OrderId. 
        // This acts as our Idempotency Key for the gRPC requests.
        var items = request.Items
            .Select(i => new OrderItem(i.ProductId, productsById[i.ProductId].Name, productsById[i.ProductId].Price, i.Quantity))
            .ToList();

        var order = new Order(userId, items);

        // 2. RESERVATION PHASE: Network Call to Node.js Microservice
        var deadline = DateTime.UtcNow.AddMilliseconds(2000); // Strict 2000ms resiliency timeout[cite: 3]
        var reservedItems = new List<OrderItem>();
        
        try
        {
            foreach (var item in order.Items)
            {
                // Replaces the old monolith MediatR command
                await _inventoryClient.ReserveQuantityAsync(new ReserveQuantityRequest 
                { 
                    ProductId = item.ProductId.ToString(),
                    Amount = item.Quantity,
                    OrderId = order.Id.ToString() // Enforces idempotency during network retries[cite: 3]
                }, deadline: deadline);

                reservedItems.Add(item);
            }
        }
        catch (RpcException ex)
        {
            // If any item fails (e.g. FAILED_PRECONDITION for Out of Stock), rollback previously reserved items in this order
            foreach (var item in reservedItems)
            {
                try 
                {
                    await _inventoryClient.ReleaseQuantityAsync(new ReleaseQuantityRequest 
                    { 
                        ProductId = item.ProductId.ToString(), 
                        Amount = item.Quantity,
                        OrderId = order.Id.ToString()
                    });
                } 
                catch 
                { 
                    // Log silent rollback failure 
                }
            }
            return (false, null, $"Inventory reservation failed: {ex.Status.Detail}");
        }

        // 3. CREATION PHASE: Saga Pattern Commit[cite: 3]
        try
        {
            _dbContext.Orders.Add(order);
            await _dbContext.SaveChangesAsync();
        }
        catch (Exception)
        {
            // If the PostgreSQL transaction fails locally, trigger gRPC ReleaseQuantity to rollback Node.js[cite: 3]
            foreach (var item in order.Items)
            {
                try 
                {
                    await _inventoryClient.ReleaseQuantityAsync(new ReleaseQuantityRequest 
                    { 
                        ProductId = item.ProductId.ToString(), 
                        Amount = item.Quantity,
                        OrderId = order.Id.ToString()
                    });
                } 
                catch 
                { 
                    // Log silent rollback failure 
                }
            }
            return (false, null, "Failed to save order. Inventory reservation has been rolled back.");
        }

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