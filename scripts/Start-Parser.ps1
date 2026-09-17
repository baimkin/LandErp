param([switch]$NoBuild)
$ErrorActionPreference = 'Stop'
$parserRoot = Split-Path $PSScriptRoot -Parent
$parserDotnet = Join-Path $parserRoot 'artifacts/stage1/dotnet/dotnet.exe'
if (-not (Test-Path -LiteralPath $parserDotnet)) { $parserDotnet = (Get-Command dotnet -ErrorAction Stop).Source }
$parserOutput = Join-Path $parserRoot 'artifacts/parser-workspace/parser'
$parserAssembly = Join-Path $parserOutput 'LandErp.ParserSpike.Desktop.dll'
if (-not $NoBuild) {
    Push-Location $parserRoot
    try {
        & $parserDotnet build 'src/LandErp.ParserSpike.Desktop/LandErp.ParserSpike.Desktop.csproj' -c Release --no-restore -o $parserOutput
        if ($LASTEXITCODE -ne 0) { throw 'Parser build failed. Close the previous parser and try again.' }
    } finally { Pop-Location }
}
if (-not (Test-Path -LiteralPath $parserAssembly)) { throw 'Parser build is missing. Run without -NoBuild.' }
Start-Process -FilePath $parserDotnet -ArgumentList ('"' + $parserAssembly + '"') -WorkingDirectory $parserRoot -WindowStyle Hidden
