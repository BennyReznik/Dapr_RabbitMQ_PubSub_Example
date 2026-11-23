namespace Contracts;

/// <summary>EventId identifies the event itself, so a consumer can recognise a redelivery.</summary>
public record OrderPlaced(Guid EventId, Guid OrderId, string CustomerId, decimal Amount, string Currency);
