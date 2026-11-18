using System.Threading.Channels;

namespace Modules.Orders.Persistence.Outbox;

// Wakes the outbox publisher right after an outbox row is committed, so the event reaches RabbitMQ
// immediately instead of on the next poll. Singleton. Many signals in a row collapse into one wake-up.
public sealed class OutboxSignal
{
    private readonly Channel<bool> _channel = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });

    public void Notify() => _channel.Writer.TryWrite(true);

    // Returns when notified or after `timeout`, whichever comes first.
    public async Task WaitAsync(TimeSpan timeout, CancellationToken stoppingToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        timeoutCts.CancelAfter(timeout);
        try
        {
            await _channel.Reader.ReadAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
        {
            // Timeout reached: time for the regular poll.
        }
    }
}
