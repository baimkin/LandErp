# Incoming image fingerprints

Baseline: `b0f15134b8db7b789b788ac46d0bb0bb7ee7af21`  
Branch: `codex/incoming-image-fingerprints`

## Implemented

- Removed URL equality from duplicate scoring.
- Added background perceptual hashing in `LandErp.Worker`.
- Source images are downloaded only for fingerprint calculation and are not persisted.
- PostgreSQL stores compact pHash records keyed by Listing + URL SHA-256.
- Current fingerprints are tied to Listing `DataRevision`; removed/old photos are ignored.
- HTTPS-only downloader blocks loopback/private/link-local targets, follows at most three validated HTTPS redirects and limits image payloads to 8 MiB.
- Image decode is dimension-limited before 32×32 grayscale pHash calculation.
- Duplicate matcher performs Hamming-distance comparison with one-to-one image pairing.
- Frequently reused exact pHashes can be ignored as template/common images.
- Added organization-level runtime thresholds and `/settings/duplicates`.
- Threshold changes require no restart and are picked up by subsequent matching passes; the Worker continuously runs a rolling re-evaluation backfill.
- Existing `Confirmed / Rejected` human decisions remain authoritative; rule-driven candidates can become `Obsolete` and re-open only when they were never rejected by a person.

## Dependencies

SkiaSharp 4.152.1 is referenced only by `LandErp.Worker`. Locked dependency files are updated for Worker and the Foundation test graph.

## Database

Migration: `20260920193000_DuplicateImageFingerprints`

New tables:

- `catalog.photo_fingerprints`
- `catalog.duplicate_settings`

No image binaries or raw image URLs are added to PostgreSQL.

## Tests prepared

- two textually unrelated listings become a duplicate candidate from two close pHashes;
- changing the photo Hamming threshold through the settings service changes the matcher result without restart;
- existing duplicate detector tests remain in place for cadastral/text/plot-number behavior.

Actual build/test execution remains with the repository verification workflow.
