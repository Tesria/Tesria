#!/usr/bin/env bash
# Disaster-recovery / point-in-time restore INTO THE LIVE DATA DIRECTORY.
# Run ONLY while Postgres is stopped (see docs/backup-recovery.md for the full
# procedure). Intended to be invoked in a one-off container that has the pgdata
# volume mounted read-write, e.g. the `db` service:
#
#   docker compose stop app db pgbackrest
#   docker compose run --rm --no-deps --entrypoint bash db /scripts/restore.sh ["<timestamp>"]
#   docker compose start db            # Postgres replays WAL (and promotes at the target)
#
# No argument  -> restore the latest backup and roll forward to the end of WAL.
# A timestamp  -> point-in-time recovery, e.g. "2026-07-23 14:32:00+00".
set -euo pipefail

STANZA=main
PGDATA_PATH=/var/lib/postgresql/18/docker
TARGET_TIME="${1:-}"

if [ -n "$TARGET_TIME" ]; then
  echo "[restore] point-in-time recovery to: $TARGET_TIME"
  gosu postgres pgbackrest --stanza="$STANZA" --pg1-path="$PGDATA_PATH" \
    --type=time --target="$TARGET_TIME" --target-action=promote --delta restore
else
  echo "[restore] restoring latest backup, rolling forward to end of archived WAL"
  gosu postgres pgbackrest --stanza="$STANZA" --pg1-path="$PGDATA_PATH" --delta restore
fi

echo "[restore] done. Start the db service; Postgres will replay WAL from the archive."
echo "[restore] (PITR promotes automatically at the target via target-action=promote)."
