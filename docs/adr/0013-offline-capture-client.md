# ADR 0013: Offline Capture client

## Status

Accepted for v0.4 — 2026-08-16.

## Decision

- `src/unitatlas-capture` is an Android Flutter client with SQLite tables for cached Units, logistic units, products, locations, a sync checkpoint and pending commands.
- An operation is stored locally before network delivery. Commands use UUIDv7 and the server derives idempotency from `(deviceId, commandId)`.
- The client replays commands in creation order. Success is acknowledged locally; transport/server failures remain retryable; HTTP 409 is retained as a visible conflict with the authoritative server parent when resolvable.
- `/api/v1/capture/bootstrap`, `/resolve` and `/sync` expose the minimum server boundary. Sync currently accepts one command per transaction, so a partial batch cannot hide which scan failed.
- Background scheduling is deferred. Explicit sync is enough for the pilot and avoids an extra battery/network plugin before device measurements exist.

## Consequences

The SQLite projection is disposable and can never overwrite canonical PostgreSQL ledgers. Enrollment, station metadata and incremental change tokens are completed in the device/station slice before release.
