using Microsoft.EntityFrameworkCore;

namespace PaymentsService;

public class Payment
{
    public Guid Id { get; init; }
    public Guid OrderId { get; init; }
    public decimal Amount { get; init; }
    public required string Currency { get; init; }
}

/// <summary>One row per handled event. The primary key is what rejects duplicates.</summary>
public class ProcessedEvent
{
    public Guid EventId { get; init; }
}

public class DeadLetter
{
    public Guid EventId { get; init; }
    public Guid OrderId { get; init; }
    public DateTime ReceivedUtc { get; init; }
}

public class PaymentsDb(DbContextOptions<PaymentsDb> options) : DbContext(options)
{
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<ProcessedEvent> ProcessedEvents => Set<ProcessedEvent>();
    public DbSet<DeadLetter> DeadLetters => Set<DeadLetter>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<ProcessedEvent>().HasKey(e => e.EventId);
        model.Entity<DeadLetter>().HasKey(e => e.EventId);
    }
}
