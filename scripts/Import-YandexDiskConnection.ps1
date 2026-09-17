# Dot-source only in the process that starts the server or the live test.
$storageDirectory = Join-Path (Split-Path $PSScriptRoot -Parent) 'local-data/yandex-disk'
$storageSettingsPath = Join-Path $storageDirectory 'settings.json'
if (-not (Test-Path -LiteralPath $storageSettingsPath)) { throw 'Run Set-YandexDiskConnection.ps1 first.' }
$storageSettings = Get-Content -Raw -LiteralPath $storageSettingsPath | ConvertFrom-Json
$storageEncrypted = (Get-Content -Raw -LiteralPath (Join-Path $storageDirectory 'token.dpapi')).Trim()
try { $storageSecret = ConvertTo-SecureString -String $storageEncrypted }
catch { throw 'Cannot unlock the local token. Run under the Windows user who saved the connection.' }
try {
    $env:Storage__Provider = 'YandexDisk'
    $env:Storage__YandexDisk__ConnectionId = $storageSettings.ConnectionId
    $env:Storage__YandexDisk__Root = $storageSettings.Root
    $env:Storage__YandexDisk__Token = ([pscredential]::new('oauth', $storageSecret)).GetNetworkCredential().Password
} finally { $storageSecret.Dispose() }
