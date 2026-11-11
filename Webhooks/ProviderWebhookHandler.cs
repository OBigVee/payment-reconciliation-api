using Microsoft.EntityFrameworkCore;
using PaymentReconciliation.Api.Data;

namespace PaymentReconciliation.Api.Webhooks;

internal static class ProviderWebhookHandler
{
    public static async Task<IResult> HandleAsync(ProviderEvent request, PaymentsDbContext db, CancellationToken cancellationToken)
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
    }
}
