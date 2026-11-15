using System.Net.Sockets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Shared.Data;

// Decides, per read, whether the read replica may answer it (Requirement 3: read replica).
//
// The replica is used only when it is SAFE:
//   1. replicas are enabled and the replica is not in its failure cooldown, and
//   2. the replica has already replayed every write the reader must see: the current user's own
//      writes plus any extra scopes (e.g. "products" for the shared catalog).
// Otherwise the read goes to the primary. If the replica fails mid-query, the read is retried
// on the primary. A read can therefore be slower, but never staler than the user's own writes.
public sealed class ReadRouter
{
    private readonly ReadReplicaOptions _options;
    private readonly ReplicaHealth _health;
    private readonly IWritePositionStore _positions;
    private readonly ICurrentUser _currentUser;
    private readonly ReadSourceStatus _status;
    private readonly ILogger<ReadRouter> _logger;

    public ReadRouter(
        IOptions<ReadReplicaOptions> options,
        ReplicaHealth health,
        IWritePositionStore positions,
        ICurrentUser currentUser,
        ReadSourceStatus status,
        ILogger<ReadRouter> logger)
    {
        _options = options.Value;
        _health = health;
        _positions = positions;
        _currentUser = currentUser;
        _status = status;
        _logger = logger;
    }

    // Runs `query` against the replica when safe, otherwise (or on replica failure) the primary.
    // `primary` and `replica` are two contexts with the same model; `replica` is read-only.
    public async Task<T> ReadAsync<TContext, T>(
        TContext primary,
        TContext replica,
        Func<TContext, CancellationToken, Task<T>> query,
        IReadOnlyCollection<string>? scopes = null,
        CancellationToken cancellationToken = default)
        where TContext : DbContext
    {
        if (await CanUseReplicaAsync(replica, scopes, cancellationToken))
        {
            try
            {
                var result = await query(replica, cancellationToken);
                _status.Record(ReadSource.Replica);
                return result;
            }
            catch (Exception ex) when (IsReplicaProblem(ex, out var replicaDown))
            {
                if (replicaDown) _health.ReportFailure();
                _logger.LogWarning(ex, "Replica read failed; retrying on the primary");
            }
        }

        var primaryResult = await query(primary, cancellationToken);
        _status.Record(ReadSource.Primary);
        return primaryResult;
    }

    private async Task<bool> CanUseReplicaAsync(DbContext replica, IReadOnlyCollection<string>? scopes, CancellationToken cancellationToken)
    {
        if (!_options.Enabled || _health.IsUnavailable) return false;

        var allScopes = new List<string>(scopes ?? Array.Empty<string>());
        if (_currentUser.UserId is { } userId) allScopes.Add(ConsistencyScopes.User(userId));

        var (known, required) = await _positions.GetLatestAsync(allScopes);
        if (!known) return false;          // can't tell what must be visible -> be safe
        if (required is null) return true; // nothing written recently in these scopes

        try
        {
            var caughtUp = await replica.Database
                .SqlQuery<bool?>($"SELECT pg_last_wal_replay_lsn() >= {required.Value.ToString()}::pg_lsn AS \"Value\"")
                .SingleAsync(cancellationToken);

            if (caughtUp != true)
                _logger.LogDebug("Replica has not replayed {Position} yet; reading from the primary", required.Value);

            return caughtUp == true;
        }
        catch (Exception ex) when (IsReplicaProblem(ex, out var replicaDown))
        {
            if (replicaDown) _health.ReportFailure();
            _logger.LogWarning(ex, "Could not check replica position; reading from the primary");
            return false;
        }
    }

    // True when the error comes from the replica itself (so the primary can answer instead).
    // replicaDown = the replica is unreachable/unhealthy, so stop using it for the cooldown.
    private static bool IsReplicaProblem(Exception ex, out bool replicaDown)
    {
        replicaDown = false;
        if (ex is OperationCanceledException) return false;

        if (Find<PostgresException>(ex) is { } pg)
        {
            // 40001: "canceling statement due to conflict with recovery" - normal on a hot standby.
            if (pg.SqlState == PostgresErrorCodes.SerializationFailure) return true;

            // 08xxx connection errors, 57xxx shutdown/starting up, 53300 too many connections.
            if (pg.SqlState.StartsWith("08") || pg.SqlState.StartsWith("57") || pg.SqlState == PostgresErrorCodes.TooManyConnections)
            {
                replicaDown = true;
                return true;
            }

            return false; // a real SQL error: the primary would fail the same way
        }

        if (Find<NpgsqlException>(ex) is not null || Find<SocketException>(ex) is not null || Find<TimeoutException>(ex) is not null)
        {
            replicaDown = true;
            return true;
        }

        return false;
    }

    private static TException? Find<TException>(Exception? ex) where TException : Exception
    {
        for (; ex is not null; ex = ex.InnerException)
            if (ex is TException match) return match;
        return null;
    }
}
