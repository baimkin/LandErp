param([ValidateSet('Script','Apply')][string]$Action = 'Script', [string]$DotnetPath = 'dotnet')
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
if ([string]::IsNullOrWhiteSpace($env:LANDERP_MIGRATOR_CONNECTION)) {
    throw 'Configure LANDERP_MIGRATOR_CONNECTION outside Git. Runtime connection is never used for migrations.'
}
if ($Action -eq 'Script') {
    New-Item -ItemType Directory -Force artifacts/stage1 | Out-Null
    & $DotnetPath tool run dotnet-ef migrations script --project src/LandErp.Infrastructure --output artifacts/stage1/migrations.sql
} else {
    & $DotnetPath tool run dotnet-ef database update --project src/LandErp.Infrastructure
}
if ($LASTEXITCODE -ne 0) { throw 'Local migration operation failed' }
