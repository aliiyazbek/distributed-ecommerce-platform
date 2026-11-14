namespace Shared.Data;

// Ordered so the "strongest" source of a request wins: if any read used the primary, report PRIMARY.
public enum ReadSource
{
    None = 0,
    Replica = 1,
    Primary = 2
}

// One per request (scoped). The read router records where reads went; the HTTP layer writes it
// as the X-Db-Read header so tests (and JMeter) can see replica vs primary routing.
public sealed class ReadSourceStatus
{
    public ReadSource Source { get; private set; } = ReadSource.None;

    public void Record(ReadSource source)
    {
        if (source > Source) Source = source;
    }
}
