using Microsoft.EntityFrameworkCore;

namespace OrdersApi;

public class OutboxDispatcher(OrdersDb db, IEventPublisher publisher, TimeProvider clock, ILogger<OutboxDispatcher> log)
{
    private const int BatchSize = 20;
    private static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Publishes due messages. Delivery is at-least-once: a crash after publishing but before
    /// saving PublishedUtc sends the message again, so consumers must be idempotent.
    /// </summary>
    public async Task<int> DispatchAsync(CancellationToken ct = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var due = await db.Outbox
            .Where(m => m.PublishedUtc == null && m.NextAttemptUtc <= now)
            .OrderBy(m => m.CreatedUtc)
            .Take(BatchSize)
            .ToListAsync(ct);

        var published = 0;
        foreach (var message in due)
        {
            message.Attempts++;
            try
            {
                await publisher.PublishAsync(message, ct);
                message.PublishedUtc = clock.GetUtcNow().UtcDateTime;
                published++;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                var delay = Backoff(message.Attempts);
                message.NextAttemptUtc = clock.GetUtcNow().UtcDateTime + delay;
                log.LogWarning("Publishing {MessageId} failed (attempt {Attempt}), next try in {Delay}: {Error}",
                    message.Id, message.Attempts, delay, ex.Message);
            }
            await db.SaveChangesAsync(ct);
        }
        return published;
    }

    /// <summary>1s, 2s, 4s, ... capped at 30s.</summary>
    public static TimeSpan Backoff(int attempt) =>
        TimeSpan.FromSeconds(Math.Min(Math.Pow(2, attempt - 1), MaxDelay.TotalSeconds));
}

public class OutboxWorker(IServiceScopeFactory scopes, ILogger<OutboxWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(ct))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<OutboxDispatcher>().DispatchAsync(ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                log.LogError(ex, "Outbox dispatch failed");
            }
        }
    }
}
