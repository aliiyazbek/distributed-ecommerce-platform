using System.Text.Json;

namespace Modules.Orders.Persistence.Outbox;

// Requirement 4: transactional outbox. Saved in the SAME transaction as the order change, so an event
// can never be lost between "order saved" and "message sent". The outbox publisher sends it to
// RabbitMQ, waits for the broker's confirm, then deletes the row.
public sealed class OutboxMessage
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Guid Id { get; private set; }                         // becomes the RabbitMQ message_id
    public string RoutingKey { get; private set; } = string.Empty;
    public string Payload { get; private set; } = string.Empty;  // the event as JSON
    public DateTime OccurredAt { get; private set; }

    private OutboxMessage() { } // for EF Core

    public static OutboxMessage From<TEvent>(string routingKey, TEvent @event) => new()
    {
        Id = Guid.CreateVersion7(), // time-ordered: new rows go to the end of the index
        RoutingKey = routingKey,
        Payload = JsonSerializer.Serialize(@event, Json),
        OccurredAt = DateTime.UtcNow
    };
}