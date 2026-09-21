param(
    [string]$StateRoot = 'C:\ProgramData\LandErp',
    [string]$ConfigPath = '',
    [string]$SiteName = '',
    [string]$HostName = '',
    [string]$LineagePath = '',
    [string]$OpenSslPath = ''
)
$ErrorActionPreference = 'Stop'

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run certificate synchronization from an elevated PowerShell or the installed SYSTEM task.'
}

if ([string]::IsNullOrWhiteSpace($ConfigPath)) {
    $ConfigPath = Join-Path $StateRoot 'config\https-renewal.json'
}
if (Test-Path -LiteralPath $ConfigPath) {
    $config = Get-Content -Raw -LiteralPath $ConfigPath | ConvertFrom-Json
    if ([string]::IsNullOrWhiteSpace($SiteName)) { $SiteName = [string]$config.SiteName }
    if ([string]::IsNullOrWhiteSpace($HostName)) { $HostName = [string]$config.HostName }
    if ([string]::IsNullOrWhiteSpace($LineagePath)) { $LineagePath = [string]$config.LineagePath }
    if ([string]::IsNullOrWhiteSpace($OpenSslPath)) { $OpenSslPath = [string]$config.OpenSslPath }
}
if ([string]::IsNullOrWhiteSpace($SiteName) -or [string]::IsNullOrWhiteSpace($HostName)
    -or [string]::IsNullOrWhiteSpace($LineagePath)) {
    throw 'SiteName, HostName and Certbot LineagePath are required.'
}
if ([string]::IsNullOrWhiteSpace($OpenSslPath)) { $OpenSslPath = 'openssl.exe' }

$lineage = [IO.Path]::GetFullPath($LineagePath)
$fullChain = Join-Path $lineage 'fullchain.pem'
$privateKey = Join-Path $lineage 'privkey.pem'
if (-not (Test-Path -LiteralPath $fullChain) -or -not (Test-Path -LiteralPath $privateKey)) {
    throw 'Certbot lineage must contain fullchain.pem and privkey.pem.'
}
$openssl = Get-Command $OpenSslPath -ErrorAction Stop
Import-Module WebAdministration -ErrorAction Stop

function Parse-Binding([string]$Value) {
    if ($Value -notmatch '^(?<ip>.*):(?<port>[0-9]+):(?<host>.*)$') {
        throw "Unexpected IIS binding format for $SiteName."
    }
    return [pscustomobject]@{ Ip=$Matches.ip; Port=[int]$Matches.port; Host=$Matches.host }
}
function Binding-Thumbprint($Binding) {
    $value = $Binding.CertificateHash
    if ($value -is [byte[]]) {
        return (($value | ForEach-Object { $_.ToString('X2') }) -join '').ToUpperInvariant()
    }
    return ([string]$value).Replace(' ','').ToUpperInvariant()
}

$bindings = @(Get-WebBinding -Name $SiteName -Protocol 'https' | Where-Object {
    (Parse-Binding ([string]$_.bindingInformation)).Host -ieq $HostName
})
if ($bindings.Count -ne 1) {
    throw "Expected exactly one HTTPS binding for site '$SiteName' and host '$HostName'; found $($bindings.Count)."
}
$binding = $bindings[0]
$bindingParts = Parse-Binding ([string]$binding.bindingInformation)
$previousThumbprint = Binding-Thumbprint $binding
$switched = $false

$tempDirectory = Join-Path $StateRoot 'certificates'
New-Item -ItemType Directory -Force -Path $tempDirectory | Out-Null
$tempPfx = Join-Path $tempDirectory ("renew-" + [Guid]::NewGuid().ToString('N') + '.pfx')
$random = [Security.Cryptography.RandomNumberGenerator]::Create()
$passwordBytes = New-Object byte[] 32
$random.GetBytes($passwordBytes)
$random.Dispose()
$passwordText = [Convert]::ToBase64String($passwordBytes)
[Array]::Clear($passwordBytes,0,$passwordBytes.Length)
$previousPassword = $env:LANDERP_TEMP_PFX_PASSWORD
try {
    $env:LANDERP_TEMP_PFX_PASSWORD = $passwordText
    & $openssl.Source pkcs12 -export -out $tempPfx -inkey $privateKey -in $fullChain -passout env:LANDERP_TEMP_PFX_PASSWORD
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $tempPfx)) {
        throw 'OpenSSL could not create the temporary PFX.'
    }

    $securePassword = ConvertTo-SecureString $passwordText -AsPlainText -Force
    $imported = @(Import-PfxCertificate -FilePath $tempPfx -CertStoreLocation 'Cert:\LocalMachine\My' -Password $securePassword -Exportable:$false)
    $leaf = $imported | Where-Object { $_.HasPrivateKey } | Sort-Object NotAfter -Descending | Select-Object -First 1
    if ($null -eq $leaf) { throw 'The renewed certificate was imported without a private key.' }
    if ($leaf.NotAfter -le [DateTime]::UtcNow.AddDays(7)) {
        throw 'The Certbot certificate expires in seven days or less; refusing to switch IIS to it.'
    }

    $thumbprint = ([string]$leaf.Thumbprint).Replace(' ','').ToUpperInvariant()
    if ((Binding-Thumbprint $binding) -ne $thumbprint) {
        $binding.AddSslCertificate($thumbprint, 'My')
        $switched = $true
    }

    $fresh = @(Get-WebBinding -Name $SiteName -Protocol 'https' | Where-Object {
        (Parse-Binding ([string]$_.bindingInformation)).Host -ieq $HostName
    })
    if ($fresh.Count -ne 1) { throw 'HTTPS binding changed unexpectedly during certificate synchronization.' }
    if ((Binding-Thumbprint $fresh[0]) -ne $thumbprint) {
        throw 'IIS did not retain the renewed certificate thumbprint.'
    }

    $connectHost = $bindingParts.Ip.Trim('[',']')
    if ([string]::IsNullOrWhiteSpace($connectHost) -or $connectHost -in @('*','0.0.0.0','::')) {
        $connectHost = '127.0.0.1'
    }
    $tcp = [Net.Sockets.TcpClient]::new()
    try {
        $tcp.Connect($connectHost, $bindingParts.Port)
        $ssl = [Net.Security.SslStream]::new($tcp.GetStream(), $false)
        try {
            $ssl.AuthenticateAsClient($HostName)
            $remote = [Security.Cryptography.X509Certificates.X509Certificate2]::new($ssl.RemoteCertificate)
            if (([string]$remote.Thumbprint).Replace(' ','').ToUpperInvariant() -ne $thumbprint) {
                throw 'The certificate served by IIS does not match the renewed certificate.'
            }
        }
        finally { if ($null -ne $ssl) { $ssl.Dispose() } }
    }
    finally { $tcp.Dispose() }

    Write-Output "IIS certificate is current for $HostName; expires $($leaf.NotAfter.ToUniversalTime().ToString('O')); thumbprint $thumbprint."
}
catch {
    if ($switched -and -not [string]::IsNullOrWhiteSpace($previousThumbprint)
        -and (Test-Path -LiteralPath ("Cert:\LocalMachine\My\" + $previousThumbprint))) {
        try {
            $rollback = @(Get-WebBinding -Name $SiteName -Protocol 'https' | Where-Object {
                (Parse-Binding ([string]$_.bindingInformation)).Host -ieq $HostName
            })
            if ($rollback.Count -eq 1) {
                $rollback[0].AddSslCertificate($previousThumbprint, 'My')
            }
        }
        catch {
            Write-Error 'Certificate synchronization failed and IIS rollback also failed. Restore the previous binding manually.'
        }
    }
    throw
}
finally {
    if ($null -eq $previousPassword) { Remove-Item Env:LANDERP_TEMP_PFX_PASSWORD -ErrorAction SilentlyContinue }
    else { $env:LANDERP_TEMP_PFX_PASSWORD = $previousPassword }
    $passwordText = $null
    Remove-Item -LiteralPath $tempPfx -Force -ErrorAction SilentlyContinue
}
