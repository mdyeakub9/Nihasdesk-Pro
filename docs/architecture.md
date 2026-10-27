# Architecture

_Status: draft. Expand before Phase 1 coding starts._

## Shape
- **Modular monolith** in .NET: one deployable API + one Workers process, modules separated by folders, DbContexts and DB schemas. Split into services only when measured load requires it.
- Modules: Identity (workspaces, users, roles), Contacts, Inbox, Channels, AiAgent, Commerce, Shipping, Billing.
- Modules communicate via `Contracts/` interfaces or integration events (MassTransit). Enforced by architecture tests.

## Data
- PostgreSQL, one database, **schema per module** (`identity.`, `contacts.`, `inbox.` …).
- Multi-tenancy: `TenantId` on every tenant-owned row, EF Core global query filters + PostgreSQL Row-Level Security.
- IDs: Guid v7. Timestamps in UTC. Soft delete where needed.
- pgvector for knowledge-base embeddings.
- Media (images, voice) in Hetzner Object Storage, not in the database.

## Message flow
```
Channel webhook → API stores raw payload, returns 200 → queue
  → Channels adapter normalizes → Inbox stores message (idempotent)
  → SignalR pushes to team members' screens
  → AiAgent decides: reply / tool calls / handoff
  → reply sent through Channels adapter → stored → metered
```

## Real-time
SignalR with Redis backplane (multiple API instances).

## Background work
RabbitMQ + MassTransit: webhook processing, AI jobs, broadcasts, summaries. Retries with backoff, dead-letter queues, idempotency keys.

## Hosting
- Local: Docker Compose (Postgres, Redis, RabbitMQ).
- Hetzner (Germany/Finland) + Coolify. Staging and production fully separate. Database on its own server at launch, private network only.
- Nightly encrypted backups to object storage; restore tested monthly.
- Domains: `app.` (dashboard), `api.`, root (marketing), `widget.`.

## Scaling path
1 server → app + DB servers → multiple app servers + load balancer → DB replica / dedicated server. Containers move between servers without code changes.
