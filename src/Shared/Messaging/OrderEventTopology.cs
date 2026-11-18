namespace Shared.Messaging;

// Requirement 4: the RabbitMQ names shared by the API (publisher), the email worker and the real-time side.
public static class OrderEventTopology
{
    public const string Exchange = "orders.events";                     // topic exchange
    public const string OrderCreatedRoutingKey = "order.created";
    public const string OrderStatusChangedRoutingKey = "order.status-changed";
}