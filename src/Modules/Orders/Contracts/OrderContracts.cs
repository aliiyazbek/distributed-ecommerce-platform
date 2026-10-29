namespace Modules.Orders.Contracts;

public record OrderItemRequest(Guid ProductId, int Quantity);

public record CreateOrderRequest(List<OrderItemRequest> Items);

public record UpdateOrderStatusRequest(string Status);

public record OrderItemResponse(Guid ProductId, string ProductName, decimal UnitPrice, int Quantity);

public record OrderResponse(Guid Id, Guid UserId, string Status, decimal TotalPrice, DateTime CreatedAt, DateTime? UpdatedAt, List<OrderItemResponse> Items);
