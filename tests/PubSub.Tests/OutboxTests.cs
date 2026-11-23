using System.Text.Json;
using Contracts;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OrdersApi;
using Xunit;

namespace PubSub.Tests;

public sealed class OutboxTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly FakeClock _clock = new();
    private readonly FakePublisher _publisher = new();

    public OutboxTests()
    {
        _connection.Open();
        using var db = NewDb();
        db.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task Placing_an_order_saves_the_order_and_its_event_together()
    {
        var order = await PlaceAsync();

        using var db = NewDb();
        Assert.Equal(order.Id, (await db.Orders.SingleAsync()).Id);
        var message = await db.Outbox.SingleAsync();
        var evt = JsonSerializer.Deserialize<OrderPlaced>(message.Payload)!;
        Assert.Equal(order.Id, evt.OrderId);
        Assert.Equal(message.Id, evt.EventId);
        Assert.Null(message.PublishedUtc);
    }

    [Fact]
    public async Task If_the_event_cannot_be_saved_the_order_is_not_saved()
    {
        using (var db = NewDb())
            await db.Database.ExecuteSqlRawAsync(
                "CREATE TRIGGER fail_outbox BEFORE INSERT ON Outbox BEGIN SELECT RAISE(ABORT, 'no'); END");

        await Assert.ThrowsAsync<DbUpdateException>(PlaceAsync);

        using var check = NewDb();
        Assert.Empty(await check.Orders.ToListAsync());
    }

    [Fact]
    public async Task A_published_message_is_not_published_again()
    {
        await PlaceAsync();

        Assert.Equal(1, await DispatchAsync());
        Assert.Equal(0, await DispatchAsync());
        Assert.Single(_publisher.Sent);
    }

    [Fact]
    public async Task A_failed_publish_is_kept_and_retried_after_the_backoff()
    {
        await PlaceAsync();
        _publisher.Fail = true;
        Assert.Equal(0, await DispatchAsync());

        _publisher.Fail = false;
        Assert.Equal(0, await DispatchAsync()); // not due yet

        _clock.Now += TimeSpan.FromSeconds(1);
        Assert.Equal(1, await DispatchAsync());

        using var db = NewDb();
        Assert.Equal(2, (await db.Outbox.SingleAsync()).Attempts);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(3, 4)]
    [InlineData(6, 30)]
    [InlineData(5000, 30)]
    public void Backoff_doubles_and_is_capped(int attempt, int seconds) =>
        Assert.Equal(TimeSpan.FromSeconds(seconds), OutboxDispatcher.Backoff(attempt));

    private OrdersDb NewDb() => new(new DbContextOptionsBuilder<OrdersDb>().UseSqlite(_connection).Options);

    private async Task<Order> PlaceAsync()
    {
        using var db = NewDb();
        return await new OrderService(db, _clock).PlaceAsync("alice", 25m, "EUR");
    }

    private async Task<int> DispatchAsync()
    {
        using var db = NewDb();
        return await new OutboxDispatcher(db, _publisher, _clock, NullLogger<OutboxDispatcher>.Instance).DispatchAsync();
    }

    private sealed class FakePublisher : IEventPublisher
    {
        public bool Fail { get; set; }
        public List<Guid> Sent { get; } = [];

        public Task PublishAsync(OutboxMessage message, CancellationToken ct)
        {
            if (Fail) throw new InvalidOperationException("broker down");
            Sent.Add(message.Id);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
