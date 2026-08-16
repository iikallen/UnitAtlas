# 1C UPP Kazakhstan 1.3 pilot profile

## Identity and boundary

- UnitAtlas profile code: `ONEC_UPP_KZ_1_3_HTTP_JSON_V1`.
- Target configuration: `1C:Enterprise 8 — Manufacturing Enterprise Management for Kazakhstan, edition 1.3`.
- Transport: a customer-installed 1C HTTP-service extension posts JSON to UnitAtlas and accepts the existing UnitAtlas outbound JSON envelope.

This is a versioned UnitAtlas pilot contract. It is not a vendor-native 1C API and has not been certified against a customer database. The 1C extension, credentials and field mapping must be validated in the real pilot environment.

Configure an enabled `ONE_C` integration endpoint with non-secret settings:

```json
{
  "profile": "ONEC_UPP_KZ_1_3_HTTP_JSON_V1",
  "eventTypes": [
    "unit.created",
    "trace_event.recorded",
    "aggregation.recorded",
    "shipment.recorded",
    "receipt.recorded",
    "production_order.completed",
    "print_job.created"
  ]
}
```

Credentials belong in `SecretRef`, never in `settings`.

## Inbound messages

The profile retains the reference messages `product.upsert`, `production.completed`, `shipment.recorded` and `receipt.recorded`. It adds this batch command:

```json
{
  "type": "production_order.completed",
  "data": {
    "externalId": "5812",
    "productExternalId": "NOMENCLATURE-42",
    "lot": "LOT-5812",
    "serialPrefix": "PO5812",
    "quantity": 100,
    "occurredAt": "2026-08-16T10:00:00Z",
    "label": {
      "templateId": "00000000-0000-0000-0000-000000000000",
      "profileId": "00000000-0000-0000-0000-000000000000",
      "printerId": "00000000-0000-0000-0000-000000000000"
    }
  }
}
```

Required headers:

```text
X-External-Message-Id: stable 1C exchange message ID
Content-Type: application/json
```

The selected template must be `UNIT / GS1 / GS1_DATA_MATRIX`; the GS1 print profile must contain the tenant's licensed Company Prefix, and the printer must be enabled. UnitAtlas rejects the whole transaction if any label payload is invalid.

## Server result

One transaction:

1. maps the external Production Order to the existing UnitAtlas `Lot` production-batch entity through `ExternalReference`;
2. creates the requested Units, identifiers, manufactured ledger events, state projections and private passport configs;
3. creates one `PrintJob` with one immutable item per Unit;
4. writes audit and Outbox messages.

Each Unit receives the external reference `<production-order-id>:<four-digit-line-number>`, for example `5812:0001`. `shipment.recorded` and `receipt.recorded` can then use that value as `unitExternalId`.

Reusing the same `X-External-Message-Id` with the same payload returns the stored result. Reusing it with a different payload returns `INBOX_IDEMPOTENCY_CONFLICT`. A new message ID cannot change an already completed Production Order: a different payload returns `ONE_C_PRODUCTION_ORDER_CONFLICT`. `production_order.completed` is rejected with `ONE_C_PROFILE_REQUIRED` unless the endpoint selects this exact profile.

## Pilot evidence still required

The automated E2E verifies 100 Unit creation, 100 label payloads, Capture confirmations, Unit → Box → Pallet aggregation, conflict handling, shipment delivery and EPCIS projection. It does not replace validation with the real 1C database, installed extension, printer, labels or scanner.
