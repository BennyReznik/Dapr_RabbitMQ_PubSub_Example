using System.Text.Json;
using Contracts;

namespace OrdersApi;

public class OrderService(OrdersDb db, TimeProvider clock)
{
    /// <summary>
    /// The order and its event are saved in one transaction (a single SaveChanges),
    /// so there is never an order without an event, or an event without an order.
    /// </summary>
    public async Task<Order> PlaceAsync(string customerId, decimal amount, string currency, CancellationToken ct = default)
    {
        var order = new Order { Id = Guid.NewGuid(), CustomerId = customerId, Amount = amount, Currency = currency };
        var evt = new OrderPlaced(Guid.NewGuid(), order.Id, customerId, amount, currency);
        var now = clock.GetUtcNow().UtcDateTime;

        db.Orders.Add(order);
        db.Outbox.Add(new OutboxMessage
        {
            Id = evt.EventId,
            Topic = Topics.OrderPlaced,
            Payload = JsonSerializer.Serialize(evt),
            CreatedUtc = now,
            NextAttemptUtc = now,
        });
        await db.SaveChangesAsync(ct);
        return order;
    }
}
