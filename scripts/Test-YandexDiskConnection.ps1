param([string]$DotnetPath = 'dotnet', [string]$ArtifactsPath = '')
$ErrorActionPreference = 'Stop'
$storageRoot = Split-Path $PSScriptRoot -Parent
$storagePreviousToken = $env:Storage__YandexDisk__Token
$storagePreviousProvider = $env:Storage__Provider
$storagePreviousId = $env:Storage__YandexDisk__ConnectionId
$storagePreviousRoot = $env:Storage__YandexDisk__Root
$storagePreviousLive = $env:LANDERP_YANDEX_LIVE_TEST
try {
    . (Join-Path $PSScriptRoot 'Import-YandexDiskConnection.ps1')
    $env:LANDERP_YANDEX_LIVE_TEST = '1'
    $storageTestArguments = @('test', (Join-Path $storageRoot 'tests/LandErp.Foundation.Tests'), '-c', 'Release', '--no-build', '--filter', 'TestCategory=LiveStorage', '--logger', 'console;verbosity=minimal')
    if ($ArtifactsPath) { $storageTestArguments += @('--artifacts-path', [IO.Path]::GetFullPath($ArtifactsPath)) }
    & $DotnetPath @storageTestArguments
    if ($LASTEXITCODE -ne 0) { throw 'Yandex Disk check failed. No secret values were printed.' }
} finally {
    $env:Storage__YandexDisk__Token = $storagePreviousToken
    $env:Storage__Provider = $storagePreviousProvider
    $env:Storage__YandexDisk__ConnectionId = $storagePreviousId
    $env:Storage__YandexDisk__Root = $storagePreviousRoot
    $env:LANDERP_YANDEX_LIVE_TEST = $storagePreviousLive
}
