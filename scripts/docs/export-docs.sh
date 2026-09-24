#!/usr/bin/env bash
# Exports the Docs space (dev-plan 10.5 step 7): the wiki pack into
# docs/site/docs-pack.zip, which is committed, and the static site into the
# directory given (default docs/site/), checked against Cloudflare's limits.
#
#   scripts/docs/export-docs.sh [site-directory]
#
# Signs in as SHOT_EMAIL / SHOT_PASSWORD from the gitignored .debug-credentials.
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

exec node scripts/docs/export-docs.mjs "$@"
