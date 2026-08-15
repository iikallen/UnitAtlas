# ADR-0005: Packaging aggregation uses an immutable event ledger plus a current membership projection

Status: Accepted for v0.2

## Context

UnitAtlas v0.1 traces individual serialized products. Real warehouse and manufacturing flows move many products as logistic units: units are packed into boxes, boxes into pallets, and pallets or boxes can later be unpacked or regrouped.

The model must preserve historical evidence while answering the operational question "what is inside this logistic unit now?" efficiently. It must also remain compatible with future GS1 EPCIS exchange, where aggregation events describe a parent/child physical relationship and ADD/DELETE actions.

## Decision

Introduce three concepts:

- `LogisticUnit`: a BOX, PALLET, or CONTAINER identified by an internal code and optional 18-digit GS1 SSCC.
- `AggregationEvent`: immutable history of ADD/DELETE operations, including occurrence/recording time, actor, source, sequence, location references and a JSONB snapshot of affected children.
- `LogisticUnitContent`: mutable projection of current direct membership. A child is either a tracked UnitAtlas unit or another logistic unit.

The API exposes creation, lookup and aggregation/disaggregation under `/api/v1/logistic-units` and keeps browser traffic behind the existing Next.js BFF.

## Invariants

1. A tracked unit or logistic unit may have at most one direct parent within a tenant.
2. A logistic unit may not contain itself, directly or transitively.
3. Every membership row contains exactly one child kind.
4. Cross-tenant parent/child relations are rejected by composite foreign keys and forced PostgreSQL RLS.
5. `AggregationEvent` is append-only at database level; corrections are represented by later events.
6. Requests are idempotent by tenant-scoped key and deterministic request hash.
7. Graph mutations take a transaction-scoped PostgreSQL advisory lock keyed by tenant before cycle/membership validation. This serializes packaging graph writes inside one tenant and prevents concurrent inverse edges from both passing validation.
8. The current membership projection may be deleted on a DELETE aggregation event, but the corresponding ledger events remain immutable.

## Why not derive current contents from the full ledger on every request?

The ledger is optimized for evidence and integrations. Operational screens need predictable low-latency reads. Keeping `LogisticUnitContent` as a projection avoids replaying every historical aggregation event while preserving an auditable source of truth.

## Why not store only current membership?

That would lose the history required to answer who packed/unpacked an item, when it happened, what source system initiated it, and how the relationship changed over time.

## Concurrency

Unique indexes prevent a child from being committed under two parents. The tenant advisory lock additionally protects graph-wide invariants such as acyclicity, which cannot be enforced by a simple unique constraint when two concurrent transactions add inverse edges.

This lock is intentionally scoped to packaging mutations, not reads or normal trace-event writes. If future tenants require very high concurrent packaging throughput, the lock can be narrowed to deterministic graph partitions after profiling.

## Security

`logistic_units`, `logistic_unit_contents`, and `aggregation_events` use forced tenant RLS. `aggregation_events` receives SELECT and INSERT policies only and an append-only trigger that rejects UPDATE, DELETE and TRUNCATE.

Permissions are split into `packaging.read` and `packaging.manage`; warehouse and production operators can manage packaging while quality/viewer roles remain read-only.

## Integration path

The domain deliberately does not depend on EPCIS classes. A future adapter can map:

- UnitAtlas `AggregationEvent.Action = ADD` to EPCIS aggregation ADD semantics.
- UnitAtlas `AggregationEvent.Action = DELETE` to EPCIS aggregation DELETE semantics.
- `LogisticUnit.Sscc` to the logistic-unit identifier used by GS1 integrations.
- `ReadPointId`, `BusinessLocationId`, `OccurredAt`, actor/source metadata and child identifiers to the outbound EPCIS representation.

1C and IS MPT adapters remain external to the core and consume outbox events such as `logistic_unit.created` and `aggregation.recorded`.

## Consequences

Positive:

- production-ready box/pallet nesting without changing the v0.1 trace ledger;
- auditable packing history and fast current-content reads;
- tenant isolation and race-resistant graph invariants;
- clean path toward EPCIS and Kazakhstan marking integrations.

Tradeoffs:

- packaging writes for one tenant are serialized by an advisory lock;
- projection consistency is maintained transactionally in the API for v0.2 rather than by an asynchronous projector;
- this stage does not yet implement EPCIS JSON-LD import/export, label printing, RFID, 1C, or IS MPT transport adapters.
