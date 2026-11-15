using Microsoft.EntityFrameworkCore;

namespace Modules.Orders.Persistence;

// Same model as OrdersDbContext, connected to the READ REPLICA (Requirement 3).
// Read-only by design: the replica rejects writes anyway, and this fails fast in code.
// Never used for migrations; use OrdersDbContext for those.
public sealed class OrdersReadDbContext : OrdersDbContext
{
    public OrdersReadDbContext(DbContextOptions<OrdersReadDbContext> options) : base(options) { }

    public override int SaveChanges(bool acceptAllChangesOnSuccess) =>
        throw new InvalidOperationException("OrdersReadDbContext is read-only (read replica). Write through OrdersDbContext.");

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("OrdersReadDbContext is read-only (read replica). Write through OrdersDbContext.");
}
