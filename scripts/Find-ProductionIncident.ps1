param(
    [Parameter(Mandatory=$true)][Guid]$Incident,
    [string]$StateRoot = 'C:\ProgramData\LandErp',
    [ValidateRange(1,100)][int]$MaxResults = 10,
    [switch]$Quiet
)
$ErrorActionPreference = 'Stop'
$logRoot = [IO.Path]::GetFullPath((Join-Path $StateRoot 'logs'))
if (-not (Test-Path -LiteralPath $logRoot -PathType Container)) { throw 'Production log directory was not found.' }
if ((Get-Item -LiteralPath $logRoot).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Log search does not traverse reparse points.' }
$directories = @($logRoot) + @(Get-ChildItem -LiteralPath $logRoot -Directory | Where-Object {
    $_.Name -match '^[0-9]{4}-[0-9]{2}$' -and -not ($_.Attributes -band [IO.FileAttributes]::ReparsePoint)
} | ForEach-Object { $_.FullName })
$files = @($directories | ForEach-Object { Get-ChildItem -LiteralPath $_ -File -Filter '*.log' } | Where-Object {
    $_.Name -match '^(server|worker)-.+\.(out|err)\.log$' -and -not ($_.Attributes -band [IO.FileAttributes]::ReparsePoint)
} | Sort-Object LastWriteTimeUtc -Descending)
if (-not $Quiet) { Write-Host "Checking $($files.Count) log files, including legacy logs. Reading complete files line by line." }
$found = New-Object 'System.Collections.Generic.List[object]'
foreach ($file in $files) {
    if ($found.Count -ge $MaxResults) { break }
    # No tail/raw read: an old incident can be anywhere inside an old, very large console log.
    Select-String -LiteralPath $file.FullName -SimpleMatch $Incident.ToString() -Encoding UTF8 |
      Select-Object -First ($MaxResults - $found.Count) | ForEach-Object {
        $hit = $_
        $entry = $null
        try { $entry = $hit.Line | ConvertFrom-Json } catch { }
        $state = if ($null -ne $entry) { $entry.State } else { $null }
        # Never print the raw line, Message/Exception, scopes or form/provider details.
        $found.Add([pscustomobject][ordered]@{
            File = $file.FullName; Line = $hit.LineNumber; Timestamp = $entry.Timestamp
            EventId = $entry.EventId; Level = $entry.LogLevel; OperationId = $state.OperationId
            Component = $state.Component; Action = $state.Action; Phase = $state.Phase
            FailureType = $state.FailureType; DiagnosticCode = $state.DiagnosticCode
            InnerFailureType = $state.InnerFailureType; SqlState = $state.SqlState
            Constraint = $state.Constraint; Table = $state.Table; Column = $state.Column
            Transient = $state.Transient; ElapsedMs = $state.ElapsedMs; CommandId = $state.CommandId
            ParsedJson = ($null -ne $entry)
        })
    }
}
if (-not $Quiet -and $found.Count -eq 0) { Write-Host 'Incident not found in the available logs.' }
$found.ToArray()
