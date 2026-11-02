#requires -Version 5.1
param(
    [Parameter(Mandatory=$true)][ValidatePattern('^[0-9a-f]{40}$')][string]$ExpectedCommit,
    [string]$RepoRoot = 'C:\LandErp',
    [string]$Version = '',
    [string]$StateRoot = 'C:\ProgramData\LandErp',
    [string]$InstallRoot = 'C:\Program Files\LandErp',
    [string]$ServerUrl = 'http://127.0.0.1:5080'
)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Run in PowerShell as Administrator.' }
if ([string]::IsNullOrWhiteSpace($Version)) { $Version = Get-Date -Format 'yyyy.MM.dd.HHmmss' }
if ($Version -notmatch '^[A-Za-z0-9._-]+$') { throw 'Invalid release version.' }
$repo = [IO.Path]::GetFullPath($RepoRoot)
if (-not (Test-Path -LiteralPath (Join-Path $repo '.git'))) { throw 'Repository not found.' }
$configPath = Join-Path $StateRoot 'config\production.json'
if (-not (Test-Path -LiteralPath $configPath)) { throw 'Existing private production config not found.' }
$releasePath = Join-Path $repo "artifacts\production\releases\$Version"
if (Test-Path -LiteralPath $releasePath) { throw 'Release folder already exists. Choose another -Version.' }
try { $config = Get-Content -Raw -LiteralPath $configPath | ConvertFrom-Json }
catch { throw 'Private production config is invalid; content was not printed.' }
if ([string]$config.Storage.Provider -ne 'YandexDisk' -or $null -eq $config.Storage.YandexDisk) { throw 'Existing storage must be YandexDisk. Do not replace its token or connection identity.' }
function Invoke-UpdateGit([string[]]$Arguments) {
    $result = & git -C $repo @Arguments
    if ($LASTEXITCODE -ne 0) { throw 'Git step failed. Local work was not reset.' }
    return $result
}
$remote = [string](Invoke-UpdateGit @('remote','get-url','origin'))
if ($remote.TrimEnd('/') -notin @('https://github.com/baimkin/LandErp.git','https://github.com/baimkin/LandErp','git@github.com:baimkin/LandErp.git')) { throw 'Unexpected repository origin.' }
$dirty = Invoke-UpdateGit @('status','--porcelain','--untracked-files=no')
if ($dirty) { throw 'Tracked files have local changes. Preserve them before updating; nothing was reset.' }
Invoke-UpdateGit @('fetch','origin') | Out-Host
Invoke-UpdateGit @('switch','main') | Out-Host
Invoke-UpdateGit @('pull','--ff-only','origin','main') | Out-Host
$head = [string](Invoke-UpdateGit @('rev-parse','HEAD'))
if ($head.Trim() -ne $ExpectedCommit) { throw 'Main differs from the reviewed commit. Deployment stopped before config changes.' }
$dotnet = (Get-Command dotnet -ErrorAction Stop).Source
# Publishing finishes while the current release is still running. No database setup/migration.
& (Join-Path $repo 'scripts\Publish-Production.ps1') -DotnetPath $dotnet -Version $Version
if ($LASTEXITCODE -ne 0) { throw 'Publishing failed; production config was not changed.' }
$metadata = Get-Content -Raw -LiteralPath (Join-Path $releasePath 'release.json') | ConvertFrom-Json
if ([string]$metadata.Commit -ne $ExpectedCommit) { throw 'Published release commit mismatch.' }
# Back up next to the private config, inheriting its protected directory permissions.
$backup = Join-Path (Split-Path $configPath -Parent) ('production.before-media-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N') + '.json')
Copy-Item -LiteralPath $configPath -Destination $backup -ErrorAction Stop
$config.Storage.YandexDisk | Add-Member -NotePropertyName MaxFileBytes -NotePropertyValue 524288000 -Force
try {
    $newConfig = $config | ConvertTo-Json -Depth 100
    # Write the same file to preserve its existing ACL; never print the JSON containing secrets.
    [IO.File]::WriteAllText($configPath, $newConfig, (New-Object Text.UTF8Encoding($false)))
    & (Join-Path $repo 'scripts\Deploy-ProductionRelease.ps1') -ReleasePath $releasePath -StateRoot $StateRoot -InstallRoot $InstallRoot -ServerUrl $ServerUrl
}
catch {
    Copy-Item -LiteralPath $backup -Destination $configPath -Force
    throw 'Deployment did not complete. Private config was restored. Check deployment logs/current release before retrying.'
}
finally { $config = $null; $newConfig = $null }
Write-Output "Updated commit: $ExpectedCommit"
Write-Output "Release source: $releasePath"
Write-Output "Private config backup: $backup"
Write-Output 'Upload limit: 500 MiB. Test a small file and your video from a user device in ERP.'