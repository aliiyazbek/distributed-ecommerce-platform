using Microsoft.EntityFrameworkCore;
using Modules.Orders.Entities;

namespace Modules.Orders.Persistence;

public class OrdersDbContext : DbContext
{
    public OrdersDbContext(DbContextOptions<OrdersDbContext> options) : base(options) { }

    public DbSet<Order> Orders { get; set; }
    public DbSet<OrderItem> OrderItems { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("orders");

        modelBuilder.Entity<Order>(builder =>
        {
            builder.HasKey(o => o.Id);
            builder.HasIndex(o => o.UserId);
            builder.Property(o => o.Status).HasConversion<string>().HasMaxLength(20);
            builder.Property(o => o.TotalPrice).HasPrecision(18, 2);
            builder.HasMany(o => o.Items)
                .WithOne()
                .HasForeignKey(i => i.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<OrderItem>(builder =>
        {
            builder.HasKey(i => i.Id);
            builder.Property(i => i.ProductName).IsRequired().HasMaxLength(255);
            builder.Property(i => i.UnitPrice).HasPrecision(18, 2);
        });

        base.OnModelCreating(modelBuilder);
    }
}
