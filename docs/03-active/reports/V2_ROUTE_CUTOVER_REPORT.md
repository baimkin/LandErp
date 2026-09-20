# V2 route cutover report

## Scope

Promote the existing V2 incoming and procurement list pages to the canonical user-facing routes and remove the legacy list components.

Baseline commit: `3062c749e31b692af995a599624e6cb22173bb04`  
Working branch: `codex/v2-route-cutover`

## Result

- `IncomingCatalogV2.razor` serves `/incoming`.
- `ProcurementQueueV2.razor` serves `/procurement`.
- Compatibility aliases `/incoming-v2` and `/procurement-v2` remain available.
- Legacy `IncomingCatalog.razor` and `ProcurementQueue.razor` are removed.
- `/incoming?manual=true` remains supported and opens the V2 manual-ingress dialog.
- App navigation, Home and Overview use canonical `/incoming` and `/procurement` links.

## Unchanged routes

The cutover does not change PropertyCase routes:

- `/procurement/{CaseId:guid}`
- `/procurement/{CaseId:guid}/inspection`
- `/procurement/listings/{ListingId:guid}`

## Backend and contracts

No backend, database, domain or public application contract changes are required. The promoted pages continue using the existing V2 read services and workspace commands.

Reviewed contracts:

- `IncomingCatalogReadContracts.cs`
- `ProcurementQueueV2ReadContracts.cs`
- `OverviewContracts.cs`

## Tests

`ProcurementUiScenario` is updated to cover the new canonical routes and V2 UI:

- `/procurement` opens the V2 queue;
- `/incoming?manual=true` opens the manual-ingress dialog;
- incoming rows are opened using the V2 row interaction;
- the V2 manual-add action label is used.

Overview URL expectations are updated where applicable.

Test execution is intentionally left to the normal verification workflow. CI status is checked on the published commit and reported separately.
