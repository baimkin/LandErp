param([Parameter(Mandatory=$true)][string]$ReleasePath, [string]$StateRoot = 'C:\ProgramData\LandErp', [string]$InstallRoot = 'C:\Program Files\LandErp', [string]$ServerUrl = 'http://127.0.0.1:5080')
$ErrorActionPreference = 'Stop'
$listenUri = [Uri]$ServerUrl
if ($listenUri.Scheme -ne 'http' -or $listenUri.Host -notin @('127.0.0.1','localhost','::1')) { throw 'Production backend URL must stay on loopback HTTP behind the HTTPS reverse proxy.' }
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Run this script from an elevated PowerShell.' }
$source = [IO.Path]::GetFullPath($ReleasePath)
$metadataFile = Join-Path $source 'release.json'
if (-not (Test-Path -LiteralPath $metadataFile)) { throw 'release.json is missing. Use Publish-Production.ps1.' }
$metadata = Get-Content -Raw -LiteralPath $metadataFile | ConvertFrom-Json
$version = [string]$metadata.Version
if ($version -notmatch '^[A-Za-z0-9._-]+$') { throw 'Release version is invalid.' }
foreach ($relative in @('Server\LandErp.Server.exe','Worker\LandErp.Worker.exe')) { if (-not (Test-Path -LiteralPath (Join-Path $source $relative))) { throw "Release is missing $relative" } }
$config = Join-Path $StateRoot 'config\production.json'
if (-not (Test-Path -LiteralPath $config)) { throw 'Run Initialize-ProductionConfig.ps1 first.' }
$releaseDirectory = Join-Path $InstallRoot "releases\$version"
if (Test-Path -LiteralPath $releaseDirectory) {
    $installedMetadataFile = Join-Path $releaseDirectory 'release.json'
    if (-not (Test-Path -LiteralPath $installedMetadataFile)) { throw 'Existing release directory has no release.json.' }
    $installedMetadata = Get-Content -Raw -LiteralPath $installedMetadataFile | ConvertFrom-Json
    if ([string]$installedMetadata.Version -ne $version -or [string]$installedMetadata.Commit -ne [string]$metadata.Commit) { throw 'Release version collision: existing immutable release has different metadata.' }
} else {
    New-Item -ItemType Directory -Force -Path $releaseDirectory | Out-Null
    Copy-Item -Path (Join-Path $source '*') -Destination $releaseDirectory -Recurse -Force
}
$runnerDirectory = Join-Path $StateRoot 'bin'
New-Item -ItemType Directory -Force -Path $runnerDirectory | Out-Null
$runner = Join-Path $runnerDirectory 'Run-ProductionProcess.ps1'
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Run-ProductionProcess.ps1') -Destination $runner -Force
$currentFile = Join-Path $StateRoot 'current-release.json'
$previousFile = Join-Path $StateRoot 'previous-release.json'
$previous = $null
if (Test-Path -LiteralPath $currentFile) { $previous = Get-Content -Raw -LiteralPath $currentFile; $previous | Set-Content -LiteralPath $previousFile -Encoding UTF8 }
$newCurrent = [ordered]@{ Version=$version; Commit=[string]$metadata.Commit; ReleasePath=$releaseDirectory; DeployedAtUtc=[DateTimeOffset]::UtcNow.ToString('O') } | ConvertTo-Json
$tempCurrent = "$currentFile.tmp"
$newCurrent | Set-Content -LiteralPath $tempCurrent -Encoding UTF8
function Register-LandErpTask([string]$Name,[string]$Service) {
    $powerShell = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    $q = [char]34
    $arguments = "-NoProfile -ExecutionPolicy Bypass -File $q$runner$q -Service $Service -StateRoot $q$StateRoot$q -ServerUrl $q$ServerUrl$q"
    $action = New-ScheduledTaskAction -Execute $powerShell -Argument $arguments
    $trigger = New-ScheduledTaskTrigger -AtStartup
    $settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -RestartCount 10 -RestartInterval (New-TimeSpan -Seconds 30) -MultipleInstances IgnoreNew -ExecutionTimeLimit ([TimeSpan]::Zero)
    $taskPrincipal = New-ScheduledTaskPrincipal -UserId 'S-1-5-19' -LogonType ServiceAccount -RunLevel Limited
    Register-ScheduledTask -TaskName $Name -Action $action -Trigger $trigger -Settings $settings -Principal $taskPrincipal -Description "Managed LandErp $Service process. Installed by repository deployment script." -Force | Out-Null
}
foreach ($task in @('LandErp Server','LandErp Worker')) { Stop-ScheduledTask -TaskName $task -ErrorAction SilentlyContinue }
Move-Item -LiteralPath $tempCurrent -Destination $currentFile -Force
Register-LandErpTask 'LandErp Server' 'Server'
Register-LandErpTask 'LandErp Worker' 'Worker'
try {
    Start-ScheduledTask -TaskName 'LandErp Worker'
    Start-ScheduledTask -TaskName 'LandErp Server'
    $live = $false
    for ($attempt=0; $attempt -lt 30; $attempt++) { try { $response = Invoke-WebRequest -UseBasicParsing -Uri ($ServerUrl.TrimEnd('/') + '/health/live') -TimeoutSec 2; if ($response.StatusCode -eq 200) { $live = $true; break } } catch { }; Start-Sleep -Seconds 1 }
    if (-not $live) { throw 'Server did not become live after deployment.' }
    $ready = Invoke-WebRequest -UseBasicParsing -Uri ($ServerUrl.TrimEnd('/') + '/health/ready') -TimeoutSec 5
    if ($ready.StatusCode -ne 200) { throw 'Server is live but Database readiness failed.' }
}
catch {
    if ($null -ne $previous) { Stop-ScheduledTask -TaskName 'LandErp Server' -ErrorAction SilentlyContinue; Stop-ScheduledTask -TaskName 'LandErp Worker' -ErrorAction SilentlyContinue; $previous | Set-Content -LiteralPath $currentFile -Encoding UTF8; Start-ScheduledTask -TaskName 'LandErp Worker'; Start-ScheduledTask -TaskName 'LandErp Server'; throw 'Deployment health check failed; previous release metadata was restored and previous tasks restarted.' }
    Stop-ScheduledTask -TaskName 'LandErp Server' -ErrorAction SilentlyContinue
    Stop-ScheduledTask -TaskName 'LandErp Worker' -ErrorAction SilentlyContinue
    throw 'First deployment health check failed; managed tasks were stopped to avoid a restart loop.'
}
Write-Output "LandErp release $version deployed."
Write-Output "Server backend: $ServerUrl (bind this only to loopback; public access must use HTTPS reverse proxy)."
Write-Output "Logs: $(Join-Path $StateRoot 'logs')"
