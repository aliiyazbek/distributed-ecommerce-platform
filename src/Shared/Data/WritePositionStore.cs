using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shared.Caching;
using StackExchange.Redis;

namespace Shared.Data;

// Remembers, per consistency scope, the latest primary WAL position written in that scope.
// Stored in Redis so every API instance sees it (needed once the API is load-balanced).
public interface IWritePositionStore
{
    // Returns false if the position could not be stored.
    Task<bool> TryRecordAsync(IReadOnlyCollection<string> scopes, WalPosition position);

    // Known = false when the store is unavailable: the caller cannot tell what the user wrote,
    // so it must not read from the replica. Position = null when nothing was written recently.
    Task<(bool Known, WalPosition? Position)> GetLatestAsync(IReadOnlyCollection<string> scopes);
}

public sealed class RedisWritePositionStore : IWritePositionStore
{
    private const string KeyPrefix = "ecommerce:rw:";

    // Atomic "keep the maximum": two concurrent writes can finish in either order, and the
    // stored position must never move backwards. Fixed-width hex compares correctly as text.
    private const string StoreMaxScript = """
        local current = redis.call('GET', KEYS[1])
        if (not current) or current < ARGV[1] then
            redis.call('SET', KEYS[1], ARGV[1], 'EX', ARGV[2])
        else
            redis.call('EXPIRE', KEYS[1], ARGV[2])
        end
        return 1
        """;

    private readonly IConnectionMultiplexer _redis;
    private readonly CacheCircuitBreaker _redisBreaker;
    private readonly int _windowSeconds;
    private readonly ILogger<RedisWritePositionStore> _logger;

    public RedisWritePositionStore(
        IConnectionMultiplexer redis,
        CacheCircuitBreaker redisBreaker,
        IOptions<ReadReplicaOptions> options,
        ILogger<RedisWritePositionStore> logger)
    {
        _redis = redis;
        _redisBreaker = redisBreaker;
        _windowSeconds = options.Value.ConsistencyWindowSeconds;
        _logger = logger;
    }

    public async Task<bool> TryRecordAsync(IReadOnlyCollection<string> scopes, WalPosition position)
    {
        if (scopes.Count == 0) return true;
        if (_redisBreaker.IsOpen) return false;

        try
        {
            var db = _redis.GetDatabase();
            var value = position.ToSortableString();
            foreach (var scope in scopes)
            {
                await db.ScriptEvaluateAsync(StoreMaxScript,
                    new RedisKey[] { KeyPrefix + scope },
                    new RedisValue[] { value, _windowSeconds });
            }
            return true;
        }
        catch (Exception ex)
        {
            _redisBreaker.ReportFailure();
            _logger.LogWarning(ex, "Could not record write position for {Scopes}", string.Join(",", scopes));
            return false;
        }
    }

    public async Task<(bool Known, WalPosition? Position)> GetLatestAsync(IReadOnlyCollection<string> scopes)
    {
        if (scopes.Count == 0) return (true, null);
        if (_redisBreaker.IsOpen) return (false, null);

        try
        {
            var keys = scopes.Select(s => (RedisKey)(KeyPrefix + s)).ToArray();
            var values = await _redis.GetDatabase().StringGetAsync(keys);

            WalPosition? latest = null;
            foreach (var value in values)
            {
                if (value.IsNullOrEmpty) continue;
                var position = WalPosition.FromSortableString(value.ToString());
                if (latest is null || position.CompareTo(latest.Value) > 0) latest = position;
            }
            return (true, latest);
        }
        catch (Exception ex)
        {
            _redisBreaker.ReportFailure();
            _logger.LogWarning(ex, "Could not read write positions for {Scopes}", string.Join(",", scopes));
            return (false, null);
        }
    }
}
