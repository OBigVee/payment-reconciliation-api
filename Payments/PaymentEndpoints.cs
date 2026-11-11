using Microsoft.EntityFrameworkCore;
using PaymentReconciliation.Api.Data;

namespace PaymentReconciliation.Api.Payments;

internal static class PaymentEndpoints
{
    public static void MapPaymentEndpoints(this WebApplication app)
    {
        app.MapPost("/payments", CreateAsync);
        app.MapGet("/payments/{id:guid}", GetAsync);
    }

    private static async Task<IResult> CreateAsync(CreatePayment request, PaymentsDbContext db, CancellationToken cancellationToken)
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
    }

    private static async Task<IResult> GetAsync(Guid id, PaymentsDbContext db, CancellationToken cancellationToken)
    {
        var payment = await db.Payments.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        return payment is null ? Results.NotFound() : Results.Ok(payment);
    }
}

internal record CreatePayment(long AmountMinor, string? Currency);
