# ADR-0007: Durable integrations use immutable outbox messages and independent deliveries

Status: Accepted for v0.3

## Context

The v0.2 outbox stored a single `ProcessedAt` value. That cannot represent fan-out: one destination may acknowledge a message while another is retrying or permanently failing. Inbound systems also need durable idempotency without trusting tenant identifiers supplied in JSON.

## Decision

- `OutboxMessage` is the immutable canonical event. Its v1 webhook contract freezes message and correlation IDs, source, type, occurrence time, subject type/ID and JSON data.
- `IntegrationEndpoint` stores system, adapter, destination, enabled state, non-secret settings and an optional `SecretRef`. Secret values are resolved from deployment configuration and never persisted in endpoint JSON.
- `IntegrationDelivery` owns per-endpoint state: `Pending → Delivering → Delivered`, `Retry`, or `DeadLetter`.
- A PostgreSQL `FOR UPDATE SKIP LOCKED` lease claims work safely across parallel runtime instances. Expired `Delivering` leases are eligible again after a crash.
- Delivery is at least once. HTTP 408/429/5xx, timeouts and network failures retry with `Retry-After` or bounded exponential backoff plus jitter. Permanent failures and exhausted attempts become dead letters.
- `InboxMessage` is an immutable receipt keyed by tenant, source system and external message ID. The canonical JSON hash makes an exact retry return its stored result and a conflicting payload return HTTP 409.
- Tenant is derived only from authenticated credentials and endpoint lookup. External JSON has no tenant field.

The first adapter is generic HTTPS JSON webhook delivery. No broker or cache is introduced.

## Security and integrity

All three integration tables have composite tenant foreign keys and forced PostgreSQL RLS. Outbox and inbox permit only tenant-scoped SELECT/INSERT and have database triggers rejecting UPDATE, DELETE and TRUNCATE. Endpoint settings reject credential-like fields; production destinations require HTTPS.

## Consequences

One failing destination cannot block another, and replay/recovery no longer mutates canonical history. Consumers must tolerate duplicates because a process can crash after the remote side commits but before UnitAtlas records the acknowledgement. Manual retry and operational views are added in the later v0.3 operations slice.

Kafka and Redis remain unnecessary at pilot scale. They may be reconsidered only after measured PostgreSQL dispatcher limits, not speculatively.

## Rollback

Stop dispatchers first, then run the migration `Down`. This removes integration endpoints/deliveries/inbox data and restores the legacy nullable `ProcessedAt` column. Export dead letters before rollback if they must be retained.
