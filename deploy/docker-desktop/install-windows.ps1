# Real visitor addresses under Docker Desktop on Windows: turns it on, or off.
#
# In PowerShell opened with "Run as administrator", in the Tesria folder:
#
#   powershell -ExecutionPolicy Bypass -File deploy\docker-desktop\install-windows.ps1
#   powershell -ExecutionPolicy Bypass -File deploy\docker-desktop\install-windows.ps1 -Uninstall
#
# Turning it on:
#   1. adds COMPOSE_FILE to .env, so every `docker compose` command in this
#      folder also uses docker-compose.real-addresses.yml;
#   2. moves Caddy to ports only this PC can reach (docker compose up -d caddy);
#   3. lets Node.js accept connections on ports 80 and 443 through Windows
#      Firewall, and adds a task that runs real-addresses.mjs whenever you
#      sign in to Windows. It takes ports 80 and 443 and hands each
#      connection to Caddy with the visitor's real address.
# Devices keep the same addresses and certificates. Administrator rights are
# needed only for the firewall rule and the task.
#Requires -RunAsAdministrator
param([switch]$Uninstall)
$ErrorActionPreference = 'Stop'

$Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
Set-Location $Root
$TaskName = 'Tesria real addresses'
$RuleName = 'Tesria real addresses (ports 80 and 443)'
# Windows separates compose files with a semicolon.
$Line = 'COMPOSE_FILE=docker-compose.yml;deploy/docker-desktop/docker-compose.real-addresses.yml'
$Comment = '# Real visitor addresses under Docker Desktop (deploy/docker-desktop)'
$EnvFile = Join-Path $Root '.env'
# Plain UTF-8: Windows PowerShell's own "UTF8" adds a byte-order mark, which
# Docker Compose would read as part of the first variable's name.
$Utf8 = New-Object System.Text.UTF8Encoding $false

if ($Uninstall) {
    Stop-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false -ErrorAction SilentlyContinue
    Get-NetFirewallRule -DisplayName $RuleName -ErrorAction SilentlyContinue | Remove-NetFirewallRule
    if (Test-Path $EnvFile) {
        $kept = [IO.File]::ReadAllLines($EnvFile) | Where-Object { $_ -ne $Line -and $_ -ne $Comment }
        [IO.File]::WriteAllLines($EnvFile, [string[]]$kept, $Utf8)
    }
    docker compose up -d caddy
    Write-Host 'Done: Caddy is back on ports 80 and 443, and the task and firewall rule are removed.'
    exit 0
}

if (-not (Test-Path $EnvFile)) { throw 'No .env here: set Tesria up first (README, Quick start).' }
$Node = (Get-Command node -ErrorAction SilentlyContinue).Source
if (-not $Node) { throw 'Node.js is needed: https://nodejs.org' }
$current = [IO.File]::ReadAllLines($EnvFile)
if (($current | Where-Object { $_ -like 'COMPOSE_FILE=*' -and $_ -ne $Line }).Count -gt 0) {
    throw '.env already sets COMPOSE_FILE to something else. Add deploy/docker-desktop/docker-compose.real-addresses.yml to it yourself, then run this again.'
}
if ($current -notcontains $Line) { [IO.File]::AppendAllLines($EnvFile, [string[]]@('', $Comment, $Line), $Utf8) }

# Caddy first, so ports 80 and 443 are free for the forwarder.
docker compose up -d caddy
if ($LASTEXITCODE -ne 0) { throw 'docker compose could not restart Caddy.' }

if (-not (Get-NetFirewallRule -DisplayName $RuleName -ErrorAction SilentlyContinue)) {
    New-NetFirewallRule -DisplayName $RuleName -Direction Inbound -Action Allow -Protocol TCP `
        -LocalPort 80, 443 -Program $Node | Out-Null
}

$script = Join-Path $Root 'deploy\docker-desktop\real-addresses.mjs'
$action = New-ScheduledTaskAction -Execute $Node -Argument "`"$script`"" -WorkingDirectory $Root
$trigger = New-ScheduledTaskTrigger -AtLogOn -User $env:USERNAME
# Restarted if it stops; never stopped for running long, never started on battery only.
$settings = New-ScheduledTaskSettingsSet -RestartCount 999 -RestartInterval (New-TimeSpan -Minutes 1) `
    -ExecutionTimeLimit ([TimeSpan]::Zero) -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries
Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Settings $settings `
    -Description 'Hands visitors to Tesria with their real addresses (deploy/docker-desktop).' -Force | Out-Null
Start-ScheduledTask -TaskName $TaskName

for ($i = 0; $i -lt 10; $i++) {
    Start-Sleep -Seconds 2
    try {
        # PowerShell 5 has no switch to skip certificate checks; curl.exe ships with Windows 10 and later.
        $code = & curl.exe -sk -o NUL -w '%{http_code}' https://localhost/api/health
        if ($code -eq '200') {
            Write-Host 'Done: Tesria answers on https://localhost, and records each visitor''s real address.'
            Write-Host 'Undo with: deploy\docker-desktop\install-windows.ps1 -Uninstall'
            exit 0
        }
    } catch { }
}
Write-Warning 'The task is installed, but Tesria did not answer on https://localhost yet. Check it in Task Scheduler.'
exit 1
