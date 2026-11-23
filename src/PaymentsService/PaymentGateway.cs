using System.Collections.Concurrent;
using Contracts;

namespace PaymentsService;

/// <summary>Worth retrying: the next attempt may succeed.</summary>
public class TransientPaymentException(string message) : Exception(message);

/// <summary>Not worth retrying: the event goes straight to the dead-letter topic.</summary>
public class PermanentPaymentException(string message) : Exception(message);

public interface IPaymentGateway
{
    /// <summary>Must charge at most once per idempotency key.</summary>
    Task ChargeAsync(Guid idempotencyKey, OrderPlaced order, CancellationToken ct);
}

/// <summary>
/// Stands in for a remote payment provider. Failures are chosen by the input so they are easy to demonstrate:
/// customer "flaky" fails twice and then succeeds, customer "down" always fails, currency "XXX" is rejected.
/// </summary>
public class SimulatedPaymentGateway : IPaymentGateway
{
    private readonly ConcurrentDictionary<Guid, int> _attempts = new();
    private readonly ConcurrentDictionary<Guid, bool> _charged = new();

    public int ChargeCount => _charged.Count;

    public Task ChargeAsync(Guid idempotencyKey, OrderPlaced order, CancellationToken ct)
    {
        if (order.Currency == "XXX")
            throw new PermanentPaymentException("Unsupported currency");

        var attempt = _attempts.AddOrUpdate(idempotencyKey, 1, (_, n) => n + 1);
        if (order.CustomerId == "down" || (order.CustomerId == "flaky" && attempt <= 2))
            throw new TransientPaymentException($"Provider unavailable (attempt {attempt})");

        _charged.TryAdd(idempotencyKey, true);
        return Task.CompletedTask;
    }
}
