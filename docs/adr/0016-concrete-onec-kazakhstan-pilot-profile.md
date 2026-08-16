# ADR 0016: Concrete 1C Kazakhstan pilot profile

## Context

The generic ONE_C adapter proves Inbox/Outbox and identifier boundaries but does not define a production-order batch contract for a named configuration. No customer database, extension source, credentials or certified vendor transport contract were supplied.

## Decision

- Select `1C:Enterprise 8 — Manufacturing Enterprise Management for Kazakhstan, edition 1.3` as the first named profile.
- Identify the UnitAtlas extension contract as `ONEC_UPP_KZ_1_3_HTTP_JSON_V1`; do not present it as a native or certified 1C API.
- Keep the existing HTTP Inbox/Outbox runtime and `ExternalReference` boundary.
- Represent the production batch with the existing `Lot` entity instead of adding another table with the same lifecycle.
- Add `production_order.completed` only for the exact profile. It atomically creates Units and one multi-item GS1 Data Matrix print job.
- Keep per-Unit shipment and receipt messages from the reference contract. The external Unit reference is deterministically derived from the production order and line number.

## Consequences

The automated contract is concrete and replayable without coupling core entities to 1C. A real pilot still requires a reviewed 1C extension and evidence from the actual database and hardware before `v0.4.0` can be tagged.

## Rollback

Disable the integration endpoint or remove the profile setting to stop new batch commands. Existing Units, trace events, audit, print jobs and external references remain immutable operational evidence and are not deleted during application rollback.
