#!/usr/bin/env bash
# Real visitor addresses under Docker Desktop on a Mac: turns it on, or off.
#
#   deploy/docker-desktop/install-macos.sh              turn it on
#   deploy/docker-desktop/install-macos.sh --uninstall  put everything back
#
# Turning it on:
#   1. adds COMPOSE_FILE to .env, so every `docker compose` command in this
#      folder also uses docker-compose.real-addresses.yml;
#   2. moves Caddy to ports only this Mac can reach (docker compose up -d caddy);
#   3. adds a login item (a launchd agent) that runs real-addresses.mjs, which
#      takes ports 80 and 443 and hands each connection to Caddy with the
#      visitor's real address.
# Devices keep the same addresses and certificates. If macOS asks whether
# node may accept incoming connections, choose Allow.
set -euo pipefail
umask 077  # .env holds secrets; nothing this writes should be readable by others

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$ROOT"

LABEL=com.tesria.real-addresses
PLIST="$HOME/Library/LaunchAgents/$LABEL.plist"
LOG="$HOME/Library/Logs/tesria-real-addresses.log"
LINE="COMPOSE_FILE=docker-compose.yml:deploy/docker-desktop/docker-compose.real-addresses.yml"
COMMENT="# Real visitor addresses under Docker Desktop (deploy/docker-desktop)"

say() { printf '%s\n' "$*"; }

if [ "${1:-}" = "--uninstall" ]; then
  launchctl bootout "gui/$(id -u)/$LABEL" 2>/dev/null || true
  rm -f "$PLIST"
  if [ -f .env ] && grep -qxF "$LINE" .env; then
    # cat into the file, not mv, so .env keeps its owner-only permissions.
    grep -vxF "$LINE" .env | grep -vxF "$COMMENT" > .env.tmp && cat .env.tmp > .env && rm -f .env.tmp
  fi
  docker compose up -d caddy
  say "Done: Caddy is back on ports 80 and 443, and the login item is removed."
  exit 0
fi

[ -f .env ] || { say "No .env here: set Tesria up first (README, Quick start)." >&2; exit 1; }
NODE="$(command -v node || true)"
[ -n "$NODE" ] || { say "Node.js is needed (https://nodejs.org, or: brew install node)." >&2; exit 1; }
if grep -q '^COMPOSE_FILE=' .env && ! grep -qxF "$LINE" .env; then
  say ".env already sets COMPOSE_FILE to something else. Add deploy/docker-desktop/docker-compose.real-addresses.yml to it yourself, then run this again." >&2
  exit 1
fi

grep -qxF "$LINE" .env || printf '\n%s\n%s\n' "$COMMENT" "$LINE" >> .env

# Caddy first, so ports 80 and 443 are free for the forwarder.
docker compose up -d caddy

mkdir -p "$(dirname "$PLIST")" "$(dirname "$LOG")"
cat > "$PLIST" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>Label</key><string>$LABEL</string>
  <key>ProgramArguments</key>
  <array>
    <string>$NODE</string>
    <string>$ROOT/deploy/docker-desktop/real-addresses.mjs</string>
  </array>
  <key>RunAtLoad</key><true/>
  <key>KeepAlive</key><true/>
  <key>StandardOutPath</key><string>$LOG</string>
  <key>StandardErrorPath</key><string>$LOG</string>
</dict>
</plist>
EOF

launchctl bootout "gui/$(id -u)/$LABEL" 2>/dev/null || true
launchctl bootstrap "gui/$(id -u)" "$PLIST"

for _ in 1 2 3 4 5 6 7 8 9 10; do
  if curl -sk -o /dev/null -w '%{http_code}' https://localhost/api/health 2>/dev/null | grep -q 200; then
    say "Done: Tesria answers on https://localhost, and records each visitor's real address."
    say "Undo with: deploy/docker-desktop/install-macos.sh --uninstall"
    exit 0
  fi
  sleep 2
done
say "The forwarder is installed, but Tesria did not answer on https://localhost yet. Its log: $LOG" >&2
exit 1
