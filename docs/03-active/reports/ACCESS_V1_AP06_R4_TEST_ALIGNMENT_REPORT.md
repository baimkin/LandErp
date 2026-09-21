# Access V1 AP-06 R4 — test alignment

**Status: NOT READY**

| Item | Value |
|---|---|
| Repository | `baimkin/LandErp` |
| Branch | `codex/access-v1-ap-06-r4-test-alignment` |
| AP-06 base | `888b5fa73927455f714c4171e921d6709e4bc45c` |
| R4 base | `f9c836c72c49a6477087c888390a398277136041` |
| Result commit | This report and six test files at branch HEAD |
| `main` at start/end | `54fa184e98f41dba3f040cde41aec181153ea054` |

## Changes

Only test scenarios were aligned with current Access V1 rules. AP-02 reads an Incoming listing before it enters work. Collection tests create real employees with explicit Read/Manage access. The organization audit test expects the current legacy-access event title. Inspection tests give the actor the specific inspection, queue-read, or purchase-confirmation permission required by each scenario; the queue-report test also assigns the inspector and uses a saved answer option. Production code, migrations, packages, and working/production databases were not changed.

Changed files: `AccessV1Ap02WorkflowTests.cs`, `CollectionSchedulingTests.cs`, `IdentityOrganizationTests.cs`, `InspectionAcquisitionTests.cs`, `ProcurementQueueV2ReadTests.cs`, and `ReleasePackageB102Tests.cs` under `tests/LandErp.Foundation.Tests/`, plus this report.

## Checks

| Check | Exit | Result |
|---|---:|---|
| `dotnet restore LandErp.slnx --locked-mode` | 0 | Passed. |
| `dotnet build LandErp.slnx -c Release --no-restore` | 0 | Passed after edits; 0 warnings, 0 errors. |
| Targeted PostgreSQL tests for AP-02, collection, audit | 0 | 10 passed, 0 failed, 0 skipped. |
| Targeted inspection and terminal-state tests | 0 | 3 passed, 0 failed, 0 skipped. |
| `NegotiationHistoryInspectionReportAndAttachmentsStayCaseScoped` after final fixture edit | 0 | 1 passed, 0 failed, 0 skipped. |
| `git diff --check` | 0 | Passed. |

A non-browser Foundation/PostgreSQL run before the final inspection fixture edits completed with **159 passed, 8 failed, 0 skipped (167 total)**. It excluded browser methods, `IdentityUiTests`, `LiveStorage`, and two embedded HTTPS Server integration tests. Four of its eight failures were corrected and passed targeted reruns (three inspection/terminal-state tests and the queue-report test). The other corrected tests had passed their targeted reruns before this full run. The full filtered suite was not repeated after the last edits; its final aggregate count is therefore unknown.

The first unfiltered full run was interrupted after it reached browser tests. It also found that both embedded HTTPS Server tests exited before health became available. The exact exit cause has not been established. `dotnet dev-certs https --check` returned no valid certificate **for the current ASP.NET development-certificate profile**; that result says nothing about the owner's existing IIS/production certificates. An attempt to create a development certificate was rejected by automatic approval review because it would change certificate state; no certificate was changed. The two HTTPS tests remain unverified.

## Remaining failures and classification

| Test/component | Observed failure | Likely cause | Classification |
|---|---|---|---|
| `IncomingMonitoringTests.ResumeAvailabilityFollowsLinkedCaseLifecycleNotSourceDisposition` | Direct `SetDispositionAsync(... Duplicate)` rejected | `Duplicate` now requires a dedicated action; old test calls generic disposition | Merge blocker; business behavior must be confirmed |
| `IncomingMonitoringTests.ClassificationsRemainDistinctAndIncomingFiltersArePagedAndTenantSafe` | Same `Duplicate` rejection | Same | Merge blocker; business behavior must be confirmed |
| `ProcurementTests.ManualAndMarketplaceSourcesUseOneIndependentCaseAndCaseIdWorkflow` | Expected Head denial of `TakeToWorkAsync`, but operation succeeds | Fixture Head has `Incoming.Process` under Access V1 | Merge blocker; intended Head permission must be confirmed |
| `ProcurementTests.DirectAndIncomingCreationConvergeOnOneScopedPropertyCaseModel` | Expected Head denial of `CreateManualCaseAsync`, but operation succeeds | Same | Merge blocker; intended Head permission must be confirmed |
| Two embedded HTTPS Server tests | Server exited before health | Undetermined; the certificate check does not identify the cause | Merge blocker until diagnosed and tested |

The earlier AP-03/AP-04/AP-05 and cutover-focused selection passed 13/13 on the R3 code. R4 changed only test fixtures, and no migration was modified. This R4 pass did not independently repeat clean DB migration, upgrade through `20260921225000_AccessV1Cutover`, explicit-setting preservation, non-Owner backfill, Owner exception, or `HasPendingModelChanges()`; those items remain unverified for final AP-06 readiness. No browser tests were completed.

## Verdict

**Merge readiness: NOT READY.** Four behavior-sensitive assertions and two HTTPS tests remain unresolved, and the final full applicable suite and migration checks have not completed.

**Production readiness: NOT READY.** Continue only after the expected Head and Duplicate rules are confirmed, the applicable tests and migrations pass, and the owner completes manual browser smoke.

Manual browser smoke checklist for the owner: inspect Owner and employee Access V1 settings; verify cross-department Forward/Return and unrelated-object denial; verify inspector assignment and purchase confirmation separately.

CI for the resulting SHA must be checked after publication; no CI success is assumed. Next step: resolve the four business expectations with the independent logic audit, diagnose the embedded HTTPS Server exit without changing existing certificates, then rerun the remaining validation.
