# Access V1 AP-06 R5 validation

**Validated code SHA:** `db0fd604ed94fb54854873265d950ed53624701f`  
**Branch:** `codex/access-v1-ap-06-r4-test-alignment`  
**Original AP-06 base:** `888b5fa73927455f714c4171e921d6709e4bc45c`  
**`main` at start and end:** `54fa184e98f41dba3f040cde41aec181153ea054`

Validation was performed on the exact R5 SHA. This report is the only change after it. No production code, migration, certificate, working database, or production database was changed during validation. All PostgreSQL tests used their disposable local databases.

## Commands and results

| Command | Exit | Result |
|---|---:|---|
| `dotnet restore LandErp.slnx --locked-mode` | 0 | Passed. |
| `dotnet build LandErp.slnx -c Release --no-restore` | 0 | Passed; 0 warnings, 0 errors. |
| `dotnet test tests/LandErp.Foundation.Tests -c Release --no-build --filter 'TestCategory!=Browser&TestCategory!=LiveStorage&FullyQualifiedName!~IdentityUiTests&FullyQualifiedName!~PropertyCaseDossierBrowserFlowUsesApprovedTabsOnDesktopAndMobile'` | 1 | 169 passed, 2 failed, 0 skipped, 171 total; 17m 21s. `LANDERP_DOTNET` pointed to the installed `dotnet`. |
| Focused AP-02/AP-03/AP-04/AP-05, cutover, and migration test selection, `--logger 'console;verbosity=normal'` | 0 | 22 passed, 0 failed, 0 skipped; 2m 35s. |

The browser exclusion includes the newly tagged `Browser` tests and two older untagged browser tests: `IdentityUiTests` and `PropertyCaseDossierBrowserFlowUsesApprovedTabsOnDesktopAndMobile`. `LiveStorage` was excluded because it uses an external storage service. No browser test was executed in this validation.

## Access V1 and migration results

| Check | Result |
|---|---|
| `AccessV1Ap02WorkflowTests` | 8/8 passed |
| `AccessV1Ap03UiTests` | 8/8 passed |
| `AccessV1Ap04CutoverTests` | 2/2 passed |
| `AccessV1Ap05HardeningTests` | 2/2 passed |
| Clean disposable PostgreSQL schema to current migration | Passed in the Foundation suite; `RealPostgresMigrationsCommentsRuntimeIsolationAndRecovery` passed separately |
| Upgrade from `20260921183000_EmployeeAccessSettings` through `20260921225000_AccessV1Cutover` to current | `CutoverMigrationBackfillsRoleMatrixPreservesExplicitSettingsAndExcludesOwner` passed |
| Existing explicit Access V1 row | Preserved, including all capability flags and scopes |
| Non-Owner backfill | Verified for ProcurementManager, ProcurementHead, Inspector, and Administrator |
| Owner settings row | Not required; Owner exclusion verified |
| `HasPendingModelChanges()` | `false` in clean/upgrade tests |

## Two remaining failures

Both `CollectorIntegrationTests.HttpsActivationReturnsMachineCredentialOnceAndProblemDetailsCodeAfterUse` and `CollectorIntegrationTests.ControlCollectorHttpsDurableRetryCatalogPresenceDedupLeaseAndRevocation` failed at `WaitLiveAsync`: `Assert.IsFalse(process.HasExited)` observed `true` before `/health/live`. The test helper starts `LandErp.Server` on an HTTPS URL but suppresses its stdout/stderr, so the precise Server exit reason is **not established**. A missing test-process default TLS certificate is a plausible test-harness cause, not a proven diagnosis. The owner's existing certificates were not changed or judged invalid.

These two failures do not exercise Access V1 authorization or migrations. They are recorded as **non-blocking for the Access V1 code assessment**, but they prevent claiming a green full Foundation suite. A successful local HTTPS deployment would establish Server startup on the real configuration; the activation, credential, retry, and revocation paths require their own manual or automated verification before declaring that integration fully validated. No production fix or certificate installation was attempted.

## Verdict

**Merge readiness: CONDITIONAL.** Access V1 regressions, Release build, and migration checks passed on R5. The full applicable suite is red solely because of the two HTTPS Server startup failures. An owner decision is needed on accepting that unrelated test-harness exception, ideally after local HTTPS startup and Collector workflow checks. No merge was performed.

**Production readiness: NOT READY for sign-off.** The owner still needs local production deployment validation and manual browser smoke. Suggested smoke: Owner and employee Access settings; cross-department Forward/Return; denial on unrelated objects; Inspector assignment; purchase confirmation; local HTTPS Server and Collector activation/revocation.

GitHub Actions returned no runs for this branch when checked after R5 publication; CI success is not claimed. This report-only commit has no code changes and its own CI result must be checked after publication.
