# Incoming price change delta

Baseline: `2d51c542108e4a9d61fcd1642d2c204f04617ba0`  
Branch: `codex/incoming-price-change-delta`

## Implemented

- SourceChanged events now persist previous and current public price values.
- Previous/current price-per-sotka snapshots are persisted at the same event boundary.
- Incoming queue reason shows `before → after`, absolute RUB delta and percentage when the public price changes.
- Incoming drawer history renders the same delta from typed event fields and additionally shows price-per-sotka change when available.
- The `Цена изменилась` preset/summary uses typed before/after values for new events, while retaining message-based fallback for historical events.
- Other simultaneous source changes remain visible as a secondary message.
- Existing historical events remain valid; the new database columns are nullable.

## Database

Migration: `20260920203000_CatalogPriceChangeDelta`

Added to `catalog.events`:

- `previous_observed_price numeric(19,4) null`
- `previous_observed_price_per_sotka numeric(19,4) null`

## Test prepared

The integration test ingests a price change from 2,000,000 ₽ to 1,800,000 ₽ and verifies typed old/new values, price-per-sotka snapshots, a -200,000 ₽ / -10% queue reason, and inclusion in the PriceChanged preset.

Actual build/test execution remains with the repository verification workflow.
