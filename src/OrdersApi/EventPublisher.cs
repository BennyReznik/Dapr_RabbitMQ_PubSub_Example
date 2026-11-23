using System.Text;
using Contracts;
using Dapr.Client;

namespace OrdersApi;

public interface IEventPublisher
{
    Task PublishAsync(OutboxMessage message, CancellationToken ct);
}

public class DaprEventPublisher(DaprClient dapr) : IEventPublisher
{
    public Task PublishAsync(OutboxMessage message, CancellationToken ct) =>
        dapr.PublishByteEventAsync(
            Topics.PubSub,
            message.Topic,
            Encoding.UTF8.GetBytes(message.Payload),
            "application/json",
            new Dictionary<string, string> { ["cloudevent.id"] = message.Id.ToString() },
            ct);
}
