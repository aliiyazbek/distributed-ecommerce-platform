using Shared;

namespace Modules.Orders.Entities;

public class Order : BaseEntity
{
    public Guid UserId { get; private set; }
    public OrderStatus Status { get; private set; }
    public decimal TotalPrice { get; private set; }
    public List<OrderItem> Items { get; private set; } = new();

    private Order() { }

    public Order(Guid userId, List<OrderItem> items)
    {
        UserId = userId;
        Items = items;
        Status = OrderStatus.Pending;
        TotalPrice = items.Sum(i => i.UnitPrice * i.Quantity);
    }

    public void ChangeStatus(OrderStatus status)
    {
        Status = status;
        UpdateTimestamp();
    }
}
