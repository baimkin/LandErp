# Access V1 — AP-06 R5 final fix report

**Repository:** `baimkin/LandErp`  
**Working branch:** `codex/access-v1-ap-06-r4-test-alignment`  
**Base:** `954b8617ff4d079ba841d33a2e4ccc7d9dee4bbe`

## Scope

R5 closes the actionable findings from the independent Access V1 audit without changing the intended Procurement, Inspection or Purchase authorization model.

### Production authorization

- `OrganizationWorkspace.InviteAsync` now uses the same Access-admin boundary as explicit Access V1 editing:
  - organization-scoped `users.manage`;
  - organization-scoped `roles.manage`.
- This prevents an actor that can only manage users from assigning arbitrary Access V1 capabilities through an invitation.
- No Procurement workflow, object visibility, inspector execution, purchase confirmation or Owner override logic was weakened or broadened.

### Test alignment

The four stale AP-06 failures are corrected as tests, not production behavior:

1. removed the obsolete assumption that Procurement Head cannot perform ordinary Manager work;
2. removed the obsolete assumption that Head cannot create a manual PropertyCase;
3. removed `Duplicate` from the generic `SetDispositionAsync` lifecycle loop;
4. classification coverage now creates `Duplicate` through the dedicated same-object link workflow.

AP-02 now explicitly verifies both sides of the independent boundary:
a Head with `Incoming=Process` can take an Incoming item to work, while a Head with `Incoming=None` cannot, even though Head includes Manager Procurement work.

### Explicit Access V1 test profiles

The primary Procurement fixture now passes explicit Manager and Head Access V1 settings instead of relying on the system-role name to establish workflow access.

Known Access-sensitive inspection and B1-03 recipient scenarios also pass explicit settings.

The helper keeps a compatibility fallback for older unrelated tests to avoid broad test churn; new Access V1 tests should pass explicit settings.

### Cutover migration coverage

The cutover test now covers:

- ProcurementManager;
- ProcurementHead;
- Inspector;
- Administrator;
- Owner exclusion from `employee_access_settings`;
- preservation of an existing explicit Access V1 row;
- all capability flags;
- Procurement read/work scopes;
- `HasPendingModelChanges()`.

The migration SQL itself was not changed.

### Browser categorization

The three Foundation tests that execute `ProcurementUiScenario` are now also tagged `Browser`:

- Phase 1 Incoming UI scenario;
- Phase 3 Incoming browser scenario;
- Phase 5 full Procurement/inspection browser scenario.

This allows AP-06 non-browser PostgreSQL validation to exclude them reliably.

### Terminology

The `AssignmentChanged` audit title no longer says “legacy-доступ”; it now describes assignment, system role and administrative scope.

## Intentionally unchanged

- `main`;
- production/work databases;
- published migrations;
- Access V1 level semantics;
- cross-organization and department visibility logic;
- Owner system override;
- inspection assignment/performance rules;
- purchase confirmation rules;
- generic `Duplicate` rejection in `SetDispositionAsync`.

The historical/model database comment describing the pre-AP-04 compatibility phase is not changed in R5 because changing an EF relational comment would create schema-model drift and require a dedicated migration. That cleanup is not needed for Access V1 correctness.

## Validation status

R5 is prepared for owner validation.

No local PostgreSQL environment or browser runner was used from this chat, so this report does **not** claim a passing build or test run.

Recommended final gate:

1. locked restore;
2. Release build;
3. full Foundation/PostgreSQL suite excluding `Browser`;
4. focused Access V1 / organization / incoming / inspection / procurement tests;
5. clean migration to latest;
6. upgrade from `20260921183000_EmployeeAccessSettings` through `20260921225000_AccessV1Cutover`;
7. verify explicit-row preservation, non-Owner backfill, Owner exclusion and `HasPendingModelChanges() == false`;
8. browser smoke separately when a browser environment is available.

No production data should be used for this validation.
