# v0.4.0 Factory Pilot evidence

`v0.4.0-rc.1` means the software baseline is frozen and physical acceptance is pending. It is not evidence that the factory pilot passed and must not be promoted to `v0.4.0` until every release gate below is complete.

## Evidence rules

- Copy `acceptance-record.template.md` to a dated sanitized record for the pilot.
- Approve the numerical thresholds before the first physical run. A result cannot define its own pass criteria afterward.
- Store only hashes, certificate fingerprints, sanitized asset IDs and links to access-controlled evidence. Never commit passwords, tokens, private keys, client secrets, customer database exports, personal data, internal hostnames/IP addresses or unsanitized screenshots/logs.
- Write `NOT AVAILABLE` for missing evidence. Do not infer `PASS` from automated CI or a simulator.
- Every result needs the tested commit/image/APK hash, UTC time and accountable approver.

## Promotion gates

| Gate | Required result |
| --- | --- |
| Software CI and Capture → shipment → 1C | `PASS` on the release commit |
| Production OIDC and signed APK on real TSD | `PASS` |
| Printer, stock and scan-back | `PASS` |
| GS1 identity | licensed GCP evidence or explicit `INTERNAL` mode |
| Customer 1C profile and extension | `PASS` on the recorded release |
| 100 labels printed and scanned | `PASS` |
| Offline work, reconnect and intentional conflict | `PASS` |
| Lost accepted commands / duplicate ledger events | `0 / 0` |
| 1,000 physical scans | measured and within pre-approved thresholds |
| Shipment → Outbox → customer 1C acknowledgment | `PASS` |
| EPCIS/passports/RLS/audit/append-only | `PASS` |
| Backup/restore rehearsal | `PASS` for the release schema/image |

The release manager may create the annotated `v0.4.0` tag only from the verified commit referenced by the completed acceptance record.
