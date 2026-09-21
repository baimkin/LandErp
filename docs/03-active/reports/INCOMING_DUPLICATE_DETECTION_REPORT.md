# Incoming duplicate detection

## Scope

Implement the first production duplicate-candidate workflow for Incoming without AI/LLM dependency.

Baseline commit: `b49b58633fad29e9f16b1199bac6bbca5a6b3536`  
Working branch: `codex/incoming-duplicate-detection`

## Identity extraction and matching

The deterministic matcher uses explainable signals:

- cadastral number extracted from title/location/description when exactly one valid number is found;
- explicit plot number and quoted КП/СНТ/ДНП name;
- normalized description similarity;
- area proximity;
- location similarity;
- seller equality;
- price proximity;
- perceptual image hashes as a strong cross-source signal; photo URLs are not compared.

Russian/Latin visual confusables used by marketplace text are normalized before comparison. Explicitly different cadastral numbers or different explicit plot numbers suppress a candidate even when advertising text is copied.

Image fingerprinting is performed asynchronously by `LandErp.Worker` using pHash. The web server and Collector ingress do not download images. Only URL hashes and perceptual hashes are persisted; source image bytes are not stored. Downloads are HTTPS-only, size-limited and block private/link-local targets.

## Persistence

A new `catalog.duplicate_candidates` table stores candidate pairs, score/reasons and `Pending / Confirmed / Rejected` status.

Rejected pairs remain persisted, so the matcher does not repeatedly offer the same pair. Confirming a candidate marks the current Incoming item as `Duplicate` while preserving both Catalog records.

One EF migration is included: `20260920165000_IncomingDuplicateCandidates`.

## Runtime

- Collector observations re-evaluate duplicate candidates, including unchanged accepted observations; this naturally backfills existing parser listings on their next collection cycle.
- Manually created Incoming items are evaluated immediately.
- Incoming V2 KPI/preset `Возможный дубль` is active.
- Candidate reasons and the matching listing are shown in the drawer.
- Manager can choose `Не дубль` or `Подтвердить дубль`.
- Both decisions are written to Audit.

## Tests prepared

- cadastral extraction using the supplied real-world style of listing text;
- copied/normalized descriptions produce a persisted candidate;
- rejected candidate disappears and remains rejected;
- different explicit plot numbers suppress copied-template false positives.

Actual test execution remains with the repository verification workflow.


## Runtime tuning

Organization-wide thresholds are editable at `/settings/duplicates`: candidate score, minimum description similarity, area tolerance, pHash Hamming distance, number of photo matches considered strong, and the threshold for ignoring common/template images. Matching reads these values from PostgreSQL; no process restart is required.

## Image processing

The Worker scans recent Incoming items plus a rolling backfill. Existing fingerprints are reused by hashed source URL, transient download failures use bounded backoff, and stale fingerprints are ignored via the Listing `DataRevision`. The detector compares current pHashes using Hamming distance and one-to-one photo pairing.


## Object-group workflow

Follow-up implementation replaces the one-sided «duplicate of listing X» workflow with a headless Catalog object group.

- a standalone Catalog item has no group;
- confirming the first pair creates one `catalog.object_groups` row and assigns both Listings to it;
- additional confirmed sources join the same group; merging two groups keeps no business-level primary Listing;
- manager can compare the two source records side by side before confirming;
- manual same-object linking uses the same persisted group model;
- unlinking restores the source as independent, records Audit and persists rejected pair decisions so matching does not immediately offer the same relation again;
- a two-member group is dissolved automatically when one member is removed;
- taking any group member into Procurement links every member as a source of one PropertyCase;
- generic `CatalogDisposition.Duplicate` classification without a concrete related Listing is rejected by the server.

The existing deterministic detector and pHash scoring remain responsible only for proposing candidates; they do not create an object group without a manager decision.
