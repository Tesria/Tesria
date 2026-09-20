#!/usr/bin/env bash
# Restore-test a physical backup into a throwaway directory and sanity check
# it, so we know the backups are actually recoverable (PLAN §5: "untested
# backups are not backups"). Safe to run anytime; touches nothing live.
#
#   docker compose exec pgbackrest bash /scripts/verify.sh              # newest
#   docker compose exec pgbackrest bash /scripts/verify.sh --set=LABEL  # one backup
#
# The admin page's Test restore runs this with --set (dev-plan 9.1).
set -euo pipefail

STANZA=main
TARGET=/tmp/pgbackrest-verify
SET_ARG=()
WHAT="the latest backup"
case "${1:-}" in
  --set=?*) SET_ARG=("$1"); WHAT="backup ${1#--set=}" ;;
  "") ;;
  *) echo "usage: verify.sh [--set=LABEL]" >&2; exit 2 ;;
esac

# The restored copy is the size of the database; do not leave it behind.
trap 'rm -rf "$TARGET"' EXIT

echo "[verify] restoring $WHAT to $TARGET ..."
rm -rf "$TARGET"
mkdir -p "$TARGET"
chown postgres:postgres "$TARGET"

# --type=none restores the files without configuring recovery, enough to prove
# the backup is complete and readable. With no --set, the latest backup is used.
gosu postgres pgbackrest --stanza="$STANZA" --pg1-path="$TARGET" --type=none "${SET_ARG[@]}" restore

echo "[verify] checking the restored cluster ..."
test -f "$TARGET/PG_VERSION" || { echo "[verify] FAIL: PG_VERSION missing"; exit 1; }
gosu postgres pg_controldata "$TARGET" | grep -E "Database cluster state|Latest checkpoint location"

echo "[verify] OK: $WHAT restored to a valid-looking cluster."
