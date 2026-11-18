using System.Data;
using Microsoft.EntityFrameworkCore;
using Modules.Orders.Entities;
using Npgsql;
using NpgsqlTypes;

namespace Modules.Orders.Persistence;

// Bulk test data for performance testing (Requirement 3): fills orders."Orders" and orders."OrderItems"
// so list queries and their indexes can be measured on a realistic table size.
//
// Uses PostgreSQL binary COPY instead of EF Core inserts: one round trip per table, no change tracking,
// and no need to bypass the entities' private setters (CreatedAt must be spread over the past year for
// the ORDER BY CreatedAt indexes to be exercised). Needs no extra NuGet packages.
public static class OrderSeeder
{
    private static readonly OrderStatus[] Statuses = Enum.GetValues<OrderStatus>();

    /// <summary>
    /// Inserts <paramref name="orderCount"/> orders for <paramref name="customerId"/>, each with
    /// <paramref name="itemsPerOrder"/> lines of the given product. Does nothing if any order exists.
    /// Write to the primary context only (never the read replica); the replica picks the rows up by streaming.
    /// </summary>
    public static async Task SeedOrdersAsync(
        OrdersDbContext context,
        Guid customerId,
        Guid productId,
        string productName,
        decimal unitPrice,
        int orderCount = 200_000,
        int itemsPerOrder = 2,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(orderCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(itemsPerOrder);
        ArgumentOutOfRangeException.ThrowIfNegative(unitPrice);

        if (await context.Orders.AnyAsync(cancellationToken)) return;

        var random = new Random(42); // fixed seed: the same data set on every machine, so results are comparable
        var now = DateTime.UtcNow;

        var orders = new List<SeedOrder>(orderCount);
        var items = new List<SeedItem>(orderCount * itemsPerOrder);

        for (var i = 0; i < orderCount; i++)
        {
            var orderId = Guid.NewGuid();
            var createdAt = now.AddSeconds(-random.Next(0, 365 * 24 * 60 * 60));
            var total = 0m;

            for (var j = 0; j < itemsPerOrder; j++)
            {
                var quantity = random.Next(1, 6);
                total += unitPrice * quantity;
                items.Add(new SeedItem(Guid.NewGuid(), orderId, quantity, createdAt));
            }

            orders.Add(new SeedOrder(orderId, Statuses[random.Next(Statuses.Length)], total, createdAt));
        }

        var connection = (NpgsqlConnection)context.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere) await connection.OpenAsync(cancellationToken);

        try
        {
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            await using (var writer = await connection.BeginBinaryImportAsync(
                """COPY orders."Orders" ("Id", "UserId", "Status", "TotalPrice", "CreatedAt") FROM STDIN (FORMAT BINARY)""",
                cancellationToken))
            {
                foreach (var o in orders)
                {
                    await writer.StartRowAsync(cancellationToken);
                    await writer.WriteAsync(o.Id, NpgsqlDbType.Uuid, cancellationToken);
                    await writer.WriteAsync(customerId, NpgsqlDbType.Uuid, cancellationToken);
                    await writer.WriteAsync(o.Status.ToString(), NpgsqlDbType.Varchar, cancellationToken); // stored as string (HasConversion<string>)
                    await writer.WriteAsync(o.TotalPrice, NpgsqlDbType.Numeric, cancellationToken);
                    await writer.WriteAsync(o.CreatedAt, NpgsqlDbType.TimestampTz, cancellationToken);
                }
                await writer.CompleteAsync(cancellationToken);
            }

            await using (var writer = await connection.BeginBinaryImportAsync(
                """COPY orders."OrderItems" ("Id", "OrderId", "ProductId", "ProductName", "UnitPrice", "Quantity", "CreatedAt") FROM STDIN (FORMAT BINARY)""",
                cancellationToken))
            {
                foreach (var item in items)
                {
                    await writer.StartRowAsync(cancellationToken);
                    await writer.WriteAsync(item.Id, NpgsqlDbType.Uuid, cancellationToken);
                    await writer.WriteAsync(item.OrderId, NpgsqlDbType.Uuid, cancellationToken);
                    await writer.WriteAsync(productId, NpgsqlDbType.Uuid, cancellationToken);
                    await writer.WriteAsync(productName, NpgsqlDbType.Varchar, cancellationToken);
                    await writer.WriteAsync(unitPrice, NpgsqlDbType.Numeric, cancellationToken);
                    await writer.WriteAsync(item.Quantity, NpgsqlDbType.Integer, cancellationToken);
                    await writer.WriteAsync(item.CreatedAt, NpgsqlDbType.TimestampTz, cancellationToken);
                }
                await writer.CompleteAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            if (openedHere) await connection.CloseAsync();
        }
    }

    private readonly record struct SeedOrder(Guid Id, OrderStatus Status, decimal TotalPrice, DateTime CreatedAt);
    private readonly record struct SeedItem(Guid Id, Guid OrderId, int Quantity, DateTime CreatedAt);
}
