<#
.SYNOPSIS
    Trust Tesria's local certificate authority (Windows).

.DESCRIPTION
    A Tesria server without a public domain makes its own certificate
    authority, so browsers warn about it. This script downloads that
    authority's certificate from the server and adds it to this computer's
    trusted certificates, so the warning stops: once per device.

    It prints the certificate's SHA-256 fingerprint and trusts it, the way
    SSH trusts a server the first time. On a network you run yourself that
    is enough. On one someone else controls, the certificate, which comes
    over plain HTTP because nothing is trusted yet, could be theirs, and a
    trusted authority vouches for every website: there, give -Fingerprint,
    and it trusts nothing unless the certificate matches (the review's
    SEC-01, made optional by the owner on 2026-09-27). Get the fingerprint
    from the server:
      - on the server:  docker compose logs app | Select-String fingerprint
      - or in Tesria:   Administration, Settings, Certificate

    Get this script from Tesria's GitHub releases, or from the
    tesria-deploy.zip you installed from, not from the server: a script
    fetched over the same plain HTTP could have been changed on the way.

    By default it trusts the server for everyone who uses this computer,
    which needs an administrator (it asks). -CurrentUser trusts it for your
    Windows account only, with no administrator.

    Re-running it is safe. If the server is reinstalled from scratch (its
    caddy_data volume deleted), it makes a new authority with a new
    fingerprint, and every device needs this again.

.PARAMETER HostName
    What you type into the browser to open Tesria, without https://, such as
    wiki-server.local. For a Tesria on ports of its own, its plain HTTP port
    goes with it, such as localhost:8080: the /trust page fills this in.

.PARAMETER Fingerprint
    Optional. The certificate's SHA-256 fingerprint, from the server; with
    it, nothing is trusted unless the certificate matches. Separators and
    case do not matter.

.PARAMETER CurrentUser
    Trust it for your Windows account only; no administrator needed.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\trust-ca.ps1 wiki-server.local

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\trust-ca.ps1 wiki-server.local -Fingerprint AB:CD:...
#>

param(
    [Parameter(Position = 0, Mandatory = $true)]
    [string]$HostName,
    [string]$Fingerprint = "",
    [switch]$CurrentUser
)

$ErrorActionPreference = "Stop"

function Get-Hex([string]$value) { return ($value -replace '[^0-9A-Fa-f]', '').ToUpperInvariant() }

# Whether -Fingerprint was given at all, apart from its value: a value with
# no hexadecimal digits in it (an empty variable, say) is refused rather
# than taken as "no fingerprint".
$fingerprintGiven = $PSBoundParameters.ContainsKey('Fingerprint')
$expected = Get-Hex $Fingerprint
if ($fingerprintGiven -and $expected.Length -ne 64) {
    Write-Error "That is not a SHA-256 fingerprint (64 hexadecimal digits, usually in pairs like AB:CD:...)."
    exit 2
}

function Test-Admin {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

# Trusting it for everyone on this computer needs an administrator:
# relaunch elevated, passing everything through.
if (-not $CurrentUser -and -not (Test-Admin)) {
    Write-Host "==> Re-launching as administrator (trusting it for everyone on this computer needs it)..."
    $scriptPath = $MyInvocation.MyCommand.Path
    $relaunch = @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", "`"$scriptPath`"", "`"$HostName`"")
    if ($expected) { $relaunch += @("-Fingerprint", "`"$expected`"") }
    Start-Process powershell.exe -Verb RunAs -ArgumentList $relaunch
    exit
}

$certUrl = "http://$HostName/ca.crt"
$tmpCert = Join-Path $env:TEMP "tesria-ca-$([guid]::NewGuid()).crt"

Write-Host "==> Fetching the certificate from $certUrl ..."
try {
    Invoke-WebRequest -Uri $certUrl -OutFile $tmpCert -UseBasicParsing -TimeoutSec 10 | Out-Null
}
catch {
    Write-Error "Couldn't download the certificate from $certUrl. Make sure Tesria is running and reachable at that address, and that nothing blocks port 80."
    Read-Host "Press Enter to close"
    exit 1
}

try {
    $cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($tmpCert)
}
catch {
    Remove-Item $tmpCert -ErrorAction SilentlyContinue
    Write-Error "What $certUrl sent is not a certificate. Nothing was trusted."
    Read-Host "Press Enter to close"
    exit 1
}

# SHA-256 of the certificate itself, computed here rather than asked of the
# certificate, which on Windows PowerShell 5.1 only offers SHA-1.
$sha = [System.Security.Cryptography.SHA256]::Create()
$actual = Get-Hex ([BitConverter]::ToString($sha.ComputeHash($cert.RawData)))
if (-not $expected) {
    Write-Host "==> Its SHA-256 fingerprint: $(($actual -split '(..)' | Where-Object { $_ }) -join ':')"
    Write-Host "    Not checked, as no -Fingerprint was given. On a network you do not"
    Write-Host "    control, compare it with the one on the server before relying on it."
}
elseif ($actual -ne $expected) {
    Remove-Item $tmpCert -ErrorAction SilentlyContinue
    Write-Host ""
    Write-Host "ERROR: the certificate from $HostName does NOT match the fingerprint you gave." -ForegroundColor Red
    Write-Host "       Nothing was trusted."
    Write-Host "       Check you copied the fingerprint from this server, and the address is right."
    Write-Host "       If both are, something on the network may be answering in the server's"
    Write-Host "       place: do not trust it, and try from another network."
    Read-Host "Press Enter to close"
    exit 1
}
if ($expected) { Write-Host "==> The certificate matches the fingerprint: $($cert.Subject)" }

$store = if ($CurrentUser) { "Cert:\CurrentUser\Root" } else { "Cert:\LocalMachine\Root" }
Write-Host "==> Adding it to $store ..."
if ($CurrentUser) {
    Write-Host "    Windows asks you to confirm a certificate from 'Caddy Local Authority': choose Yes."
}
Import-Certificate -FilePath $tmpCert -CertStoreLocation $store | Out-Null
Remove-Item $tmpCert -ErrorAction SilentlyContinue

Write-Host "==> Done. Restart your browser (quit it completely and open it again)."
Write-Host "    Chrome and Edge use Windows' trusted certificates, so both trust it now."
Write-Host "    Firefox keeps its own list: see the Tesria docs, Trusting the local certificate."

# The address is the plain HTTP one, with its port on a Tesria that has
# ports of its own (localhost:8080), so its HTTPS address is found where
# that sends a browser: https://localhost:8443 (WIN-003). Only an address
# on the same host is believed; with no port given, HTTPS is on 443.
$name = if ($HostName -match '^(\[[^\]]+\]|[^:]+):\d+$') { $Matches[1] } else { $HostName }
$origin = $null
try {
    $request = [System.Net.HttpWebRequest]::Create("http://$HostName/api/health")
    $request.AllowAutoRedirect = $false
    $request.Timeout = 10000
    $response = $request.GetResponse()
    $location = $response.Headers["Location"]
    $response.Close()
    if ($location -match '^(https://[^/?#]+)') {
        $found = $Matches[1]
        if ($found -eq "https://$name" -or $found.StartsWith("https://${name}:", [StringComparison]::OrdinalIgnoreCase)) {
            $origin = $found
        }
    }
}
catch {
    # No answer, or not a redirect: decided below.
}
if (-not $origin -and $name -eq $HostName) { $origin = "https://$HostName" }

Write-Host ""
if (-not $origin) {
    Write-Host "==> Could not tell which HTTPS address $HostName sends browsers to, so this was not checked."
    Write-Host "    Open Tesria in your browser: it should show no warning."
}
else {
    Write-Host "==> Checking $origin/ ..."
    # In a new PowerShell each time, and a few times over some seconds: this
    # process can keep its first answer about the certificate, from before it
    # was trusted, and said "Still failing" on the very run that trusted it
    # (R-002, the 0.8.3 Windows retest).
    $quoted = $origin -replace "'", "''"
    $ok = $false
    foreach ($attempt in 1..5) {
        & "$PSHOME\powershell.exe" -NoProfile -NonInteractive -Command "try { Invoke-WebRequest -Uri '$quoted/api/health' -UseBasicParsing -TimeoutSec 10 | Out-Null; exit 0 } catch { exit 1 }"
        if ($LASTEXITCODE -eq 0) { $ok = $true; break }
        Start-Sleep -Seconds 2
    }
    if ($ok) {
        Write-Host "    Success: this computer now trusts $($origin.Substring(8))."
    }
    else {
        Write-Host "    Still failing. Quit and reopen your browser. If the warning stays, see the Tesria docs, Trusting the local certificate."
    }
}

Write-Host ""
Read-Host "Press Enter to close this window"
