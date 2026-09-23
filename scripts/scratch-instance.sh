#!/bin/sh
# A throwaway Tesria on http://localhost:8099, for testing first-run
# behavior that cannot be reached on an instance that already has accounts
# (dev-plan 10.2).
#
#   scripts/scratch-instance.sh up      build and start, empty database
#   scripts/scratch-instance.sh reset   destroy and start again, still empty
#   scripts/scratch-instance.sh down    destroy it, volumes included
#   scripts/scratch-instance.sh logs    follow the app log
#
# It runs under the compose project name "tesria-scratch" with its own
# volumes. `down -v` here cannot reach the real instance's data, which is the
# whole point of it being a separate project.
set -e
HERE="$(cd "$(dirname "$0")" && pwd)"
FILE="$HERE/../deploy/scratch-instance.yml"
compose() { docker compose -f "$FILE" "$@"; }

case "${1:-up}" in
  up)
    compose up -d --build
    printf 'waiting for the app'
    i=0
    until curl -fsS http://localhost:8099/api/instance >/dev/null 2>&1; do
      i=$((i + 1))
      [ "$i" -gt 60 ] && { echo; echo "gave up; try: $0 logs" >&2; exit 1; }
      printf '.'
      sleep 2
    done
    echo
    echo "ready: http://localhost:8099"
    curl -fsS http://localhost:8099/api/instance
    echo
    ;;
  down)
    compose down -v
    ;;
  reset)
    compose down -v
    "$0" up
    ;;
  logs)
    compose logs -f app
    ;;
  *)
    echo "usage: $0 [up|down|reset|logs]" >&2
    exit 2
    ;;
esac
