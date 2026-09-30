#!/usr/bin/env bash
# Trust Tesria's local certificate authority (macOS and Linux).
#
# A Tesria server without a public domain makes its own certificate
# authority, so browsers warn about it. This script downloads that
# authority's certificate from the server and adds it to this computer's
# trusted certificates, so the warning stops: once per device.
#
# It prints the certificate's SHA-256 fingerprint and trusts it, the way SSH
# trusts a server the first time. On a network you run yourself that is
# enough. On one someone else controls, the certificate, which comes over
# plain HTTP because nothing is trusted yet, could be theirs, and a trusted
# authority vouches for every website: there, give --fingerprint, and it
# trusts nothing unless the certificate matches (the review's SEC-01, made
# optional by the owner on 2026-09-27). Get the fingerprint from the server:
#   - on the server:  docker compose logs app | grep -i fingerprint
#   - or in Tesria:   Administration, Settings, Certificate
#
# Usage:
#   bash trust-ca.sh [--fingerprint <SHA-256 fingerprint>] <address>
#
#   address   What you type into the browser to open Tesria, without
#             https://, such as wiki-server.local. For a Tesria on ports of
#             its own, its plain HTTP port goes with it, such as
#             localhost:8080: the /trust page fills this in.
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
	echo "Usage: bash trust-ca.sh [--fingerprint <SHA-256 fingerprint>] <address>" >&2
	echo "  Optional: the fingerprint is on the server: docker compose logs app | grep -i fingerprint" >&2
	exit 2
}

HOST=""
EXPECTED=""
# Whether --fingerprint was given at all, apart from its value: a value with
# no hexadecimal digits in it (an empty shell variable, say) is refused
# rather than taken as "no fingerprint".
FINGERPRINT_GIVEN=0
while [ $# -gt 0 ]; do
	case "$1" in
	--fingerprint) [ $# -ge 2 ] || usage; EXPECTED="$2"; FINGERPRINT_GIVEN=1; shift 2 ;;
	--fingerprint=*) EXPECTED="${1#*=}"; FINGERPRINT_GIVEN=1; shift ;;
	-h | --help) usage ;;
	-*) echo "Unknown option: $1" >&2; usage ;;
	*) [ -z "$HOST" ] || usage; HOST="$1"; shift ;;
	esac
done
[ -n "$HOST" ] || { echo "ERROR: give the address you open Tesria at." >&2; usage; }

# Compared as 64 hex digits, whatever the separators and case.
normalize() { printf '%s' "$1" | tr -cd '0-9A-Fa-f' | tr 'a-f' 'A-F'; }
EXPECTED="$(normalize "$EXPECTED")"
if [ "$FINGERPRINT_GIVEN" -eq 1 ] && [ "${#EXPECTED}" -ne 64 ]; then
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
if [ "$FINGERPRINT_GIVEN" -eq 0 ]; then
	echo "==> Its SHA-256 fingerprint: $(printf '%s' "$ACTUAL" | sed 's/../&:/g; s/:$//')"
	echo "    Not checked, as no --fingerprint was given. On a network you do not"
	echo "    control, compare it with the one on the server before relying on it."
elif [ "$ACTUAL" != "$EXPECTED" ]; then
	echo "ERROR: the certificate from ${HOST} does NOT match the fingerprint you gave." >&2
	echo "       Nothing was trusted." >&2
	echo "       Check you copied the fingerprint from this server, and the address is" >&2
	echo "       right. If both are, something on the network may be answering in the" >&2
	echo "       server's place: do not trust it, and try from another network." >&2
	exit 1
fi
[ "$FINGERPRINT_GIVEN" -eq 0 ] || echo "==> The certificate matches the fingerprint: ${SUBJECT}"

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
# The address is the plain HTTP one, with its port on a Tesria that has
# ports of its own (localhost:8080), so its HTTPS address is found where
# that sends a browser: https://localhost:8443 (WIN-003). Only an address
# on the same host is believed; with no port given, HTTPS is on 443.
case "$HOST" in
\[*\]:*) NAME="${HOST%:*}" ;;
\[*\]) NAME="$HOST" ;;
*:*) NAME="${HOST%:*}" ;;
*) NAME="$HOST" ;;
esac
lower() { printf '%s' "$1" | tr 'A-Z' 'a-z'; }
REDIRECT="$(curl -sS -o /dev/null --max-time 10 -w '%{redirect_url}' "http://${HOST}/api/health" 2>/dev/null || true)"
ORIGIN="$(printf '%s' "$REDIRECT" | sed -E 's|^(https://[^/?#]+).*|\1|')"
case "$(lower "$ORIGIN")" in
"https://$(lower "$NAME")" | "https://$(lower "$NAME"):"[0-9]*) ;;
*)
	if [ "$NAME" = "$HOST" ]; then ORIGIN="https://${HOST}"; else ORIGIN=""; fi
	;;
esac
if [ -z "$ORIGIN" ]; then
	echo "==> Could not tell which HTTPS address ${HOST} sends browsers to, so this was"
	echo "    not checked. Open Tesria in your browser: it should show no warning."
else
	echo "==> Verifying: fetching ${ORIGIN}/ (should now succeed with no -k)..."
	if curl -fsS --max-time 10 "${ORIGIN}/api/health" >/dev/null 2>&1; then
		echo "    Success: this machine now trusts ${ORIGIN#https://}."
	else
		echo "    Still failing. Fully quit and reopen your browser. If the warning stays,"
		echo "    see the Tesria docs, Trusting the local certificate."
	fi
fi
