param([ValidateSet('Server','Worker')][string]$Service = 'Server', [string]$DotnetPath = 'dotnet',
    [string]$SettingsPath = '', [string]$LegacyFilesRoot = '', [string]$ListenUrl = 'https://localhost:7240',
    [string]$ArtifactsPath = '', [switch]$ServerOnly)
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
$stageWorkerProcess = $null
try {
    if ($Service -eq 'Server' -and -not $ServerOnly) {
        # The local website and scheduler are one operating session. No schema changes.
        $stageWorkerAssembly = if ($ArtifactsPath) {
            Join-Path ([IO.Path]::GetFullPath($ArtifactsPath)) 'bin/LandErp.Worker/release/LandErp.Worker.dll'
        } else { Join-Path $stageRoot 'src/LandErp.Worker/bin/Release/net10.0/LandErp.Worker.dll' }
        if (-not (Test-Path -LiteralPath $stageWorkerAssembly)) { throw 'Build LandErp.slnx first: Worker assembly is missing.' }
        $stageLogDirectory = Join-Path $stageRoot 'artifacts/local-session'
        New-Item -ItemType Directory -Force -Path $stageLogDirectory | Out-Null
        $stageLogId = [Guid]::NewGuid().ToString('N')
        $stageWorkerProcess = Start-Process -FilePath (Get-Command $DotnetPath).Source -ArgumentList @('"' + $stageWorkerAssembly + '"') -WorkingDirectory (Join-Path $stageRoot 'src/LandErp.Worker') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $stageLogDirectory "$stageLogId-worker.out.log") -RedirectStandardError (Join-Path $stageLogDirectory "$stageLogId-worker.err.log")
        Start-Sleep -Milliseconds 1000
        if ($stageWorkerProcess.HasExited) { throw 'Local scheduler failed to start. Check artifacts/local-session privately.' }
        Write-Output 'Local scheduler started alongside Server. Its health is shown on /collectors.'
    }
    if ($Service -eq 'Server' -and (Test-Path -LiteralPath (Join-Path $stageRoot 'local-data/yandex-disk/settings.json'))) {
        . (Join-Path $PSScriptRoot 'Import-YandexDiskConnection.ps1')
    }
    $stageRunArguments = @('run', '--project', "src/LandErp.$Service", '-c', 'Release', '--no-build', '--no-launch-profile')
    if ($ArtifactsPath) { $stageRunArguments += @('--artifacts-path', [IO.Path]::GetFullPath($ArtifactsPath)) }
    & $DotnetPath @stageRunArguments
    if ($LASTEXITCODE -ne 0) { throw 'Local host stopped with an error; connection values were not printed' }
} finally {
    # Stop only the Worker this exact invocation created, never another user's process.
    if ($null -ne $stageWorkerProcess) {
        if (-not $stageWorkerProcess.HasExited) { $stageWorkerProcess.Kill(); $stageWorkerProcess.WaitForExit() }
        $stageWorkerProcess.Dispose()
    }
    $env:Storage__YandexDisk__Token = $storagePreviousToken
    $env:Storage__Provider = $storagePreviousProvider
    $env:Storage__YandexDisk__ConnectionId = $storagePreviousId
    $env:Storage__YandexDisk__Root = $storagePreviousRoot
}
