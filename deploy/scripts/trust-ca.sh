#!/usr/bin/env bash
# Trust Tesria's local certificate authority (macOS and Linux).
#
# A Tesria server without a public domain makes its own certificate
# authority, so browsers warn about it. This script downloads that
# authority's certificate from the server and adds it to this computer's
# trusted certificates, so the warning stops: once per device.
#
# It only does so when the certificate's SHA-256 fingerprint matches the one
# you give it (the review's SEC-01, dev-plan 14.4). The certificate comes
# over plain HTTP, because nothing is trusted yet, so on a network someone
# else controls it could be theirs; and a trusted authority vouches for every
# website, not only Tesria. The fingerprint is how you know it is your
# server's. Get it from the server itself, never from the network:
#   - on the server:  docker compose logs app | grep -i fingerprint
#   - or in Tesria:   Administration, Settings, Certificate, opened on the
#                     server computer (https://localhost) or through Tailscale
#
# Usage:
#   bash trust-ca.sh --fingerprint <SHA-256 fingerprint> <address>
#
#   address   What you type into the browser to open Tesria, without
#             https://, such as wiki-server.local.
#
# Get this script from Tesria's GitHub releases, or from the tesria-deploy.zip
# you installed from, not from the server: a script fetched over the same
# plain HTTP could have been changed on the way.
#
# Re-running it is safe. If the server is reinstalled from scratch (its
# caddy_data volume deleted), it makes a new authority with a new
# fingerprint, and every device needs this again.

set -euo pipefail

usage() {
	echo "Usage: bash trust-ca.sh --fingerprint <SHA-256 fingerprint> <address>" >&2
	echo "  The fingerprint is on the server: docker compose logs app | grep -i fingerprint" >&2
	exit 2
}

HOST=""
EXPECTED=""
while [ $# -gt 0 ]; do
	case "$1" in
	--fingerprint) [ $# -ge 2 ] || usage; EXPECTED="$2"; shift 2 ;;
	--fingerprint=*) EXPECTED="${1#*=}"; shift ;;
	-h | --help) usage ;;
	-*) echo "Unknown option: $1" >&2; usage ;;
	*) [ -z "$HOST" ] || usage; HOST="$1"; shift ;;
	esac
done
[ -n "$HOST" ] || { echo "ERROR: give the address you open Tesria at." >&2; usage; }
[ -n "$EXPECTED" ] || { echo "ERROR: give the certificate's fingerprint; without it this script will not trust anything." >&2; usage; }

# Compared as 64 hex digits, whatever the separators and case.
normalize() { printf '%s' "$1" | tr -cd '0-9A-Fa-f' | tr 'a-f' 'A-F'; }
EXPECTED="$(normalize "$EXPECTED")"
if [ "${#EXPECTED}" -ne 64 ]; then
	echo "ERROR: that is not a SHA-256 fingerprint (64 hexadecimal digits, usually in pairs like AB:CD:...)." >&2
	exit 2
fi

TMP_CERT="$(mktemp -t tesria-ca.XXXXXX).crt"
trap 'rm -f "$TMP_CERT"' EXIT

echo "==> Fetching the certificate from http://${HOST}/ca.crt ..."
if ! curl -fsS --max-time 10 "http://${HOST}/ca.crt" -o "$TMP_CERT"; then
	echo "ERROR: couldn't download the certificate from http://${HOST}/ca.crt" >&2
	echo "       Make sure Tesria is running and reachable at that address, and that" >&2
	echo "       nothing blocks port 80." >&2
	exit 1
fi

if ! openssl x509 -in "$TMP_CERT" -noout -subject >/dev/null 2>&1; then
	echo "ERROR: what http://${HOST}/ca.crt sent is not a certificate. Nothing was trusted." >&2
	exit 1
fi

SUBJECT="$(openssl x509 -in "$TMP_CERT" -noout -subject | sed 's/^subject= *//')"
ACTUAL="$(normalize "$(openssl x509 -in "$TMP_CERT" -noout -fingerprint -sha256 | cut -d= -f2)")"
if [ "$ACTUAL" != "$EXPECTED" ]; then
	echo "ERROR: the certificate from ${HOST} does NOT match the fingerprint you gave." >&2
	echo "       Nothing was trusted." >&2
	echo "       Check you copied the fingerprint from this server, and the address is" >&2
	echo "       right. If both are, something on the network may be answering in the" >&2
	echo "       server's place: do not trust it, and try from another network." >&2
	exit 1
fi
echo "==> The certificate matches the fingerprint: ${SUBJECT}"

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
	echo "    Firefox needs a separate step: see the Tesria docs, Trusting the local certificate."
	;;
*)
	echo "ERROR: unsupported OS '$OS'. This script handles macOS and Linux only:" >&2
	echo "       for Windows, use trust-ca.ps1 instead." >&2
	exit 1
	;;
esac

echo
echo "==> Verifying: refetching https://${HOST}/ (should now succeed with no -k)..."
if curl -fsS --max-time 10 "https://${HOST}/api/health" >/dev/null 2>&1; then
	echo "    Success: this machine now trusts ${HOST}."
else
	echo "    Still failing. Fully quit and reopen your browser. If the warning stays,"
	echo "    see the Tesria docs, Trusting the local certificate."
fi
