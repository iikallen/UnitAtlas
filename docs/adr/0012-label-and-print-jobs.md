# ADR 0012: Label payloads and print jobs

## Status

Accepted for v0.4 — 2026-08-16.

## Decision

- Label templates are built-in tenant rows for internal UnitAtlas QR, GS1 DataMatrix Unit and internal/GS1 logistics labels.
- A print profile is explicitly `INTERNAL` or `GS1`. GS1 profiles require a licensed 6-12 digit Company Prefix; Unit and SSCC payloads must match it and pass their check digit validation.
- The server creates idempotent `PrintJob` + immutable payload item records. A printer edge polls pending jobs and reports `DISPATCHED`, then `PRINTED` or `FAILED`; retry is `FAILED → DISPATCHED`.
- Every transition is audited. `PrintAttempt` is append-only and terminal outcomes enter the existing Outbox.
- Printer vendor drivers and label rendering stay outside core. This slice owns identifiers, payloads, state and evidence, not Zebra/Honeywell transport SDKs.

## Consequences

UnitAtlas cannot accidentally present an internal identifier as GS1. Printer integrations can be added at the edge without changing canonical label or audit records.
