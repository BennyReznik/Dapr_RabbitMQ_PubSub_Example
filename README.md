# Dapr + RabbitMQ pub/sub example

Two small .NET 10 services that exchange events reliably through Dapr and RabbitMQ: placing an order charges a payment, once, even when things fail.

```
POST /orders -> OrdersApi -> orders + outbox (one transaction)
                                   |
                             outbox worker -> Dapr -> RabbitMQ -> Dapr -> PaymentsService
                                                                              |
                                                     retries, then dead-letter topic
```

## What it shows

| Problem | Solution | Code |
|---|---|---|
| Order saved but event lost | Transactional outbox | `OrderService`, `OutboxDispatcher` |
| Broker is down | Event stays in the outbox, retried with backoff | `OutboxDispatcher` |
| Same event delivered twice | Processed-event table and an idempotency key | `PaymentHandler` |
| Temporary failure in the consumer | Dapr resiliency policy: 3 retries | `dapr/resiliency.yaml` |
| Permanent failure, or retries used up | Dead-letter topic | `PaymentsService/Program.cs` |

## Run it

Needs Docker only.

```
docker compose up -d --build
```

Place an order, then look at the result:

```
curl -X POST http://localhost:8081/orders -H "Content-Type: application/json" -d '{"customerId":"alice","amount":25,"currency":"EUR"}'
curl http://localhost:8082/payments
```

Change the input to see other cases:

| Input | Result |
|---|---|
| `customerId: "flaky"` | Fails twice, then is charged |
| `customerId: "down"` | Dead-lettered after 4 attempts (1 + 3 retries) |
| `currency: "XXX"` | Dead-lettered without retries |
| `docker compose stop rabbitmq`, place an order, then `start` | Charged after the broker returns |

Also available: `GET :8081/outbox`, `GET :8082/dead-letters`, and the RabbitMQ UI at http://localhost:15672 (guest / guest).

## Tests

```
dotnet test DaprPubSub.slnx
```

The tests use in-memory SQLite, so they need neither Docker nor a broker.

## Design notes and limits

- Delivery is at-least-once. A crash between publishing and marking a message as published sends it again, which is why the consumer is idempotent.
- The charge and the database write cannot share a transaction, so the event ID is passed to the payment gateway as an idempotency key. The gateway here is simulated and in memory; a real one must remember keys across restarts.
- Only one OrdersApi instance should run. Several would need to claim outbox rows before publishing (for example with a lease or row locks).
- Messages are published one at a time, so a slow broker delays the messages behind.
- Event ordering is not guaranteed.
- Start the consumer once before publishing: RabbitMQ only keeps events for queues that already exist.
- The default credentials, SQLite files and `EnsureCreated` are for a local demo only.
