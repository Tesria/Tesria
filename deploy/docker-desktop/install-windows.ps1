# Real visitor addresses under Docker Desktop on Windows: turns it on, or off.
#
# In PowerShell opened with "Run as administrator", in the Tesria folder:
#
#   powershell -ExecutionPolicy Bypass -File deploy\docker-desktop\install-windows.ps1
#   powershell -ExecutionPolicy Bypass -File deploy\docker-desktop\install-windows.ps1 -Uninstall
#
# Turning it on:
#   1. adds COMPOSE_FILE to .env, so every `docker compose` command in this
#      folder also uses docker-compose.real-addresses.yml, and the loopback
#      ports Caddy moves to;
#   2. moves Caddy to those ports, which only this PC can reach
#      (docker compose up -d caddy);
#   3. lets Node.js accept connections on this Tesria's ports (80 and 443
#      unless TESRIA_HTTP_PORT and TESRIA_HTTPS_PORT say otherwise) through
#      Windows Firewall, and adds a task that runs real-addresses.mjs in the
#      background from startup, with no window. It takes those ports and
#      hands each connection to Caddy with the visitor's real address.
# Devices keep the same addresses and certificates. Administrator rights are
# needed only for the firewall rule and the task.
#
# Each Tesria on the computer is set up on its own (WIN-007): run this in
# each folder. A second Tesria (its own COMPOSE_PROJECT_NAME and ports, see
# the docs page Installing with Docker Compose) gets its own task, firewall
# rule and loopback ports, and -Uninstall removes only this folder's.
#Requires -RunAsAdministrator
param([switch]$Uninstall)
$ErrorActionPreference = 'Stop'

$Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
Set-Location $Root
$EnvFile = Join-Path $Root '.env'
# Plain UTF-8: Windows PowerShell's own "UTF8" adds a byte-order mark, which
# Docker Compose would read as part of the first variable's name.
$Utf8 = New-Object System.Text.UTF8Encoding $false

# A setting as Docker Compose reads it: this shell's environment first, then
# the last line in .env that sets it, without quotes or a trailing comment.
function Get-Setting([string]$Name, [string]$Default) {
    $value = [Environment]::GetEnvironmentVariable($Name)
    if (-not $value -and (Test-Path $EnvFile)) {
        foreach ($l in [IO.File]::ReadAllLines($EnvFile)) {
            if ($l -match ('^\s*' + [regex]::Escape($Name) + '\s*=(.*)$')) { $value = $Matches[1] }
        }
    }
    if ($value) {
        $value = $value.Trim()
        if ($value -match '^"(.*)"$' -or $value -match "^'(.*)'$") { $value = $Matches[1] }
        else { $value = ($value -replace '\s+#.*$', '').Trim() }
    }
    if ($value) { return $value }
    return $Default
}

# A port setting: the number, or the number after the last colon, since
# Compose also takes an address in front of it (127.0.0.1:8443).
function Get-Port([string]$Name, [int]$Default) {
    $text = Get-Setting $Name ''
    if (-not $text) { return $Default }
    if ($text -match '(?:^|:)(\d{1,5})$' -and [int]$Matches[1] -ge 1 -and [int]$Matches[1] -le 65535) { return [int]$Matches[1] }
    throw "$Name is not a port: $text"
}

# Which Tesria this folder is, and its ports (0.8.2's second install).
$Project = (Get-Setting 'COMPOSE_PROJECT_NAME' 'tesria').ToLowerInvariant()
$HttpPort = Get-Port 'TESRIA_HTTP_PORT' 80
$HttpsPort = Get-Port 'TESRIA_HTTPS_PORT' 443
# Where Caddy waits for the forwarder, on this PC only: 18080 and 18443 for
# a Tesria on 80 and 443, as before; 20000 above its own ports otherwise, so
# two Tesrias never ask for the same ones.
if ($HttpPort -eq 80 -and $HttpsPort -eq 443) { $LoopHttp = 18080; $LoopHttps = 18443 }
else { $LoopHttp = $HttpPort + 20000; $LoopHttps = $HttpsPort + 20000 }
$LoopHttp = Get-Port 'TESRIA_LOOPBACK_HTTP_PORT' $LoopHttp
$LoopHttps = Get-Port 'TESRIA_LOOPBACK_HTTPS_PORT' $LoopHttps
if ($LoopHttp -gt 65535 -or $LoopHttps -gt 65535) {
    throw "No loopback ports for Caddy above $HttpPort and $HttpsPort. Set TESRIA_LOOPBACK_HTTP_PORT and TESRIA_LOOPBACK_HTTPS_PORT in .env to two free ports, then run this again."
}

# The first Tesria keeps the names it always had, so an earlier install is
# found and replaced; any other is named after its project.
$Suffix = if ($Project -eq 'tesria') { '' } else { " ($Project)" }
$TaskName = "Tesria real addresses$Suffix"
$RuleName = "Tesria real addresses$Suffix"
$OldRuleName = 'Tesria real addresses (ports 80 and 443)'
# Windows separates compose files with a semicolon.
$Line = 'COMPOSE_FILE=docker-compose.yml;deploy/docker-desktop/docker-compose.real-addresses.yml'
$Comment = '# Real visitor addresses under Docker Desktop (deploy/docker-desktop)'
function Test-OurLine([string]$l) {
    return $l -eq $Line -or $l -eq $Comment -or $l -like 'TESRIA_LOOPBACK_HTTP_PORT=*' -or $l -like 'TESRIA_LOOPBACK_HTTPS_PORT=*'
}

# The folder another Tesria's task with this name runs in, if it is not
# this one and is still there. Before 0.8.3 every install used the same name.
function Get-OtherFolder {
    $task = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
    if (-not $task) { return $null }
    $dir = @($task.Actions)[0].WorkingDirectory
    if ($dir -and $dir.TrimEnd('\') -ne $Root.TrimEnd('\') -and (Test-Path (Join-Path $dir 'docker-compose.yml'))) { return $dir }
    return $null
}

# Set up before 0.8.3, a second Tesria's task has the first one's name:
# removed when it runs this folder.
function Remove-EarlierTask {
    if ($Project -eq 'tesria') { return }
    $old = Get-ScheduledTask -TaskName 'Tesria real addresses' -ErrorAction SilentlyContinue
    if ($old -and "$(@($old.Actions)[0].WorkingDirectory)".TrimEnd('\') -eq $Root.TrimEnd('\')) {
        Stop-ScheduledTask -TaskName 'Tesria real addresses' -ErrorAction SilentlyContinue
        Unregister-ScheduledTask -TaskName 'Tesria real addresses' -Confirm:$false -ErrorAction SilentlyContinue
        Get-NetFirewallRule -DisplayName $OldRuleName -ErrorAction SilentlyContinue | Remove-NetFirewallRule
    }
}

# .env without this script's lines, and without blank lines at its end.
function Get-KeptLines {
    $kept = New-Object System.Collections.Generic.List[string]
    if (Test-Path $EnvFile) {
        foreach ($l in [IO.File]::ReadAllLines($EnvFile)) { if (-not (Test-OurLine $l)) { $kept.Add($l) } }
    }
    while ($kept.Count -gt 0 -and $kept[$kept.Count - 1].Trim() -eq '') { $kept.RemoveAt($kept.Count - 1) }
    return ,$kept
}

if ($Uninstall) {
    $other = Get-OtherFolder
    if ($other) {
        Write-Warning "The task '$TaskName' runs the Tesria in $other, not this one, so it and its firewall rule were left alone."
    }
    else {
        Stop-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
        Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false -ErrorAction SilentlyContinue
        Get-NetFirewallRule -DisplayName $RuleName -ErrorAction SilentlyContinue | Remove-NetFirewallRule
        if ($Project -eq 'tesria') { Get-NetFirewallRule -DisplayName $OldRuleName -ErrorAction SilentlyContinue | Remove-NetFirewallRule }
    }
    Remove-EarlierTask
    if (Test-Path $EnvFile) { [IO.File]::WriteAllLines($EnvFile, (Get-KeptLines).ToArray(), $Utf8) }
    docker compose up -d caddy
    if ($LASTEXITCODE -ne 0) { throw 'docker compose could not restart Caddy: run docker compose up -d in this folder.' }
    Write-Host "Done: Caddy is back on ports $HttpPort and $HttpsPort, and this Tesria's task and firewall rule are removed."
    exit 0
}

if (-not (Test-Path (Join-Path $Root 'docker-compose.yml'))) { throw 'No docker-compose.yml in the Tesria folder: is this script in its deploy/docker-desktop folder?' }
# Since 0.8.0 a .env is optional (dev-plan 25.1); make one for this line.
if (-not (Test-Path $EnvFile)) { [IO.File]::WriteAllText($EnvFile, '', $Utf8) }
$Node = (Get-Command node -ErrorAction SilentlyContinue).Source
if (-not $Node) { throw 'Node.js is needed: https://nodejs.org' }
$current = [IO.File]::ReadAllLines($EnvFile)
if (($current | Where-Object { $_ -like 'COMPOSE_FILE=*' -and $_ -ne $Line }).Count -gt 0) {
    throw '.env already sets COMPOSE_FILE to something else. Add deploy/docker-desktop/docker-compose.real-addresses.yml to it yourself, then run this again.'
}
$other = Get-OtherFolder
if ($other) {
    throw "Task Scheduler already has '$TaskName', for the Tesria in $other. Nothing was changed. Run this script with -Uninstall in that folder first, or give this Tesria its own COMPOSE_PROJECT_NAME (see the docs page Installing with Docker Compose)."
}
# Nothing changes unless Caddy's new ports are free: taken, for example by
# another Tesria's Caddy, Caddy could not start there, and this Tesria would
# be left down (WIN-007). Already set up, they are this Tesria's own.
if ($current -notcontains $Line) {
    foreach ($port in @($LoopHttp, $LoopHttps)) {
        if (Get-NetTCPConnection -State Listen -LocalPort $port -ErrorAction SilentlyContinue) {
            throw "Port $port on this PC is already in use, perhaps by another Tesria's real addresses. Nothing was changed. If this is a second Tesria, give it its own TESRIA_HTTP_PORT and TESRIA_HTTPS_PORT in .env (see the docs page Installing with Docker Compose), or set TESRIA_LOOPBACK_HTTP_PORT and TESRIA_LOOPBACK_HTTPS_PORT to two free ports."
        }
    }
}

$before = [IO.File]::ReadAllBytes($EnvFile)
$lines = Get-KeptLines
if ($lines.Count -gt 0) { $lines.Add('') }
$lines.Add($Comment)
$lines.Add($Line)
$lines.Add("TESRIA_LOOPBACK_HTTP_PORT=$LoopHttp")
$lines.Add("TESRIA_LOOPBACK_HTTPS_PORT=$LoopHttps")
[IO.File]::WriteAllLines($EnvFile, $lines.ToArray(), $Utf8)

Remove-EarlierTask
# A version already running (an earlier install) holds this Tesria's ports.
$hadTask = [bool](Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue)
Stop-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue

# Put .env back, and Caddy and the task with it, rather than leave this
# Tesria down.
function Undo-Move([string]$Why) {
    [IO.File]::WriteAllBytes($EnvFile, $before)
    docker compose up -d caddy
    if ($hadTask) { Start-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue }
    throw "$Why Nothing was changed: .env is as it was, and Caddy is back where it was."
}

# Caddy first, so this Tesria's ports are free for the forwarder.
docker compose up -d caddy
if ($LASTEXITCODE -ne 0) { Undo-Move "docker compose could not move Caddy to ports $LoopHttp and $LoopHttps." }
# Still taken once Caddy has left them, they belong to something else, such
# as another Tesria when this one's ports come from a
# docker-compose.override.yml rather than TESRIA_HTTP_PORT and
# TESRIA_HTTPS_PORT: the forwarder could not start, and https://localhost
# would answer from that other one.
$busy = @($HttpPort, $HttpsPort)
for ($i = 0; $i -lt 5 -and $busy.Count -gt 0; $i++) {
    Start-Sleep -Seconds 1
    $busy = @($HttpPort, $HttpsPort | Where-Object { Get-NetTCPConnection -State Listen -LocalPort $_ -ErrorAction SilentlyContinue })
}
if ($busy.Count -gt 0) {
    Undo-Move "Port $($busy -join ' and ') is used by something other than this Tesria, so the forwarder could not listen there. If it is another Tesria, give this one its own TESRIA_HTTP_PORT and TESRIA_HTTPS_PORT in .env (see the docs page Installing with Docker Compose)."
}

Get-NetFirewallRule -DisplayName $RuleName -ErrorAction SilentlyContinue | Remove-NetFirewallRule
if ($Project -eq 'tesria') { Get-NetFirewallRule -DisplayName $OldRuleName -ErrorAction SilentlyContinue | Remove-NetFirewallRule }
New-NetFirewallRule -DisplayName $RuleName -Direction Inbound -Action Allow -Protocol TCP `
    -LocalPort $HttpPort, $HttpsPort -Program $Node | Out-Null

$script = Join-Path $Root 'deploy\docker-desktop\real-addresses.mjs'
$arguments = "`"$script`" LISTEN_HTTP=$HttpPort LISTEN_HTTPS=$HttpsPort TARGET_HTTP=$LoopHttp TARGET_HTTPS=$LoopHttps"
$action = New-ScheduledTaskAction -Execute $Node -Argument $arguments -WorkingDirectory $Root
# At startup, as this user but without signing in ("S4U"): no console window
# to close by mistake (the first version ran at sign-in in a visible window,
# found testing, 2026-09-24), and no password stored.
$trigger = New-ScheduledTaskTrigger -AtStartup
$principal = New-ScheduledTaskPrincipal -UserId "$env:USERDOMAIN\$env:USERNAME" -LogonType S4U -RunLevel Limited
# Restarted if it stops; never stopped for running long, never started on battery only.
$settings = New-ScheduledTaskSettingsSet -RestartCount 999 -RestartInterval (New-TimeSpan -Minutes 1) `
    -ExecutionTimeLimit ([TimeSpan]::Zero) -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries
Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Settings $settings `
    -Principal $principal -Force `
    -Description "Hands visitors to the Tesria in $Root with their real addresses (deploy/docker-desktop)." | Out-Null
Start-ScheduledTask -TaskName $TaskName

$Url = if ($HttpsPort -eq 443) { 'https://localhost' } else { "https://localhost:$HttpsPort" }
for ($i = 0; $i -lt 10; $i++) {
    Start-Sleep -Seconds 2
    try {
        # PowerShell 5 has no switch to skip certificate checks; curl.exe ships with Windows 10 and later.
        $code = & curl.exe -sk -o NUL -w '%{http_code}' "$Url/api/health"
        if ($code -eq '200') {
            Write-Host "Done: Tesria answers on $Url, and records each visitor's real address."
            Write-Host 'Undo with: deploy\docker-desktop\install-windows.ps1 -Uninstall'
            exit 0
        }
    } catch { }
}
Write-Warning "The task '$TaskName' is installed, but Tesria did not answer on $Url yet. Is Docker Desktop running? Check the task in Task Scheduler."
exit 1
