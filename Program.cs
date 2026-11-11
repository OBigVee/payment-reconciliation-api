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

await using (var scope = app.Services.CreateAsyncScope())
{
    // This first milestone uses a dedicated development database.
    var db = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
    await db.Database.EnsureCreatedAsync();
}

await app.RunAsync();

record CreatePayment(long AmountMinor, string? Currency);
record Payment(Guid Id, long AmountMinor, string Currency, string Status, DateTimeOffset CreatedAt);

sealed class PaymentsDbContext(DbContextOptions<PaymentsDbContext> options) : DbContext(options)
{
    public DbSet<Payment> Payments => Set<Payment>();
}
