using Microsoft.EntityFrameworkCore;
using Modules.Products.Entities;

namespace Modules.Products.Persistence;

public class ProductsDbContext : DbContext
{
    public ProductsDbContext(DbContextOptions<ProductsDbContext> options) : base(options) { }

    public DbSet<Product> Products { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("products"); // Keeps products isolated in the DB

        modelBuilder.Entity<Product>(builder =>
        {
            // Product list: ORDER BY CreatedAt, Id LIMIT n (Requirement 3).
            // Lets Postgres read the page straight from the index instead of sorting the whole table.
            builder.HasIndex(p => new { p.CreatedAt, p.Id })
                .HasDatabaseName("IX_Products_CreatedAt_Id");

            // The category filter uses an expression index on lower("Category"), which EF Core
            // cannot express here; it is created with raw SQL in the AddProductListIndexes migration.
        });

        base.OnModelCreating(modelBuilder);
    }
}