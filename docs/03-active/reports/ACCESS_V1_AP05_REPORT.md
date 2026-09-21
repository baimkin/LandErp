# Access V1 — AP-05 hardening report

**Repository:** `baimkin/LandErp`  
**Working branch:** `codex/access-v1-ap-05-hardening`  
**Base commit:** `86389029125fbc6483d032df3b73939f7a8cccd5` (AP-04)  
**Canonical main at task start:** `54fa184e98f41dba3f040cde41aec181153ea054`  
**Canonical main before publication preparation:** `54fa184e98f41dba3f040cde41aec181153ea054`

## Result

AP-05 hardens the Access V1 Procurement scope semantics found during the independent post-AP-04 review.

The defect was caused by treating `Own / AssignedObjects / Team / Department / Organization` as if every neighboring scope were a strict set subset. `Own` and `AssignedObjects` are responsibility-based and can cross Department/Team boundaries after a legitimate handover or Head assignment. A user could therefore be allowed to work a concrete case through `ProcurementWorkScope` while the same case disappeared from `ProcurementReadScope`.

The runtime rule is now explicit:

> **A concrete Procurement case that the employee can work is always readable by that employee.**

This is implemented only in the read projection. Mutation authorization is unchanged and still requires the configured `ProcurementWorkScope` plus the existing workflow/state/capability checks.

## Production changes

### Central visibility

`ProcurementVisibility.ApplyRead` now combines:

- configured `ProcurementReadScope`;
- concrete `ProcurementWorkScope` visibility, only when the employee has Manager/Head working capability.

The existing `Apply(..., AccessContext)` work filter is unchanged.

The configuration rank check in `EmployeeAccessRules.ContainsScope` remains a structural UI/configuration guard only. It is no longer relied on as proof that responsibility-based scopes are strict subsets of organizational scopes.

### Read surfaces moved to effective read visibility

The hardened read projection is used by:

- legacy Procurement queue;
- PropertyCase card;
- Incoming link targets;
- legacy listing → PropertyCase resolution;
- Procurement inspection read;
- Procurement attachment read;
- Procurement V2 list/detail;
- Procurement V2 negotiation/inspection modals;
- Overview Procurement aggregates/work feed.

No write command was broadened.

### UI explanation

Access V1 employee summary now explains that concrete working objects remain visible even when they are outside the base read area.

## Regression tests prepared as code

New PostgreSQL integration suite:

`tests/LandErp.Foundation.Tests/AccessV1Ap05HardeningTests.cs`

It covers:

1. cross-department handover to an employee with:
   - ReadScope = Department;
   - WorkScope = AssignedObjects;
   - the transferred case remains visible and writable;
   - an unrelated case from the other department remains hidden;
   - Procurement V2 follows the same visibility.

2. cross-department Forward to an effective Head with:
   - ReadScope = Department;
   - WorkScope = AssignedObjects;
   - the assigned Head can read and decide the case;
   - Return to the original manager remains valid.

Per owner policy for AP-05, tests/build/browser automation are **not executed in this task**. AP-06 is the factual build/test validation stage.

## Persistence

No schema or data-model change is required.

- no migration;
- no EF snapshot change;
- no dependency/package change;
- AP-01/AP-04 published migrations are untouched.

## Scope deliberately not changed

AP-05 does not redesign:

- Access V1 levels or presets;
- employee role/account authorization;
- handover workflow;
- Head decision workflow;
- inspection permissions;
- purchase confirmation;
- Collection access;
- Audit access;
- Owner override.

## Integration notes

1. AP-05 depends directly on AP-04 and must be moved together with the AP-01 → AP-04 linear series.
2. Do not cherry-pick AP-05 onto pre-AP-04 `main` by itself.
3. AP-06 should run Release restore/build and the applicable full PostgreSQL/Foundation test suite on the final AP-05 commit.
4. Migration validation in AP-06 still covers the existing AP-01/AP-04 migrations; AP-05 adds none.
5. Browser automation is intentionally out of scope. Owner performs the manual browser smoke after AP-06.
6. The key manual scenario is a cross-department assigned object: it must be visible to its actual worker without exposing unrelated cases outside the configured ReadScope.
