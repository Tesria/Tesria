#!/usr/bin/env bash
# Trust Tesria's local certificate authority (macOS and Linux).
#
# Every Tesria deployment that isn't using a real domain + Let's
# Encrypt serves HTTPS using a self-signed certificate authority that Caddy
# generates for itself. That's why your browser warns you the first time you
# visit. This script downloads that CA's root certificate from a running
# Tesria server and installs it into your system's trust store, so
# every browser and HTTP client on this machine trusts it from then on — no
# more warnings, on this device, for this server (or any other hostname/IP it
# answers on).
#
# This is a ONE-TIME, PER-DEVICE step. Run it once on every Mac/Linux machine
# you want to access the wiki from without warnings. It does NOT affect
# Firefox (which keeps its own certificate store — see docs/tls-and-lan-access.md)
# or mobile devices (which need a manual profile install, also covered there).
#
# Usage:
#   ./trust-ca.sh [host]
#
#   host   Where to reach your Tesria server: a hostname (NOT a raw
#          IP -- see docs/tls-and-lan-access.md for why). Defaults to
#          "localhost". Examples:
#            ./trust-ca.sh
#            ./trust-ca.sh wiki-server.local
#            ./trust-ca.sh mymac.local
#
# Re-running this script is safe — it replaces any previously trusted copy of
# this same CA rather than adding a duplicate.
#
# IMPORTANT: if the server's `caddy_data` Docker volume is ever deleted (e.g.
# `docker compose down -v`), Caddy generates a brand-new CA with a new private
# key, and everyone will need to re-run this script — the old trust doesn't
# carry over.

set -euo pipefail

HOST="${1:-localhost}"
CERT_NAME="Tesria Local CA (${HOST})"
TMP_CERT="$(mktemp -t tesria-ca.XXXXXX).crt"
trap 'rm -f "$TMP_CERT"' EXIT

echo "==> Fetching CA certificate from http://${HOST}/ca.crt ..."
if ! curl -fsS --max-time 10 "http://${HOST}/ca.crt" -o "$TMP_CERT"; then
	echo "ERROR: couldn't download the CA certificate from http://${HOST}/ca.crt" >&2
	echo "       Make sure Tesria is running and reachable at that address," >&2
	echo "       and that nothing is blocking port 80 (this fetch deliberately uses" >&2
	echo "       plain HTTP, since nothing is trusted yet)." >&2
	exit 1
fi

if ! openssl x509 -in "$TMP_CERT" -noout -subject >/dev/null 2>&1; then
	echo "ERROR: the file downloaded from http://${HOST}/ca.crt doesn't look like a" >&2
	echo "       valid certificate. Got:" >&2
	head -c 300 "$TMP_CERT" >&2
	echo >&2
	exit 1
fi

SUBJECT="$(openssl x509 -in "$TMP_CERT" -noout -subject | sed 's/^subject= *//')"
FINGERPRINT="$(openssl x509 -in "$TMP_CERT" -noout -fingerprint -sha256 | cut -d= -f2)"
echo "==> Got certificate: ${SUBJECT}"
echo "    SHA-256 fingerprint: ${FINGERPRINT}"

OS="$(uname -s)"

case "$OS" in
Darwin)
	echo "==> Installing into the macOS System keychain (you'll be asked for your password)..."
	sudo security add-trusted-cert -d -r trustRoot -k /Library/Keychains/System.keychain "$TMP_CERT"
	echo "==> Done. Restart your browser (fully quit and reopen, not just the tab)."
	echo "    Safari and Chrome read the System keychain, so both will trust it now."
	;;
Linux)
	if command -v update-ca-certificates >/dev/null 2>&1; then
		# Debian/Ubuntu and derivatives.
		SAFE_NAME="tesria-local-ca-$(echo "$HOST" | tr -c 'a-zA-Z0-9._-' '-')"
		DEST="/usr/local/share/ca-certificates/${SAFE_NAME}.crt"
		echo "==> Installing to ${DEST} (you'll be asked for your password)..."
		sudo cp "$TMP_CERT" "$DEST"
		sudo update-ca-certificates
	elif command -v update-ca-trust >/dev/null 2>&1; then
		# Fedora/RHEL/CentOS and derivatives.
		SAFE_NAME="tesria-local-ca-$(echo "$HOST" | tr -c 'a-zA-Z0-9._-' '-')"
		DEST="/etc/pki/ca-trust/source/anchors/${SAFE_NAME}.pem"
		echo "==> Installing to ${DEST} (you'll be asked for your password)..."
		sudo cp "$TMP_CERT" "$DEST"
		sudo update-ca-trust
	else
		echo "ERROR: neither update-ca-certificates nor update-ca-trust was found." >&2
		echo "       Install the CA manually for your distribution using:" >&2
		echo "         $TMP_CERT" >&2
		exit 1
	fi
	echo "==> Done. Restart your browser. Chrome/Chromium read the system store directly;"
	echo "    Firefox needs a separate manual import (see docs/tls-and-lan-access.md)."
	;;
*)
	echo "ERROR: unsupported OS '$OS'. This script handles macOS and Linux only —" >&2
	echo "       for Windows, use trust-ca.ps1 instead." >&2
	exit 1
	;;
esac

echo
echo "==> Verifying: refetching https://${HOST}/ (should now succeed with no -k)..."
if curl -fsS --max-time 10 "https://${HOST}/api/health" >/dev/null 2>&1; then
	echo "    Success — this machine now trusts ${HOST}."
else
	echo "    Still failing without -k. Try fully restarting your browser, and see"
	echo "    docs/tls-and-lan-access.md if the warning persists."
fi
