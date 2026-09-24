#!/usr/bin/env bash
# Writes the Support space (dev-plan 10.5 step 6): the pages of Tesria's
# support site, and the screenshots on them.
#
#   scripts/support/publish-support.sh                     every section
#   scripts/support/publish-support.sh getting-started     one section
#   scripts/support/publish-support.sh --no-shoot ...      reuse the last pictures
#
# Signs in as SHOT_EMAIL / SHOT_PASSWORD from the gitignored
# .debug-credentials, takes each section's screenshots from the Tesria Demo
# space with the harness in scripts/screenshots (a desktop and a phone run),
# and writes the pages. Safe to run again: only what differs changes.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$ROOT"

if [ ! -f .debug-credentials ]; then
  echo "No .debug-credentials at the repository root; see scripts/screenshots/README.md." >&2
  exit 1
fi
set -a
# shellcheck disable=SC1091
. ./.debug-credentials
set +a

CA="$(mktemp -t tesria-ca.XXXXXX)"
trap 'rm -f "$CA"' EXIT
if docker compose cp caddy:/data/caddy/pki/authorities/local/root.crt "$CA" >/dev/null 2>&1; then
  export NODE_EXTRA_CA_CERTS="$CA"
fi

exec node scripts/support/publish-support.mjs "$@"
