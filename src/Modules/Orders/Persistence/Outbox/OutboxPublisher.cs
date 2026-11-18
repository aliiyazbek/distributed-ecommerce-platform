using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using Shared.Messaging;

namespace Modules.Orders.Persistence.Outbox;

// Requirement 4: moves committed outbox rows to RabbitMQ (transactional outbox, "polling publisher").
// Woken by OutboxSignal right after a commit, and polls every PollInterval as a fallback
// (e.g. while RabbitMQ was down). A row is deleted only after RabbitMQ confirmed it stored the message,
// so nothing is lost; a crash between confirm and delete can publish a row twice (at-least-once),
// which consumers handle by ignoring a message_id they already processed.
public sealed class OutboxPublisher : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
    private const int BatchSize = 50;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly OutboxSignal _signal;
    private readonly RabbitMqOptions _options;
    private readonly ILogger<OutboxPublisher> _logger;

    private IConnection? _connection;
    private IChannel? _channel;

    public OutboxPublisher(
        IServiceScopeFactory scopeFactory,
        OutboxSignal signal,
        IOptions<RabbitMqOptions> options,
        ILogger<OutboxPublisher> logger)
    {
        _scopeFactory = scopeFactory;
        _signal = signal;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var channel = await GetChannelAsync(stoppingToken);

                // Drain the backlog: keep going while full batches come back.
                while (await PublishBatchAsync(channel, stoppingToken) == BatchSize) { }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Outbox publishing failed; messages stay in the outbox and will be retried");
                await CloseAsync(); // reconnect on the next round
            }

            await _signal.WaitAsync(PollInterval, stoppingToken);
        }
    }

    // Publishes up to BatchSize rows and deletes them in one transaction. Returns how many were published.
    private async Task<int> PublishBatchAsync(IChannel channel, CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // SKIP LOCKED: rows already taken by another API instance are skipped, not published twice.
        var batch = await db.OutboxMessages
            .FromSql($"""SELECT * FROM orders."OutboxMessages" ORDER BY "OccurredAt" LIMIT {BatchSize} FOR UPDATE SKIP LOCKED""")
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        if (batch.Count == 0) return 0;

        var published = new List<Guid>(batch.Count);
        try
        {
            foreach (var message in batch)
            {
                var properties = new BasicProperties
                {
                    MessageId = message.Id.ToString(),            // consumers use it to drop duplicates
                    ContentType = "application/json",
                    DeliveryMode = DeliveryModes.Persistent,       // survives a RabbitMQ restart
                    Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds()),
                    Type = message.RoutingKey
                };

                // Publisher confirms are on: this returns only after RabbitMQ stored the message,
                // and throws if RabbitMQ refused it.
                await channel.BasicPublishAsync(OrderEventTopology.Exchange, message.RoutingKey,
                    mandatory: false, basicProperties: properties,
                    body: Encoding.UTF8.GetBytes(message.Payload), cancellationToken: cancellationToken);

                published.Add(message.Id);
            }
        }
        finally
        {
            // Delete what was confirmed, even if a later message in the batch failed.
            if (published.Count > 0)
            {
                await db.OutboxMessages.Where(m => published.Contains(m.Id)).ExecuteDeleteAsync(CancellationToken.None);
                await transaction.CommitAsync(CancellationToken.None);
            }
        }

        return published.Count;
    }

    private async Task<IChannel> GetChannelAsync(CancellationToken cancellationToken)
    {
        if (_channel is { IsOpen: true }) return _channel;

        await CloseAsync();
        _connection = await _options.CreateConnectionFactory("ecommerce-api-outbox").CreateConnectionAsync(cancellationToken);
        _channel = await _connection.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
            cancellationToken);

        // Make sure the exchange and the email queues exist before the first publish.
        await RabbitMqTopology.DeclareAsync(_channel, cancellationToken);
        _logger.LogInformation("Outbox publisher connected to RabbitMQ");
        return _channel;
    }

    private async Task CloseAsync()
    {
        try { if (_channel is not null) await _channel.DisposeAsync(); } catch { /* already broken */ }
        try { if (_connection is not null) await _connection.DisposeAsync(); } catch { /* already broken */ }
        _channel = null;
        _connection = null;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        await CloseAsync();
    }
}
