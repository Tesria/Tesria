<#
.SYNOPSIS
    Trust ConfluenceClone's local certificate authority (Windows).

.DESCRIPTION
    Every ConfluenceClone deployment that isn't using a real domain + Let's
    Encrypt serves HTTPS using a self-signed certificate authority that Caddy
    generates for itself. That's why your browser warns you the first time
    you visit. This script downloads that CA's root certificate from a
    running ConfluenceClone server and installs it into the Windows machine
    trust store, so every browser and HTTP client on this machine trusts it
    from then on -- no more warnings, on this device, for this server (or
    any other hostname/IP it answers on).

    This is a ONE-TIME, PER-DEVICE step. Run it once on every Windows machine
    you want to access the wiki from without warnings. It does NOT affect
    Firefox (which keeps its own certificate store -- see
    docs/tls-and-lan-access.md) or mobile devices (which need a manual
    profile install, also covered there).

    Re-running this script is safe -- it replaces any previously trusted
    copy of this same CA rather than adding a duplicate.

    IMPORTANT: if the server's `caddy_data` Docker volume is ever deleted
    (e.g. `docker compose down -v`), Caddy generates a brand-new CA with a
    new private key, and everyone will need to re-run this script -- the old
    trust doesn't carry over.

.PARAMETER HostName
    Where to reach your ConfluenceClone server: a hostname, NOT a raw IP --
    see docs/tls-and-lan-access.md for why. Defaults to "localhost".

.EXAMPLE
    .\trust-ca.ps1
    .\trust-ca.ps1 wiki-server.local
    .\trust-ca.ps1 mymac.local
#>

param(
    [Parameter(Position = 0)]
    [string]$HostName = "localhost"
)

$ErrorActionPreference = "Stop"

function Test-Admin {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

# Installing into the machine (not just the user) trust store needs admin --
# relaunch elevated if we're not already, passing the HostName through.
if (-not (Test-Admin)) {
    Write-Host "==> Re-launching with Administrator privileges (installing into the machine trust store needs it)..."
    $scriptPath = $MyInvocation.MyCommand.Path
    Start-Process powershell.exe -Verb RunAs -ArgumentList @(
        "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", "`"$scriptPath`"", "`"$HostName`""
    )
    exit
}

$certUrl = "http://$HostName/ca.crt"
$tmpCert = Join-Path $env:TEMP "confluenceclone-ca-$([guid]::NewGuid()).crt"

Write-Host "==> Fetching CA certificate from $certUrl ..."
try {
    Invoke-WebRequest -Uri $certUrl -OutFile $tmpCert -UseBasicParsing -TimeoutSec 10 | Out-Null
}
catch {
    Write-Error "Couldn't download the CA certificate from $certUrl. Make sure ConfluenceClone is running and reachable at that address, and that nothing is blocking port 80 (this fetch deliberately uses plain HTTP, since nothing is trusted yet)."
    Read-Host "Press Enter to close"
    exit 1
}

try {
    $cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($tmpCert)
}
catch {
    Write-Error "The file downloaded from $certUrl doesn't look like a valid certificate."
    Remove-Item $tmpCert -ErrorAction SilentlyContinue
    Read-Host "Press Enter to close"
    exit 1
}

Write-Host "==> Got certificate: $($cert.Subject)"
Write-Host "    SHA-1 thumbprint: $($cert.Thumbprint)"

Write-Host "==> Installing into the Windows machine Root trust store..."
Import-Certificate -FilePath $tmpCert -CertStoreLocation Cert:\LocalMachine\Root | Out-Null

Remove-Item $tmpCert -ErrorAction SilentlyContinue

Write-Host "==> Done. Restart your browser (fully quit and reopen, not just the tab)."
Write-Host "    Chrome and Edge read the Windows machine trust store, so both will trust it now."
Write-Host "    Firefox keeps its own certificate store and needs a separate manual import"
Write-Host "    -- see docs/tls-and-lan-access.md."

Write-Host ""
Write-Host "==> Verifying: refetching https://$HostName/ (should now succeed with no warning)..."
try {
    Invoke-WebRequest -Uri "https://$HostName/api/health" -UseBasicParsing -TimeoutSec 10 | Out-Null
    Write-Host "    Success -- this machine now trusts $HostName."
}
catch {
    Write-Host "    Still failing. Try fully restarting your browser, and see docs/tls-and-lan-access.md if the warning persists."
}

Write-Host ""
Read-Host "Press Enter to close this window"
