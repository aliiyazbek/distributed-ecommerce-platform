using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Shared.Data;

// Attached to the PRIMARY DbContexts. After a write is committed it records the primary's WAL
// position for the writing user (and any extra scopes), so later reads can check whether the
// replica already contains it. Services don't change: this runs inside SaveChanges.
//
// It never throws: the write is already committed. If recording fails, replica reads are
// paused for the cooldown, so nobody can read data older than their own write.
public sealed class WritePositionInterceptor : ISaveChangesInterceptor, IDbTransactionInterceptor
{
    private readonly IWritePositionStore _positions;
    private readonly ReplicaHealth _health;
    private readonly ICurrentUser _currentUser;
    private readonly ReadReplicaOptions _options;
    private readonly ILogger<WritePositionInterceptor> _logger;
    private readonly string _primaryConnectionString;
    private readonly IReadOnlyCollection<string> _extraScopes;
    private bool _saving; // true while EF's own SaveChanges is running (its internal transaction is handled in SavedChanges)

    public WritePositionInterceptor(
        IWritePositionStore positions,
        ReplicaHealth health,
        ICurrentUser currentUser,
        IOptions<ReadReplicaOptions> options,
        ILogger<WritePositionInterceptor> logger,
        string primaryConnectionString,
        IReadOnlyCollection<string> extraScopes)
    {
        _positions = positions;
        _health = health;
        _currentUser = currentUser;
        _options = options.Value;
        _logger = logger;
        _primaryConnectionString = primaryConnectionString;
        _extraScopes = extraScopes;
    }

    // Convenience factory for AddDbContext((sp, options) => ...): `extraScopes` are recorded for
    // every write made through that context (e.g. "products" for the shared catalog).
    public static WritePositionInterceptor Create(IServiceProvider services, string primaryConnectionString, params string[] extraScopes) =>
        new(services.GetRequiredService<IWritePositionStore>(),
            services.GetRequiredService<ReplicaHealth>(),
            services.GetRequiredService<ICurrentUser>(),
            services.GetRequiredService<IOptions<ReadReplicaOptions>>(),
            services.GetRequiredService<ILogger<WritePositionInterceptor>>(),
            primaryConnectionString,
            extraScopes);

    // ---------- SaveChanges ----------

    public InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        _saving = true;
        return result;
    }

    public ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        _saving = true;
        return ValueTask.FromResult(result);
    }

    public int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        _saving = false;
        if (ShouldRecordAfterSave(eventData.Context, result))
            RecordAsync().GetAwaiter().GetResult();
        return result;
    }

    public async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        _saving = false;
        if (ShouldRecordAfterSave(eventData.Context, result))
            await RecordAsync();
        return result;
    }

    public void SaveChangesFailed(DbContextErrorEventData eventData) => _saving = false;

    public Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        _saving = false;
        return Task.CompletedTask;
    }

    // ---------- Explicit transactions: the changes become visible only at commit ----------

    public void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData)
    {
        if (!_saving) RecordAsync().GetAwaiter().GetResult();
    }

    public async Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        if (!_saving) await RecordAsync();
    }

    // Inside a caller's open transaction nothing is visible yet: TransactionCommitted records it.
    private static bool ShouldRecordAfterSave(DbContext? context, int result) =>
        result > 0 && context is not null && context.Database.CurrentTransaction is null;

    private async Task RecordAsync()
    {
        if (!_options.Enabled) return;

        var scopes = new List<string>(_extraScopes);
        if (_currentUser.UserId is { } userId) scopes.Add(ConsistencyScopes.User(userId));
        if (scopes.Count == 0) return;

        try
        {
            // After commit, the primary's current WAL position is at or beyond the commit record,
            // so a replica that has replayed this position contains the write.
            await using var connection = new NpgsqlConnection(_primaryConnectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("SELECT pg_current_wal_lsn()::text", connection);
            var position = WalPosition.Parse((string)(await command.ExecuteScalarAsync())!);

            if (!await _positions.TryRecordAsync(scopes, position))
            {
                _health.ReportFailure();
                _logger.LogWarning("Write position not stored; replica reads paused for the cooldown");
            }
        }
        catch (Exception ex)
        {
            _health.ReportFailure();
            _logger.LogWarning(ex, "Could not record write position; replica reads paused for the cooldown");
        }
    }
}
