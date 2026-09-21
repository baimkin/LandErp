# Access V1 — AP-03 report

## Result

AP-03 connects the Access V1 model introduced in AP-01 and enforced by server workflows in AP-02 to the existing employee-management UI and the affected working UI.

Canonical base:

`AP-02 @ b3358031e52efb05f16d4f93b9e45be4713c36f2`

Working branch:

`codex/access-v1-ap-03-ui`

`main` is intentionally not part of this linear branch and is not modified by AP-03.

AP-03 does not introduce another authorization model. Persisted workflow access remains the single `EmployeeAccessSettings` row per employee, with the AP-01 legacy fallback retained only for employees that do not yet have an explicit row.

## Employee card — Access V1

The existing Organization / Employees UI now reads effective Access V1 for every visible employee and exposes a dedicated **Доступ** section for an employee.

The editor uses business language instead of permission IDs:

- Incoming: None / Read / Process;
- Procurement: None / Read / Manager / Head;
- Procurement ReadScope;
- Procurement WorkScope;
- Collection: None / Read / Manage;
- CanAssignInspections;
- CanPerformInspections;
- CanConfirmPurchase;
- CanManageTemplates;
- CanReadAudit.

The employee's position, department, team, manager and legacy role remain separate organization/transition fields and do not populate Access V1 automatically.

For the transition to AP-04, legacy Role / Scope are kept in a collapsed **compatibility** section. They are not presented as the Access V1 editor.

Owner is rendered as protected system access. The UI does not offer an Access V1 save for Owner, and the server independently rejects such a save.

## Effective access in the employee list

The employee list now includes a compact access summary:

- Procurement level;
- Incoming level;
- Collection / parser level;
- inspection capability;
- purchase capability;
- Procurement read/work scopes;
- access source: explicit Access V1, legacy fallback or system Owner.

This keeps job position separate from real working access.

## UI-only presets

`AccessV1Ui.Presets` provides quick form-fill presets:

- Оператор входящих;
- Менеджер закупки;
- Руководитель закупки;
- Осмотрщик;
- Закупка + парсеры;
- Наблюдатель.

A preset is only an `EmployeeAccessConfiguration` value copied into the form.

There is:

- no preset table;
- no preset ID in `EmployeeAccessSettings`;
- no inheritance;
- no profile authorization entity.

After applying a preset the individual fields remain editable. Future changes to preset definitions therefore cannot change already persisted employee access.

## Human-readable access summary

`AccessV1Ui.Summary` builds a short explanation directly from the current form/effective settings.

It describes:

- what Procurement scope is visible;
- whether Procurement is read-only or writable and in what WorkScope;
- Incoming read/process state;
- Collection read/manage state;
- assign/perform inspection capabilities separately;
- purchase confirmation;
- template management;
- full audit access.

The summary never derives workflow capabilities from a role name.

## Explicit settings read/write path

Application contract additions in `IOrganizationWorkspace`:

- `ReadEmployeeAccessAsync`;
- `SaveEmployeeAccessAsync`.

The Organization read model also includes `EmployeeAccessView` per employee.

Minimal HTTP endpoints were added:

- `GET /api/organization/employees/{employeeId}/access`;
- `PUT /api/organization/employees/{employeeId}/access`.

The HTTP path delegates to the same workspace methods used by the Blazor UI. It does not duplicate Access V1 rules.

The PUT endpoint validates antiforgery and the workspace remains the authoritative security/validation boundary.

## Server validation, concurrency and Owner protection

`SaveEmployeeAccessAsync`:

1. validates `EmployeeAccessConfiguration` through the existing `EmployeeAccessRules`;
2. requires organization-wide employee administration plus the existing access-administration permission;
3. rejects Owner changes;
4. validates Team/Department scopes against the employee's real organization assignment;
5. uses the existing organization advisory lock;
6. checks `EmployeeAccessSettings.Version` for optimistic concurrency;
7. validates active-work invariants before applying a reduction;
8. persists only the existing `EmployeeAccessSettings` entity;
9. writes one audit event with effective before-state and explicit after-state.

No second Access V1 source is introduced.

## Audit

Access changes create:

`EmployeeAccessChanged`

The audit payload contains:

- effective settings before the change;
- previous access source;
- previous explicit version, if any;
- full settings after the change.

Audit UI maps the event to:

**Изменены настройки Access V1 сотрудника**

The Audit page, API and navigation now use effective Access V1 rather than the legacy `audit.read` route policy as their workflow-access boundary.

Transition compatibility is preserved: for `LegacyPermissions` / system Owner sources, Audit still passes through the previous `audit.read` security gate, including its organization-scope and Owner/Administrator MFA behavior. Explicit Access V1 `CanReadAudit` is independent of Procurement scope as designed.

## Safe access reduction with active work

AP-03 reuses the existing AP-02 handover model.

Before reducing access, the server checks the employee's current work state.

The save is blocked when the proposed access would leave work that the employee can no longer complete, including:

- unfinished `SiteInspection` when `CanPerformInspections` would be removed;
- active Procurement responsibility when Procurement would fall below Manager;
- active cases that would fall outside the new WorkScope;
- an assigned `pending_head` case when Head access would be removed.

The error instructs the administrator to transfer active work first.

The employee UI exposes **Переназначить активную работу**, which uses the existing AP-02 handover flow. No new handover/workflow engine was created.

The handover modal also shows unfinished inspections explicitly.

## Navigation on effective Access V1

The application shell now resolves effective Access V1 for the authenticated employee.

Visibility is based on:

- Incoming -> `CanReadIncoming`;
- Procurement -> `CanReadProcurement`;
- My Inspections -> `CanPerformInspections`;
- Searches / Parser -> `CanReadCollection`;
- Audit -> effective `CanReadAudit`.

The shell no longer uses the workflow legacy policies `manager_queue.read`, `inspections.read`, `collection.read` or `audit.read` as the navigation model. For legacy/system transition sources only, the Audit entry additionally preserves the old security/MFA gate until AP-04.

Menu visibility is only UX. AP-02 server authorization remains the security boundary.

## Read-only UI

### Incoming.Read

The page and detail remain readable.

Processing controls are not active:

- manual creation is hidden;
- saved-filter mutations are hidden;
- classification/monitoring/duplicate processing controls are unavailable;
- the processing footer is replaced by a read-only message;
- a direct `?manual=true` URL does not render the mutation form.

Checklist processing buttons are disabled for read-only access.

### Procurement.Read

The list and detail remain readable according to ReadScope.

Working controls continue to use server-projected capability flags.

AP-03 additionally removes the previously active generic **Решение по объекту** button for a read-only card.

Inspection UI now uses separate server-projected `CanAssignInspections` and `CanPerformInspections` flags rather than generic `CanManageDossier`.

### Collection.Read

Search/parser state and history remain visible.

Create/update/run/connect/revoke/configuration controls are rendered only for effective `CanManageCollection`.

The page no longer uses `searches.manage` / `agents.manage` AuthorizeView blocks for these workflow controls.

## Incoming -> Procurement

AP-02 contracts are connected to Incoming V2:

- `ReadProcurementTargetsAsync`;
- `TransferToProcurementAsync`.

For an Incoming Process employee, the UI provides a separate action:

**Передать в закупку**

The modal lists only effective Procurement recipients returned by AP-02.

The current employee is excluded from that recipient list when self-work is available, keeping the two meanings explicit:

- **Передать в закупку** -> another eligible procurement employee becomes responsible;
- **Взять в работу** -> shown separately only when the current employee also has effective Procurement Manager+ access.

Successful transfer does not navigate an Incoming-only employee into Procurement.

## Inspections UI

The literal-role message:

`Нет сотрудников с ролью Inspector`

was removed.

The UI now says that no employees have the **right to perform inspections** and points to Access V1.

The My Inspections navigation item is driven by `CanPerformInspections`.

Procurement inspection controls are also separated:

- assignment requires `CanAssignInspections`;
- execution/media actions require `CanPerformInspections`;
- read-only inspection result remains readable through Procurement read access.

## Tests as code

A new focused suite was prepared:

`tests/LandErp.Foundation.Tests/AccessV1Ap03UiTests.cs`

It covers:

- explicit settings surfaced in Organization read model;
- explicit save;
- invalid WorkScope > ReadScope;
- Owner protection;
- before/after audit payload;
- optimistic concurrency;
- explicit CanReadAudit through the effective Audit boundary;
- presets as form values rather than a persistence/security model;
- navigation for inspector-only, incoming-only, combined Procurement/Collection and purchase-enabled scenarios;
- read-only effective capabilities;
- Incoming transfer UI contract;
- Collection manage controls using effective access;
- Procurement decision UI using server capability flags;
- inspection UI using separate assign/perform capability flags;
- active unfinished inspection cannot be orphaned by access reduction;
- active Procurement responsibility cannot be orphaned by access reduction;
- UI does not depend on the literal role name `Inspector`.

Per AP-03 instruction these tests were **not executed**.

## Static verification performed

No actual build, test run, browser automation or production migration was executed.

Static pre-publication checks include:

- final scope compared against AP-02 base;
- no migrations;
- no package/dependency changes;
- no appsettings / secrets / credential files;
- balanced C# braces in changed C# files;
- no conflict markers;
- no temporary generated placeholders;
- affected UI has no targeted legacy workflow AuthorizeView policies;
- literal `Inspector` role dependency removed from inspection UI;
- new Procurement inspection capability fields were added as init-only record properties, preserving existing positional constructors and reducing compatibility risk;
- AP-01/AP-02 workflow code remains present and the AP-03 changes are additive around the existing model.

## Intentionally not done in AP-03

AP-03 does not:

- remove legacy fallback;
- backfill every employee with explicit settings;
- remove Role / RolePermissions;
- remove legacy RoleId / Scope from employee organization assignment;
- introduce a permission designer;
- change Owner override;
- introduce a purchase-approval workflow;
- redesign Incoming, Procurement or Organization outside the Access V1 integration;
- execute migrations/build/tests/browser automation.

Newly created/invited employees still begin with the AP-01 legacy fallback until an administrator opens the employee and saves explicit Access V1 settings. This is intentional until AP-04 cutover.

# Integration notes for AP-04

1. **Backfill/cutover**
   - create a deliberate migration/backfill plan for employees that still have `EmployeeAccessSource.LegacyPermissions`;
   - preserve the effective access they had immediately before cutover unless a business decision explicitly changes it;
   - do not infer new workflow access from position/job title.

2. **New employee creation**
   - after fallback removal, employee create/invite must persist explicit Access V1 atomically or require explicit access selection before activation;
   - the AP-03 UI presets may continue to fill that form, but a preset ID must not become persisted authorization state.

3. **Remove transition UI**
   - remove or repurpose the collapsed legacy Role / Scope compatibility section only after all remaining consumers are audited;
   - do not remove organization structure fields such as department/team/manager/position.

4. **Remaining legacy permission inventory**
   - inventory all remaining `RolePermissions` / policy consumers;
   - distinguish workflow permissions already replaced by Access V1 from administrative/account permissions that may intentionally remain;
   - remove legacy workflow authority only after the backfill gate passes.

5. **Audit fallback compatibility**
   - AP-03 preserves the historical organization-scope and Owner/Administrator MFA gate for legacy/system `audit.read`;
   - after fallback removal, remove the transition-only legacy audit check while retaining the intended account-security requirements for Owner.

6. **Active-work cutover gate**
   - before mass-changing existing settings, run the same handover/work-impact checks used by AP-03;
   - do not create dangling inspections, Procurement assignments, checks or pending-head responsibilities.

7. **Navigation and direct routes**
   - verify that every Access V1 navigation entry and direct route has the same effective server boundary;
   - hiding a menu item must remain UX only.

8. **Series acceptance**
   - run the full real build/test suite;
   - run the agreed browser scenarios for the employee Access UI, Incoming read/process/transfer, Procurement read/manager/head, Collection read/manage and inspector-only flow;
   - apply the migration on a clean database and an upgraded AP-03 database in test environments;
   - verify the five core combinations: inspector-only, incoming-only, incoming+procurement+collection, the same plus purchase, and Head performing normal Manager work.

9. **Compatibility cleanup**
   - after successful cutover, remove fallback-only compatibility constructors/helpers and dead legacy workflow-policy UI conditions if they have no remaining callers.

10. **Final evidence**
    - AP-04 should produce a final matrix mapping every user-visible capability to its persisted setting, navigation condition and server authorization check.
