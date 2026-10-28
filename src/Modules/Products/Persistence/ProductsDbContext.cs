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
        base.OnModelCreating(modelBuilder);
    }
}