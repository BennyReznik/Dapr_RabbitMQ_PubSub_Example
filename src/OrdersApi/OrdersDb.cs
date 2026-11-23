using Microsoft.EntityFrameworkCore;

namespace OrdersApi;

public class Order
{
    public Guid Id { get; init; }
    public required string CustomerId { get; init; }
    public decimal Amount { get; init; }
    public required string Currency { get; init; }
}

public class OutboxMessage
{
    public Guid Id { get; init; }
    public required string Topic { get; init; }
    public required string Payload { get; init; }
    public DateTime CreatedUtc { get; init; }
    public DateTime NextAttemptUtc { get; set; }
    public DateTime? PublishedUtc { get; set; }
    public int Attempts { get; set; }
}

public class OrdersDb(DbContextOptions<OrdersDb> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder model) =>
        model.Entity<OutboxMessage>().HasIndex(m => new { m.PublishedUtc, m.NextAttemptUtc });
}
