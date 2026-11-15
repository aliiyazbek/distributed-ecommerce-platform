namespace Shared.Caching;

// Shared by all requests (registered as a singleton).
// After a Redis failure, IsOpen stays true for the cooldown, so requests go straight
// to the database instead of each waiting for a Redis timeout.
public sealed class CacheCircuitBreaker
{
    private readonly TimeSpan _cooldown;
    private long _skipUntilTicks; // UTC ticks; 0 means "Redis is fine"

    public CacheCircuitBreaker(TimeSpan cooldown)
    {
        _cooldown = cooldown;
    }

    public bool IsOpen => DateTime.UtcNow.Ticks < Interlocked.Read(ref _skipUntilTicks);

    public void ReportFailure() =>
        Interlocked.Exchange(ref _skipUntilTicks, DateTime.UtcNow.Add(_cooldown).Ticks);
}
