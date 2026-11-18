using RabbitMQ.Client;

namespace Shared.Messaging;

// Requirement 4: declares the exchange and the email queues. Called at startup by BOTH the API and the
// email worker. Declarations are idempotent, and because both sides run this same code the arguments
// always match (a mismatch fails with PRECONDITION_FAILED).
//
// The email queue must exist BEFORE the first event is published: RabbitMQ drops messages that match
// no queue, so declaring it only in the worker would lose emails sent before its first start.
public static class RabbitMqTopology
{
    public const string EmailQueue = "email.order-created";
    public const string EmailRetryQueue = "email.order-created.retry";
    public const string EmailDeadLetterQueue = "email.order-created.dlq";
    public const int EmailRetryDelayMs = 10_000;
    public const int EmailMaxAttempts = 3;

    public static async Task DeclareAsync(IChannel channel, CancellationToken cancellationToken = default)
    {
        await channel.ExchangeDeclareAsync(OrderEventTopology.Exchange, ExchangeType.Topic,
            durable: true, autoDelete: false, cancellationToken: cancellationToken);

        // Main queue. A message the worker rejects is dead-lettered to the retry queue
        // (through the default exchange "", which routes by queue name).
        await channel.QueueDeclareAsync(EmailQueue, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?>
            {
                ["x-dead-letter-exchange"] = "",
                ["x-dead-letter-routing-key"] = EmailRetryQueue
            },
            cancellationToken: cancellationToken);

        await channel.QueueBindAsync(EmailQueue, OrderEventTopology.Exchange,
            OrderEventTopology.OrderCreatedRoutingKey, cancellationToken: cancellationToken);

        // Retry queue: nobody consumes it. Each message waits EmailRetryDelayMs, expires, and is
        // dead-lettered BACK to the main queue only (not through orders.events, so Ali never sees retries).
        await channel.QueueDeclareAsync(EmailRetryQueue, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?>
            {
                ["x-message-ttl"] = EmailRetryDelayMs,
                ["x-dead-letter-exchange"] = "",
                ["x-dead-letter-routing-key"] = EmailQueue
            },
            cancellationToken: cancellationToken);

        // Dead-letter queue: messages that failed EmailMaxAttempts times, or failed permanently,
        // are parked here for inspection instead of being dropped.
        await channel.QueueDeclareAsync(EmailDeadLetterQueue, durable: true, exclusive: false, autoDelete: false,
            cancellationToken: cancellationToken);
    }
}