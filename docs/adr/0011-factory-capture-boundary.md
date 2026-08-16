# ADR 0011: Factory capture boundary

## Status

Accepted for v0.4 — 2026-08-16.

## Context

Factory operators need scanning, printing and short offline operation. The existing server already owns tenant isolation, immutable trace/aggregation ledgers, idempotency and integrations. Making a device database authoritative would create a second conflict-prone truth.

## Decision

- `/api/v1/capture/*` accepts device commands and returns authoritative outcomes; it does not expose a second ledger.
- A Capture client stores only bootstrap reference data, projections and pending commands. Each command has a globally unique ID and deterministic idempotency key.
- The server validates every replay against current state. Conflicts are explicit and never use last-write-wins.
- Bootstrap and incremental changes transfer only data required by the enrolled device. The Event Ledger is not downloaded to devices.
- Scanners enter through a small scan-source boundary; parsing and identifier resolution remain device-independent.
- Print jobs and attempts are server records. Printer transport is replaceable at the edge, while payload and status transitions remain auditable.
- Server events record actor, device, station, read point, business location and occurrence time when supplied by an enrolled session.

## Consequences

Offline work can queue safely and replay idempotently, while PostgreSQL constraints and ledgers remain canonical. A client may show an optimistic local projection, but must replace it with the server result after sync. RFID and a distributed sync broker stay deferred until measured pilot needs justify them.
