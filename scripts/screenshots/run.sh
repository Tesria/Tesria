#!/bin/sh
# Take screenshots of the running instance.
#
#   scripts/screenshots/run.sh spec.json [one-shot-name]
#
# The spec is a JSON file of shots (see shot.mjs for the fields). Output lands
# in ./shots next to the spec. Two environment variables are required:
#
#   SHOT_EMAIL / SHOT_PASSWORD   an account on this instance to sign in as
#
# Chromium comes from the PDF sidecar's image, which already carries one
# matched to its Playwright version -- nothing to install.
#
# It runs inside CADDY's network namespace, so https://tesria.localhost is
# this instance through the real proxy. Pointing it at the app container
# instead looks like it works and does not: the session cookie is Secure, so
# plain HTTP silently drops it and every shot comes out signed-out, and
# /collab is routed by Caddy, so the collaborative editor renders its toolbar
# over an empty document.
set -e
HERE="$(cd "$(dirname "$0")" && pwd)"
SPEC="$1"
[ -n "$SPEC" ] || { echo "usage: run.sh <spec.json> [shot-name]" >&2; exit 2; }
SPECDIR="$(cd "$(dirname "$SPEC")" && pwd)"
[ -n "$SHOT_EMAIL" ] && [ -n "$SHOT_PASSWORD" ] || { echo "set SHOT_EMAIL and SHOT_PASSWORD" >&2; exit 2; }
# Output goes beside the spec unless told otherwise; the onboarding set
# writes straight into the SPA's public/ directory (dev-plan 10.4).
OUTDIR="${SHOT_OUT:-$SPECDIR/shots}"
mkdir -p "$OUTDIR"
OUTDIR="$(cd "$OUTDIR" && pwd)"
docker run --rm --network "container:tesria-caddy-1" --shm-size 256mb \
  -e SHOT_BROWSER="${SHOT_BROWSER:-chromium}" -e SHOT_MOBILE="${SHOT_MOBILE:-}" -e SHOT_THEME="${SHOT_THEME:-}" -e SHOT_ACCENT="${SHOT_ACCENT:-}" \
  -e BASE="${SHOT_BASE:-https://tesria.localhost}" \
  -e EMAIL="$SHOT_EMAIL" -e PASSWORD="$SHOT_PASSWORD" \
  -v "$HERE/shot.mjs":/app/shot.mjs \
  -v "$SPECDIR":/work -v "$OUTDIR":/out -w /app \
  --entrypoint node tesria-pdf /app/shot.mjs "/work/$(basename "$SPEC")" "$2"
