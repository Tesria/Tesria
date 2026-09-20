#!/bin/sh
# Record the onboarding media (dev-plan 10.4).
#
#   scripts/screenshots/onboarding.sh [shot-name]
#
# Runs onboarding.json in both themes and writes the result into
# src/web/public/onboarding/, which Vite copies into the build. The spec
# creates the DEMO space it films and deletes it again, so the clips never
# depend on this instance's real content and never leak it.
#
# Credentials come from .debug-credentials at the repo root (gitignored),
# which sets SHOT_EMAIL and SHOT_PASSWORD. The account needs spaces.create
# and spaces.delete (the spec builds and removes its own demo space) plus
# dashboard.view and backups.view (the two admin stills). An administrator
# or the owner has all four.
set -e
HERE="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(cd "$HERE/../.." && pwd)"
OUT="$ROOT/src/web/public/onboarding"

if [ -z "$SHOT_EMAIL" ] || [ -z "$SHOT_PASSWORD" ]; then
  if [ -f "$ROOT/.debug-credentials" ]; then
    . "$ROOT/.debug-credentials"
    export SHOT_EMAIL SHOT_PASSWORD
  fi
fi
[ -n "$SHOT_EMAIL" ] && [ -n "$SHOT_PASSWORD" ] || {
  echo "No credentials. Create .debug-credentials at the repo root with:" >&2
  echo "  SHOT_EMAIL=you@example.com" >&2
  echo "  SHOT_PASSWORD='…'" >&2
  exit 2
}

mkdir -p "$OUT"
for theme in light dark; do
  echo "--- $theme"
  SHOT_THEME="$theme" SHOT_OUT="$OUT" "$HERE/run.sh" "$HERE/onboarding.json" "$1"
done

# The budget is part of the deliverable: these ship inside the SPA and are
# fetched by people being onboarded, often on their first visit.
CLIP_MAX_KB=600
TOTAL_MAX_KB=8192
fail=0

for f in "$OUT"/*.webm; do
  [ -e "$f" ] || continue
  kb=$(( $(wc -c < "$f") / 1024 ))
  if [ "$kb" -gt "$CLIP_MAX_KB" ]; then
    echo "TOO BIG: $(basename "$f") is ${kb} KB (limit ${CLIP_MAX_KB} KB)" >&2
    fail=1
  fi
done

total=$(( $(cat "$OUT"/* 2>/dev/null | wc -c) / 1024 ))
echo "onboarding media: ${total} KB total"
if [ "$total" -gt "$TOTAL_MAX_KB" ]; then
  echo "TOO BIG: the set is ${total} KB (limit ${TOTAL_MAX_KB} KB)" >&2
  fail=1
fi

exit "$fail"
