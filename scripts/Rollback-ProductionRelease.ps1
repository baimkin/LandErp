param([string]$StateRoot = 'C:\ProgramData\LandErp', [string]$ServerUrl = 'http://127.0.0.1:5080')
$ErrorActionPreference = 'Stop'
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Run this script from an elevated PowerShell.' }
$currentFile = Join-Path $StateRoot 'current-release.json'
$previousFile = Join-Path $StateRoot 'previous-release.json'
if (-not (Test-Path -LiteralPath $currentFile) -or -not (Test-Path -LiteralPath $previousFile)) { throw 'Current/previous release metadata is incomplete.' }
$current = Get-Content -Raw -LiteralPath $currentFile
$previous = Get-Content -Raw -LiteralPath $previousFile
$previousObject = $previous | ConvertFrom-Json
if (-not (Test-Path -LiteralPath ([string]$previousObject.ReleasePath))) { throw 'Previous release directory is missing.' }
Stop-ScheduledTask -TaskName 'LandErp Server' -ErrorAction SilentlyContinue
Stop-ScheduledTask -TaskName 'LandErp Worker' -ErrorAction SilentlyContinue
$previous | Set-Content -LiteralPath $currentFile -Encoding UTF8
$current | Set-Content -LiteralPath $previousFile -Encoding UTF8
Start-ScheduledTask -TaskName 'LandErp Worker'
Start-ScheduledTask -TaskName 'LandErp Server'
$live = $false
for ($attempt=0; $attempt -lt 30; $attempt++) { try { $response = Invoke-WebRequest -UseBasicParsing -Uri ($ServerUrl.TrimEnd('/') + '/health/live') -TimeoutSec 2; if ($response.StatusCode -eq 200) { $live = $true; break } } catch { }; Start-Sleep -Seconds 1 }
if (-not $live) { throw 'Rollback switched metadata, but previous Server did not become live.' }
$ready = Invoke-WebRequest -UseBasicParsing -Uri ($ServerUrl.TrimEnd('/') + '/health/ready') -TimeoutSec 5
if ($ready.StatusCode -ne 200) { throw 'Rollback Server is live but Database readiness failed.' }
Write-Output "Rollback complete. Active release: $($previousObject.Version)"
