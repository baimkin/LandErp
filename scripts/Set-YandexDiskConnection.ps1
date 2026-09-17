param()
$ErrorActionPreference = 'Stop'
if ($env:OS -ne 'Windows_NT') { throw 'This local setup uses Windows DPAPI. On production use the server secret store.' }
$storageDirectory = Join-Path (Split-Path $PSScriptRoot -Parent) 'local-data/yandex-disk'
$storageSettingsPath = Join-Path $storageDirectory 'settings.json'
$storageTokenPath = Join-Path $storageDirectory 'token.dpapi'
# Input is never passed as a command argument or written to the console/history.
$storageSecret = Read-Host 'Paste the Yandex Disk OAuth token (input is hidden)' -AsSecureString
try {
    if ($storageSecret.Length -lt 20) { throw 'Token is empty or too short.' }
    New-Item -ItemType Directory -Path $storageDirectory -Force | Out-Null
    $storageConnectionId = [guid]::NewGuid().ToString('N')
    if (Test-Path -LiteralPath $storageSettingsPath) {
        throw 'Connection already exists. Do not silently replace accounts: use a separate clean environment.'
    }
    $storageSecret | ConvertFrom-SecureString | Set-Content -LiteralPath $storageTokenPath -Encoding ASCII
    @{ ConnectionId = $storageConnectionId; Root = 'app:/LandErp/development' } |
        ConvertTo-Json | Set-Content -LiteralPath $storageSettingsPath -Encoding UTF8
    Write-Host 'Connection saved for this Windows user. Token was not printed. No cloud files have been changed.'
} finally {
    if ($null -ne $storageSecret) { $storageSecret.Dispose() }
}
