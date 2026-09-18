param([Parameter(Mandatory=$true)][string]$BackupPath, [Parameter(Mandatory=$true)][string]$TargetDatabase, [string]$PostgresBin = '', [string]$ApplicationFilesDestination = '')
$ErrorActionPreference = 'Stop'
if ($TargetDatabase -notmatch '^[A-Za-z0-9_]+$') { throw 'TargetDatabase must contain only letters, digits and underscore.' }
foreach ($name in @('PGHOST','PGUSER')) { if ([string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($name))) { throw "$name must be configured for the restore operator. Use pgpass/PGPASSFILE for the password." } }
$port = if ([string]::IsNullOrWhiteSpace($env:PGPORT)) { '5432' } else { $env:PGPORT }
$backup = [IO.Path]::GetFullPath($BackupPath)
$manifestFile = Join-Path $backup 'manifest.json'
$dump = Join-Path $backup 'database.dump'
if (-not (Test-Path -LiteralPath $manifestFile) -or -not (Test-Path -LiteralPath $dump)) { throw 'Backup is incomplete.' }
$manifest = Get-Content -Raw -LiteralPath $manifestFile | ConvertFrom-Json
if ((Get-FileHash -Algorithm SHA256 -LiteralPath $dump).Hash -ne [string]$manifest.DatabaseSha256) { throw 'Database backup hash does not match manifest.' }
function Resolve-PgTool([string]$Name) { if (-not [string]::IsNullOrWhiteSpace($PostgresBin)) { $candidate = Join-Path ([IO.Path]::GetFullPath($PostgresBin)) $Name; if (Test-Path -LiteralPath $candidate) { return $candidate } }; $command = Get-Command $Name -ErrorAction SilentlyContinue; if ($null -eq $command) { throw "$Name was not found. Install PostgreSQL 18 client tools or pass -PostgresBin." }; return $command.Source }
$psql = Resolve-PgTool 'psql.exe'
$createdb = Resolve-PgTool 'createdb.exe'
$pgRestore = Resolve-PgTool 'pg_restore.exe'
$existing = & $psql --host=$env:PGHOST --port=$port --username=$env:PGUSER --dbname=postgres --no-password --tuples-only --no-align --command="SELECT 1 FROM pg_database WHERE datname = '$TargetDatabase';"
if ($LASTEXITCODE -ne 0) { throw 'Could not check target database.' }
if (($existing -join '').Trim() -eq '1') { throw 'Target database already exists. Restore drill never overwrites an existing database.' }
& $createdb --host=$env:PGHOST --port=$port --username=$env:PGUSER --no-password $TargetDatabase
if ($LASTEXITCODE -ne 0) { throw 'Could not create clean restore database.' }
try {
    & $pgRestore --host=$env:PGHOST --port=$port --username=$env:PGUSER --dbname=$TargetDatabase --no-owner --no-password --exit-on-error $dump
    if ($LASTEXITCODE -ne 0) { throw 'pg_restore failed.' }
    $migrationCount = & $psql --host=$env:PGHOST --port=$port --username=$env:PGUSER --dbname=$TargetDatabase --no-password --tuples-only --no-align --command='SELECT count(*) FROM foundation.migration_history;'
    $migrationCountValue = [int](($migrationCount -join '').Trim())
    if ($LASTEXITCODE -ne 0 -or $migrationCountValue -lt 1) { throw 'Restored migration history is missing.' }
    $critical = & $psql --host=$env:PGHOST --port=$port --username=$env:PGUSER --dbname=$TargetDatabase --no-password --tuples-only --no-align --command="SELECT 'organizations='||(SELECT count(*) FROM organization.organizations) UNION ALL SELECT 'users='||(SELECT count(*) FROM identity.users) UNION ALL SELECT 'property_cases='||(SELECT count(*) FROM procurement.property_cases) UNION ALL SELECT 'stored_files='||(SELECT count(*) FROM foundation.stored_files);"
    if ($LASTEXITCODE -ne 0) { throw 'Critical restored rows could not be inspected.' }
    Write-Output 'Restore database checks:'
    $critical | ForEach-Object { Write-Output $_ }
} catch { Write-Warning "Restore failed. Clean target database '$TargetDatabase' was left in place for diagnosis; drop it explicitly after inspection."; throw }
if (-not [string]::IsNullOrWhiteSpace($ApplicationFilesDestination)) { $source = Join-Path $backup 'application'; if (-not (Test-Path -LiteralPath $source)) { throw 'Application files are missing from backup.' }; $destination = [IO.Path]::GetFullPath($ApplicationFilesDestination); if (Test-Path -LiteralPath $destination) { throw 'ApplicationFilesDestination must not already exist.' }; Copy-Item -LiteralPath $source -Destination $destination -Recurse; Write-Output "Application config/keys restored to isolated path: $destination" }
Write-Output "Restore drill database ready: $TargetDatabase"
Write-Output 'Create a separate drill production.json pointing to this database before starting Server against the restored copy.'
