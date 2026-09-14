param([string]$DotnetPath = 'dotnet', [switch]$NoBuild)
$ErrorActionPreference = 'Stop'
$stageRoot = Split-Path $PSScriptRoot -Parent
Set-Location $stageRoot
if ([string]::IsNullOrWhiteSpace($env:LANDERP_TEST_ADMIN_CONNECTION)) {
    $env:LANDERP_TEST_ADMIN_CONNECTION = [Environment]::GetEnvironmentVariable('LANDERP_TEST_ADMIN_CONNECTION', 'User')
}
if ([string]::IsNullOrWhiteSpace($env:LANDERP_TEST_ADMIN_CONNECTION)) {
    throw 'Configure LANDERP_TEST_ADMIN_CONNECTION in process/user environment; do not pass credentials as arguments.'
}
$env:LANDERP_DOTNET = (Get-Command $DotnetPath).Source
if (-not $NoBuild) {
    & $DotnetPath restore LandErp.slnx --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'Locked restore failed' }
    & $DotnetPath build LandErp.slnx -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Release build failed' }
}
& $DotnetPath test tests/LandErp.Foundation.Tests -c Release --no-build
if ($LASTEXITCODE -ne 0) { throw 'Foundation tests failed' }
