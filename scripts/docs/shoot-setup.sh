#!/usr/bin/env bash
# Screenshots of the first-run setup wizard, taken on the scratch instance.
#
#   scripts/scratch-instance.sh reset          an empty instance on :8099
#   scripts/docs/shoot-setup.sh before     the steps before an owner exists
#   (a person creates the owner at http://localhost:8099, saves the recovery
#    codes, presses Continue once and stops there)
#   scripts/docs/shoot-setup.sh after      every step after the account
#
# The owner is made by hand because making accounts is a person's job here,
# and because the recovery codes it shows are real. The "after" phase signs
# in as that owner with SCRATCH_EMAIL / SCRATCH_PASSWORD from the gitignored
# .debug-credentials. The scratch instance is thrown away afterwards, so
# those are throwaway too.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$ROOT"

if [ "${1:-}" = after ]; then
  set -a
  # shellcheck disable=SC1091
  [ -f .debug-credentials ] && . ./.debug-credentials
  set +a
  if [ -z "${SCRATCH_EMAIL:-}" ] || [ -z "${SCRATCH_PASSWORD:-}" ]; then
    echo "Add SCRATCH_EMAIL and SCRATCH_PASSWORD (the scratch owner) to .debug-credentials." >&2
    exit 1
  fi
fi

exec node scripts/docs/shoot-setup.mjs "$@"
