namespace Shared.Data;

// Bound from the "ReadReplica" section of appsettings.json (Requirement 3: read replica).
public sealed class ReadReplicaOptions
{
    public const string SectionName = "ReadReplica";

    // false = every read goes to the primary (also used for the "before" benchmark)
    public bool Enabled { get; set; } = true;

    // How long a write position is remembered. Within this window, reads that must see the
    // write wait for the replica to catch up (or go to the primary). Longer than any normal lag.
    public int ConsistencyWindowSeconds { get; set; } = 120;

    // After a replica error, send all reads to the primary for this long before trying again.
    public int FailureCooldownSeconds { get; set; } = 30;
}
