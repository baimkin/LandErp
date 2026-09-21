# Access V1 — AP-04 cutover report

## Result

AP-04 completes the Access V1 series.

Canonical base:

`AP-03 @ db1336075a09aafe283ba1fb3190c3ea54fe6f5e`

Working branch:

`codex/access-v1-ap-04-cutover`

`main` remains intentionally unchanged.

The runtime workflow authorization model after AP-04 is:

- **Owner** -> protected system override;
- **every non-Owner employee** -> explicit `EmployeeAccessSettings`;
- missing explicit settings for a non-Owner -> **fail closed / AccessDenied**.

There is no runtime fallback from workflow access to `RolePermissions`.

## 1. Data cutover migration

New data-only migration:

`20260921225000_AccessV1Cutover`

The migration inserts `EmployeeAccessSettings` only for non-Owner employees that do not already have a row.

Existing explicit Access V1 settings are never overwritten.

The backfill reproduces the exact AP-01/AP-03 compatibility mapping that existed before cutover:

- `manager_decisions.create` -> Incoming Process;
- `manager_queue.read` -> Incoming Read;
- `procurement_approvals.decide` -> Procurement Head;
- `manager_decisions.create` -> Procurement Manager;
- `manager_queue.read` -> Procurement Read;
- legacy assignment Scope -> Procurement ReadScope and WorkScope;
- `searches.manage` or `agents.manage` -> Collection Manage;
- `collection.read` -> Collection Read;
- `inspections.request` -> CanAssignInspections;
- `inspections.perform` -> CanPerformInspections;
- `procurement_purchase.confirm` -> CanConfirmPurchase;
- Manager/Head legacy decision permission -> CanManageTemplates;
- `audit.read` -> CanReadAudit.

Owner is intentionally not backfilled because its Access V1 remains a system override.

This is an irreversible data cutover migration. Its Down method does not delete rows because after AP-04 those rows may have been legitimately edited. Operational rollback therefore requires a pre-cutover database backup rather than destructive reverse SQL.

No schema/model snapshot change is required because the migration changes data only.

## 2. Runtime fallback removed

`EmployeeAccessService` no longer:

- reads `RolePermissions`;
- contains `LegacyConfiguration`;
- derives workflow capabilities from legacy role grants.

For a non-Owner it loads the existing `EmployeeAccessSettings` row.

If the row is missing, authorization fails closed.

This prevents a future role-permission change from silently changing workflow access.

`EmployeeAccessSource` now has only:

- `Configured`;
- `SystemOwner`.

The transition-only `LegacyPermissions` source is removed.

## 3. New employee creation and invitation

`CreateEmployee` and `InviteEmployee` now accept an Access V1 configuration.

Creation/invitation persists the employee, organization assignment and explicit `EmployeeAccessSettings` in the same transaction.

The Organization UI submits the Access V1 form together with:

- direct employee creation;
- invitation creation.

The Access V1 editor is therefore visible before a new employee is created.

For compatibility with internal callers that do not yet pass the optional field, the server persists explicit `NoAccess` rather than falling back to a role.

This is fail-safe:

- missing field does not grant workflow access;
- the new employee still has an authoritative explicit row.

Owner is the only exception: its system override remains authoritative and no normal Access V1 row is required.

## 4. UI after cutover

The transitional text:

- “legacy fallback”;
- “compatibility until AP-04”;
- “save Access V1 later”

is removed from the Organization UI.

Legacy Role / Scope fields remain because they still serve administrative/account authorization.

They are now labelled:

**Системная роль и административная область**

with an explicit explanation that workflow capabilities are controlled only by Access V1.

When creating a new employee, the form starts with explicit `NoAccess`.

The administrator can:

- choose a UI preset;
- edit individual fields;
- create/invite the employee with those settings atomically.

When the selected role is Owner, the UI shows the protected system-access explanation instead of editable ordinary Access V1 fields.

## 5. Role independence after cutover

Changing a normal employee's system/identity role no longer recalculates workflow access.

The employee keeps the existing explicit `EmployeeAccessSettings`.

The role continues to matter only for remaining administrative/account permission checks.

### Owner transitions

If an Owner is demoted to a normal role and has no explicit row, the server creates explicit `NoAccess` before the role change completes.

Therefore an Owner cannot be demoted into an undefined/fallback authorization state.

If a normal employee becomes Owner, the Owner system override becomes authoritative. Existing explicit settings can remain stored but are ignored while the employee is Owner.

## 6. Organization changes and active work

AP-04 extends the AP-03 safe-change invariant to organization assignment changes.

When a non-Owner employee's department/team/system role is changed, the server checks the employee's current explicit Access V1 against the proposed organization placement.

The change is blocked if it would make existing work invalid, including:

- active Procurement work moving outside the proposed WorkScope;
- a Team scope with no team;
- a Department scope with no department;
- pending Head responsibility losing compatible context;
- unfinished work that cannot remain visible under the proposed assignment.

This reuses the AP-02/AP-03 work-impact and handover semantics.

No new workflow is introduced.

## 7. Audit after cutover

For normal employees, full Audit access is determined by explicit:

`CanReadAudit`

and no longer depends on Procurement scope or legacy `audit.read`.

Owner remains a special case:

- Access V1 gives system Owner full Audit capability;
- the existing legacy/account security check is retained only as the Owner MFA/security gate.

This is not workflow fallback. It preserves the existing Owner/Administrator security requirement without introducing a new IAM subsystem.

## 8. What RolePermissions still do

AP-04 does **not** delete Role / RolePermissions.

They remain intentionally available for non-workflow administrative/account authorization such as:

- users.read;
- users.manage;
- organization.manage;
- roles.manage;
- account/security requirements;
- Owner MFA gate for Audit.

They no longer determine:

- Incoming workflow access;
- Procurement workflow access;
- Procurement read/work scope;
- Collection workflow access;
- inspection assign/perform capabilities;
- purchase confirmation;
- template management;
- normal employee Audit capability.

## 9. Capability matrix after AP-04

| User-visible capability | Persisted Access V1 | Navigation / UI | Server authority |
| --- | --- | --- | --- |
| View Incoming | `IncomingAccess >= Read` | Incoming menu + read page | effective CanReadIncoming |
| Process Incoming | `IncomingAccess = Process` | processing controls | effective CanProcessIncoming |
| Transfer Incoming to Procurement | Incoming Process | “Передать в закупку” | AP-02 TransferToProcurement + effective recipient |
| View Procurement | `ProcurementAccess >= Read` + ReadScope | Procurement menu/read-only UI | ProcurementReadContext |
| Work Procurement | `ProcurementAccess >= Manager` + WorkScope | dossier/actions flags | ProcurementWorkContext |
| Head decisions | `ProcurementAccess = Head` | Head action flags | CanHeadProcurement + state/self-approval invariants |
| View searches/parsers | `CollectionAccess >= Read` | Collection menu/read-only UI | CanReadCollection |
| Manage searches/parsers | `CollectionAccess = Manage` | create/run/connect controls | CanManageCollection |
| My Inspections | `CanPerformInspections` | My Inspections menu | capability + actual assignment |
| Assign inspection | `CanAssignInspections` | assign controls | capability + Procurement WorkScope |
| Perform inspection | `CanPerformInspections` | execution/media controls | capability + actual assignment |
| Confirm purchase | `CanConfirmPurchase` | purchase control | capability + concrete case + WorkScope/state |
| Manage shared templates | `CanManageTemplates` | template controls | effective capability |
| Read full Audit | `CanReadAudit` | Audit menu | effective capability; Owner also retains MFA security gate |
| Owner access | system override | full relevant navigation | SystemOwner |

## 10. Tests as code

AP-04 updates the earlier Access V1 tests to the post-cutover model.

### Updated

`EmployeeAccessSettingsTests`

now verifies:

- new non-Owner employees resolve from explicit settings;
- persisted changes are effective;
- WorkScope <= ReadScope invariant;
- deleting a non-Owner explicit row causes AccessDenied;
- Owner remains SystemOwner even if a restrictive row exists;
- the AP-04 migration backfills a missing row;
- the migration does not overwrite an existing explicit row.

`AccessV1Ap02WorkflowTests`

now expects fixture employees to resolve from explicit settings instead of legacy fallback.

`AccessV1Ap03UiTests`

now starts from explicit employee settings and removes transition-only LegacyPermissions assertions.

The shared Procurement test helper creates fixture employees with explicit test Access V1 settings. This test-only mapping does not exist in production code.

### New

`AccessV1Ap04CutoverTests`

verifies:

- changing system role does not recalculate explicit workflow access;
- runtime resolver source contains no RolePermissions fallback;
- Organization UI contains no legacy-fallback wording;
- create/invite UI submits explicit Access V1;
- shell still applies Owner-specific security handling.

## 11. Static verification

Per the established Access V1 workflow, AP-04 does not execute:

- actual build;
- actual test run;
- browser automation;
- production migration.

Prepared/static verification covers:

- one new data-only migration;
- no dependency/package changes;
- no unrelated configuration changes;
- no changes to old applied migrations;
- no workflow RolePermissions lookup in `EmployeeAccessService`;
- no `LegacyConfiguration`;
- no runtime `EmployeeAccessSource.LegacyPermissions`;
- explicit settings on create/invite;
- fail-closed missing settings;
- AP-03 effective navigation preserved;
- AP-02 workflow server checks preserved.

## 12. Deployment order

AP-04 must be deployed in normal migration-first application startup/deployment order:

1. take a database backup;
2. apply `20260921225000_AccessV1Cutover`;
3. verify every non-Owner employee has one `identity.employee_access_settings` row;
4. deploy AP-04 application code;
5. perform smoke checks for the agreed employee combinations.

Do not deploy the AP-04 resolver before the backfill migration has been applied.

## 13. Final acceptance scenarios

The series is designed to support these real combinations without role-name inference:

1. **Inspector-only**
   - My Inspections only;
   - performs only assigned inspections;
   - no Incoming/Procurement/Collection.

2. **Incoming-only**
   - reads/processes Incoming;
   - transfers to eligible Procurement employee;
   - does not gain Procurement visibility.

3. **Incoming + Procurement + Collection**
   - processes Incoming;
   - works cases inside WorkScope;
   - manages parser/search;
   - cannot inspect or purchase unless those flags are explicitly enabled.

4. **Incoming + Procurement + Collection + Purchase**
   - same as above;
   - can confirm purchase only for an accessible concrete case.

5. **Head performing normal Manager work**
   - Head includes Manager working capability;
   - normal manager operations remain available;
   - self-approval invariant remains enforced.

## AP-01 → AP-04 final state

- AP-01: Access V1 data model and effective access service.
- AP-02: real workflow authorization.
- AP-03: employee management UI, navigation and read-only UX.
- AP-04: explicit-data cutover, removal of workflow fallback and final consistency gate.

After AP-04, Access V1 is the authoritative workflow access model for LandErp.
