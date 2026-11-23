using Microsoft.EntityFrameworkCore;
using OrdersApi;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDbContext<OrdersDb>(o => o.UseSqlite(builder.Configuration.GetConnectionString("Db") ?? "Data Source=orders.db"));
builder.Services.AddDaprClient();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<IEventPublisher, DaprEventPublisher>();
builder.Services.AddScoped<OrderService>();
builder.Services.AddScoped<OutboxDispatcher>();
builder.Services.AddHostedService<OutboxWorker>();

var app = builder.Build();
using (var scope = app.Services.CreateScope())
    scope.ServiceProvider.GetRequiredService<OrdersDb>().Database.EnsureCreated();

app.MapPost("/orders", async (PlaceOrder request, OrderService orders, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.CustomerId) || request.Amount <= 0 || string.IsNullOrWhiteSpace(request.Currency))
        return Results.BadRequest("customerId, currency and a positive amount are required.");

    var order = await orders.PlaceAsync(request.CustomerId, request.Amount, request.Currency, ct);
    return Results.Accepted($"/orders/{order.Id}", order);
});
app.MapGet("/orders/{id:guid}", async (Guid id, OrdersDb db) =>
    await db.Orders.FindAsync(id) is { } order ? Results.Ok(order) : Results.NotFound());
app.MapGet("/outbox", (OrdersDb db) => db.Outbox.OrderByDescending(m => m.CreatedUtc).Take(50).ToListAsync());
app.MapGet("/health", () => Results.Ok());
app.Run();

record PlaceOrder(string CustomerId, decimal Amount, string Currency);
