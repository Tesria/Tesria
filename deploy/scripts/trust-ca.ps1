<#
.SYNOPSIS
    Trust Tesria's local certificate authority (Windows).

.DESCRIPTION
    A Tesria server without a public domain makes its own certificate
    authority, so browsers warn about it. This script downloads that
    authority's certificate from the server and adds it to this computer's
    trusted certificates, so the warning stops: once per device.

    It only does so when the certificate's SHA-256 fingerprint matches the
    one you give it (the review's SEC-01, dev-plan 14.4). The certificate
    comes over plain HTTP, because nothing is trusted yet, so on a network
    someone else controls it could be theirs; and a trusted authority vouches
    for every website, not only Tesria. The fingerprint is how you know it is
    your server's. Get it from the server itself, never from the network:
      - on the server:  docker compose logs app | Select-String fingerprint
      - or in Tesria:   Administration, Settings, Certificate, opened on the
                        server computer (https://localhost) or through Tailscale

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
    wiki-server.local.

.PARAMETER Fingerprint
    The certificate's SHA-256 fingerprint, from the server. Separators and
    case do not matter.

.PARAMETER CurrentUser
    Trust it for your Windows account only; no administrator needed.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\trust-ca.ps1 wiki-server.local -Fingerprint AB:CD:...
#>

param(
    [Parameter(Position = 0, Mandatory = $true)]
    [string]$HostName,
    [Parameter(Mandatory = $true)]
    [string]$Fingerprint,
    [switch]$CurrentUser
)

$ErrorActionPreference = "Stop"

function Get-Hex([string]$value) { return ($value -replace '[^0-9A-Fa-f]', '').ToUpperInvariant() }

$expected = Get-Hex $Fingerprint
if ($expected.Length -ne 64) {
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
    Start-Process powershell.exe -Verb RunAs -ArgumentList @(
        "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", "`"$scriptPath`"",
        "`"$HostName`"", "-Fingerprint", "`"$expected`""
    )
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
if ($actual -ne $expected) {
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
Write-Host "==> The certificate matches the fingerprint: $($cert.Subject)"

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

Write-Host ""
Write-Host "==> Checking https://$HostName/ ..."
try {
    Invoke-WebRequest -Uri "https://$HostName/api/health" -UseBasicParsing -TimeoutSec 10 | Out-Null
    Write-Host "    Success: this computer now trusts $HostName."
}
catch {
    Write-Host "    Still failing. Quit and reopen your browser. If the warning stays, see the Tesria docs, Trusting the local certificate."
}

Write-Host ""
Read-Host "Press Enter to close this window"
