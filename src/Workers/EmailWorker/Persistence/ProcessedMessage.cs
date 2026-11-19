namespace EmailWorker.Persistence;

// One row per RabbitMQ message the worker has finished (email sent, or dead-lettered).
// RabbitMQ delivers at-least-once, so the same message can arrive twice; this table makes the worker
// idempotent: a message_id already here is acknowledged without sending a second email.
public sealed class ProcessedMessage
{
    public Guid MessageId { get; set; }   // the RabbitMQ message_id (= the API's outbox row id)
    public Guid OrderId { get; set; }
    public DateTime ProcessedAt { get; set; }
}
