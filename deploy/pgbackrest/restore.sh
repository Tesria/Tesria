#!/usr/bin/env bash
# Disaster-recovery / point-in-time restore INTO THE LIVE DATA DIRECTORY.
# Run ONLY while Postgres is stopped (see docs/backup-recovery.md for the full
# procedure). Intended to be invoked in a one-off container that has the pgdata
# volume mounted read-write, e.g. the `db` service:
#
#   docker compose stop app pgbackrest db
#   docker compose run --rm --no-deps --entrypoint bash db /scripts/restore.sh ["<timestamp>"]
#   docker compose start db            # Postgres replays WAL (and promotes at the target)
#
# No argument  -> restore the latest backup and roll forward to the end of WAL.
# A timestamp  -> point-in-time recovery, e.g. "2026-07-23 14:32:00+00".
# --repo=2     -> restore from the cloud repository instead (runbook, "The
#                 machine is gone", section 4). The OFFSITE_CLOUD_* settings
#                 must be in .env.
set -euo pipefail

STANZA=main
PGDATA_PATH=/var/lib/postgresql/18/docker
PGROOT=/var/lib/postgresql
REPO_PATH=/var/lib/pgbackrest
TARGET_TIME=""
REPO=""
for arg in "$@"; do
  case "$arg" in
    --repo=1|--repo=2) REPO="${arg#--repo=}" ;;
    --repo=*) echo "usage: restore.sh [--repo=1|--repo=2] [\"<timestamp>\"]" >&2; exit 2 ;;
    *) TARGET_TIME="$arg" ;;
  esac
done
# pgBackRest takes "2026-07-23 14:32:00+00"; the admin page and the logs
# write ISO 8601 ("2026-07-23T14:32:00Z"), so take that as well.
if [[ "$TARGET_TIME" =~ ^[0-9]{4}-[0-9]{2}-[0-9]{2}T ]]; then
  TARGET_TIME="${TARGET_TIME/T/ }"
  [[ "$TARGET_TIME" == *Z ]] && TARGET_TIME="${TARGET_TIME%Z}+00"
fi

# The backup key and the cloud settings (T8-016). This runs in a one-off
# container started with --entrypoint bash, which skips the db entrypoint
# that normally writes both into pgBackRest's configuration, so without this
# pgBackRest could not decrypt its own repository: "FormatError: key/value
# found outside of section", then "unable to find backup set".
# shellcheck source=cipher.sh
. /scripts/cipher.sh
cipher_write_conf
# shellcheck source=offsite.sh
. /scripts/offsite.sh
if ! offsite_write_conf; then
  [ "$REPO" = 2 ] && { echo "[restore] ERROR: the OFFSITE_CLOUD_* settings in .env are incomplete; the cloud repository cannot be read"; exit 1; }
fi
if [ "$REPO" = 2 ] && [ -z "${OFFSITE_CLOUD_TYPE:-}" ]; then
  echo "[restore] ERROR: --repo=2 is the cloud repository, and no OFFSITE_CLOUD_* settings are in .env" >&2
  exit 1
fi
mkdir -p /var/log/pgbackrest /var/spool/pgbackrest
chown postgres:postgres /var/log/pgbackrest /var/spool/pgbackrest

pgbr() { gosu postgres pgbackrest --stanza="$STANZA" "$@"; }

# A database that was killed rather than stopped leaves postmaster.pid
# behind, and pgBackRest then refuses: "unable to restore while PostgreSQL is
# running" (T8-018). `docker compose stop` gives it ten seconds, and a
# database whose WAL archiving is failing can take longer than that to shut
# down. The socket is on a volume this container shares with `db`, so a
# database that is really running answers on it; one that does not answer is
# not running, and the file is stale.
if [ -f "$PGDATA_PATH/postmaster.pid" ]; then
  if gosu postgres pg_isready -h /var/run/postgresql -q -t 3; then
    echo "[restore] ERROR: PostgreSQL is running. Stop it first: docker compose stop db" >&2
    exit 1
  fi
  rc=0; gosu postgres pg_isready -h /var/run/postgresql -q -t 3 || rc=$?
  if [ "$rc" = 1 ]; then
    echo "[restore] ERROR: PostgreSQL is still starting or stopping. Wait, then: docker compose stop db" >&2
    exit 1
  fi
  echo "[restore] removing postmaster.pid left by a database that did not shut down cleanly"
  rm -f "$PGDATA_PATH/postmaster.pid"
fi

REPO_ARG=()
[ -n "$REPO" ] && REPO_ARG=(--repo="$REPO")

if [ -n "$TARGET_TIME" ]; then
  # Along the timeline of the backup pgBackRest picks for that time, not the
  # newest one: after an earlier point-in-time restore, the newest timeline
  # forked off before the moments that restore replaced (t8-R01).
  echo "[restore] point-in-time recovery to: $TARGET_TIME${REPO:+ (from repo$REPO)}"
  pgbr --pg1-path="$PGDATA_PATH" "${REPO_ARG[@]}" \
    --type=time --target="$TARGET_TIME" --target-timeline=current --target-action=promote --delta restore
else
  echo "[restore] restoring latest backup${REPO:+ from repo$REPO}, rolling forward to end of archived WAL"
  pgbr --pg1-path="$PGDATA_PATH" "${REPO_ARG[@]}" --delta restore
fi

# The local repository, after a restore from the cloud onto another machine
# (T8-018). The one here was made by the empty install the runbook starts
# from, for a different database cluster, and pgBackRest refuses to use it
# for the restored one ("backup and archive info files exist but do not
# match the database"), so no local backup would ever be taken again. Moved
# aside, not deleted: the pgbackrest service creates a new one when it
# starts, and the old one can be removed once the restored wiki is backed up.
if [ "$REPO" = 2 ]; then
  sysid() {
    pgbr --repo="$1" --log-level-console=off --output=json info 2>/dev/null \
      | tr ',{}' '\n\n\n' | sed -n 's/.*"system-id":\([0-9]*\).*/\1/p' | tail -1
  }
  local_id="$(sysid 1 || true)"
  cloud_id="$(sysid 2 || true)"
  if [ -n "$local_id" ] && [ -n "$cloud_id" ] && [ "$local_id" != "$cloud_id" ]; then
    stamp="$(date -u +%Y%m%dT%H%M%SZ)"
    for part in archive backup; do
      [ -d "$REPO_PATH/$part/$STANZA" ] && mv "$REPO_PATH/$part/$STANZA" "$REPO_PATH/$part/$STANZA.before-restore-$stamp"
    done
    echo "[restore] the local backup repository belonged to another database; moved aside as $STANZA.before-restore-$stamp"
    echo "[restore] (in the pgbackrest volume; delete it once the restored wiki has a new backup)"
  fi
fi

# The restored cluster keeps the passwords it had when it was backed up. On
# the same machine those are this machine's, but after a restore onto a new
# one they are the old machine's, and nothing could sign in (T8-018: migrate
# failed "password authentication failed", so the app never started). This
# file tells the db entrypoint to set the owner's password to this
# machine's once the database is up; migrate then sets the app's, as it
# always does.
touch "$PGROOT/.tesria-reset-owner-password"
chown postgres:postgres "$PGROOT/.tesria-reset-owner-password" 2>/dev/null || true

echo "[restore] done. Start the db service; Postgres will replay WAL from the archive."
echo "[restore] (PITR promotes automatically at the target via target-action=promote)."
echo "[restore] Then put back the attachment files the restored database refers to:"
echo "[restore]   docker compose exec backup /scripts/restore-uploads.sh"
