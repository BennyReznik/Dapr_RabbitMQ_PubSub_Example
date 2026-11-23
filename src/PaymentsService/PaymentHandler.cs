using Contracts;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace PaymentsService;

public enum HandleResult { Charged, Duplicate }

public class PaymentHandler(PaymentsDb db, IPaymentGateway gateway, ILogger<PaymentHandler> log)
{
    private const int SqlitePrimaryKeyViolation = 1555;

    public async Task<HandleResult> HandleAsync(OrderPlaced evt, CancellationToken ct = default)
    {
        if (await db.ProcessedEvents.AnyAsync(e => e.EventId == evt.EventId, ct))
            return Duplicate(evt);

        // EventId is the idempotency key: if we crash after charging but before saving,
        // the redelivered event must not charge the customer again.
        await gateway.ChargeAsync(evt.EventId, evt, ct);

        db.ProcessedEvents.Add(new ProcessedEvent { EventId = evt.EventId });
        db.Payments.Add(new Payment { Id = Guid.NewGuid(), OrderId = evt.OrderId, Amount = evt.Amount, Currency = evt.Currency });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqliteException { SqliteExtendedErrorCode: SqlitePrimaryKeyViolation })
        {
            // Another delivery of the same event saved first.
            return Duplicate(evt);
        }
        log.LogInformation("Charged order {OrderId}", evt.OrderId);
        return HandleResult.Charged;
    }

    private HandleResult Duplicate(OrderPlaced evt)
    {
        log.LogInformation("Ignored duplicate event {EventId}", evt.EventId);
        return HandleResult.Duplicate;
    }
}
