param([string]$StateRoot = 'C:\ProgramData\LandErp', [string]$ServerUrl = 'http://127.0.0.1:5080', [string]$InstallRoot = 'C:\Program Files\LandErp')
$ErrorActionPreference = 'Stop'
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Run this script from an elevated PowerShell.' }
$currentFile = Join-Path $StateRoot 'current-release.json'
$previousFile = Join-Path $StateRoot 'previous-release.json'
$config = Join-Path $StateRoot 'config\production.json'
if (-not (Test-Path -LiteralPath $currentFile) -or -not (Test-Path -LiteralPath $previousFile)) { throw 'Current/previous release metadata is incomplete.' }
if (-not (Test-Path -LiteralPath $config)) { throw 'Production config is missing.' }
$productionConfig = Get-Content -Raw -LiteralPath $config | ConvertFrom-Json
$allowedHosts = @(([string]$productionConfig.AllowedHosts).Split(';') | ForEach-Object { $_.Trim() } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
$healthHeaders = @{}
if ($allowedHosts -notcontains '*') {
    $healthHost = $allowedHosts | Where-Object { $_ -notmatch '\*' } | Select-Object -First 1
    if ([string]::IsNullOrWhiteSpace([string]$healthHost)) { throw 'AllowedHosts must include a concrete hostname for the local rollback health check.' }
    $healthHeaders['Host'] = [string]$healthHost
}
$current = Get-Content -Raw -LiteralPath $currentFile
$previous = Get-Content -Raw -LiteralPath $previousFile
$previousObject = $previous | ConvertFrom-Json
if (-not (Test-Path -LiteralPath ([string]$previousObject.ReleasePath))) { throw 'Previous release directory is missing.' }
function Stop-InstalledLandErpProcesses {
    $releasesRoot = [IO.Path]::GetFullPath((Join-Path $InstallRoot 'releases')).TrimEnd('\') + '\'
    foreach ($process in @(Get-CimInstance Win32_Process -Filter "Name = 'LandErp.Server.exe' OR Name = 'LandErp.Worker.exe'")) {
        $path = [string]$process.ExecutablePath
        if ($path.StartsWith($releasesRoot, [StringComparison]::OrdinalIgnoreCase)) {
            Stop-Process -Id $process.ProcessId -Force -ErrorAction SilentlyContinue
        }
    }
}
Stop-ScheduledTask -TaskName 'LandErp Server' -ErrorAction SilentlyContinue
Stop-ScheduledTask -TaskName 'LandErp Worker' -ErrorAction SilentlyContinue
Stop-InstalledLandErpProcesses
$previous | Set-Content -LiteralPath $currentFile -Encoding UTF8
$current | Set-Content -LiteralPath $previousFile -Encoding UTF8
Start-ScheduledTask -TaskName 'LandErp Worker'
Start-ScheduledTask -TaskName 'LandErp Server'
$live = $false
for ($attempt=0; $attempt -lt 30; $attempt++) { try { $response = Invoke-WebRequest -UseBasicParsing -Uri ($ServerUrl.TrimEnd('/') + '/health/live') -Headers $healthHeaders -TimeoutSec 2; if ($response.StatusCode -eq 200) { $live = $true; break } } catch { }; Start-Sleep -Seconds 1 }
if (-not $live) { throw 'Rollback switched metadata, but previous Server did not become live.' }
$listenUri = [Uri]$ServerUrl
$addresses = if ($listenUri.Host -eq 'localhost') { @('127.0.0.1','::1') } else { @($listenUri.Host) }
$listeners = @(Get-NetTCPConnection -LocalPort $listenUri.Port -State Listen -ErrorAction SilentlyContinue | Where-Object { $_.LocalAddress -in $addresses })
$expected = [IO.Path]::GetFullPath((Join-Path ([string]$previousObject.ReleasePath) 'Server\LandErp.Server.exe'))
$expectedIsListening = $false
foreach ($listener in $listeners) {
    $process = Get-CimInstance Win32_Process -Filter "ProcessId = $($listener.OwningProcess)"
    if ($process -and [string]$process.ExecutablePath -ieq $expected) { $expectedIsListening = $true; break }
}
if (-not $expectedIsListening) { throw 'Rollback health check reached a Server other than the previous release.' }
$ready = Invoke-WebRequest -UseBasicParsing -Uri ($ServerUrl.TrimEnd('/') + '/health/ready') -Headers $healthHeaders -TimeoutSec 5
if ($ready.StatusCode -ne 200) { throw 'Rollback Server is live but Database readiness failed.' }
Write-Output "Rollback complete. Active release: $($previousObject.Version)"
