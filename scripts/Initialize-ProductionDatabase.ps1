param([string]$StateRoot = 'C:\ProgramData\LandErp', [string]$DotnetPath = 'dotnet')
$ErrorActionPreference = 'Stop'
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Run this script from an elevated PowerShell.' }
$configFile = Join-Path $StateRoot 'config\production.json'
if (-not (Test-Path -LiteralPath $configFile)) { throw 'Run Initialize-ProductionConfig.ps1 before database initialization.' }
$config = Get-Content -Raw -LiteralPath $configFile | ConvertFrom-Json
$runtimeConnection = [string]$config.Database.ConnectionString
if ([string]::IsNullOrWhiteSpace($runtimeConnection)) { throw 'Production runtime database connection is missing.' }

function Read-SecretText([string]$Prompt) {
    $secure = Read-Host -Prompt $Prompt -AsSecureString
    $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
    try { return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer) }
}

$setProvisioner = [string]::IsNullOrWhiteSpace($env:LANDERP_PROVISIONER_CONNECTION)
$setMigrator = [string]::IsNullOrWhiteSpace($env:LANDERP_MIGRATOR_CONNECTION)
$hadRuntime = Test-Path Env:LANDERP_RUNTIME_CONNECTION
$previousRuntime = $env:LANDERP_RUNTIME_CONNECTION
try {
    if ($setProvisioner) { $env:LANDERP_PROVISIONER_CONNECTION = Read-SecretText 'PostgreSQL provisioner connection' }
    if ($setMigrator) { $env:LANDERP_MIGRATOR_CONNECTION = Read-SecretText 'LandErp migrator connection' }
    $env:LANDERP_RUNTIME_CONNECTION = $runtimeConnection
    & $DotnetPath run --project (Join-Path (Split-Path $PSScriptRoot -Parent) 'src\LandErp.ProductionSetup\LandErp.ProductionSetup.csproj') -c Release -- database
    if ($LASTEXITCODE -ne 0) { throw 'Production database initialization failed.' }
}
finally {
    if ($hadRuntime) { $env:LANDERP_RUNTIME_CONNECTION = $previousRuntime }
    else { Remove-Item Env:LANDERP_RUNTIME_CONNECTION -ErrorAction SilentlyContinue }
    $runtimeConnection = $null
    $previousRuntime = $null
    if ($setProvisioner) { Remove-Item Env:LANDERP_PROVISIONER_CONNECTION -ErrorAction SilentlyContinue }
    if ($setMigrator) { Remove-Item Env:LANDERP_MIGRATOR_CONNECTION -ErrorAction SilentlyContinue }
}
