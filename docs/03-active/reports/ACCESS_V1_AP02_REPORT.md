# Access V1 — AP-02 report

**Repository:** `baimkin/LandErp`  
**Working branch:** `codex/access-v1-ap-02-workflows`  
**Base commit:** `9c520decc8428bcc7891dc6ba22910421be3452a` (AP-01)  
**Canonical main at task start:** `54fa184e98f41dba3f040cde41aec181153ea054`  
**Main before publication check:** `54fa184e98f41dba3f040cde41aec181153ea054` — unchanged.  
**Main policy:** intentionally unchanged during the Access V1 linear series.

## Result

AP-02 makes the Access V1 model introduced by AP-01 the server-side authorization source for the targeted working workflows:

- Incoming;
- Procurement;
- Collection / Searches / Parser administration;
- inspections;
- purchase confirmation;
- candidate/recipient selection and employee handover paths touched by those workflows.

The existing AP-01 legacy fallback remains inside `IEmployeeAccessService`. Workflow code no longer bypasses an explicit `EmployeeAccessSettings` row by querying the employee's legacy role/permission grants directly.

No universal authorization framework, role inheritance model, second scope system, workflow engine or new persistence entity was added.

## Minimal extension of AP-01

The only cross-cutting extension to `IEmployeeAccessService` is:

`ResolveActiveEmployeesAsync(organizationId)`

It returns the same effective settings used for the current actor for all active employees in an organization. This allows candidate lists to use one semantic source without reproducing AP-01 fallback rules in Procurement, inspections or handover code.

Convenience predicates were added to `EffectiveEmployeeAccess` for the existing levels:

- Incoming Read / Process;
- Procurement Read / Manager / Head;
- Collection Read / Manage.

Owner protection and the AP-01 explicit-settings-first / legacy-fallback behavior remain unchanged.

## Incoming

Incoming authorization is now independent of Procurement:

- `None` rejects Incoming reads and commands;
- `Read` permits reading but not mutations;
- `Process` permits classification, monitoring, duplicate processing, manual Incoming creation and review-state mutations.

The V2 read projection and saved filter presets use the same effective Incoming access.

### Transfer to Procurement

The old `TakeToWork` path mixed two different product actions:

1. a Procurement-capable employee taking/linking a source into their own accessible Procurement work;
2. an Incoming-only employee handing a source to Procurement.

AP-02 keeps `TakeToWorkAsync` for the first case and adds:

- `ReadProcurementTargetsAsync`;
- `TransferToProcurementAsync`;
- `TransferCatalogItemToProcurement`;
- `IncomingProcurementTarget`.

The transfer target is selected by effective `ProcurementAccess >= Manager` and a work scope capable of receiving the new PropertyCase. The new case is created with the target employee as manager/assignee/task owner. The Incoming actor remains the audit/source-link actor and does not acquire Procurement visibility.

An Incoming-only employee therefore can hand work to Procurement without becoming the PropertyCase manager.

### Incoming Read UI boundary

Opening an Incoming detail currently records a `ReviewStarted` event. That is a mutation and remains a Process operation.

The page was adjusted so denial of this optional review-registration side effect does not prevent an `IncomingAccess.Read` employee from opening the detail or gallery. Read access therefore stays genuinely read-only.

## Procurement

All targeted Procurement reads use:

`EffectiveEmployeeAccess.ProcurementReadContext`

All targeted PropertyCase mutations use:

`EffectiveEmployeeAccess.ProcurementWorkContext`

The server checks both the minimum capability level and concrete case visibility under the corresponding scope.

Semantics:

- `None`: no general Procurement access;
- `Read`: read-only within ReadScope;
- `Manager`: read plus normal case work within WorkScope;
- `Head`: includes Manager work plus Head decisions.

A wider ReadScope and narrower WorkScope are now real runtime semantics rather than only stored configuration.

### Head includes Manager

Manager operations test `ProcurementAccess >= Manager`, so Head performs ordinary manager work where the PropertyCase is inside its WorkScope.

Head-only actions still require `Head`.

The existing self-approval protection is retained: a Head cannot approve a pending request where that same employee is the PropertyCase manager through the ordinary approval path.

The workflow/stage rules themselves were not redesigned.

## Procurement V2 read model

`ProcurementQueueV2ReadService` now uses the same Access V1 ReadScope/WorkScope semantics as `ProcurementWorkspace`.

This includes:

- queue visibility;
- detail visibility;
- negotiation/history modals;
- inspection report reads;
- `CanCreateManualCase`;
- Manager/Head action flags;
- available assignee lists.

This prevents `/procurement-v2` from remaining a parallel legacy-permission path after the main workspace has switched.

## Collection / Searches / Parser

`CollectionAdministration` now uses only effective `CollectionAccess`:

- `None`: no Collection administration;
- `Read`: current agents/groups/searches/jobs/status can be read;
- `Manage`: existing agent, search, group, schedule and enqueue operations are allowed.

Manage remains organization-scoped as agreed.

Collection access does not grant Incoming or Procurement access.

The Overview collection summary/settings path was moved to the same effective semantics so the Home view does not reintroduce the old Collection permission combination.

## Inspections

Inspection assignment and execution are now separate capabilities.

### Assign

`AssignInspectionAsync` requires:

- `ProcurementAccess >= Manager`;
- target PropertyCase inside `ProcurementWorkScope`;
- `CanAssignInspections`.

Inspector candidates are selected through effective `CanPerformInspections`, not role name or `RolePermissions`.

### Perform

Performing an inspection requires:

- effective `CanPerformInspections`;
- an actual `SiteInspection.InspectorEmployeeId` assignment to the employee;
- a non-terminal PropertyCase/inspection state.

It does **not** require general Procurement access.

The previous Manager/Head dossier bypass was removed. A Procurement Manager or Head with `CanPerformInspections=false` can read an inspection result through Procurement ReadScope but cannot perform the inspection.

The same semantics are used for:

- “Мои осмотры”;
- inspection workspace;
- inspector candidate selection;
- starting/saving/completing the inspection;
- adding inspection attachments;
- reading inspection attachments;
- retry/recovery of inspection attachment uploads.

A general Procurement user can read inspection material inside an accessible dossier; that read does not grant execution rights.

## Incomplete inspections and handover

The existing employee-work handover model previously ignored unfinished `SiteInspection` assignments.

AP-02 adds unfinished inspections to `EmployeeWorkImpact` as `OpenInspections`.

For normal handover:

- a recipient must have effective `CanPerformInspections` when unfinished inspections exist;
- unfinished inspections are reassigned to the selected recipient;
- timeline and notification records are added.

For emergency deactivation without handover:

- the unfinished inspection remains explicit outstanding work;
- a handover-pending timeline entry is recorded;
- the employee cannot be treated as having no active work merely because they have no Procurement case/task responsibility.

No new inspection workflow was introduced.

## Purchase

`MarkAcquiredAsync` and acquisition correction require:

- `ProcurementAccess >= Manager`;
- the PropertyCase inside `ProcurementWorkScope`;
- `CanConfirmPurchase=true`;
- all existing state/concurrency/business invariants.

`CanConfirmPurchase` does not imply Head, inspection execution, template administration or any Collection/Incoming capability.

The purchase approval business process itself was not changed.

## Candidate and recipient lists

Within AP-02 scope, direct legacy `RolePermissions` filtering was removed from:

- Incoming → Procurement recipients;
- Procurement Manager/Head/assignee candidates;
- Procurement V2 available assignees;
- inspection performers;
- employee handover candidates.

Candidate eligibility is now calculated from the same effective settings as execution authorization, including AP-01 fallback for unmigrated employees.

This prevents an explicitly capable employee from disappearing from a list merely because their legacy role lacks the corresponding grant, and prevents an explicitly disabled capability from being restored by an old role.

## Legacy compatibility

AP-02 does not delete roles or `RolePermissions`.

For an employee without `EmployeeAccessSettings`, `IEmployeeAccessService` still derives effective values from the existing role grants and assignment scope exactly through the AP-01 fallback.

For an employee with explicit settings, the targeted AP-02 workflows use those effective settings and do not fall through to a second legacy permission check.

Owner remains the AP-01 protected system override.

## Endpoint and page authorization boundary

Legacy ASP.NET policy attributes for the switched Incoming / Procurement / Collection / Inspections pages and the `/api/procurement` group would otherwise create a second authorization path before the Access V1 use case.

Those entry points now require authentication only. Actual business authorization is performed by the server service method through `IEmployeeAccessService`.

This is not a removal of authorization; it removes duplicate legacy authorization in front of the new authoritative workflow checks.

## Persistence / migrations

No persistence change is required in AP-02.

- no new migration;
- no EF snapshot change;
- no package/dependency change.

AP-02 reuses `EmployeeAccessSettings` and `AccessScope` from AP-01.

## Tests prepared as code

A dedicated `AccessV1Ap02WorkflowTests.cs` covers:

1. inspector-only employee:
   - sees assigned inspections;
   - performs the assigned inspection;
   - no Incoming, Procurement queue or Collection access;
2. explicit Read levels:
   - Incoming/Procurement/Collection reads succeed;
   - their mutations are rejected;
3. Incoming-only processor:
   - processes Incoming;
   - transfers to an effective Procurement recipient;
   - does not gain Procurement;
   - an explicit Procurement capability makes a legacy Inspector-role employee a valid recipient;
   - explicit Procurement=None overrides a legacy ProcurementManager role;
4. wider ReadScope than WorkScope:
   - another same-department case is readable;
   - mutation outside WorkScope is denied;
5. Collection Manage is independent of Procurement;
6. Manager with `CanPerformInspections=false` cannot perform an inspection;
7. `CanConfirmPurchase=false` denies purchase;
8. explicit purchase capability permits purchase on an accessible case without granting Head/template rights;
9. Head performs ordinary Manager operations;
10. Head cannot ordinarily self-approve its own pending request;
11. AP-01 legacy fallback remains active for an unmigrated employee;
12. unfinished inspection handover uses effective performer access and reassigns the inspection.

Existing inspection snapshot/concurrency/media tests were adapted to the new rule by explicitly enabling `CanPerformInspections` and assigning the test inspection before performing it. Their underlying behavior assertions remain unchanged.

Per the AP-02 task instruction, **no build, test run, browser test or migration execution was performed**. No successful runtime result is claimed.

## Scope deliberately not included

AP-02 does not implement:

- EmployeeAccessSettings editor UI;
- OrganizationPage redesign;
- explicit-settings migration/backfill;
- removal of roles or RolePermissions;
- position changes;
- universal IAM;
- arbitrary role builder;
- mandatory purchase approval redesign;
- Owner workflow override changes;
- unrelated UX work.

## Integration notes for AP-03

1. AP-03 should edit the existing single `EmployeeAccessSettings` row. Do not create UI roles as authorization entities; presets/“roles” may only fill these fields.
2. Add a server write/read contract for employee access settings with optimistic `Version`, `EmployeeAccessRules.Validate`, organization boundary checks and before/after audit. UI must not write the EF entity directly.
3. Owner must be rendered as protected system access. A normal editable row must not be presented as the authority for Owner.
4. Navigation/menu visibility should move from legacy permission policies to effective Access V1 capabilities. AP-02 pages are authenticated at the route boundary and authorize inside the use case specifically to avoid a second legacy authority.
5. Incoming UI should distinguish:
   - “take/link into my Procurement work” when the actor has suitable Procurement access;
   - “transfer to Procurement” with `ReadProcurementTargetsAsync` when the employee only processes Incoming.
6. Procurement UI should use the server-returned action flags and effective access for read-only vs editable states; do not infer edit rights from job title or legacy role name.
7. Inspection UI must keep execution controls driven by `CanPerform` returned by the server. A Manager/Head title must never enable the checklist by itself.
8. Collection UI should expose read-only state for `CollectionAccess.Read` and management controls only for `Manage`.
9. Employee handover/deactivation UI should display `OpenInspections` along with cases/tasks/checks. It must not suggest that an employee is safe to deactivate while an unfinished inspection remains.
10. When AP-03 allows changing explicit settings, reducing `CanPerformInspections` for an employee with unfinished inspections needs an explicit product-safe path (block the change until handover or require a handover). Do not silently orphan assigned inspections.
11. Likewise, reducing Procurement WorkScope/capability for an employee with active responsibility should reuse the existing handover impact semantics rather than leave inaccessible work.
12. Keep AP-01 legacy fallback through AP-03. Final backfill/cutover and removal of workflow dependency on legacy roles belongs to AP-04.
