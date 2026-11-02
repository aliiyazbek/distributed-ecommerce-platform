using Microsoft.EntityFrameworkCore;
using Modules.Users.Entities;

namespace Modules.Users.Persistence;

public class UsersDbContext : DbContext
{
    public UsersDbContext(DbContextOptions<UsersDbContext> options) : base(options) { }

    public DbSet<User> Users { get; set; }
    public DbSet<Role> Roles { get; set; }
    
    // Add DbSets for the new tables
    public DbSet<CustomerProfile> CustomerProfiles { get; set; }
    public DbSet<AdminProfile> AdminProfiles { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("users");

        // ... Keep your existing Roles configuration ...

        // 1. Configure CustomerProfile 1-to-1 relationship
        modelBuilder.Entity<CustomerProfile>()
            .HasKey(cp => cp.UserId); // Explicitly define UserId as the PK

        modelBuilder.Entity<User>()
            .HasOne(u => u.CustomerProfile)
            .WithOne(cp => cp.User)
            .HasForeignKey<CustomerProfile>(cp => cp.UserId) // UserId is also the FK
            .OnDelete(DeleteBehavior.Cascade); // Automatically delete profile if user is deleted

        // 2. Configure AdminProfile 1-to-1 relationship
        modelBuilder.Entity<AdminProfile>()
            .HasKey(ap => ap.UserId);

        modelBuilder.Entity<User>()
            .HasOne(u => u.AdminProfile)
            .WithOne(ap => ap.User)
            .HasForeignKey<AdminProfile>(ap => ap.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // ... Keep your seeded default roles ...
        // Explicit Data Seeding: Prevent EF Core from deleting required system roles
        modelBuilder.Entity<Role>().HasData(
            new Role { Id = 1, Name = "Admin" },
            new Role { Id = 2, Name = "Customer" }
        );

        base.OnModelCreating(modelBuilder);
    }
}