using System.Globalization;

namespace Shared.Data;

// A PostgreSQL WAL position (LSN), e.g. "0/3A0001F8": how far the primary has written,
// or how far a replica has replayed. Comparing two positions tells whether a replica
// already contains a given write.
public readonly record struct WalPosition(ulong Value) : IComparable<WalPosition>
{
    // Parses PostgreSQL's "X/Y" text form (two hex numbers: high and low 32 bits).
    public static WalPosition Parse(string text)
    {
        var parts = text.Split('/');
        if (parts.Length != 2)
            throw new FormatException($"Invalid WAL position '{text}'.");

        var high = ulong.Parse(parts[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var low = ulong.Parse(parts[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return new WalPosition((high << 32) | low);
    }

    // Fixed-width hex: sorts correctly as a plain string (used for the atomic max in Redis).
    public string ToSortableString() => Value.ToString("X16", CultureInfo.InvariantCulture);

    public static WalPosition FromSortableString(string text) =>
        new(ulong.Parse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture));

    // PostgreSQL's pg_lsn text form, usable as "{value}::pg_lsn" in SQL.
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{Value >> 32:X}/{Value & 0xFFFFFFFF:X}");

    public int CompareTo(WalPosition other) => Value.CompareTo(other.Value);
}
