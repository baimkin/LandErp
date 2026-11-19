# PRODUCTION-LOGGING

19.11.2026. Gate: docs/03-active/GATE-PRODUCTION-LOGGING.md.
Scope approved: day/month logs, 10 MiB per file, retention 7 days; safe diagnostics
and incident search for a future communication-save failure. Local implementation;
production state and existing server logs were not accessed or changed.

## Implementation

- The existing production runner now streams the child stdout/stderr through a
  C# helper loaded by Windows PowerShell 5.1 Add-Type. No package/service/project
  was added. Deploy copies the helper before the runner into StateRoot/bin.
- Same StateRoot/logs, Moscow month/day folders, separate Server/Worker and
  stdout/stderr, unique session, numbered parts, strict 10,485,760-byte limit.
  Normal records stay together; an oversized record spans UTF-8-safe parts.
  No full growing-file copy, process restart, or unbounded pending log queue.
- Midnight closes old streams even when idle; cleanup runs on startup/each hour.
  Exact seven-day last-write threshold; recognized legacy files are included.
  Active/exclusively locked logs, unrelated files/directories, reparse files and
  month/root junctions are preserved/rejected. No directory recursion/deletion.
- Child exit code and simultaneous stdout/stderr drain are preserved. Runner
  stream-write failure is surfaced as a runner failure; managed cleanup stops its
  child and existing scheduler failure/restart behavior applies. OS termination
  retains the existing deployer's explicit child-process stop path.
- Server/Worker JSON console entries have UTC timestamps and scopes. EF SQL
  command logging remains disabled.
- Workspace EventId 1101 retains the old fields and gains allowlisted diagnostics:
  SQLSTATE/code, inner type, validated constraint/table/column identifiers,
  transient flag, elapsed milliseconds and communication CommandId. Existing
  EventId 1102 storage codes are unchanged. Unknown remains Unknown; no writes
  are automatically replayed by logging. Communication pending ID remains stable.
- No provider Message/Detail/Hint/SQL/parameters or exception object is emitted by
  this diagnostic. Identifier validation and bounded inner-chain traversal prevent
  accidental payload output. Logging-provider failure still cannot change the
  confirmed business outcome.
- Find-ProductionIncident.ps1 streams recognized logs in root/month directories,
  bounds matches, includes old files regardless of filename date and emits only
  selected metadata. No Raw/tail/first-six cutoff or raw line dump.

## Files

scripts/Run-ProductionProcess.ps1; ProductionLogRunner.cs;
Deploy-ProductionRelease.ps1; Find-ProductionIncident.ps1;
Test-ProductionLogging.ps1; Server/Program.cs; Worker/Program.cs;
Server/Foundation/WorkspaceFailureDiagnostics.cs; Components/WorkspaceComponent.cs;
Components/Procurement/CaseCommunication.razor; tests/WorkspaceOperationTests.cs;
tests/WorkspaceFailureDiagnosticsTests.cs; PRODUCTION_RUNBOOK; Gate/ACTIVE_TASK;
this report. Existing dirty changes in shared files were preserved.

## Checks

```powershell
dotnet restore tests/LandErp.Foundation.Tests --locked-mode --artifacts-path artifacts/production-logging/build
dotnet build tests/LandErp.Foundation.Tests -c Release --no-restore --artifacts-path artifacts/production-logging/build --disable-build-servers -p:UseSharedCompilation=false
dotnet test tests/LandErp.Foundation.Tests -c Release --no-restore --artifacts-path artifacts/production-logging/build --disable-build-servers -p:UseSharedCompilation=false --filter 'FullyQualifiedName~WorkspaceOperationTests|FullyQualifiedName~WorkspaceFailureDiagnosticsTests' --logger 'trx;LogFileName=production-logging-diagnostics.trx' --results-directory artifacts/production-logging/tests
& "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -NonInteractive -ExecutionPolicy Bypass -File scripts/Test-ProductionLogging.ps1
```

- Locked restore Passed (network access for NuGet audit).
- Release build Passed, 0 warnings/errors. A new test initially triggered CA2201;
  replaced its deliberately unclassified System.Exception with a named synthetic
  exception, then build/test succeeded.
- **26 .NET tests Passed**, 0 Failed/Skipped, 140ms. SQLSTATE 23505/23503/23502/
  42501/40P01/40001/57014, timeout/connection/concurrency/cancellation/unknown,
  metadata suppression, bounded traversal, stable command/new incident, actual
  elapsed time and existing committed/rejected/unknown/logging-failure semantics.
- **35 Windows PowerShell 5.1.19041.6456 checks Passed**, including real compiled
  helper and real child processes. Moscow midnight/month, idle close, size bounds,
  oversized Unicode preservation, actual 10 MiB boundary, seven-day exact boundary,
  expired legacy/month logs, active old log, recent data in old filenames,
  unrelated files, root/month junctions, incident before 25,000 later lines and
  beyond six fresher files, private-field suppression, bounded/no-match search,
  concurrent streams and exit code 17. Both Server and Worker were started through
  copied installed runner/helper pairs with synthetic release/config metadata;
  each produced all 400 expected lines and preserved exit code.
- Final helper fixture/results:
  artifacts/production-logging/helper-tests/5dbd06f7d0464a0c91242794db9fa92d/results.json.
  .NET TRX: artifacts/production-logging/tests/production-logging-diagnostics.trx.

## Limits / activation

Local preview and previous Incoming/Parsing/Parser work were preserved. No packages,
schema, Git mutation, commit/push, production deploy or real log deletion.
The prior communication incident's root cause is still unverified. New metadata
helps distinguish failure causes after deployment; it cannot recover metadata
that old logs never recorded, or guarantee every unexpected code defect's cause.
Legacy files with recent data remain until retention expires; 10 MiB limits new
parts, not the total last-week volume. Cleanup may defer active/inaccessible files.
Runner behavior under actual production account/policy and live acceptance remain
for the server update; local tests do not claim production acceptance.

Next: separately authorized publication/deploy of the agreed package. After that,
capture a new incident ID and use Find-ProductionIncident.ps1; verify the reported
cause before changing communication business logic.
