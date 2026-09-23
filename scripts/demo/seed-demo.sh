#!/usr/bin/env bash
# Seeds the Tesria Demo space (dev-plan 10.5 step 5): the pages the support
# site's screenshots and clips are taken from.
#
#   scripts/demo/seed-demo.sh
#
# Signs in as two fictional people from the gitignored .debug-credentials:
# SHOT_EMAIL / SHOT_PASSWORD (Alex Rivera, an administrator) and
# SHOT2_EMAIL / SHOT2_PASSWORD (Sam Okafor), so that history, comments and
# contributors show two real people. Safe to run again: pages are found by
# title and only rewritten when their content differs.
#
# BASE overrides the address (default https://localhost). The local
# certificate is trusted by taking Caddy's root from the running stack, so
# nothing turns certificate checking off.
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

exec node scripts/demo/seed-demo.mjs "$@"
