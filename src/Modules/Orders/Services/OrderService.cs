using Grpc.Core;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Modules.Orders.Contracts;
using Modules.Orders.Entities;
using Modules.Orders.Interfaces;
using Modules.Orders.Persistence;
using Shared.Data;
using Shared.Events;
using Shared.Queries;

using Modules.Orders.Integration.Protos;
namespace Modules.Orders.Services;

public class OrderService : IOrderService
{
    private readonly OrdersDbContext _dbContext;         // primary: writes + read-before-write
    private readonly OrdersReadDbContext _readDbContext; // read replica
    private readonly ReadRouter _readRouter;
    private readonly ISender _sender;
    private readonly IPublisher _publisher;
    private readonly InventoryService.InventoryServiceClient _inventoryClient;

    public OrderService(
        OrdersDbContext dbContext,
        OrdersReadDbContext readDbContext,
        ReadRouter readRouter,
        ISender sender,
        IPublisher publisher,
        InventoryService.InventoryServiceClient inventoryClient)
    {
        _dbContext = dbContext;
        _readDbContext = readDbContext;
        _readRouter = readRouter;
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

        // 2. RESERVATION PHASE: ONE gRPC call reserves every line in a single inventory transaction
        //    (all lines or none), instead of one call per line (avoids N+1).
        var reserveRequest = new ReserveItemsRequest { OrderId = order.Id.ToString() };
        reserveRequest.Items.AddRange(order.Items.Select(i => new ReservationItem
        {
            ProductId = i.ProductId.ToString(),
            Amount = i.Quantity
        }));

        try
        {
            var reservation = await _inventoryClient.ReserveItemsAsync(
                reserveRequest, deadline: DateTime.UtcNow.AddMilliseconds(2000));

            if (!reservation.Success)
                return (false, null, DescribeReservationFailure(reservation, productsById));
        }
        catch (RpcException ex)
        {
            // The call failed or timed out. A timeout can still have committed on the inventory side,
            // so release defensively; ReleaseOrder is idempotent and does nothing if nothing was reserved.
            await ReleaseReservationAsync(order.Id);
            return (false, null, $"Inventory reservation failed: {ex.Status.Detail}");
        }

        // 3. CREATION PHASE: Saga Pattern Commit
        try
        {
            _dbContext.Orders.Add(order);
            await _dbContext.SaveChangesAsync();
        }
        catch (Exception)
        {
            // Compensate: give the reserved stock back
            await ReleaseReservationAsync(order.Id);
            return (false, null, "Failed to save order. Inventory reservation has been rolled back.");
        }

        return (true, MapToResponse(order), null);
    }

    // Builds the error message for a rejected reservation. The "Insufficient stock for '<name>'. ...
    // Available: N" format is what the frontend parses to show which product ran out.
    private static string DescribeReservationFailure(ReserveItemsResponse reservation, IReadOnlyDictionary<Guid, ProductSummary> productsById)
    {
        string NameOf(string productId) =>
            Guid.TryParse(productId, out var id) && productsById.TryGetValue(id, out var p) ? p.Name : productId;

        var shortage = reservation.Shortages.FirstOrDefault();
        if (shortage != null)
            return $"Insufficient stock for '{NameOf(shortage.ProductId)}'. Requested: {shortage.Requested}, Available: {shortage.Available}.";

        var missing = reservation.MissingProductIds.FirstOrDefault();
        if (missing != null)
            return $"Product '{NameOf(missing)}' is not available in inventory.";

        return $"Inventory reservation failed: {reservation.Message}";
    }

    // --- SAGA ROLLBACK HELPER: one call releases everything reserved for the order ---
    private async Task ReleaseReservationAsync(Guid orderId)
    {
        try
        {
            await _inventoryClient.ReleaseOrderAsync(
                new ReleaseOrderRequest { OrderId = orderId.ToString() },
                deadline: DateTime.UtcNow.AddMilliseconds(2000));
        }
        catch (RpcException)
        {
            // Extreme edge case: the order could not be saved AND the inventory service is unreachable.
            // In production this needs a LogCritical + reconciliation job; reservations stay keyed by order id.
        }
    }

    // Reads go to the read replica when it already contains the caller's own writes
    // (the router adds the current user's scope), otherwise to the primary.
    public async Task<(bool Success, OrderResponse? Data, string? Error)> GetOrderAsync(Guid orderId)
    {
        var order = await _readRouter.ReadAsync<OrdersDbContext, Order?>(_dbContext, _readDbContext,
            (db, ct) => db.Orders
                .AsNoTracking()
                .Include(o => o.Items)
                .FirstOrDefaultAsync(o => o.Id == orderId, ct));

        if (order == null)
            return (false, null, "Order not found.");

        return (true, MapToResponse(order), null);
    }

    public async Task<PaginatedOrdersResponse> GetOrdersAsync(
        Guid? userId,
        int page = 1,
        int pageSize = 20)
    {
        if (page < 1 || pageSize < 1 || pageSize > 100)
            throw new ArgumentOutOfRangeException(
                nameof(page), "Invalid pagination parameters.");

        var orders = await _readRouter.ReadAsync<OrdersDbContext, List<Order>>(_dbContext, _readDbContext,
            (db, ct) =>
            {
                IQueryable<Order> query = db.Orders
                    .AsNoTracking()
                    .Include(o => o.Items);

                if (userId.HasValue)
                    query = query.Where(o => o.UserId == userId.Value);

                return query
                    .OrderByDescending(o => o.CreatedAt)
                    .ThenByDescending(o => o.Id)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize + 1)
                    .ToListAsync(ct);
            });

        return new PaginatedOrdersResponse(
            orders.Take(pageSize).Select(MapToResponse).ToList(),
            orders.Count > pageSize);
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
