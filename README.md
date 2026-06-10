# Transactional Inbox Pattern Demo / 事务性收件箱模式演示

![Architecture](inbox-pattern-demo-architecture.svg)

A .NET 10 demo of the **Transactional Inbox Pattern** — ensuring exactly-once message processing by decoupling message reception from business logic execution.

事务性收件箱模式演示：通过将消息接收与业务逻辑执行解耦，确保消息的精确一次处理。

## Tech Stack / 技术栈

| Layer | Technology |
|-------|-------------|
| API | ASP.NET Core Minimal API, MassTransit 8.3, RabbitMQ |
| Processor | .NET Worker Service, MassTransit Consumer, Dapper |
| Frontend | Blazor Server (InteractiveServer), Bootstrap 5 |
| Database | PostgreSQL 16 |
| Message Broker | RabbitMQ 3 |
| Infrastructure | Docker Compose |

## Modules / 模块

| Project | Type | Responsibility |
|---------|------|---------------|
| **InboxDemo.Api** | Minimal API | `POST /api/orders` publishes `OrderCreated` events; `GET /api/inbox-messages` queries inbox status |
| **InboxDemo.Processor** | Worker Service | `InboxConsumer` persists to `inbox_messages`; `InboxBackgroundProcessor` polls and dispatches to handlers |
| **InboxDemo.Common** | Class Library | Shared models: `OrderCreated`, `InboxMessage` |
| **InboxDemo.Frontend** | Blazor Server | Order creation form, inbox messages monitor, RabbitMQ queue viewer |

## Data Flow / 数据流

```
Frontend ─POST /api/orders─> Api ─publish─> RabbitMQ ─consume─> InboxConsumer
                                                                        │
                                        InboxBackgroundProcessor <──  INSERT
                                              │                  inbox_messages
                                              │                       │
                                              └─ poll (2s) ──────────┘
                                                  │
                                                  ▼
                                          IMessageHandler
                                        (OrderCreatedHandler)
```

**Key guarantees:**
- **Idempotent**: `ON CONFLICT (id) DO NOTHING` — duplicate messages silently ignored
- **Concurrency-safe**: `FOR UPDATE SKIP LOCKED` — multiple processor instances safe
- **Retry**: max 3 attempts; failed messages tracked with error text
- **Routing**: `MessageHandlerFactory` maps message types to handlers

## Quick Start / 快速启动

```bash
# 1. Start infrastructure
docker compose up -d

# 2. Build solution
dotnet build "Inbox Pattern Demo.slnx"

# 3. Run services (in separate terminals)
dotnet run --project InboxDemo.Api          # http://localhost:5264
dotnet run --project InboxDemo.Processor    # (background worker)
dotnet run --project InboxDemo.Frontend    # http://localhost:5181
```

RabbitMQ Management: http://localhost:15673 (guest/guest)

## API Endpoints

### POST /api/orders

Create an order and publish `OrderCreated` event.

**Request:**
```json
{
  "customerName": "Alice",
  "amount": 99.99
}
```

**Response:**
```json
{
  "orderId": "f12e2ac7-cf3a-474b-9cd4-5d83b4529997",
  "message": "Order created event published"
}
```

### GET /api/inbox-messages

Query inbox message status (last 100, ordered by received time desc).

**Response:**
```json
[
  {
    "id": "b0490000-091b-8447-...",
    "messageType": "InboxDemo.Common.Models.OrderCreated",
    "payload": "{\"Amount\": 99.99, ...}",
    "receivedOnUtc": "2026-06-10T00:33:32.144Z",
    "processedOnUtc": "2026-06-10T00:33:33.683Z",
    "error": null,
    "retryCount": 0
  }
]
```

## Frontend Pages

| Route | Description |
|-------|-------------|
| `/` | Architecture overview and pattern explanation |
| `/create-order` | Form to create orders and publish events |
| `/inbox-messages` | Real-time inbox message status (auto-refresh 3s) |
| `/rabbitmq` | RabbitMQ queue viewer with message inspection |
