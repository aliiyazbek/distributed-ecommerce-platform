using System.Collections;
using System.Collections.Concurrent;
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
//
// Parallel sending: sending an email is I/O-bound (the worker mostly waits for the mail server), so the
// RabbitMQ client dispatches up to Email:MaxParallelSends deliveries at the same time on the .NET thread
// pool (no custom threads). Prefetch equals that number, so each parallel slot has one message ready.
// The few operations on the shared RabbitMQ channels (ack, reject, DLQ publish) are serialized by a
// small lock; the slow part, sending the email, runs fully in parallel.
public sealed class OrderCreatedConsumer : BackgroundService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IEmailSender _emailSender;
    private readonly RabbitMqOptions _options;
    private readonly ILogger<OrderCreatedConsumer> _logger;
    private readonly ushort _parallelism;

    // Channel operations are not run concurrently: one handler at a time may ack, reject or publish.
    private readonly SemaphoreSlim _channelLock = new(1, 1);

    // Message ids being processed right now. Two copies of the same message (e.g. an outbox re-publish)
    // handled at the same moment would both pass the ProcessedMessages check and send two emails.
    private readonly ConcurrentDictionary<Guid, byte> _inProgress = new();

    private IConnection? _connection;
    private IChannel? _consumeChannel;
    private IChannel? _publishChannel; // separate channel with publisher confirms, for the DLQ copies

    public OrderCreatedConsumer(
        IServiceScopeFactory scopeFactory,
        IEmailSender emailSender,
        IOptions<RabbitMqOptions> options,
        IOptions<EmailOptions> emailOptions,
        ILogger<OrderCreatedConsumer> logger)
    {
        _scopeFactory = scopeFactory;
        _emailSender = emailSender;
        _options = options.Value;
        _parallelism = (ushort)emailOptions.Value.MaxParallelSends;
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
        // consumerDispatchConcurrency: up to _parallelism deliveries are handled at the same time.
        _consumeChannel = await _connection.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: false, publisherConfirmationTrackingEnabled: false,
                consumerDispatchConcurrency: _parallelism),
            stoppingToken);

        // Same declarations as the API: the queues exist even if the API has never run.
        await RabbitMqTopology.DeclareAsync(_consumeChannel, stoppingToken);

        // Prefetch = parallelism: at most that many unacknowledged messages in this worker at a time.
        await _consumeChannel.BasicQosAsync(prefetchSize: 0, prefetchCount: _parallelism, global: false, stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(_consumeChannel);
        consumer.ReceivedAsync += (_, delivery) => HandleAsync(delivery, stoppingToken);

        await _consumeChannel.BasicConsumeAsync(RabbitMqTopology.EmailQueue, autoAck: false, consumer, stoppingToken);
        _logger.LogInformation("Email worker consuming '{Queue}' ({Parallel} emails in parallel)", RabbitMqTopology.EmailQueue, _parallelism);
    }

    private async Task HandleAsync(BasicDeliverEventArgs delivery, CancellationToken stoppingToken)
    {
        var attempt = RejectionCount(delivery.BasicProperties) + 1;
        Guid? claimed = null;

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

            // Another handler is processing a copy of this very message right now: this copy is a duplicate.
            if (!_inProgress.TryAdd(messageId, 0))
            {
                _logger.LogInformation("Message {MessageId} is already being processed; dropping this duplicate copy", messageId);
                await AckAsync(delivery);
                return;
            }
            claimed = messageId;

            // Idempotency: this message was already handled (redelivery or duplicate publish).
            if (await AlreadyProcessedAsync(messageId, stoppingToken))
            {
                _logger.LogInformation("Message {MessageId} (order {OrderId}) already processed; skipping", messageId, order.OrderId);
                await AckAsync(delivery);
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
                await RejectAsync(delivery);
                return;
            }

            // The email is out. Record it, then acknowledge. If recording fails we still acknowledge:
            // a second email would be worse than a missing row.
            await MarkProcessedAsync(messageId, order.OrderId);
            await AckAsync(delivery);
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
                await RejectAsync(delivery);
        }
        finally
        {
            if (claimed is { } id) _inProgress.TryRemove(id, out _);
        }
    }

    private async Task AckAsync(BasicDeliverEventArgs delivery)
    {
        await _channelLock.WaitAsync();
        try { await _consumeChannel!.BasicAckAsync(delivery.DeliveryTag, multiple: false, CancellationToken.None); }
        finally { _channelLock.Release(); }
    }

    private async Task RejectAsync(BasicDeliverEventArgs delivery)
    {
        await _channelLock.WaitAsync();
        try { await _consumeChannel!.BasicRejectAsync(delivery.DeliveryTag, requeue: false, CancellationToken.None); }
        finally { _channelLock.Release(); }
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

        var moved = false;
        await _channelLock.WaitAsync();
        try
        {
            await _publishChannel!.BasicPublishAsync(exchange: "", routingKey: RabbitMqTopology.EmailDeadLetterQueue,
                mandatory: true, basicProperties: properties, body: delivery.Body, cancellationToken: CancellationToken.None);
            await _consumeChannel!.BasicAckAsync(delivery.DeliveryTag, multiple: false, CancellationToken.None);
            moved = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not move delivery {DeliveryTag} to the DLQ; rejecting it to the retry queue", delivery.DeliveryTag);
        }
        finally
        {
            _channelLock.Release();
        }
        if (!moved) await RejectAsync(delivery);
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
