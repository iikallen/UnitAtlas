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

Development rehearsal evidence (not a production-data backup):

| Field | Result |
| --- | --- |
| Date / code | 2026-08-16 10:56 UTC; PR #16 head `8cda77c164c70a817e4de5e2b5c10ba68f96227f` (merged as `5d7b67c989f98fa562482fdf745e4c23e34c6a4a`) |
| Archive | PostgreSQL custom format; `pg_restore --list` readable; SHA-256 `462148480b7008254bcb20a817f6a19a08499a0854ba74530062c8973c53f548` |
| Source = restore counts | Units 223; TraceEvents 393; AggregationEvents 54; AuditEntries 488; PrintJobs 22; DeviceSessions 9; DeviceEnrollments 9; Inbox 8,772; Outbox 788 |
| Schema | 10 EF migrations; 30 tenant tables with both RLS and FORCE RLS |
| Isolation | runtime role without tenant context saw zero rows in Units, trace/aggregation/audit and Outbox; demo tenant saw 222 of 223 Units, proving the other tenant remained excluded |
| Immutability | restored `trace_events` trigger rejected an UPDATE, including an UPDATE whose predicate matched no rows |
| Cleanup | temporary restore database and in-container dump removed after verification; source volume preserved |

Important: restore without `--no-owner`. A trial with `--no-owner` correctly restored data but transferred table ownership to the admin role, leaving the runtime `unitatlas` role without table access. Production restore must preserve the dumped `unitatlas` owner (or apply reviewed equivalent grants before traffic).

## 1,000-scan acceptance

Use the actual pilot scanner/camera and label stock. Record device, printer, symbology, network mode, success count, recognition errors, duplicate responses, sync retries, p50 and p95 acknowledgement latency. Synthetic parser tests are useful but do not satisfy this physical gate.

## Incident and rollback

- Never clear the local command queue before server acknowledgement.
- On a sync conflict, stop the affected task, inspect the server object and cancel or explicitly retry the local command.
- On printer uncertainty, do not mark a job `PRINTED`; retry from the same job so audit and attempt history remain intact.
- Roll application images back to the previous SHA. Preserve data volumes. Restore a backup into a new database if a schema rollback could lose data.
