# ADR 0010: Integration operations and regulatory routing

## Status

Accepted — 2026-08-16.

## Context

At-least-once delivery needs an operator-visible state and a controlled recovery path. Kazakhstan pilots may route regulatory operations through 1C or a future direct IS MPT adapter, but never both for the same tenant operation.

## Decision

- `/integrations` reads delivery projections from the existing runtime; no second monitoring store is introduced.
- Endpoint configuration, enable/disable and dead-letter retry require `integrations.manage` and append an audit entry.
- Manual retry is accepted only from `DeadLetter`, clears the failed lease and starts a fresh attempt budget without changing the immutable Outbox message.
- UI and list contracts expose only `hasSecretRef`; secret values and the reference name are not returned.
- `Tenant.RegulatoryGatewayMode` is one enum-like value: `NONE`, `ONE_C` or `DIRECT_IS_MPT`. A single value makes dual regulatory routing unrepresentable.
- Delivery, Inbox and EPCIS failure metrics use the existing OpenTelemetry meter.

## Consequences

Operators can diagnose and recover integrations without database access. The direct IS MPT adapter remains deferred until sandbox credentials and a product scenario exist; selecting its mode does not pretend that the adapter is implemented.
