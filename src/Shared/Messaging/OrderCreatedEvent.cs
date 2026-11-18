namespace Shared.Messaging;

// Published once per order, after it is saved. CustomerEmail is copied from the customer's JWT,
// so the email worker never has to call the Users module.
public sealed record OrderCreatedEvent(
    Guid OrderId,
    Guid UserId,
    string CustomerEmail,
    decimal TotalPrice,
    IReadOnlyList<OrderCreatedItem> Items,
    DateTimeOffset CreatedAt);

public sealed record OrderCreatedItem(Guid ProductId, string ProductName, decimal UnitPrice, int Quantity);