using System.Collections;
using System.Text;
using System.Text.Json;
using EmailWorker.Email;
using EmailWorker.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Shared.Messaging;

namespace EmailWorker.Messaging;

// Requirement 4: the independent email worker. Consumes order.created events from the durable
// email.order-created queue and sends a confirmation email for each.
//
// No email is lost:
//   - manual acknowledgement: a message leaves the queue only after its email was sent; if the worker
//     stops or crashes mid-way, RabbitMQ gives the message to the worker again.
//   - temporary failure  -> reject -> dead-lettered to email.order-created.retry, which holds it for
//     10 s (TTL) and dead-letters it back here. Attempts are counted from RabbitMQ's x-death header.
//   - permanent failure, or the last attempt failed -> copied to email.order-created.dlq (confirmed by
//     RabbitMQ) with the reason in its headers, then removed from the main queue.
// No email is sent twice: message ids already in email."ProcessedMessages" are acknowledged and skipped.
public sealed class OrderCreatedConsumer : BackgroundService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(5);
    private const ushort Prefetch = 5;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IEmailSender _emailSender;
    private readonly RabbitMqOptions _options;
    private readonly ILogger<OrderCreatedConsumer> _logger;

    private IConnection? _connection;
    private IChannel? _consumeChannel;
    private IChannel? _publishChannel; // separate channel with publisher confirms, for the DLQ copies

    public OrderCreatedConsumer(
        IServiceScopeFactory scopeFactory,
        IEmailSender emailSender,
        IOptions<RabbitMqOptions> options,
        ILogger<OrderCreatedConsumer> logger)
    {
        _scopeFactory = scopeFactory;
        _emailSender = emailSender;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Keep trying until RabbitMQ is reachable. After that, the client's automatic recovery
        // reconnects and re-registers the consumer by itself if the connection drops.
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await StartConsumingAsync(stoppingToken);
                return;
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogWarning("RabbitMQ not reachable ({Error}); retrying in {Delay} s", ex.Message, ReconnectDelay.TotalSeconds);
                await CloseAsync();
                await Task.Delay(ReconnectDelay, stoppingToken);
            }
        }
    }

    private async Task StartConsumingAsync(CancellationToken stoppingToken)
    {
        _connection = await _options.CreateConnectionFactory("email-worker").CreateConnectionAsync(stoppingToken);
        _publishChannel = await _connection.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
            stoppingToken);
        _consumeChannel = await _connection.CreateChannelAsync(cancellationToken: stoppingToken);

        // Same declarations as the API: the queues exist even if the API has never run.
        await RabbitMqTopology.DeclareAsync(_consumeChannel, stoppingToken);

        // Prefetch: at most 5 unacknowledged messages in this worker at a time.
        await _consumeChannel.BasicQosAsync(prefetchSize: 0, prefetchCount: Prefetch, global: false, stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(_consumeChannel);
        consumer.ReceivedAsync += (_, delivery) => HandleAsync(delivery, stoppingToken);

        await _consumeChannel.BasicConsumeAsync(RabbitMqTopology.EmailQueue, autoAck: false, consumer, stoppingToken);
        _logger.LogInformation("Email worker consuming '{Queue}' (prefetch {Prefetch})", RabbitMqTopology.EmailQueue, Prefetch);
    }

    private async Task HandleAsync(BasicDeliverEventArgs delivery, CancellationToken stoppingToken)
    {
        var channel = _consumeChannel!;
        var attempt = RejectionCount(delivery.BasicProperties) + 1;

        try
        {
            if (!Guid.TryParse(delivery.BasicProperties.MessageId, out var messageId))
            {
                await DeadLetterAsync(delivery, "Missing or invalid message_id", attempt);
                return;
            }

            OrderCreatedEvent? order;
            try
            {
                order = JsonSerializer.Deserialize<OrderCreatedEvent>(delivery.Body.Span, Json);
            }
            catch (JsonException)
            {
                order = null;
            }

            if (order is null || string.IsNullOrWhiteSpace(order.CustomerEmail))
            {
                await DeadLetterAsync(delivery, "Body is not a valid OrderCreatedEvent", attempt);
                return;
            }

            // Idempotency: this message was already handled (redelivery or duplicate publish).
            if (await AlreadyProcessedAsync(messageId, stoppingToken))
            {
                _logger.LogInformation("Message {MessageId} (order {OrderId}) already processed; skipping", messageId, order.OrderId);
                await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, CancellationToken.None);
                return;
            }

            try
            {
                await _emailSender.SendOrderConfirmationAsync(order, stoppingToken);
            }
            catch (EmailSendException ex) when (ex.IsPermanent || attempt >= RabbitMqTopology.EmailMaxAttempts)
            {
                _logger.LogError("Email for order {OrderId} failed on attempt {Attempt}/{Max} ({Kind}): {Error}. Moving to the DLQ",
                    order.OrderId, attempt, RabbitMqTopology.EmailMaxAttempts, ex.IsPermanent ? "permanent" : "temporary", ex.Message);
                await DeadLetterAsync(delivery, ex.Message, attempt);
                await MarkProcessedAsync(messageId, order.OrderId);
                return;
            }
            catch (EmailSendException ex)
            {
                _logger.LogWarning("Email for order {OrderId} failed on attempt {Attempt}/{Max}: {Error}. Retrying in {Delay} s",
                    order.OrderId, attempt, RabbitMqTopology.EmailMaxAttempts, ex.Message, RabbitMqTopology.EmailRetryDelayMs / 1000);
                // requeue: false -> dead-lettered to the retry queue (see RabbitMqTopology).
                await channel.BasicRejectAsync(delivery.DeliveryTag, requeue: false, CancellationToken.None);
                return;
            }

            // The email is out. Record it, then acknowledge. If recording fails we still acknowledge:
            // a second email would be worse than a missing row.
            await MarkProcessedAsync(messageId, order.OrderId);
            await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, CancellationToken.None);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down: leave the message unacknowledged; RabbitMQ redelivers it after restart.
        }
        catch (Exception ex)
        {
            // Unexpected (e.g. database down before sending): treat as temporary, the retry limit still applies.
            _logger.LogError(ex, "Unexpected error for delivery {DeliveryTag}; sending it to the retry queue", delivery.DeliveryTag);
            if (attempt >= RabbitMqTopology.EmailMaxAttempts)
                await DeadLetterAsync(delivery, $"Unexpected error: {ex.Message}", attempt);
            else
                await channel.BasicRejectAsync(delivery.DeliveryTag, requeue: false, CancellationToken.None);
        }
    }

    // Copies the message to the DLQ, waits for RabbitMQ's confirm, then removes it from the main queue.
    // If the copy fails, the message is rejected instead (retry queue), so it is never dropped.
    private async Task DeadLetterAsync(BasicDeliverEventArgs delivery, string reason, long attempt)
    {
        var source = delivery.BasicProperties;
        var properties = new BasicProperties
        {
            MessageId = source.MessageId,
            ContentType = source.ContentType,
            DeliveryMode = DeliveryModes.Persistent,
            Timestamp = source.Timestamp,
            Type = source.Type,
            Headers = new Dictionary<string, object?>
            {
                ["x-failure-reason"] = reason,
                ["x-failed-attempts"] = attempt,
                ["x-failed-at"] = DateTimeOffset.UtcNow.ToString("O"),
                ["x-original-routing-key"] = delivery.RoutingKey
            }
        };

        try
        {
            await _publishChannel!.BasicPublishAsync(exchange: "", routingKey: RabbitMqTopology.EmailDeadLetterQueue,
                mandatory: true, basicProperties: properties, body: delivery.Body, cancellationToken: CancellationToken.None);
            await _consumeChannel!.BasicAckAsync(delivery.DeliveryTag, multiple: false, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not move delivery {DeliveryTag} to the DLQ; rejecting it to the retry queue", delivery.DeliveryTag);
            await _consumeChannel!.BasicRejectAsync(delivery.DeliveryTag, requeue: false, CancellationToken.None);
        }
    }

    // How many times this message was already rejected from the main queue. RabbitMQ keeps this count in
    // the x-death header, one entry per (queue, reason) pair, updated every time the message is dead-lettered.
    private static long RejectionCount(IReadOnlyBasicProperties properties)
    {
        if (properties.Headers is null || !properties.Headers.TryGetValue("x-death", out var raw) || raw is not IEnumerable deaths)
            return 0;

        foreach (var entry in deaths)
        {
            if (entry is not IDictionary<string, object?> death) continue;
            if (Text(Field(death, "queue")) == RabbitMqTopology.EmailQueue
                && Text(Field(death, "reason")) == "rejected")
            {
                return Field(death, "count") is long count ? count : 0;
            }
        }
        return 0;
    }

    private static object? Field(IDictionary<string, object?> table, string key) =>
        table.TryGetValue(key, out var value) ? value : null;

    private static string? Text(object? value) => value switch
    {
        byte[] bytes => Encoding.UTF8.GetString(bytes), // AMQP strings arrive as bytes
        string text => text,
        _ => null
    };

    private async Task<bool> AlreadyProcessedAsync(Guid messageId, CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EmailWorkerDbContext>();
        return await db.ProcessedMessages.AnyAsync(m => m.MessageId == messageId, cancellationToken);
    }

    private async Task MarkProcessedAsync(Guid messageId, Guid orderId)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<EmailWorkerDbContext>();
            db.ProcessedMessages.Add(new ProcessedMessage { MessageId = messageId, OrderId = orderId, ProcessedAt = DateTime.UtcNow });
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Already recorded by an earlier delivery: fine.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not record message {MessageId} as processed", messageId);
        }
    }

    private async Task CloseAsync()
    {
        foreach (var disposable in new IAsyncDisposable?[] { _consumeChannel, _publishChannel, _connection })
        {
            try { if (disposable is not null) await disposable.DisposeAsync(); } catch { /* already broken */ }
        }
        _consumeChannel = null;
        _publishChannel = null;
        _connection = null;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        await CloseAsync(); // unacknowledged messages go back to the queue
    }
}
