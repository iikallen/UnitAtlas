# UnitAtlas v0.4 factory-pilot runbook

## Before the shift

- Keep the first pilot on one API/Web replica; the existing process-local rate limiter is acceptable only under that constraint.
- Verify HTTPS, production OIDC, session renewal, runtime PostgreSQL role, printer connectivity and enrolled device/station assignments.
- Confirm the tenant identifier mode. `INTERNAL` labels must not be presented as GS1 identifiers. `GS1` requires the tenant's licensed GS1 Company Prefix and validated check digits.
- Confirm profile `ONEC_UPP_KZ_1_3_HTTP_JSON_V1`, the reviewed customer 1C extension, credentials by secret reference, enabled integration endpoint, empty dead-letter backlog and expected regulatory route.
- Apply reviewed migration SQL with a release role before switching traffic.

## Shift checks

1. Bootstrap one enrolled Capture device and verify its user, tenant, station, site and location.
2. Produce and print one test Unit label, then scan it back before releasing the batch.
3. Run Unit → Box and Box → Pallet flows once online and once with connectivity disabled.
4. Reconnect and verify every accepted local command is acknowledged exactly once.
5. Create one deliberate aggregation conflict and verify the operator sees server and local parents; do not resolve by overwriting server state.
6. Complete a shipment and verify Outbox delivery, 1C acknowledgement and EPCIS export.
7. Check audit, immutable ledgers, integration backlog and error/latency metrics.

## Backup/restore release gate

After the complete v0.4 migration set is applied:

1. Create a custom-format backup outside the database host and verify `pg_restore --list`.
2. Restore into a new database.
3. Compare row counts for Units, trace/aggregation/audit ledgers, PrintJobs, Capture commands, devices/stations, Inbox/Outbox and `__EFMigrationsHistory`.
4. Verify FORCE RLS, tenant isolation, append-only triggers and zero runtime-role visibility without tenant context.
5. Record the date, source/restore counts, migration count and tested image SHA below.

Evidence: **pending v0.4 implementation and rehearsal**.

## 1,000-scan acceptance

Use the actual pilot scanner/camera and label stock. Record device, printer, symbology, network mode, success count, recognition errors, duplicate responses, sync retries, p50 and p95 acknowledgement latency. Synthetic parser tests are useful but do not satisfy this physical gate.

## Incident and rollback

- Never clear the local command queue before server acknowledgement.
- On a sync conflict, stop the affected task, inspect the server object and cancel or explicitly retry the local command.
- On printer uncertainty, do not mark a job `PRINTED`; retry from the same job so audit and attempt history remain intact.
- Roll application images back to the previous SHA. Preserve data volumes. Restore a backup into a new database if a schema rollback could lose data.
