#!/usr/bin/env bash
# Restore the wiki from a logical backup (dev-plan 9.4).
#
#   docker compose exec backup /scripts/restore.sh                # newest cycle
#   docker compose exec backup /scripts/restore.sh db-2026....dump # one cycle
#   RESTORE_DRY_RUN=1 docker compose exec backup /scripts/restore.sh …
#
# **This restores beside, then swaps.** It does not drop the live database,
# which is what it used to do and what made a restore from the admin page
# impossible: the job row recording the restore lives in the database being
# dropped, so the restore destroyed its own record and the page watching it
# read the restored past as the present.
#
# Instead the dump is restored into <db>_restore while the wiki stays up and
# readable, verified there, and swapped in with two renames. The database it
# replaces is renamed to <db>_pre_restore rather than dropped: that is the
# undo, instant and complete, and it is why a restore is recoverable at all.
#
# The order matters and is not arbitrary:
#   1. preconditions, each refusing with a reason, while nothing is touched
#   2. a safety backup, always, because this is the moment somebody most
#      wants a backup they did not have to remember to take
#   3. restore into a new database and verify it there
#   4. the last cancel check
#   5. the point of no return: the swap, in one psql session
#   6. the uploads, by rename, so a crash leaves both copies
#   7. reconciliation: the backup history carried across, the job row
#      written into the database that now exists
#
# For point-in-time recovery (an exact moment rather than a backup), see
# deploy/pgbackrest/restore.sh and docs/backup-recovery.md.
set -uo pipefail

# The sidecar's shared helpers, when this is running inside the backup
# container. **This script is a child process, not a sourced file**, so
# without this the restore_* functions below simply would not exist and the
# carry-across, the cancel check and the completion record would all be
# quietly skipped. That is exactly what happened the first time this ran for
# real: the wiki restored correctly and its own bookkeeping vanished.
#
# Guarded, because the runbook also runs this by hand in containers that may
# not have it; every call below is still guarded individually so a missing
# helper degrades rather than fails.
AGENT="${AGENT:-logical}"
# shellcheck source=common.sh
[ -r /opt/tesria/common.sh ] && . /opt/tesria/common.sh

BACKUP_DIR="${BACKUP_DIR:-/backups}"
UPLOADS="${WIKI_PATH:-/data/uploads}"
RESTORE_DRY_RUN="${RESTORE_DRY_RUN:-0}"
LIVE_DB="${PGDATABASE}"
NEW_DB="${LIVE_DB}_restore"
KEPT_DB="${LIVE_DB}_pre_restore"
KEPT_UPLOADS="$UPLOADS/.pre-restore"
JOB_ID="${RESTORE_JOB_ID:-}"
DIR="${RESTORE_DIR:-}"

# Defined after sourcing common.sh so these win: the prefix should say what
# is happening, not which agent is doing it.
log() { echo "[restore $(date -u +%FT%TZ)] $*"; }
die() { echo "[restore] ERROR: $*" >&2; exit 1; }
dry() { [ "$RESTORE_DRY_RUN" = 1 ]; }
step() {
  log "$*"
  [ -n "$DIR" ] && printf '%s %s\n' "$(date -u +%FT%TZ)" "$*" >>"$DIR/phases"
  return 0
}

# psql against the maintenance database, which is the only place from which
# the live database can be renamed.
adm() { psql -X -q -t -A -v ON_ERROR_STOP=1 --dbname=postgres "$@"; }
# psql against a named database.
in_db() { local d="$1"; shift; psql -X -q -t -A -v ON_ERROR_STOP=1 --dbname="$d" "$@"; }

# --- 1. Which backup, and can it be restored at all? -----------------------

ARG="${1:-}"
if [ -n "$ARG" ]; then
  DUMP="${BACKUP_DIR}/$(basename "$ARG")"
else
  # shellcheck disable=SC2012  # controlled db-<timestamp>.dump names, mtime order is intended
  DUMP="$(ls -1t "${BACKUP_DIR}"/db-*.dump 2>/dev/null | head -n1 || true)"
fi
[ -n "${DUMP:-}" ] && [ -f "$DUMP" ] || die "no backup file found (looked for ${ARG:-newest db-*.dump})"

STAMP="$(basename "$DUMP" | sed -n 's/^db-\(.*\)\.dump$/\1/p')"
ARCHIVE="${BACKUP_DIR}/uploads-${STAMP}.tar.gz"

step "checking ${DUMP}"
pg_restore --list "$DUMP" >/dev/null 2>&1 || die "$DUMP is not a readable custom-format dump"
[ -f "$ARCHIVE" ] || log "WARNING: no uploads archive for this cycle; attachments will be left as they are"

# A restored database is bigger than the dump it came from, and the disk has
# to hold both it and the live one at the same time. Four times is
# deliberately conservative: refusing here is cheap, and running out of disk
# halfway through a restore is not.
DUMP_BYTES="$(stat -c %s "$DUMP" 2>/dev/null || echo 0)"
NEEDED=$(( DUMP_BYTES * 4 ))
[ -f "$ARCHIVE" ] && NEEDED=$(( NEEDED + $(stat -c %s "$ARCHIVE" 2>/dev/null || echo 0) * 2 ))
FREE="$(df -B1 --output=avail "$BACKUP_DIR" 2>/dev/null | tail -1 | tr -d ' ')"
if [ -n "${FREE:-}" ] && [ "$FREE" -lt "$NEEDED" ]; then
  die "not enough free space: ${NEEDED} bytes are needed and ${FREE} are free"
fi

if dry; then
  log "DRY RUN: would restore ${DUMP}${ARCHIVE:+ and $ARCHIVE}"
  log "DRY RUN: would take a safety backup, restore into ${NEW_DB}, verify it,"
  log "DRY RUN: rename ${LIVE_DB} to ${KEPT_DB} and ${NEW_DB} to ${LIVE_DB},"
  log "DRY RUN: move the uploads aside into ${KEPT_UPLOADS} and extract the archive."
  exit 0
fi

# --- 2. The safety backup, which is never optional -------------------------

step "taking a safety backup before anything is replaced"
SAFETY_STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
if BACKUP_STAMP="$SAFETY_STAMP" /scripts/backup.sh && BACKUP_STAMP="$SAFETY_STAMP" /scripts/backup-files.sh; then
  log "safety backup db-${SAFETY_STAMP}.dump taken"
  [ -n "$DIR" ] && printf '%s' "$SAFETY_STAMP" > "$DIR/safety"
else
  die "the safety backup failed; nothing has been changed"
fi

# The backup history describes the disk, not the wiki: without carrying it
# across, a dump from last week makes the page forget every backup since,
# including the safety one taken a moment ago.
if [ -n "$DIR" ] && declare -f restore_export_carry >/dev/null; then
  step "noting which backups exist, to carry across the restore"
  restore_export_carry "$DIR"
fi

# --- 3. Restore beside the live database -----------------------------------

step "restoring into ${NEW_DB} (the wiki stays up and readable)"
adm -c "DROP DATABASE IF EXISTS \"${NEW_DB}\";" >/dev/null \
  || die "could not clear a previous ${NEW_DB}"
adm -c "CREATE DATABASE \"${NEW_DB}\";" >/dev/null || die "could not create ${NEW_DB}"

if ! pg_restore --no-owner --no-privileges --dbname="$NEW_DB" "$DUMP" 2>&1; then
  # pg_restore warns about things that do not matter (an extension comment it
  # may not set); what matters is whether the tables arrived, which the
  # verification below decides. A hard failure still leaves nothing behind.
  log "pg_restore reported problems; checking whether the result is usable"
fi

step "checking the restored copy before it replaces anything"
COUNTS="$(in_db "$NEW_DB" <<'SQL' 2>/dev/null || true
SELECT (SELECT count(*) FROM "Pages") || '|' || (SELECT count(*) FROM "Users")
    || '|' || (SELECT count(*) FROM "__EFMigrationsHistory");
SQL
)"
IFS='|' read -r R_PAGES R_USERS R_MIGRATIONS <<<"${COUNTS:-||}"
[ -n "${R_USERS:-}" ] || { adm -c "DROP DATABASE \"${NEW_DB}\";" >/dev/null; die "the restored copy has no readable tables; nothing was changed"; }
[ "${R_USERS:-0}" -gt 0 ] || { adm -c "DROP DATABASE \"${NEW_DB}\";" >/dev/null; die "the restored copy has no accounts in it; nothing was changed"; }

# A dump from a NEWER Tesria carries migrations this build has never run, and
# starting the app against it would mean running an older application on a
# newer schema. That is a downgrade: a restart cannot repair it, so it is
# refused rather than attempted.
#
# Compared by count rather than by name. Naming them would mean reading one
# database from inside another, which needs dblink, which is not installed
# and is not worth installing for this: migrations only ever accumulate, so
# more of them is exactly the case being refused.
LIVE_MIGRATIONS="$(in_db "$LIVE_DB" -c 'SELECT count(*) FROM "__EFMigrationsHistory";' 2>/dev/null | tr -d ' ')"
LIVE_MIGRATIONS="${LIVE_MIGRATIONS:-0}"
if [ "${R_MIGRATIONS:-0}" -gt "$LIVE_MIGRATIONS" ]; then
  adm -c "DROP DATABASE \"${NEW_DB}\";" >/dev/null
  die "that backup was taken by a newer version of Tesria (${R_MIGRATIONS} migrations against ${LIVE_MIGRATIONS}); upgrade first"
fi
log "restored copy looks sound: ${R_PAGES} pages, ${R_USERS} accounts, ${R_MIGRATIONS} migrations"

# --- 4. The last moment anyone can call this off ---------------------------

if declare -f restore_cancelled >/dev/null && restore_cancelled; then
  adm -c "DROP DATABASE \"${NEW_DB}\";" >/dev/null
  die "cancelled before the wiki was changed; nothing was replaced"
fi

# --- 5. The point of no return ---------------------------------------------
#
# One session, and connections are REFUSED before they are terminated: the
# app reconnects within milliseconds, so terminating alone would race an
# automatic reconnect and the rename would fail with "database is being
# accessed by other users".

step "switching over"
[ -n "$DIR" ] && date -u +%FT%TZ > "$DIR/committed"
adm <<SQL || die "the switch failed; the wiki is unchanged and ${NEW_DB} holds the restored copy"
ALTER DATABASE "${LIVE_DB}" WITH ALLOW_CONNECTIONS false;
SELECT pg_terminate_backend(pid) FROM pg_stat_activity
 WHERE datname = '${LIVE_DB}' AND pid <> pg_backend_pid();
DROP DATABASE IF EXISTS "${KEPT_DB}";
ALTER DATABASE "${LIVE_DB}" RENAME TO "${KEPT_DB}";
ALTER DATABASE "${NEW_DB}" RENAME TO "${LIVE_DB}";
SQL
log "the wiki is now the restored copy; the previous one is kept as ${KEPT_DB}"

# --- 6. The uploads ---------------------------------------------------------
#
# Renames within one volume, so this is fast and a crash leaves both copies
# rather than half of each.

if [ -f "$ARCHIVE" ] && [ -d "$UPLOADS" ]; then
  step "putting the attachments back"
  if [ -w "$UPLOADS" ]; then
    rm -rf "$KEPT_UPLOADS"
    mkdir -p "$KEPT_UPLOADS"
    find "$UPLOADS" -mindepth 1 -maxdepth 1 ! -name '.pre-restore' -exec mv -t "$KEPT_UPLOADS" {} + 2>/dev/null
    if tar -xzf "$ARCHIVE" -C "$UPLOADS"; then
      log "attachments restored from $(basename "$ARCHIVE")"
    else
      log "ERROR: could not extract ${ARCHIVE}; the previous attachments are in ${KEPT_UPLOADS}"
    fi
  else
    log "ERROR: ${UPLOADS} is read-only in this container; attachments were NOT restored"
    log "ERROR: the database was restored. Extract ${ARCHIVE} into the uploads volume by hand."
  fi
else
  log "no uploads archive for this cycle; attachments left as they are"
fi

# --- 7. Reconciliation ------------------------------------------------------
#
# From here everything is written into the database that now exists.

step "finishing up"
# A safety dump taken while the wiki was in maintenance carries the
# maintenance flags. Restoring such a dump one day must not leave the wiki
# read-only for ever, so they are cleared unconditionally.
in_db "$LIVE_DB" >/dev/null 2>&1 <<'SQL' || true
UPDATE "SiteSettings"
   SET "RestoreJobId" = NULL, "RestoreStartedAt" = NULL, "RestoreCancelRequestedAt" = NULL;
SQL

# Wait for the app to restart and migrate: a dump older than the current build
# is missing columns the carried-across rows need. Ten minutes at most, then
# carry on regardless, because a wiki that is up matters more than a tidy
# history.
if [ "${R_MIGRATIONS:-0}" -lt "${LIVE_MIGRATIONS:-0}" ]; then
  step "waiting for the application to bring the schema up to date"
  for _ in $(seq 1 120); do
    NOW_MIGRATIONS="$(in_db "$LIVE_DB" -c 'SELECT count(*) FROM "__EFMigrationsHistory";' 2>/dev/null || echo 0)"
    [ "${NOW_MIGRATIONS:-0}" -ge "${LIVE_MIGRATIONS:-0}" ] && break
    sleep 5
  done
  log "schema is at ${NOW_MIGRATIONS:-?} migrations"
fi

if [ -n "$DIR" ] && declare -f restore_import_carry >/dev/null; then
  restore_import_carry "$DIR"
fi

KEPT_JSON=""
if [ -n "$JOB_ID" ]; then
  KEPT_BYTES="$(adm -c "SELECT pg_database_size('${KEPT_DB}');" 2>/dev/null | tr -d ' ')"
  KEPT_JSON="$(printf '{"jobId":"%s","mode":"logical","restoredAt":"%s","database":"%s","uploads":"%s","restoredFrom":"%s","databaseBytes":%s,"uploadsBytes":null,"removedAt":null}' \
    "$JOB_ID" "$(date -u +%FT%TZ)" "$KEPT_DB" "$KEPT_UPLOADS" "db-${STAMP}.dump" "${KEPT_BYTES:-null}")"
  if declare -f restore_record_done >/dev/null; then
    PGDATABASE="$LIVE_DB" restore_record_done "$JOB_ID" "db-${STAMP}.dump" "$KEPT_JSON"
  fi
  [ -n "$DIR" ] && printf '{"restoredFrom":"db-%s.dump","safetyBackup":"db-%s.dump","pages":%s,"accounts":%s,"keptCopy":"%s"}' \
    "$STAMP" "$SAFETY_STAMP" "${R_PAGES:-0}" "${R_USERS:-0}" "$KEPT_DB" > "$DIR/result"
fi

log "restore complete from ${DUMP}"
log "the previous wiki is kept as ${KEPT_DB}; Undo on the backups page puts it back"
