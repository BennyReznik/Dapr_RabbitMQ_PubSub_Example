using Contracts;
using Dapr;
using Microsoft.EntityFrameworkCore;
using PaymentsService;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDbContext<PaymentsDb>(o => o.UseSqlite(builder.Configuration.GetConnectionString("Db") ?? "Data Source=payments.db"));
builder.Services.AddSingleton<IPaymentGateway, SimulatedPaymentGateway>();
builder.Services.AddScoped<PaymentHandler>();

var app = builder.Build();
using (var scope = app.Services.CreateScope())
    scope.ServiceProvider.GetRequiredService<PaymentsDb>().Database.EnsureCreated();

app.UseCloudEvents();
app.MapSubscribeHandler();

app.MapPost("/events/order-placed", async (OrderPlaced evt, PaymentHandler handler, ILogger<Program> log, CancellationToken ct) =>
{
    try
    {
        await handler.HandleAsync(evt, ct);
        return Results.Ok(new { status = "SUCCESS" });
    }
    catch (PermanentPaymentException ex)
    {
        log.LogWarning("Dead-lettering {EventId}: {Error}", evt.EventId, ex.Message);
        return Results.Ok(new { status = "DROP" });
    }
    catch (TransientPaymentException ex)
    {
        // A failed response makes the Dapr sidecar apply the retry policy in resiliency.yaml.
        log.LogWarning("Will retry {EventId}: {Error}", evt.EventId, ex.Message);
        return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    }
}).WithTopic(new TopicOptions
{
    PubsubName = Topics.PubSub,
    Name = Topics.OrderPlaced,
    DeadLetterTopic = Topics.OrderPlacedDeadLetter,
});

app.MapPost("/events/order-placed-dead", async (OrderPlaced evt, PaymentsDb db, CancellationToken ct) =>
{
    if (!await db.DeadLetters.AnyAsync(d => d.EventId == evt.EventId, ct))
    {
        db.DeadLetters.Add(new DeadLetter { EventId = evt.EventId, OrderId = evt.OrderId, ReceivedUtc = DateTime.UtcNow });
        await db.SaveChangesAsync(ct);
    }
    return Results.Ok(new { status = "SUCCESS" });
}).WithTopic(new TopicOptions { PubsubName = Topics.PubSub, Name = Topics.OrderPlacedDeadLetter });

app.MapGet("/payments", (PaymentsDb db) => db.Payments.ToListAsync());
app.MapGet("/dead-letters", (PaymentsDb db) => db.DeadLetters.ToListAsync());
app.MapGet("/health", () => Results.Ok());
app.Run();
