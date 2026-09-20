param(
    [Parameter(Mandatory=$true)][string]$Login,
    [Parameter(Mandatory=$true)][string]$OrganizationName,
    [string]$DotnetPath = 'dotnet'
)
$ErrorActionPreference = 'Stop'
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Run this script from an elevated PowerShell.' }

function Read-SecretText([string]$Prompt) {
    $secure = Read-Host -Prompt $Prompt -AsSecureString
    $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
    try { return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer) }
}

$setMigrator = [string]::IsNullOrWhiteSpace($env:LANDERP_MIGRATOR_CONNECTION)
$hadOwnerLogin = Test-Path Env:LANDERP_INITIAL_OWNER_LOGIN
$hadOwnerPassword = Test-Path Env:LANDERP_INITIAL_OWNER_PASSWORD
$hadOrganization = Test-Path Env:LANDERP_INITIAL_ORGANIZATION_NAME
$previousOwnerLogin = $env:LANDERP_INITIAL_OWNER_LOGIN
$previousOwnerPassword = $env:LANDERP_INITIAL_OWNER_PASSWORD
$previousOrganization = $env:LANDERP_INITIAL_ORGANIZATION_NAME
try {
    if ($setMigrator) { $env:LANDERP_MIGRATOR_CONNECTION = Read-SecretText 'LandErp migrator connection' }
    $env:LANDERP_INITIAL_OWNER_LOGIN = $Login
    $env:LANDERP_INITIAL_OWNER_PASSWORD = Read-SecretText 'Initial Owner password'
    $env:LANDERP_INITIAL_ORGANIZATION_NAME = $OrganizationName
    & $DotnetPath run --project (Join-Path (Split-Path $PSScriptRoot -Parent) 'src\LandErp.ProductionSetup\LandErp.ProductionSetup.csproj') -c Release -- owner
    if ($LASTEXITCODE -ne 0) { throw 'Initial production Owner setup failed.' }
}
finally {
    if ($hadOwnerLogin) { $env:LANDERP_INITIAL_OWNER_LOGIN = $previousOwnerLogin } else { Remove-Item Env:LANDERP_INITIAL_OWNER_LOGIN -ErrorAction SilentlyContinue }
    if ($hadOwnerPassword) { $env:LANDERP_INITIAL_OWNER_PASSWORD = $previousOwnerPassword } else { Remove-Item Env:LANDERP_INITIAL_OWNER_PASSWORD -ErrorAction SilentlyContinue }
    if ($hadOrganization) { $env:LANDERP_INITIAL_ORGANIZATION_NAME = $previousOrganization } else { Remove-Item Env:LANDERP_INITIAL_ORGANIZATION_NAME -ErrorAction SilentlyContinue }
    $previousOwnerPassword = $null
    if ($setMigrator) { Remove-Item Env:LANDERP_MIGRATOR_CONNECTION -ErrorAction SilentlyContinue }
}
