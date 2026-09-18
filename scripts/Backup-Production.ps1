param([string]$StateRoot = 'C:\ProgramData\LandErp', [string]$BackupRoot = '', [string]$PostgresBin = '')
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($BackupRoot)) { $BackupRoot = Join-Path $StateRoot 'backups' }
foreach ($name in @('PGHOST','PGDATABASE','PGUSER')) { if ([string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($name))) { throw "$name must be configured for the backup operator. Use pgpass/PGPASSFILE for the password." } }
$port = if ([string]::IsNullOrWhiteSpace($env:PGPORT)) { '5432' } else { $env:PGPORT }
function Resolve-PgTool([string]$Name) { if (-not [string]::IsNullOrWhiteSpace($PostgresBin)) { $candidate = Join-Path ([IO.Path]::GetFullPath($PostgresBin)) $Name; if (Test-Path -LiteralPath $candidate) { return $candidate } }; $command = Get-Command $Name -ErrorAction SilentlyContinue; if ($null -eq $command) { throw "$Name was not found. Install PostgreSQL 18 client tools or pass -PostgresBin." }; return $command.Source }
$pgDump = Resolve-PgTool 'pg_dump.exe'
$stamp = [DateTimeOffset]::UtcNow.ToString('yyyyMMdd-HHmmss')
$target = Join-Path ([IO.Path]::GetFullPath($BackupRoot)) $stamp
if (Test-Path -LiteralPath $target) { throw "Backup target already exists: $target" }
New-Item -ItemType Directory -Path $target | Out-Null
& icacls.exe $target /inheritance:r /grant:r '*S-1-5-18:(OI)(CI)(F)' '*S-1-5-32-544:(OI)(CI)(F)' | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Could not restrict backup directory ACL.' }
$dump = Join-Path $target 'database.dump'
& $pgDump --host=$env:PGHOST --port=$port --username=$env:PGUSER --dbname=$env:PGDATABASE --format=custom --no-password --file=$dump
if ($LASTEXITCODE -ne 0) { throw 'pg_dump failed. Backup was not accepted.' }
$config = Join-Path $StateRoot 'config\production.json'
if (-not (Test-Path -LiteralPath $config)) { throw 'Production config is missing.' }
New-Item -ItemType Directory -Force -Path (Join-Path $target 'application') | Out-Null
Copy-Item -LiteralPath $config -Destination (Join-Path $target 'application\production.json')
if (Test-Path -LiteralPath (Join-Path $StateRoot 'current-release.json')) { Copy-Item -LiteralPath (Join-Path $StateRoot 'current-release.json') -Destination (Join-Path $target 'application\current-release.json') }
$configJson = Get-Content -Raw -LiteralPath $config | ConvertFrom-Json
$keySource = [IO.Path]::GetFullPath([string]$configJson.Security.DataProtectionKeysPath)
if (-not (Test-Path -LiteralPath $keySource)) { throw 'Data Protection keys directory is missing.' }
Copy-Item -LiteralPath $keySource -Destination (Join-Path $target 'application\data-protection') -Recurse
$hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $dump).Hash
$applicationRoot = Join-Path $target 'application'
$applicationPrefix = $applicationRoot.TrimEnd('\') + '\'
$applicationFiles = @(Get-ChildItem -LiteralPath $applicationRoot -File -Recurse | Sort-Object FullName | ForEach-Object {
    [ordered]@{
        Path = $_.FullName.Substring($applicationPrefix.Length).Replace('\','/')
        Sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $_.FullName).Hash
    }
})
$release = $null
$releaseVersion = $null
$releaseCommit = $null
if (Test-Path -LiteralPath (Join-Path $StateRoot 'current-release.json')) { $release = Get-Content -Raw -LiteralPath (Join-Path $StateRoot 'current-release.json') | ConvertFrom-Json; $releaseVersion = [string]$release.Version; $releaseCommit = [string]$release.Commit }
[ordered]@{ CreatedAtUtc=[DateTimeOffset]::UtcNow.ToString('O'); DatabaseHost=$env:PGHOST; DatabasePort=$port; Database=$env:PGDATABASE; BackupUser=$env:PGUSER; DatabaseSha256=$hash; ApplicationFiles=$applicationFiles; ReleaseVersion=$releaseVersion; ReleaseCommit=$releaseCommit; ExternalStorage='Provider-managed; B4-04 must verify at least one stored attachment is readable after DB restore.' } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $target 'manifest.json') -Encoding UTF8
Write-Output "Production backup created: $target"
Write-Output 'This backup contains secrets and Data Protection keys; keep its ACL restricted.'
