using Microsoft.Extensions.Options;

namespace Shared.Data;

// Shared by all requests (singleton). After a replica failure - or anything that makes
// replica reads unsafe - IsUnavailable stays true for the cooldown and all reads use the primary.
public sealed class ReplicaHealth
{
    private readonly TimeSpan _cooldown;
    private long _skipUntilTicks; // UTC ticks; 0 = replica usable

    public ReplicaHealth(IOptions<ReadReplicaOptions> options)
    {
        _cooldown = TimeSpan.FromSeconds(options.Value.FailureCooldownSeconds);
    }

    public bool IsUnavailable => DateTime.UtcNow.Ticks < Interlocked.Read(ref _skipUntilTicks);

    public void ReportFailure() =>
        Interlocked.Exchange(ref _skipUntilTicks, DateTime.UtcNow.Add(_cooldown).Ticks);
}
