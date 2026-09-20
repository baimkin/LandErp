# Incoming KPI cards & review state

## Scope

Activate the Incoming V2 KPI cards that already have enough backend semantics and add the missing shared review/processed-today semantics without introducing a database migration.

Baseline commit: `485032993decb50dec3408d078e60644a1f5d34b`  
Working branch: `codex/incoming-kpi-cards`

## Implemented

- `Новые` is now an organization-wide shared state.
  - first detailed open creates one `CatalogEventKind.ReviewStarted`;
  - every detailed open creates `CatalogItemViewed` in Audit;
  - no extra claim/assignment button is introduced;
  - prior classification, monitoring, case resume or confirmed PropertyCase source link also counts as already reviewed for historical compatibility.
- `Цена изменилась` is clickable from the top KPI block.
- `Не хватает данных` is clickable from the top KPI block.
- `Обработано сегодня` is implemented from existing facts:
  - classification;
  - monitoring start;
  - PropertyCase resume;
  - confirmed PropertyCase source link / take-to-work.
- Simple view/open does not count as processed.
- Business-day aggregation uses the canonical LandErp business timezone.
- Top KPI clicks reset other filters so KPI count and resulting segment describe the same population.
- Incoming rows display `Новое` / `Просмотрено`.
- Audit renders `CatalogItemViewed` as `Просмотрено входящее предложение`.

## Explicitly out of scope

`Возможный дубль` remains a placeholder. Duplicate candidate persistence and matching are a separate task.

## Database

No migration is required. Existing `catalog.events`, `audit_events` and PropertyCase source links are reused. `CatalogEvent.Kind` and Audit `Action` are string-backed.

## Tests prepared

- shared review state across two managers;
- exactly one `ReviewStarted` event with multiple opens;
- every open is audited;
- semantic audit title;
- processed-today aggregation and preset;
- simple view excluded from processed-today;
- browser scenario verifies the top `Новые` KPI is clickable and a new row is labelled before opening.

Actual test execution is left to the repository verification workflow. CI status is checked on the published commit and reported separately.
