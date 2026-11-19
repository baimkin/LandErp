param([string]$OutputRoot = '')
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
if (-not $OutputRoot) { $OutputRoot = Join-Path $repo 'artifacts\production-logging\helper-tests' }
$fixture = Join-Path ([IO.Path]::GetFullPath($OutputRoot)) ([Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture -Force | Out-Null
Add-Type -Path (Join-Path $PSScriptRoot 'ProductionLogRunner.cs')
$checks = New-Object 'System.Collections.Generic.List[string]'
function Check([bool]$Condition, [string]$Name) {
    if (-not $Condition) { throw "Production logging check failed: $Name" }
    $checks.Add($Name)
}
foreach ($name in @('Run-ProductionProcess.ps1','Deploy-ProductionRelease.ps1','Find-ProductionIncident.ps1','Test-ProductionLogging.ps1')) {
    $tokens=$null; $parseErrors=$null
    $null=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot $name),[ref]$tokens,[ref]$parseErrors)
    Check ($parseErrors.Count -eq 0) "PowerShell 5.1 syntax: $name"
}
$logs = Join-Path $fixture 'rotation'
$before = [DateTimeOffset]::Parse('2026-11-30T20:59:59Z')
$after = $before.AddSeconds(1)
$writer = [LandErp.Production.DailyLog]::new($logs,'server','out','aabbccdd',1024)
try {
    $writer.WriteLine('before-midnight', $before); $first = $writer.CurrentPath
    Check ($first.Contains('2026-11\server-2026-11-30-')) 'Moscow date before midnight'
    $writer.AdvanceDate($after)
    Check ($null -eq $writer.CurrentPath) 'Idle midnight closes the old file'
    $probe=[IO.File]::Open($first,[IO.FileMode]::Open,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None); $probe.Dispose()
    $writer.WriteLine('after-midnight',$after)
    Check ($writer.CurrentPath.Contains('2026-12\server-2026-12-01-')) 'Moscow day and month switch without restart'
    $writer.WriteLine(('a' * 900),$after); $writer.WriteLine(('b' * 900),$after)
} finally { $writer.Dispose() }
$parts=@(Get-ChildItem -LiteralPath (Join-Path $logs '2026-12') -File)
Check ($parts.Count -ge 2 -and @($parts | Where-Object Length -gt 1024).Count -eq 0) 'Size rotation respects limit'
$unicode=([string][char]0x0416)+[char]::ConvertFromUtf32(0x1F3E0)
$longLine=$unicode * 1000
$writer=[LandErp.Production.DailyLog]::new($logs,'worker','err','11223344',1024)
try { $writer.WriteLine($longLine,$after) } finally { $writer.Dispose() }
$utf8=[Text.UTF8Encoding]::new($false,$true)
$bytesText=''
$parts=@(Get-ChildItem -LiteralPath (Join-Path $logs '2026-12') -File -Filter 'worker-*.log' | Sort-Object Name)
foreach ($part in $parts) { Check ($part.Length -le 1024) 'Oversized UTF-8 part stays bounded'; $bytesText += [IO.File]::ReadAllText($part.FullName,$utf8) }
Check ($bytesText -ceq ($longLine+[Environment]::NewLine)) 'Oversized UTF-8 record is preserved without broken characters'
$tenMiB=10*1024*1024
$writer=[LandErp.Production.DailyLog]::new($logs,'worker','out','55667788',$tenMiB)
try { $writer.WriteLine(('z' * ($tenMiB+512)),$after) } finally { $writer.Dispose() }
$parts=@(Get-ChildItem -LiteralPath (Join-Path $logs '2026-12') -File -Filter 'worker-*-55667788.*')
Check ($parts.Count -eq 2 -and @($parts | Where-Object Length -gt $tenMiB).Count -eq 0) 'Actual 10 MiB boundary'
$retention=Join-Path $fixture 'retention'
New-Item -ItemType Directory -Path $retention -Force | Out-Null
$now=[DateTimeOffset]::UtcNow
function FixtureFile([string]$Path,[int]$Days) {
    New-Item -ItemType Directory -Path (Split-Path $Path -Parent) -Force | Out-Null
    [IO.File]::WriteAllText($Path,'synthetic', [Text.UTF8Encoding]::new($false))
    [IO.File]::SetLastWriteTimeUtc($Path,$now.UtcDateTime.AddDays(-$Days))
    return $Path
}
$expired=FixtureFile (Join-Path $retention 'server-20260920-120000-aabbccdd.out.log') 8
$exact=FixtureFile (Join-Path $retention 'server-20260920-120000-11223344.out.log') 7
$recentLegacy=FixtureFile (Join-Path $retention 'server-20260920-120000-55667788.out.log') 1
$active=FixtureFile (Join-Path $retention 'worker-20260920-120000-aabbccdd.err.log') 8
$rolled=FixtureFile (Join-Path $retention '2026-10\worker-2026-10-01-aabbccdd.001.out.log') 8
$unrelated=FixtureFile (Join-Path $retention 'notes.log') 30
$nested=FixtureFile (Join-Path $retention 'other\server-20260920-120000-aabbccdd.out.log') 30
$handle=[IO.File]::Open($active,[IO.FileMode]::Open,[IO.FileAccess]::Write,[IO.FileShare]::Read)
try { $removed=[LandErp.Production.DailyLog]::Cleanup($retention,$now) } finally { $handle.Dispose() }
Check ($removed -eq 2 -and -not (Test-Path -LiteralPath $expired) -and -not (Test-Path -LiteralPath $rolled)) 'Expired closed legacy and monthly logs are removed'
Check ((Test-Path -LiteralPath $active)) 'Active old log is preserved'
Check ((Test-Path -LiteralPath $exact) -and (Test-Path -LiteralPath $recentLegacy)) 'Seven-day boundary and recent writes in old filenames are preserved'
Check ((Test-Path -LiteralPath $unrelated) -and (Test-Path -LiteralPath $nested)) 'Unrelated files and directories are untouched'
$outside=Join-Path $fixture 'protected-neighbor'
$protected=FixtureFile (Join-Path $outside 'server-2026-09-01-aabbccdd.001.out.log') 30
New-Item -ItemType Junction -Path (Join-Path $retention '2026-09') -Target $outside | Out-Null
$null=[LandErp.Production.DailyLog]::Cleanup($retention,$now)
Check ((Test-Path -LiteralPath $protected)) 'Month junction is not traversed'
$link=Join-Path $fixture 'root-junction'
New-Item -ItemType Junction -Path $link -Target $outside | Out-Null
$refused=$false
try { $null=[LandErp.Production.DailyLog]::Cleanup($link,$now) } catch { $refused=$true }
Check ($refused -and (Test-Path -LiteralPath $protected)) 'Root junction is rejected'
$state=Join-Path $fixture 'search-state'
$searchLogs=Join-Path $state 'logs'
New-Item -ItemType Directory -Path (Join-Path $searchLogs '2026-11') -Force | Out-Null
$incident=[Guid]::NewGuid()
$record=@{Timestamp='2026-11-19T12:00:00.000Z';EventId=1101;LogLevel='Warning';Message='SYNTHETIC_PRIVATE_TEXT';Exception='SYNTHETIC_PRIVATE_TEXT';State=@{OperationId=$incident.ToString();Phase='Command';Component='CaseCommunication';Action='Persist';FailureType='DbUpdateException';DiagnosticCode='PG_23505';SqlState='23505';Constraint='test_constraint';CommandId=[Guid]::NewGuid().ToString();ElapsedMs=42}} | ConvertTo-Json -Depth 4 -Compress
$legacy=Join-Path $searchLogs 'server-20260920-120000-aabbccdd.out.log'
$stream=[IO.StreamWriter]::new($legacy,$false,[Text.UTF8Encoding]::new($false))
try { $stream.WriteLine($record); for ($i=0;$i -lt 25000;$i++) { $stream.WriteLine('{"EventId":0}') } } finally { $stream.Dispose() }
[IO.File]::SetLastWriteTimeUtc($legacy,$now.UtcDateTime.AddHours(-2))
[IO.File]::WriteAllText((Join-Path $searchLogs '2026-11\server-2026-11-19-11223344.001.out.log'),$record,[Text.UTF8Encoding]::new($false))
for ($i=0;$i -lt 7;$i++) { [IO.File]::WriteAllText((Join-Path $searchLogs ("server-20261119-12000$i-1234abcd.out.log")),'no incident') }
$hits=@(& (Join-Path $PSScriptRoot 'Find-ProductionIncident.ps1') -Incident $incident -StateRoot $state -Quiet)
Check ($hits.Count -eq 2 -and @($hits | Where-Object File -eq $legacy).Count -eq 1) 'Incident search includes old files beyond first six and before last 20000 lines'
Check ($hits[0].SqlState -eq '23505' -and $hits[0].ElapsedMs -eq 42) 'Incident search returns structured cause'
Check (-not (($hits | ConvertTo-Json -Depth 4).Contains('SYNTHETIC_PRIVATE_TEXT'))) 'Incident search never dumps raw exception or message'
$hits=@(& (Join-Path $PSScriptRoot 'Find-ProductionIncident.ps1') -Incident $incident -StateRoot $state -MaxResults 1 -Quiet)
Check ($hits.Count -eq 1) 'Incident search stops at requested bound'
$missing=@(& (Join-Path $PSScriptRoot 'Find-ProductionIncident.ps1') -Incident ([Guid]::NewGuid()) -StateRoot $state -Quiet)
Check ($missing.Count -eq 0) 'Missing incident has no false matches'
$child=Join-Path $fixture 'LogProbe.exe'
$code=@'
using System;
using System.Text;
using System.Threading.Tasks;
public static class LogProbe {
    public static int Main() {
        Console.OutputEncoding = new UTF8Encoding(false);
        Task a=Task.Run(()=>{for(int i=0;i<200;i++) Console.WriteLine("OUT-"+i);});
        Task b=Task.Run(()=>{for(int i=0;i<200;i++) Console.Error.WriteLine("ERR-"+i);});
        Task.WaitAll(a,b); return 17;
    }
}
'@
Add-Type -TypeDefinition $code -OutputAssembly $child -OutputType ConsoleApplication
$processLogs=Join-Path $fixture 'process-logs'
$exit=[LandErp.Production.LogProcess]::Run($child,$fixture,$processLogs,'server')
Check ($exit -eq 17) 'Child exit code is preserved'
$outFiles=@(Get-ChildItem -LiteralPath $processLogs -Recurse -File -Filter '*.out.log')
$errFiles=@(Get-ChildItem -LiteralPath $processLogs -Recurse -File -Filter '*.err.log')
Check (@(Get-Content -LiteralPath $outFiles.FullName).Count -eq 200) 'All concurrent stdout lines drain before exit'
Check (@(Get-Content -LiteralPath $errFiles.FullName).Count -eq 200) 'All concurrent stderr lines drain before exit'
Check ($outFiles[0].FullName -ne $errFiles[0].FullName) 'Stdout and stderr cannot overwrite each other'
$release=Join-Path $fixture 'release'
$installedRunner=Join-Path $fixture 'bin'
New-Item -ItemType Directory -Path $installedRunner -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'ProductionLogRunner.cs') -Destination $installedRunner
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Run-ProductionProcess.ps1') -Destination $installedRunner
foreach ($service in @('Server','Worker')) {
    $serviceDirectory=Join-Path $release $service
    New-Item -ItemType Directory -Path $serviceDirectory -Force | Out-Null
    Copy-Item -LiteralPath $child -Destination (Join-Path $serviceDirectory ("LandErp.$service.exe"))
}
$launcherState=Join-Path $fixture 'launcher-state'
New-Item -ItemType Directory -Path (Join-Path $launcherState 'config') -Force | Out-Null
@{ReleasePath=$release} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $launcherState 'current-release.json') -Encoding UTF8
'{}' | Set-Content -LiteralPath (Join-Path $launcherState 'config\production.json') -Encoding UTF8
foreach ($service in @('Server','Worker')) {
    $start=[Diagnostics.ProcessStartInfo]::new((Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'))
    $start.UseShellExecute=$false; $start.CreateNoWindow=$true; $start.RedirectStandardOutput=$true; $start.RedirectStandardError=$true
    $start.Arguments='-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "'+(Join-Path $installedRunner 'Run-ProductionProcess.ps1')+'" -Service '+$service+' -StateRoot "'+$launcherState+'"'
    $proc=[Diagnostics.Process]::Start($start)
    $stdoutTask=$proc.StandardOutput.ReadToEndAsync(); $stderrTask=$proc.StandardError.ReadToEndAsync()
    try {
        if (-not $proc.WaitForExit(15000)) { $proc.Kill(); throw 'Launcher test timed out.' }
        $null=$stdoutTask.GetAwaiter().GetResult(); $null=$stderrTask.GetAwaiter().GetResult()
        Check ($proc.ExitCode -eq 17) "Installed $service launcher preserves child exit code"
    } finally { $proc.Dispose() }
    $prefix=$service.ToLowerInvariant()
    $logsForService=@(Get-ChildItem -LiteralPath (Join-Path $launcherState 'logs') -Recurse -File -Filter "$prefix-*.log")
    Check ($logsForService.Count -eq 2 -and @(Get-Content -LiteralPath $logsForService.FullName).Count -eq 400) "Installed $service launcher records both complete streams"
}
[ordered]@{Passed=$checks.Count;Failed=0;PowerShell=$PSVersionTable.PSVersion.ToString();Fixture=$fixture;Checks=$checks.ToArray()} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $fixture 'results.json') -Encoding UTF8
Write-Output "Production logging: $($checks.Count) checks passed; PowerShell $($PSVersionTable.PSVersion). Results: $fixture"
