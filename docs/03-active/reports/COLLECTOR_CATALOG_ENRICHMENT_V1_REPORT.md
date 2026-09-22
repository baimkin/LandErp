# Collector / Catalog enrichment V1 — implementation report

**Base:** `main @ 54fa184e98f41dba3f040cde41aec181153ea054`
**Branch:** `codex/collector-catalog-enrichment-v1`

## Scope

Additive server enrichment for data already proven useful in the local Parser, plus a minimal
foundation for public listing contacts. This is an evolution of the existing Collector/Catalog
pipeline, not a Catalog rewrite or a seller CRM.

## Transport

`ListingData V1` gains additive optional fields:

- cadastral number;
- source publication timestamp;
- WGS84 latitude/longitude;
- source-declared normalized land types;
- public contacts.

Legacy V1 payloads remain valid because every new field has an absent/empty default.

The current local Parser transport mapping is extended for cadastral number, publication date,
coordinates and declared land types. Contact extraction is intentionally not added to source
adapters in this server package; the server contract/storage are ready for it later.

## Catalog persistence

`catalog.listings` gains:

- `source_published_at`;
- `latitude` / `longitude` numeric(9,6);
- `declared_land_types text[]`.

`CadastralNumber` already existed and is now accepted directly from Collector.

New `catalog.listing_contacts` stores per-listing contact observations:

- type;
- original and display value;
- server-normalized identity;
- source;
- primary flag;
- first/last observed UTC.

Uniqueness is `ListingId + Type + NormalizedValue`. Missing contacts in a later observation
do not delete historical contacts. No seller identity/CRM merge is introduced.

## Land-type semantics

- `DeclaredLandTypes`: what the marketplace structured fields say.
- `IncomingLandTypeClassifier`: what Title + Description say.
- Both may contain multiple values.
- Neither is a legal/registry truth.
- Incoming filtering matches either layer.
- Incoming detail shows both separately and highlights a mismatch.

## UI/read model

Incoming detail exposes and renders:

- source publication date;
- WGS84 coordinates;
- declared vs text-inferred land types and conflict;
- stored contacts with contact type, primary marker and last observed time.

The existing row type column uses the union of declared and inferred values and marks conflict.

## Migration

Migration: `20260921193000_CollectorCatalogEnrichment`.

The EF model snapshot is updated in the same package because production tests assert
`HasPendingModelChanges == false`.

## Deliberately out of scope

- seller/party CRM identity across multiple listings;
- automatic deletion of contacts;
- legal confirmation of land category/VRI;
- raw source JSON/session/profile data;
- new contact extraction logic in Cian/Avito adapters;
- new source types such as Yandex Realty or Domclick.

## Tests prepared

- additive contract compatibility and invalid coordinate/contact validation;
- local Parser → transport mapping of enrichment fields;
- PostgreSQL Collector ingress persistence of enrichment and contact deduplication;
- older observations cannot roll back current listing state;
- contact history survives absence in a later payload;
- Incoming detail exposes declared/inferred conflict, coordinates, publication date and contacts;
- land-type filter matches declared-only values;
- existing PostgreSQL migration test continues to assert no pending model changes.

Per project workflow, automated test/build execution is left to the owner.
