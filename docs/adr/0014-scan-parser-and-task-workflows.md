# ADR 0014: Scanner boundary and task workflows

## Status

Accepted for v0.4 — 2026-08-16.

## Decision

- `ScanSource` isolates keyboard-wedge, camera and Android intent input. Camera uses `mobile_scanner`; Android intents accept DataWedge and generic payload extras. No device-vendor SDK enters core.
- `ScanParser` recognizes UnitAtlas QR/IDs, GS1 DataMatrix, plain DataMatrix, EAN, GS1-128 and SSCC. Supported GS1 AIs are `00`, `01`, `10`, `11`, `17` and `21`.
- Operators enter task screens rather than domain menus: quality, packaging, palletization, move, shipment, receipt and lookup. Production stays visibly locked until the concrete pilot profile can supply a real production order.
- Trace commands and aggregation commands enter the same SQLite queue and server idempotency boundary. Normal API and Capture reuse one trace-event implementation.
- RFID stays out of scope; an empty future adapter would not validate hardware behavior.

## Consequences

Camera, keyboard and intent scanners produce the same parsed command. Offline task actions retain order and can be replayed without a second business-rules implementation on Android.
