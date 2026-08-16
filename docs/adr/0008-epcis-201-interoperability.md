# ADR-0008: EPCIS 2.0.1 is an anti-corruption mapping layer

Status: Accepted for v0.3

## Context

UnitAtlas stores trace and aggregation ledgers in its own domain model. GS1 EPCIS is an interchange standard, not a replacement persistence model. Claiming complete EPCIS Repository conformance would also require query and subscription behavior that v0.3 does not implement.

## Decision

The integration layer maps the supported subset in both directions:

- UnitAtlas `TraceEvent` ↔ EPCIS `ObjectEvent`;
- UnitAtlas `AggregationEvent` ↔ EPCIS `AggregationEvent`.

`GET /api/v1/epcis/documents` exports the active tenant ledger as an EPCISDocument JSON-LD document. `POST` captures one ObjectEvent or AggregationEvent per document into the existing immutable ledgers. A single-event capture limit keeps validation and transactional failure semantics explicit; batch capture, SimpleEventQuery and subscriptions are deferred.

Generated documents use the normative EPCIS 2.0.1 JSON-LD context. CI downloads the versioned official GS1 JSON Schema and validates the document emitted by the live stack.

## Identifier policy

- A tracked unit with a real GTIN and serial is represented as a GS1 Digital Link URI using AI `01` and `21`.
- A logistic unit with a valid SSCC is represented using AI `00`.
- `AtlasId`, internal logistic-unit codes and internal locations remain `urn:unitatlas:*` identifiers when no GS1 identifier exists.
- UnitAtlas never derives or invents a GS1 Company Prefix.

Inbound GS1 Digital Link and `urn:unitatlas:*` identifiers must resolve inside the authenticated tenant. Tenant is never read from EPCIS JSON. Object capture supports business steps that map honestly to the existing UnitAtlas event types; unsupported event types and business steps return 422.

## Consequences

The domain remains independent of EPCIS contracts, while exported trace and packaging history can be exchanged as normative JSON/JSON-LD. XML/WSDL, full repository/query/subscription conformance, TransformationEvent and TransactionEvent remain out of scope.
