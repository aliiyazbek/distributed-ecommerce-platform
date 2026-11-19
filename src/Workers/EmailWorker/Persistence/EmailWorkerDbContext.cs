using Microsoft.EntityFrameworkCore;

namespace EmailWorker.Persistence;

// The worker's own data, in its own "email" schema (it never touches the API modules' tables).
public sealed class EmailWorkerDbContext : DbContext
{
    public EmailWorkerDbContext(DbContextOptions<EmailWorkerDbContext> options) : base(options) { }

    public DbSet<ProcessedMessage> ProcessedMessages => Set<ProcessedMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("email");

        modelBuilder.Entity<ProcessedMessage>(builder =>
        {
            builder.ToTable("ProcessedMessages");
            builder.HasKey(m => m.MessageId); // primary key = the duplicate check
            builder.HasIndex(m => m.OrderId);
        });
    }
}
