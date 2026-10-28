using Microsoft.EntityFrameworkCore;
using Modules.Orders.Entities;

namespace Modules.Orders.Persistence;

public class OrdersDbContext : DbContext
{
    public OrdersDbContext(DbContextOptions<OrdersDbContext> options) : base(options) { }

    public DbSet<Order> Orders { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("orders");
        base.OnModelCreating(modelBuilder);
    }
}