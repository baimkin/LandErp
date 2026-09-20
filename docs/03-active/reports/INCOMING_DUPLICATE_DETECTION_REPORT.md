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
- exact photo URL overlap as a weak additional signal.

Russian/Latin visual confusables used by marketplace text are normalized before comparison. Explicitly different cadastral numbers or different explicit plot numbers suppress a candidate even when advertising text is copied.

The first version intentionally does not download images or add an image-decoding dependency. Perceptual image hashing can be added later as another matching signal without changing the candidate workflow.

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
