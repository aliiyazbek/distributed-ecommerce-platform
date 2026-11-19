using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Shared.Messaging;

namespace Modules.Notifications.Realtime;

public sealed class OrderEventsConsumer(
    IHubContext<OrdersHub> ordersHub,
    IHubContext<AdminOrdersHub> adminHub,
    IOptions<RabbitMqOptions> rabbitMq,
    ILogger<OrderEventsConsumer> logger) : BackgroundService
{
    private const string Queue = "realtime.order-events";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ConsumeAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Real-time consumer could not start, retrying in {Delay}", RetryDelay);
                await Task.Delay(RetryDelay, stoppingToken);
            }
        }
    }

    private async Task ConsumeAsync(CancellationToken stoppingToken)
    {
        var factory = rabbitMq.Value.CreateConnectionFactory("api-realtime");
        await using var connection = await factory.CreateConnectionAsync(stoppingToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);

        await channel.ExchangeDeclareAsync(OrderEventTopology.Exchange, ExchangeType.Topic,
            durable: true, autoDelete: false, cancellationToken: stoppingToken);
        await channel.QueueDeclareAsync(Queue, durable: true, exclusive: false, autoDelete: false,
            cancellationToken: stoppingToken);
        await channel.QueueBindAsync(Queue, OrderEventTopology.Exchange,
            OrderEventTopology.OrderCreatedRoutingKey, cancellationToken: stoppingToken);
        await channel.QueueBindAsync(Queue, OrderEventTopology.Exchange,
            OrderEventTopology.OrderStatusChangedRoutingKey, cancellationToken: stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (_, message) => HandleAsync(channel, message);
        await channel.BasicConsumeAsync(Queue, autoAck: false, consumer, stoppingToken);

        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private async Task HandleAsync(IChannel channel, BasicDeliverEventArgs message)
    {
        try
        {
            var pushed = message.RoutingKey switch
            {
                OrderEventTopology.OrderCreatedRoutingKey => await PushNewOrderAsync(message.Body),
                OrderEventTopology.OrderStatusChangedRoutingKey => await PushStatusChangeAsync(message.Body),
                _ => false
            };

            if (pushed)
            {
                await channel.BasicAckAsync(message.DeliveryTag, multiple: false);
                return;
            }

            logger.LogWarning("Dropped a bad order event {MessageId}", message.BasicProperties.MessageId);
            await channel.BasicRejectAsync(message.DeliveryTag, requeue: false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not push order event {MessageId}", message.BasicProperties.MessageId);
            await channel.BasicNackAsync(message.DeliveryTag, multiple: false, requeue: !message.Redelivered);
        }
    }

    private async Task<bool> PushNewOrderAsync(ReadOnlyMemory<byte> body)
    {
        var order = Read<OrderCreatedEvent>(body);
        if (order is null || order.OrderId == Guid.Empty || order.Items is null)
            return false;

        await adminHub.Clients.All.SendAsync("OrderCreated",
            new { Id = order.OrderId, order.UserId, Status = "Pending", order.TotalPrice, order.CreatedAt, order.Items });
        return true;
    }

    private async Task<bool> PushStatusChangeAsync(ReadOnlyMemory<byte> body)
    {
        var change = Read<OrderStatusChangedEvent>(body);
        if (change is null || change.UserId == Guid.Empty || string.IsNullOrWhiteSpace(change.NewStatus))
            return false;

        await ordersHub.Clients.User(change.UserId.ToString()).SendAsync("OrderStatusChanged",
            new { change.OrderId, Status = change.NewStatus, change.ChangedAt });
        return true;
    }

    private static T? Read<T>(ReadOnlyMemory<byte> body) where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(body.Span, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
