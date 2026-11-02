param(
    [string]$ProductionConfigPath = 'C:\ProgramData\LandErp\config\production.json',
    [string]$Origin = '',
    [ValidateSet('msedge','chrome')][string]$Browser = 'msedge'
)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
if (-not (Test-Path -LiteralPath $ProductionConfigPath -PathType Leaf)) {
    throw 'Не найдена production-конфигурация. Укажите -ProductionConfigPath. Сам файл присылать не нужно.'
}
$probeCandidates = @(
    (Join-Path $PSScriptRoot 'probe\LandErp.StorageProbe.dll'),
    (Join-Path $PSScriptRoot '..\artifacts\media-large-upload\direct-probe\probe\LandErp.StorageProbe.dll'),
    (Join-Path $PSScriptRoot '..\tools\LandErp.StorageProbe\bin\Release\net10.0\LandErp.StorageProbe.dll')
)
$probe = $probeCandidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
if (-not $probe) { throw 'Распакуйте весь архив рядом со скриптом: нужна папка probe.' }
$dotnet = (Get-Command dotnet -ErrorAction Stop).Source
$probeArguments = @($probe, '--config', $ProductionConfigPath, '--browser', $Browser)
if ($Origin) { $probeArguments += @('--origin', $Origin) }
Write-Host 'Проверка прямой загрузки: отдельный браузер, маленький тестовый файл, без изменения ERP и БД.'
Write-Host 'Токен и подписанная ссылка не выводятся. Тестовый файл останется в папке Checks на Диске.'
$outputLines = & $dotnet @probeArguments 2>$null
$probeExit = $LASTEXITCODE
try { $result = ($outputLines -join "`n") | ConvertFrom-Json }
catch { throw 'Проверка не вернула безопасный JSON-отчёт. Проверьте наличие .NET 10 и Edge; конфигурацию и токен не присылайте.' }
# The utility emits only this explicit allowlist, never raw provider/browser diagnostics.
$result | Select-Object Outcome, Stage, Code, Origin, CheckId, FileBytes, PreflightStatus,
    AllowOrigin, AllowPut, AllowContentType, BrowserEvent, BrowserStatus, ProgressObserved, ProviderVerified |
    ConvertTo-Json
switch ([string]$result.Outcome) {
    'DIRECT_SUPPORTED' { Write-Host 'Прямая загрузка из браузера подтверждена, размер и SHA-256 совпали.' }
    'DIRECT_BLOCKED_CORS' { Write-Host 'Браузерная загрузка по свежей ссылке заблокирована CORS. Можно выбирать потоковый путь через ERP.' }
    default { Write-Host "Результат неопределённый (код процесса $probeExit). Это не означает, что прямая загрузка не поддерживается." }
}