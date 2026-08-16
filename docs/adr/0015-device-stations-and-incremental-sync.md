# ADR 0015: Device stations and incremental Capture sync

## Status

Accepted for v0.4 — 2026-08-16.

## Decision

- A one-time `DeviceEnrollment` binds one enabled `Device` to one enabled `Station`. Enrollment codes and `DeviceSession` tokens are returned once and stored only as SHA-256 hashes on the server.
- A device session is also bound to the authenticated user and tenant. Every enrolled Capture endpoint requires the opaque `X-UnitAtlas-Device-Session` header; admin can revoke a session.
- `Station` owns `Site`, `ReadPoint` and `BusinessLocation`. Capture ignores client-supplied location identifiers and stamps these server-owned identifiers plus Device/Station onto trace and aggregation events.
- The existing append-only Outbox is the incremental change source. A database identity `Sequence` supplies the numeric sync token; `GET /api/v1/capture/changes?after=` returns projections, not the event ledger.
- Flutter stores the device session in platform secure storage, retains its SQLite command queue and advances the checkpoint only after a change page is applied.

## Consequences

Forging a `deviceId` in a command cannot change event provenance. A stolen device token is insufficient without the same authenticated user and tenant, and can be revoked. Outbox delivery and Capture change polling share one durable ordering without adding Kafka, Redis or a second change-log table.
