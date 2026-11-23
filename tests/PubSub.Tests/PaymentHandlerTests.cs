using Contracts;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PaymentsService;
using Xunit;

namespace PubSub.Tests;

public sealed class PaymentHandlerTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly SimulatedPaymentGateway _gateway = new();

    public PaymentHandlerTests()
    {
        _connection.Open();
        using var db = NewDb();
        db.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task The_same_event_delivered_twice_is_charged_once()
    {
        var evt = NewEvent();

        Assert.Equal(HandleResult.Charged, await HandleAsync(evt));
        Assert.Equal(HandleResult.Duplicate, await HandleAsync(evt));

        Assert.Equal(1, await PaymentCountAsync());
        Assert.Equal(1, _gateway.ChargeCount);
    }

    [Fact]
    public async Task A_delivery_that_loses_the_race_is_a_duplicate()
    {
        var evt = NewEvent();
        // The other delivery saves while this one is waiting for the gateway.
        var racing = new RacingGateway(_gateway, () => HandleAsync(evt));

        Assert.Equal(HandleResult.Duplicate, await HandleAsync(evt, racing));

        Assert.Equal(1, await PaymentCountAsync());
        Assert.Equal(1, _gateway.ChargeCount);
    }

    [Fact]
    public async Task A_transient_failure_leaves_no_trace_so_a_retry_can_succeed()
    {
        var evt = NewEvent(customer: "flaky");

        await Assert.ThrowsAsync<TransientPaymentException>(() => HandleAsync(evt));
        await Assert.ThrowsAsync<TransientPaymentException>(() => HandleAsync(evt));
        Assert.Equal(0, await PaymentCountAsync());

        Assert.Equal(HandleResult.Charged, await HandleAsync(evt));
        Assert.Equal(1, await PaymentCountAsync());
    }

    [Fact]
    public async Task A_permanent_failure_is_not_recorded_as_paid()
    {
        await Assert.ThrowsAsync<PermanentPaymentException>(() => HandleAsync(NewEvent(currency: "XXX")));
        Assert.Equal(0, await PaymentCountAsync());
    }

    private static OrderPlaced NewEvent(string customer = "alice", string currency = "EUR") =>
        new(Guid.NewGuid(), Guid.NewGuid(), customer, 25m, currency);

    private PaymentsDb NewDb() => new(new DbContextOptionsBuilder<PaymentsDb>().UseSqlite(_connection).Options);

    private async Task<HandleResult> HandleAsync(OrderPlaced evt, IPaymentGateway? gateway = null)
    {
        using var db = NewDb();
        return await new PaymentHandler(db, gateway ?? _gateway, NullLogger<PaymentHandler>.Instance).HandleAsync(evt);
    }

    private async Task<int> PaymentCountAsync()
    {
        using var db = NewDb();
        return await db.Payments.CountAsync();
    }

    private sealed class RacingGateway(IPaymentGateway inner, Func<Task> otherDelivery) : IPaymentGateway
    {
        public async Task ChargeAsync(Guid idempotencyKey, OrderPlaced order, CancellationToken ct)
        {
            await otherDelivery();
            await inner.ChargeAsync(idempotencyKey, order, ct);
        }
    }
}
