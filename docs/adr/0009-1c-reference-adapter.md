# ADR 0009: 1C reference adapter

## Status

Accepted — 2026-08-16.

## Context

UnitAtlas needs a pilot-ready 1C integration without coupling Products, Units or Packaging to a specific 1C edition or transport profile. The durable Inbox/Outbox runtime and `ExternalReference` model already provide delivery, replay and identifier boundaries.

## Decision

- `ONE_C` is an adapter over the existing integration port, not a dependency of the domain modules.
- Inbound messages use a small versioned reference contract for product upsert, production completion, shipment and receipt. The route and endpoint select the tenant and source system; payloads cannot select a tenant.
- `X-External-Message-Id` plus payload hash provides durable replay and conflict semantics through Inbox.
- 1C identifiers are stored as `ExternalReference`; UnitAtlas remains the owner of its internal IDs and trace ledger.
- Outbound `unit.created`, trace and aggregation messages are mapped to a stable 1C-facing envelope and delivered by the existing retry/dead-letter runtime.
- Secrets remain indirect through `SecretRef`. HTTP is the reference transport; the exact HTTP/OData profile is a pilot configuration decision.

## Consequences

The reference flow is testable end to end and does not assume 1C:ERP, UPP or Trade Management. A pilot-specific extension can replace the adapter mapping without changing core domain code. IS MPT/QazMarka remains outside this adapter and requires a separate gateway decision.
