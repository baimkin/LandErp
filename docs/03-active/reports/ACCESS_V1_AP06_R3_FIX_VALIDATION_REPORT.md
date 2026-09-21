# Access V1 AP-06 R3 — small build fixes and validation

**Status: NOT READY**

| Item | Value |
|---|---|
| Repository | `baimkin/LandErp` |
| Branch | `codex/access-v1-ap-06-r3-fix-validation` |
| Base | `888b5fa73927455f714c4171e921d6709e4bc45c` |
| Result commit | sole fix/report commit at branch HEAD |
| `main` at start/end | `54fa184e98f41dba3f040cde41aec181153ea054` |

## Changes

- `OrganizationPage.razor`: give both nested `AuthorizeView` child-content blocks distinct context names, resolving `RZ9999`; no access rule changed.
- `EmployeeAccessSettingsTests.cs`: rename the first local DbContext to avoid `CS0136`.
- `YandexDiskAttachmentTests.cs`: use the existing `ProcurementWorkspace(factory, time, storage)` constructor, which supplies the current Access V1 service, instead of passing the obsolete `IAccessControl` dependency; resolves `CS1503`.

No migration, package, production database, or other business logic was changed.

## Commands and results

| Command | Exit code | Result |
|---|---:|---|
| `dotnet restore LandErp.slnx --locked-mode` | 0 | Passed. |
| `dotnet build LandErp.slnx -c Release --no-restore` after Razor edit | 1 | Two test compilation errors: `CS0136` and `CS1503`. |
| `dotnet build LandErp.slnx -c Release --no-restore` after test edits | 0 | Passed, 0 warnings, 0 errors. |
| `dotnet test tests/LandErp.Foundation.Tests -c Release --no-build` | 1 (interrupted) | Started on real PostgreSQL; six failures were reported, the last during interruption. No final test totals were produced; passed/skipped counts are unknown. |

Observed failures before interruption:

1. `AccessV1Ap02WorkflowTests.ReadLevelsAllowReadingButRejectMutations`: `Assert.IsTrue(incoming.Items.Any())` at line 75. The test takes its only listing into work before reading with default `IncomingCatalogFilter`, whose default disposition is `Incoming`; `TakeToWork` sets `InWork`. The assertion/test scenario needs review. The underlying read and work authorization semantics were not changed.
2. `CollectionSchedulingTests.ManagementReadUsesExactAggregatesStructuredLastRunAndSeparateReadPermission` and `CollectionManagementDoesNotBroadenProcurementScope`: `AccessDeniedException` from `EmployeeAccessService.ResolveAsync`. These tests use newly generated, non-employee `Subject` IDs; after Access V1 cutover such subjects fail closed. Test identities/expected permissions need review, not a production authorization bypass.
3. `CollectorIntegrationTests.HttpsActivationReturnsMachineCredentialOnceAndProblemDetailsCodeAfterUse` and `ControlCollectorHttpsDurableRetryCatalogPresenceDedupLeaseAndRevocation`: host launch failed because the direct test command had no `LANDERP_DOTNET`; it looked for a nonexistent worktree `artifacts/stage1/dotnet/dotnet.exe`. The repository's `Test-Foundation.ps1` sets this environment variable. This is a test invocation issue, not an observed product failure.
4. `IdentityOrganizationTests.InvitationsPermissionsScopesConcurrencyAndLastOwner` also reported an audit-title assertion failure while interruption was processed; the expected `Изменены назначение и доступ сотрудника` event was absent. Cause not established; audit behavior needs review.

These failures are **merge and production blockers** because the complete suite has not passed and authorization/audit behavior remains unverified. The Access V1 AP-03, AP-04 and AP-05 groups were not separately completed. Clean DB, cutover upgrade, explicit settings/backfill, migration history and `HasPendingModelChanges()` were not separately validated. No browser tests were run.

## Verdict

**Merge readiness: NOT READY.** Build is fixed, but full Foundation/PostgreSQL validation is incomplete and multiple tests failed.

**Production readiness: NOT READY.** Do not deploy this branch until test scenarios are reconciled with Access V1, the full suite and migration paths pass, and the owner performs browser smoke.

Next focused step: decide expected behavior for the incoming-list test and update stale test subjects to real configured employees only if that matches the intended access policy. Investigate the audit assertion. Then run the standard Foundation test setup with `LANDERP_DOTNET` set, followed by the specific migration validation. Do not weaken fail-closed authorization to make the old tests pass.

Manual browser smoke after automated validation: inspect Access V1 settings as Owner and employee; verify cross-department handover/Forward/Return and unrelated-object denial; verify Inspector and purchase permissions independently.

CI for the resulting SHA cannot run before publication; check it after push. No CI result is presumed successful.
