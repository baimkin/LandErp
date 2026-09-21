# Local Parser Cian/Data Debugging — implementation report

**Scope:** local Parser Agent only  
**Branch target:** `codex/release-package-04`  
**Server changes:** none

## Implemented

- Cian polygon pagination compares semantic search filters instead of literal URL decoration.
- Cian structured state is read through a strict business whitelist and joined to visible DOM cards by public listing ID.
- Local observation schema adds cadastral number, source publication timestamp, WGS84 coordinates, declared land types and inferred land types.
- Declared and inferred land types are independent multi-value facts; mismatch is visible as a diagnostic conflict.
- Cian badges, ranking/premium data, internal geo IDs, contacts, profile/session state and raw embedded JSON are intentionally excluded.
- Exact structured address is accepted only when Cian explicitly reports `isNeedHideExactAddress=false`; otherwise visible DOM location wins.
- Cian `totalOffers` is retained as local completion `SourceCountHint`.
- Local SQLite schema is unchanged; new values remain inside existing observation/listing JSON.
- Fresh-result compatibility is advanced to adapter generation 2 so old observations do not suppress collection of the new fields.
- Results workspace now supports source/search/run/quality filters, richer columns and richer CSV export.
- Listing details are reusable in a side panel or a single synchronized separate window, selected in local settings.
- Details include normal/technical views, collection context, warnings and field-by-field history changes.

## Tests added

- semantic Cian polygon identity and rejection of changed polygon/business filters;
- structured vs inferred multi-value land types and conflict behavior;
- fixture-based Cian structured extraction, `totalOffers`, hidden-address guard and pagination;
- local quality filters/search by cadastral number;
- WPF separate-window selection synchronization, reopen behavior and persisted display mode.

The fixture mirrors the relevant shape observed in the supplied Cian polygon page without retaining user/session/contact data.

## Deliberately not changed

- `LandErp.Collector.Contracts`;
- Server / Catalog entities;
- PostgreSQL migrations;
- server-side land-type behavior;
- server UI.

Those belong to the later, separately approved server task after local manual validation.

## Validation ownership

Automated tests were added as code but are not executed as part of this task workflow. The owner performs the actual build/test/manual browser run after publication. Recommended manual checks are documented in `docs/05-collection/LOCAL_PARSER_DATA_DEBUGGING.md`.
