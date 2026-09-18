param([string]$DotnetPath = 'dotnet', [string]$Version = '')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root
if ([string]::IsNullOrWhiteSpace($Version)) { $Version = [DateTimeOffset]::UtcNow.ToString('yyyyMMdd-HHmmss') }
if ($Version -notmatch '^[A-Za-z0-9._-]+$') { throw 'Version may contain only letters, digits, dot, underscore and dash.' }
$releaseRoot = Join-Path $root "artifacts\production\releases\$Version"
if (Test-Path -LiteralPath $releaseRoot) { throw "Release $Version already exists." }
New-Item -ItemType Directory -Force -Path $releaseRoot | Out-Null
& $DotnetPath restore LandErp.slnx --locked-mode
if ($LASTEXITCODE -ne 0) { throw 'Locked restore failed.' }
& $DotnetPath publish src/LandErp.Server/LandErp.Server.csproj -c Release --no-restore -o (Join-Path $releaseRoot 'Server')
if ($LASTEXITCODE -ne 0) { throw 'Server publish failed.' }
& $DotnetPath publish src/LandErp.Worker/LandErp.Worker.csproj -c Release --no-restore -o (Join-Path $releaseRoot 'Worker')
if ($LASTEXITCODE -ne 0) { throw 'Worker publish failed.' }
$commit = 'unknown'
try { $commit = (& git rev-parse HEAD 2>$null).Trim() } catch { }
[ordered]@{ Version=$Version; Commit=$commit; CreatedAtUtc=[DateTimeOffset]::UtcNow.ToString('O') } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $releaseRoot 'release.json') -Encoding UTF8
Write-Output "Production release prepared: $releaseRoot"
Write-Output 'No tests were run by this script.'
