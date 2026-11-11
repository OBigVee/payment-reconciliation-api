using Microsoft.EntityFrameworkCore;

namespace PaymentReconciliation.Api.Data;

internal record Payment(Guid Id, long AmountMinor, string Currency, string Status, DateTimeOffset CreatedAt);

internal record ProviderEvent(string? EventId, Guid PaymentId, string? Status);

internal sealed class PaymentsDbContext(DbContextOptions<PaymentsDbContext> options) : DbContext(options)
{
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<ProviderEvent> ProviderEvents => Set<ProviderEvent>();

    public async Task InitializeDevelopmentSchemaAsync()
    {
        // Use a dedicated development database.
        await Database.EnsureCreatedAsync();
        // EnsureCreated only initializes an empty database. Add the event table to databases from the first milestone.
        await Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "ProviderEvents" (
                "EventId" character varying(128) PRIMARY KEY,
                "PaymentId" uuid NOT NULL,
                "Status" text NOT NULL
            )
            """);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProviderEvent>().HasKey(providerEvent => providerEvent.EventId);
        modelBuilder.Entity<ProviderEvent>().Property(providerEvent => providerEvent.EventId).HasMaxLength(128);
        modelBuilder.Entity<ProviderEvent>().Property(providerEvent => providerEvent.Status).IsRequired();
    }
}
