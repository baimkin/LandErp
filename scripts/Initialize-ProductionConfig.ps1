param([Parameter(Mandatory=$true)][string]$SourcePath, [string]$StateRoot = 'C:\ProgramData\LandErp', [switch]$Replace)
$ErrorActionPreference = 'Stop'
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Run this script from an elevated PowerShell.' }
$source = [IO.Path]::GetFullPath($SourcePath)
if (-not (Test-Path -LiteralPath $source)) { throw 'Prepared production config was not found.' }
$json = Get-Content -Raw -LiteralPath $source | ConvertFrom-Json
if ([string]::IsNullOrWhiteSpace([string]$json.Database.ConnectionString) -or [string]::IsNullOrWhiteSpace([string]$json.Storage.Provider) -or [string]::IsNullOrWhiteSpace([string]$json.Security.DataProtectionKeysPath) -or [string]::IsNullOrWhiteSpace([string]$json.AllowedHosts)) { throw 'Production config must define Database.ConnectionString, Storage.Provider, Security.DataProtectionKeysPath and AllowedHosts.' }
$raw = Get-Content -Raw -LiteralPath $source
if ($raw -match 'REPLACE_WITH_') { throw 'Replace all placeholders before installing production config.' }
$configDirectory = Join-Path $StateRoot 'config'
$logsDirectory = Join-Path $StateRoot 'logs'
$backupDirectory = Join-Path $StateRoot 'backups'
$binDirectory = Join-Path $StateRoot 'bin'
foreach ($directory in @($StateRoot,$configDirectory,$logsDirectory,$backupDirectory,$binDirectory)) { New-Item -ItemType Directory -Force -Path $directory | Out-Null }
$keyDirectory = [IO.Path]::GetFullPath([string]$json.Security.DataProtectionKeysPath)
New-Item -ItemType Directory -Force -Path $keyDirectory | Out-Null
function Invoke-Icacls([string[]]$Arguments) { & icacls.exe @Arguments | Out-Null; if ($LASTEXITCODE -ne 0) { throw 'Failed to apply required production ACL.' } }
Invoke-Icacls @($StateRoot,'/inheritance:r','/grant:r','*S-1-5-18:(OI)(CI)(F)','*S-1-5-32-544:(OI)(CI)(F)','*S-1-5-19:(OI)(CI)(RX)')
Invoke-Icacls @($configDirectory,'/inheritance:r','/grant:r','*S-1-5-18:(OI)(CI)(F)','*S-1-5-32-544:(OI)(CI)(F)','*S-1-5-19:(OI)(CI)(RX)')
foreach ($directory in @($logsDirectory,$keyDirectory)) { Invoke-Icacls @($directory,'/inheritance:r','/grant:r','*S-1-5-18:(OI)(CI)(F)','*S-1-5-32-544:(OI)(CI)(F)','*S-1-5-19:(OI)(CI)(M)') }
$target = Join-Path $configDirectory 'production.json'
if ((Test-Path -LiteralPath $target) -and -not $Replace) { throw 'Production config already exists. Use -Replace only for an intentional credential/config rotation.' }
Copy-Item -LiteralPath $source -Destination $target -Force
Invoke-Icacls @($target,'/inheritance:r','/grant:r','*S-1-5-18:(F)','*S-1-5-32-544:(F)','*S-1-5-19:(R)')
Write-Output "Production config installed privately at $target"
Write-Output "Persistent Data Protection directory: $keyDirectory"
