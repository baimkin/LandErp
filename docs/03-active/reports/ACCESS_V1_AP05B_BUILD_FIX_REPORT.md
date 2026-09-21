# Access V1 — AP-05B build fix report

**Repository:** `baimkin/LandErp`  
**Working branch:** `codex/access-v1-ap-05b-build-fix`  
**Base commit:** `752692d54747a4dd641790914963f1ea3f949961` (AP-05)  
**Canonical main at task start:** `54fa184e98f41dba3f040cde41aec181153ea054`  
**Canonical main before publication:** `54fa184e98f41dba3f040cde41aec181153ea054`

## Purpose

AP-06 validation stopped at Release build with five `CS9113` errors because five Access V1 services still carried an unused `IAccessControl legacyAccess` primary-constructor parameter after the AP-04 cutover.

This task removes only that obsolete constructor dependency and updates direct test construction callsites to the current Access V1 constructor shape.

## Production changes

Removed `IAccessControl legacyAccess` from the primary constructors of:

- `ProcurementWorkspace`;
- `CollectionAdministration`;
- `OverviewService`;
- `IncomingCatalogReadService`;
- `ProcurementQueueV2ReadService`.

Their convenience constructors now create `EmployeeAccessService` directly and no longer accept the ignored legacy access argument.

No authorization rule, Access V1 scope rule, workflow rule, persistence model, endpoint, migration or DI registration semantics were changed.

## Test-code compatibility updates

Foundation test callsites that directly instantiate the five services were updated by removing the already ignored legacy `IAccessControl` argument.

Where that removal made a local variable or the test-only legacy access adapter dead, the dead test code was removed as part of the same compile-only adjustment.

No test assertions were weakened or disabled.

## Persistence and dependencies

- no migration;
- no EF snapshot change;
- no package/dependency change;
- no configuration change;
- no parser-runtime change;
- no change to `main`.

## Validation policy

Per owner rules for this fix stage, build/tests/manual browser testing are not executed here.

The required next step is AP-06 R2 from the final commit of this branch:

1. locked restore;
2. Release build;
3. full applicable Foundation/PostgreSQL tests;
4. AP-05 regressions;
5. clean-DB migration validation;
6. upgrade-path migration validation;
7. owner manual browser smoke after automated validation succeeds.

## Integration notes

1. This branch depends directly on AP-05 `752692d54747a4dd641790914963f1ea3f949961`.
2. It must travel with the linear Access V1 series AP-01 → AP-05; do not cherry-pick it onto pre-AP-05 `main` in isolation.
3. AP-06 validation commit `e60c4c6609a46c212b22cc7c7e56ee8309d0dcc4` is documentation-only and is not a production dependency of this fix.
4. The separate parser branch `codex/parser-runtime-url-stability` is intentionally not included.
5. Production readiness still depends on a successful AP-06 R2 and subsequent manual browser smoke.
