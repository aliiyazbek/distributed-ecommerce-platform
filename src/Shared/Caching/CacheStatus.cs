namespace Shared.Caching;

// Ordered from best to worst, so the "worst" outcome of a request wins.
public enum CacheOutcome
{
    None = 0,   // no cache lookup happened in this request
    Hit = 1,    // answered from Redis
    Miss = 2,   // not in Redis, loaded from the database
    Bypass = 3  // Redis failed or is being skipped, loaded from the database
}

// One instance per HTTP request (registered as scoped).
// Caching code records what happened; the HTTP layer reads it to write the X-Cache header.
public sealed class CacheStatus
{
    public CacheOutcome Outcome { get; private set; } = CacheOutcome.None;

    public void Record(CacheOutcome outcome)
    {
        if (outcome > Outcome) Outcome = outcome;
    }
}