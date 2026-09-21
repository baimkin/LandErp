param(
    [Parameter(Mandatory=$true)][string]$SiteName,
    [Parameter(Mandatory=$true)][string]$HostName,
    [Parameter(Mandatory=$true)][string]$LineagePath,
    [string]$OpenSslPath = 'openssl.exe',
    [string]$StateRoot = 'C:\ProgramData\LandErp'
)
$ErrorActionPreference = 'Stop'

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run this script from an elevated PowerShell.'
}
if (-not (Test-Path -LiteralPath (Join-Path $LineagePath 'fullchain.pem'))
    -or -not (Test-Path -LiteralPath (Join-Path $LineagePath 'privkey.pem'))) {
    throw 'The supplied Certbot lineage does not contain fullchain.pem and privkey.pem.'
}
$openssl = Get-Command $OpenSslPath -ErrorAction Stop
Import-Module WebAdministration -ErrorAction Stop
$bindings = @(Get-WebBinding -Name $SiteName -Protocol 'https' | Where-Object {
    ([string]$_.bindingInformation) -match (':' + [regex]::Escape($HostName) + '
$configDirectory = Join-Path $StateRoot 'config'
$binDirectory = Join-Path $StateRoot 'bin'
foreach ($directory in @($StateRoot,$configDirectory,$binDirectory)) {
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
}
$targetScript = Join-Path $binDirectory 'Sync-IisCertificate.ps1'
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Sync-IisCertificate.ps1') -Destination $targetScript -Force
$configPath = Join-Path $configDirectory 'https-renewal.json'
[ordered]@{
    SiteName = $SiteName
    HostName = $HostName
    LineagePath = [IO.Path]::GetFullPath($LineagePath)
    OpenSslPath = $openssl.Source
} | ConvertTo-Json | Set-Content -LiteralPath $configPath -Encoding UTF8

function Invoke-Icacls([string[]]$Arguments) {
    & icacls.exe @Arguments | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Failed to restrict HTTPS renewal configuration ACL.' }
}
Invoke-Icacls @($configPath,'/inheritance:r','/grant:r','*S-1-5-18:(F)','*S-1-5-32-544:(F)')
Invoke-Icacls @($targetScript,'/inheritance:r','/grant:r','*S-1-5-18:(RX)','*S-1-5-32-544:(F)')

$powerShell = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
$q = [char]34
$arguments = "-NoProfile -ExecutionPolicy Bypass -File $q$targetScript$q -StateRoot $q$StateRoot$q"
$action = New-ScheduledTaskAction -Execute $powerShell -Argument $arguments
$trigger = New-ScheduledTaskTrigger -Daily -At '03:30'
$settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -RestartCount 3 -RestartInterval (New-TimeSpan -Minutes 5) -MultipleInstances IgnoreNew -ExecutionTimeLimit (New-TimeSpan -Minutes 10)
$taskPrincipal = New-ScheduledTaskPrincipal -UserId 'S-1-5-18' -LogonType ServiceAccount -RunLevel Highest
Register-ScheduledTask -TaskName 'LandErp IIS Certificate' -Action $action -Trigger $trigger -Settings $settings -Principal $taskPrincipal -Description 'Synchronizes the renewed Certbot certificate to the exact LandErp IIS HTTPS binding.' -Force -ErrorAction Stop | Out-Null

& $targetScript -StateRoot $StateRoot
if ($LASTEXITCODE -ne 0) { throw 'Initial IIS certificate synchronization failed.' }

$hook = Join-Path $binDirectory 'Certbot-DeployHook.cmd'
$hookContent = '@echo off' + [Environment]::NewLine +
    '"' + $powerShell + '" -NoProfile -ExecutionPolicy Bypass -File "' + $targetScript +
    '" -StateRoot "' + $StateRoot + '"' + [Environment]::NewLine
$hookContent | Set-Content -LiteralPath $hook -Encoding ASCII
Invoke-Icacls @($hook,'/inheritance:r','/grant:r','*S-1-5-18:(RX)','*S-1-5-32-544:(F)')

Write-Output "Installed daily fallback task: LandErp IIS Certificate."
Write-Output "Certbot deploy hook command: $hook"
Write-Output 'Configure the existing Certbot renewal to call this deploy hook after a successful renewal.'
)
})
if ($bindings.Count -ne 1) {
    throw "Expected exactly one HTTPS binding for site '$SiteName' and host '$HostName'; found $($bindings.Count)."
}

$configDirectory = Join-Path $StateRoot 'config'
$binDirectory = Join-Path $StateRoot 'bin'
foreach ($directory in @($StateRoot,$configDirectory,$binDirectory)) {
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
}
$targetScript = Join-Path $binDirectory 'Sync-IisCertificate.ps1'
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Sync-IisCertificate.ps1') -Destination $targetScript -Force
$configPath = Join-Path $configDirectory 'https-renewal.json'
[ordered]@{
    SiteName = $SiteName
    HostName = $HostName
    LineagePath = [IO.Path]::GetFullPath($LineagePath)
    OpenSslPath = $openssl.Source
} | ConvertTo-Json | Set-Content -LiteralPath $configPath -Encoding UTF8

function Invoke-Icacls([string[]]$Arguments) {
    & icacls.exe @Arguments | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Failed to restrict HTTPS renewal configuration ACL.' }
}
Invoke-Icacls @($configPath,'/inheritance:r','/grant:r','*S-1-5-18:(F)','*S-1-5-32-544:(F)')
Invoke-Icacls @($targetScript,'/inheritance:r','/grant:r','*S-1-5-18:(RX)','*S-1-5-32-544:(F)')

$powerShell = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
$q = [char]34
$arguments = "-NoProfile -ExecutionPolicy Bypass -File $q$targetScript$q -StateRoot $q$StateRoot$q"
$action = New-ScheduledTaskAction -Execute $powerShell -Argument $arguments
$trigger = New-ScheduledTaskTrigger -Daily -At '03:30'
$settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -RestartCount 3 -RestartInterval (New-TimeSpan -Minutes 5) -MultipleInstances IgnoreNew -ExecutionTimeLimit (New-TimeSpan -Minutes 10)
$taskPrincipal = New-ScheduledTaskPrincipal -UserId 'S-1-5-18' -LogonType ServiceAccount -RunLevel Highest
Register-ScheduledTask -TaskName 'LandErp IIS Certificate' -Action $action -Trigger $trigger -Settings $settings -Principal $taskPrincipal -Description 'Synchronizes the renewed Certbot certificate to the exact LandErp IIS HTTPS binding.' -Force -ErrorAction Stop | Out-Null

& $targetScript -StateRoot $StateRoot
if ($LASTEXITCODE -ne 0) { throw 'Initial IIS certificate synchronization failed.' }

$hook = Join-Path $binDirectory 'Certbot-DeployHook.cmd'
$hookContent = '@echo off' + [Environment]::NewLine +
    '"' + $powerShell + '" -NoProfile -ExecutionPolicy Bypass -File "' + $targetScript +
    '" -StateRoot "' + $StateRoot + '"' + [Environment]::NewLine
$hookContent | Set-Content -LiteralPath $hook -Encoding ASCII
Invoke-Icacls @($hook,'/inheritance:r','/grant:r','*S-1-5-18:(RX)','*S-1-5-32-544:(F)')

Write-Output "Installed daily fallback task: LandErp IIS Certificate."
Write-Output "Certbot deploy hook command: $hook"
Write-Output 'Configure the existing Certbot renewal to call this deploy hook after a successful renewal.'
