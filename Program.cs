using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("Payments")
    ?? throw new InvalidOperationException("Set ConnectionStrings__Payments before starting the API.");
builder.Services.AddDbContext<PaymentsDbContext>(options => options.UseNpgsql(connectionString));

var app = builder.Build();

app.MapPost("/payments", async (CreatePayment request, PaymentsDbContext db, CancellationToken cancellationToken) =>
{
    if (request.AmountMinor <= 0)
    {
        return Results.BadRequest(new { error = "AmountMinor must be greater than zero." });
    }

    var currency = request.Currency?.Trim().ToUpperInvariant();
    if (currency is null || currency.Length != 3 || !currency.All(char.IsAsciiLetterUpper))
    {
        return Results.BadRequest(new { error = "Currency must be a three-letter code." });
    }

    var payment = new Payment(Guid.NewGuid(), request.AmountMinor, currency, "pending", DateTimeOffset.UtcNow);
    db.Payments.Add(payment);
    await db.SaveChangesAsync(cancellationToken);
    return Results.Created($"/payments/{payment.Id}", payment);
});

app.MapGet("/payments/{id:guid}", async (Guid id, PaymentsDbContext db, CancellationToken cancellationToken) =>
{
    var payment = await db.Payments.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
    return payment is null ? Results.NotFound() : Results.Ok(payment);
});

app.MapPost("/webhooks/provider", async (ProviderEvent request, PaymentsDbContext db, CancellationToken cancellationToken) =>
{
    var eventId = request.EventId?.Trim();
    if (string.IsNullOrEmpty(eventId) || eventId.Length > 128 || request.PaymentId == Guid.Empty
        || request.Status is not ("succeeded" or "failed"))
    {
        return Results.BadRequest(new { error = "Supply an eventId (up to 128 characters), paymentId, and status (succeeded or failed)." });
    }

    await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
    var inserted = await db.Database.ExecuteSqlInterpolatedAsync(
        $"""
        INSERT INTO "ProviderEvents" ("EventId", "PaymentId", "Status")
        VALUES ({eventId}, {request.PaymentId}, {request.Status})
        ON CONFLICT ("EventId") DO NOTHING
        """, cancellationToken);

    if (inserted == 0)
    {
        var existing = await db.ProviderEvents.AsNoTracking()
            .SingleAsync(providerEvent => providerEvent.EventId == eventId, cancellationToken);
        return existing.PaymentId == request.PaymentId && existing.Status == request.Status
            ? Results.Ok(new { applied = false })
            : Results.Conflict(new { error = "This event ID was already used with a different payload." });
    }

    var updated = await db.Payments.Where(payment => payment.Id == request.PaymentId && payment.Status == "pending")
        .ExecuteUpdateAsync(update => update.SetProperty(payment => payment.Status, request.Status), cancellationToken);

    if (updated == 0)
    {
        // Disposing the transaction rolls back the event insert, so a rejected event is never recorded as applied.
        var exists = await db.Payments.AnyAsync(payment => payment.Id == request.PaymentId, cancellationToken);
        return exists
            ? Results.Conflict(new { error = "This payment already has a final status." })
            : Results.NotFound(new { error = "Payment not found." });
    }

    await transaction.CommitAsync(cancellationToken);
    return Results.Ok(new { applied = true });
});

await using (var scope = app.Services.CreateAsyncScope())
{
    // Use a dedicated development database.
    var db = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
    await db.Database.EnsureCreatedAsync();
    // EnsureCreated only initializes an empty database. Add the event table to databases from the first milestone.
    await db.Database.ExecuteSqlRawAsync(
        """
        CREATE TABLE IF NOT EXISTS "ProviderEvents" (
            "EventId" character varying(128) PRIMARY KEY,
            "PaymentId" uuid NOT NULL,
            "Status" text NOT NULL
        )
        """);
}

await app.RunAsync();

record CreatePayment(long AmountMinor, string? Currency);
record Payment(Guid Id, long AmountMinor, string Currency, string Status, DateTimeOffset CreatedAt);

record ProviderEvent(string? EventId, Guid PaymentId, string? Status);

sealed class PaymentsDbContext(DbContextOptions<PaymentsDbContext> options) : DbContext(options)
{
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<ProviderEvent> ProviderEvents => Set<ProviderEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProviderEvent>().HasKey(providerEvent => providerEvent.EventId);
        modelBuilder.Entity<ProviderEvent>().Property(providerEvent => providerEvent.EventId).HasMaxLength(128);
        modelBuilder.Entity<ProviderEvent>().Property(providerEvent => providerEvent.Status).IsRequired();
    }
}
