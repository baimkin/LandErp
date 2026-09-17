param([ValidateSet('Server','Worker')][string]$Service = 'Server', [string]$DotnetPath = 'dotnet',
    [string]$SettingsPath = '', [string]$LegacyFilesRoot = '', [string]$ListenUrl = 'https://localhost:7240',
    [string]$ArtifactsPath = '')
$ErrorActionPreference = 'Stop'
$stageRoot = Split-Path $PSScriptRoot -Parent
Set-Location $stageRoot
$stageSettingsPath = if ($SettingsPath) { [IO.Path]::GetFullPath($SettingsPath) } else { Join-Path $stageRoot 'local-data/stage1/settings.json' }
if (-not (Test-Path -LiteralPath $stageSettingsPath)) { throw 'Run Initialize-Local.ps1 first' }
$stageSettings = Get-Content -Raw -LiteralPath $stageSettingsPath | ConvertFrom-Json
$env:Database__ConnectionString = $stageSettings.RuntimeConnection
$env:DOTNET_ENVIRONMENT = 'Local'
$env:ASPNETCORE_URLS = $ListenUrl
if ($LegacyFilesRoot) { $env:Storage__Root = [IO.Path]::GetFullPath($LegacyFilesRoot) }
$storagePreviousToken = $env:Storage__YandexDisk__Token
$storagePreviousProvider = $env:Storage__Provider
$storagePreviousId = $env:Storage__YandexDisk__ConnectionId
$storagePreviousRoot = $env:Storage__YandexDisk__Root
try {
    if ($Service -eq 'Server' -and (Test-Path -LiteralPath (Join-Path $stageRoot 'local-data/yandex-disk/settings.json'))) {
        . (Join-Path $PSScriptRoot 'Import-YandexDiskConnection.ps1')
    }
    $stageRunArguments = @('run', '--project', "src/LandErp.$Service", '-c', 'Release', '--no-build', '--no-launch-profile')
    if ($ArtifactsPath) { $stageRunArguments += @('--artifacts-path', [IO.Path]::GetFullPath($ArtifactsPath)) }
    & $DotnetPath @stageRunArguments
    if ($LASTEXITCODE -ne 0) { throw 'Local host stopped with an error; connection values were not printed' }
} finally {
    $env:Storage__YandexDisk__Token = $storagePreviousToken
    $env:Storage__Provider = $storagePreviousProvider
    $env:Storage__YandexDisk__ConnectionId = $storagePreviousId
    $env:Storage__YandexDisk__Root = $storagePreviousRoot
}
