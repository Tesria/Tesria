#!/usr/bin/env bash
# End-to-end point-in-time-recovery self-test (PLAN §5). Proves the whole PITR
# chain works: it writes a marker, records a target time, writes a SECOND marker
# after it, then restores the repository to the target time into a throwaway
# instance and checks that only the pre-target marker survived. Touches a
# temporary table in the live database and cleans it up; starts a temporary
# Postgres on port 5599 and stops it. Read-only w.r.t. real application data.
#
#   docker compose exec pgbackrest bash /scripts/pitr-selftest.sh
set -euo pipefail

STANZA=main
# The pgbackrest service is given the database's owner and name as PGUSER and
# PGDATABASE (docker-compose.yml); POSTGRES_* is accepted too, for running this
# elsewhere. No guessed default: a wrong name would fail halfway through.
DB="${PGDATABASE:-${POSTGRES_DB:?set PGDATABASE or POSTGRES_DB to the database name}}"
USER_="${PGUSER:-${POSTGRES_USER:?set PGUSER or POSTGRES_USER to the database owner}}"
SOCK=/var/run/postgresql
TARGET=/tmp/pitr-selftest
PORT=5599

live() { gosu postgres psql -h "$SOCK" -U "$USER_" -d "$DB" -tAqc "$1"; }
tmp()  { gosu postgres psql -h "$SOCK" -p "$PORT" -U "$USER_" -d "$DB" -tAqc "$1"; }

cleanup() {
  gosu postgres pg_ctl -D "$TARGET" -w -m immediate stop >/dev/null 2>&1 || true
  live "DROP TABLE IF EXISTS pitr_selftest;" >/dev/null 2>&1 || true
  rm -rf "$TARGET"
}
trap cleanup EXIT

echo "[pitr] writing marker A (before target) ..."
live "CREATE TABLE IF NOT EXISTS pitr_selftest(id int primary key, note text);" >/dev/null
live "INSERT INTO pitr_selftest VALUES (1,'before-target') ON CONFLICT DO NOTHING;" >/dev/null

sleep 1
TARGET_TIME="$(live "SELECT now();")"
echo "[pitr] recovery target time = $TARGET_TIME"
sleep 1

echo "[pitr] writing marker B (after target) ..."
live "INSERT INTO pitr_selftest VALUES (2,'after-target') ON CONFLICT DO NOTHING;" >/dev/null
live "SELECT pg_switch_wal();" >/dev/null   # force the segment to archive
sleep 3

echo "[pitr] restoring repository to the target time into $TARGET ..."
rm -rf "$TARGET"; mkdir -p "$TARGET"; chown postgres:postgres "$TARGET"
gosu postgres pgbackrest --stanza="$STANZA" --pg1-path="$TARGET" \
  --type=time --target="$TARGET_TIME" --target-action=promote restore

echo "[pitr] starting throwaway instance on port $PORT to replay to target ..."
gosu postgres pg_ctl -D "$TARGET" -w -t 60 \
  -o "-p $PORT -c archive_mode=off -c listen_addresses='' -c unix_socket_directories=$SOCK" \
  start

# Recovery accepts read-only connections at the backup's consistency point,
# well before the target. Wait until it finishes replaying to the target and
# promotes (pg_is_in_recovery() -> false) before checking the data.
echo "[pitr] waiting for recovery to reach the target and promote ..."
promoted=no
for _ in $(seq 1 60); do
  state="$(tmp "SELECT pg_is_in_recovery();" 2>/dev/null || echo error)"
  if [[ "$state" == "f" ]]; then promoted=yes; break; fi
  sleep 1
done
[[ "$promoted" == "yes" ]] || { echo "[pitr] FAIL: recovery did not promote in time"; exit 1; }

BEFORE="$(tmp "SELECT count(*) FROM pitr_selftest WHERE note='before-target';")"
AFTER="$(tmp "SELECT count(*) FROM pitr_selftest WHERE note='after-target';")"
echo "[pitr] recovered instance: before-target rows=$BEFORE  after-target rows=$AFTER"

if [[ "$BEFORE" == "1" && "$AFTER" == "0" ]]; then
  echo "[pitr] PASS: recovered exactly to the target time (marker B correctly excluded)."
else
  echo "[pitr] FAIL: expected before=1 after=0, got before=$BEFORE after=$AFTER"
  exit 1
fi
