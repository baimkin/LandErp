# Access V1 — AP-01 report

**Repository:** `baimkin/LandErp`  
**Working branch:** `codex/access-v1-ap-01-foundation`  
**Base commit:** `54fa184e98f41dba3f040cde41aec181153ea054`  
**Main before publication check:** `54fa184e98f41dba3f040cde41aec181153ea054` (unchanged from base)

## Result

AP-01 adds the persistence and server-contract foundation for employee-specific Access V1 settings without switching existing Incoming, Procurement, Collection or Inspection authorization checks.

The implementation deliberately uses one new entity/table, `EmployeeAccessSettings`, keyed by `EmployeeId`. It does not add role-profile entities, permission inheritance, a generic IAM layer, duplicate organization identifiers, or a second scope model.

The existing unique `organization.employees.user_id` constraint remains the one-employee/one-account boundary. Positions remain organizational metadata and are not consulted by the new access resolver.

## Persisted settings

`identity.employee_access_settings` stores:

- `IncomingAccess`: None / Read / Process;
- `ProcurementAccess`: None / Read / Manager / Head;
- `ProcurementReadScope`;
- `ProcurementWorkScope`;
- `CollectionAccess`: None / Read / Manage;
- `CanAssignInspections`;
- `CanPerformInspections`;
- `CanConfirmPurchase`;
- `CanManageTemplates`;
- `CanReadAudit`;
- `Version` for optimistic concurrency.

Existing `AccessScope` is reused. No separate Access V1 scope enum/table is introduced.

## Invariants

`EmployeeAccessRules` is the common rule boundary. It validates enum values and enforces that Procurement work scope is not broader than Procurement read scope.

The same rule is executed from `LandErpDbContext.SaveChanges/SaveChangesAsync` for added or modified `EmployeeAccessSettings`, so normal EF persistence cannot save an invalid combination.

No artificial coupling was added between independent capabilities. In particular, performing an assigned inspection does not require Procurement access, and purchase confirmation remains a separate flag.

## Effective access and compatibility

`IEmployeeAccessService` / `EmployeeAccessService` resolves the active employee and returns:

- effective Access V1 settings;
- employee/organization/department/team context;
- separate `ProcurementReadContext` and `ProcurementWorkContext` for AP-02 object-scope checks;
- source of the effective settings.

Compatibility policy:

1. **System Owner** always resolves to full Access V1 capabilities and Organization read/work scopes. A restrictive explicit row cannot reduce Owner.
2. If an explicit `EmployeeAccessSettings` row exists, it is used.
3. If no row exists, settings are derived from the current role permission grants and current assignment scope.

This means the migration creates no access rows and does not silently remove access from existing employees. Current authorization remains authoritative until AP-02 switches individual operations.

Legacy mapping intentionally follows the permissions used by the current production code:

- Incoming Read ← `manager_queue.read`;
- Incoming Process ← `manager_decisions.create`;
- Procurement Read / Manager / Head ← `manager_queue.read` / `manager_decisions.create` / `procurement_approvals.decide`;
- Collection Manage ← current search-manage or agent-manage permission;
- inspection assignment/performance, purchase confirmation and audit read map from their existing permissions;
- template management follows the current Manager/Head seam.

## Migration

One migration is sufficient:

`20260921183000_EmployeeAccessSettings`

It only creates `identity.employee_access_settings` with a PK/FK to `organization.employees`, required PostgreSQL comments, and no data backfill. The EF model snapshot is updated to the same model.

No published migration was edited.

## Tests added

`EmployeeAccessSettingsTests.cs` covers:

- work scope cannot exceed read scope;
- scope-order helper used by future object-scope checks;
- legacy permission fallback for an existing ProcurementManager;
- explicit settings overriding the fallback for one employee;
- EF persistence rejecting an invalid work/read scope combination;
- protected Owner resolving to full Access V1 settings even if a restrictive row exists;
- runtime-role read of the new table after standard production grants are applied.

Per task rules, the owner performs the factual test run/manual testing. The code was prepared for the existing PostgreSQL test harness; no test result is claimed in this report.

## Out of scope retained

No changes were made to:

- OrganizationPage or employee UI;
- existing permission policies/checks in Incoming, Procurement, Collection or Inspections;
- procurement workflow;
- inspection workflow;
- purchase/approval business logic;
- built-in role deletion or migration;
- dependency/package versions.

## Dependencies on other branches

None. AP-01 is based only on the supplied canonical main commit.

## Integration notes for AP-02

1. Switch operations incrementally; do not replace all legacy checks in one sweep.
2. For Procurement reads use `EffectiveEmployeeAccess.ProcurementReadContext`; for mutations use `ProcurementWorkContext` plus the required capability level/flag.
3. Keep “capability + concrete object scope” as two checks. The new contexts intentionally fit the existing Procurement visibility model instead of introducing a generic object-authorizer.
4. Assigned inspection execution must check `CanPerformInspections` plus the actual inspection assignment and must not require Procurement Read.
5. Inspection assignment must use `CanAssignInspections` and the caller's applicable Procurement read/work scope for the target case.
6. Owner is a system override. The future settings UI should show it as protected and should not present a normal editable Access V1 row as the source of Owner authority.
7. Do not bulk-backfill settings merely to remove the fallback. Migrate employees only when AP-02 has mapped the affected operations and verified equivalent behavior.
8. The old Collection model has separate search-manage and agent-manage permissions while Access V1 intentionally has one Manage level. The fallback treats either legacy manage permission as Manage so AP-01 cannot remove an existing management capability. AP-02 should switch built-in profiles and operation checks together.
9. When the employee-settings write API/UI is added, reuse `IEmployeeAccessService.Validate` / `EmployeeAccessRules`, optimistic `Version`, and add audit of before/after values.
10. Existing role/permission tables remain required during transition and must not be deleted until all AP-02 checks and migration/backfill are complete.
