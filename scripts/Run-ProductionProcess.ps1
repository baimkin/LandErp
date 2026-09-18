param(
    [Parameter(Mandatory=$true)][ValidateSet('Server','Worker')][string]$Service,
    [string]$StateRoot = 'C:\ProgramData\LandErp',
    [string]$ServerUrl = 'http://127.0.0.1:5080'
)
$ErrorActionPreference = 'Stop'
$listenUri = [Uri]$ServerUrl
if ($listenUri.Scheme -ne 'http' -or $listenUri.Host -notin @('127.0.0.1','localhost','::1')) { throw 'Production backend URL must stay on loopback HTTP behind the HTTPS reverse proxy.' }
$currentFile = Join-Path $StateRoot 'current-release.json'
$configFile = Join-Path $StateRoot 'config\production.json'
if (-not (Test-Path -LiteralPath $currentFile)) { throw 'Production release metadata is missing.' }
if (-not (Test-Path -LiteralPath $configFile)) { throw 'Production configuration is missing.' }
$current = Get-Content -Raw -LiteralPath $currentFile | ConvertFrom-Json
$releasePath = [IO.Path]::GetFullPath([string]$current.ReleasePath)
$exe = if ($Service -eq 'Server') { Join-Path $releasePath 'Server\LandErp.Server.exe' } else { Join-Path $releasePath 'Worker\LandErp.Worker.exe' }
if (-not (Test-Path -LiteralPath $exe)) { throw "Published $Service executable is missing." }
$env:DOTNET_ENVIRONMENT = 'Production'
$env:LANDERP_CONFIG_FILE = $configFile
if ($Service -eq 'Server') { $env:ASPNETCORE_URLS = $ServerUrl } else { Remove-Item Env:ASPNETCORE_URLS -ErrorAction SilentlyContinue }
$logDirectory = Join-Path $StateRoot 'logs'
New-Item -ItemType Directory -Force -Path $logDirectory | Out-Null
$stamp = [DateTimeOffset]::UtcNow.ToString('yyyyMMdd-HHmmss')
$suffix = [Guid]::NewGuid().ToString('N').Substring(0,8)
$prefix = $Service.ToLowerInvariant()
$outLog = Join-Path $logDirectory "$prefix-$stamp-$suffix.out.log"
$errLog = Join-Path $logDirectory "$prefix-$stamp-$suffix.err.log"
$process = Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe -Parent) -PassThru -Wait -RedirectStandardOutput $outLog -RedirectStandardError $errLog
exit $process.ExitCode
