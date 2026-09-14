param([ValidateSet('Server','Worker')][string]$Service = 'Server', [string]$DotnetPath = 'dotnet')
$ErrorActionPreference = 'Stop'
$stageRoot = Split-Path $PSScriptRoot -Parent
Set-Location $stageRoot
$stageSettingsPath = Join-Path $stageRoot 'local-data/stage1/settings.json'
if (-not (Test-Path -LiteralPath $stageSettingsPath)) { throw 'Run Initialize-Local.ps1 first' }
$stageSettings = Get-Content -Raw -LiteralPath $stageSettingsPath | ConvertFrom-Json
$env:Database__ConnectionString = $stageSettings.RuntimeConnection
$env:DOTNET_ENVIRONMENT = 'Local'
$env:ASPNETCORE_URLS = 'https://localhost:7240'
& $DotnetPath run --project "src/LandErp.$Service" -c Release --no-build
if ($LASTEXITCODE -ne 0) { throw 'Local host stopped with an error; connection values were not printed' }
