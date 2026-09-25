#!/usr/bin/env bash
# Restore the wiki from a logical backup (dev-plan 9.4).
#
#   docker compose exec backup /scripts/restore.sh                # newest cycle
#   docker compose exec backup /scripts/restore.sh db-2026....dump # one cycle
#   docker compose exec -e RESTORE_DRY_RUN=1 backup /scripts/restore.sh …
#
# The variable has to go through `exec -e`: set in front of `docker compose`
# it lands in the host's shell, never reaches the container, and the restore
# runs for real.
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
#   3. restore into a new database and verify it there, then have the
#      migrate service bring it up to date and grant the app its access
#      (14.4), and unpack the attachments beside the live ones
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
# The archive is unpacked here, beside the live files and on the same volume,
# before anything is switched (the review's DATA-03): a bad archive then stops
# the restore while nothing has changed, and putting the files in place after
# the switch is two renames. Neither folder is ever backed up (DATA-02).
STAGING="$UPLOADS/.restore-staging"
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
# Drops the restored copy, for every refusal before the switch.
drop_restored() { adm -c "DROP DATABASE IF EXISTS \"${NEW_DB}\";" >/dev/null 2>&1 || true; }
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
  log "DRY RUN: would take a safety backup, restore into ${NEW_DB}, verify it, have migrate update it,"
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

# Fails closed (the review's DATA-03, 2026-09-24): any error fails the restore,
# except the one kind known to be harmless here, a COMMENT ON EXTENSION that
# only the extension's owner may set, which a dump restored without owners
# always reports. Every error is matched by its command, so a real failure
# that happens to arrive with that warning still fails.
RESTORE_LOG="$(mktemp)"
if ! pg_restore --no-owner --no-privileges --dbname="$NEW_DB" "$DUMP" >"$RESTORE_LOG" 2>&1; then
  ERRORS="$(grep -c '^pg_restore: error:' "$RESTORE_LOG" || true)"
  HARMLESS="$(grep -c '^Command was: COMMENT ON EXTENSION' "$RESTORE_LOG" || true)"
  if [ "${ERRORS:-0}" -eq 0 ] || [ "${ERRORS:-0}" -ne "${HARMLESS:-0}" ]; then
    grep -E '^(pg_restore: error|Command was)' "$RESTORE_LOG" | head -20 >&2 || true
    rm -f "$RESTORE_LOG"
    adm -c "DROP DATABASE IF EXISTS \"${NEW_DB}\";" >/dev/null 2>&1 || true
    die "pg_restore failed (${ERRORS:-0} errors, shown above); nothing was changed"
  fi
  log "pg_restore reported ${ERRORS} harmless warning(s) about extension comments"
fi
rm -f "$RESTORE_LOG"

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

# --- 3b. The database's own maintenance, on the restored copy ---------------
#
# The copy was restored without privileges, so the app's role cannot read it,
# and a copy from an older Tesria lacks this version's migrations. Only the
# `migrate` service does that work (it holds the code for both), so it is
# asked, and waited for, while nothing has been replaced (the review's
# DATA-01, 2026-09-24). The request and the answer are the copy's database
# comment: this script and `migrate` both hold the owner's connection, and
# the signal lives on the very database it is about.

step "asking the migrate service to bring the restored copy up to date"
REQUEST_ID="${JOB_ID:-manual}"
adm -c "COMMENT ON DATABASE \"${NEW_DB}\" IS 'tesria-maintenance requested ${REQUEST_ID}';" >/dev/null \
  || { drop_restored; die "could not ask for the restored copy to be brought up to date; nothing was changed"; }
ANSWER=""
for _ in $(seq 1 200); do
  sleep 3
  ANSWER="$(adm -v db="$NEW_DB" <<'SQL' 2>/dev/null || true
SELECT coalesce(shobj_description(oid, 'pg_database'), '') FROM pg_database WHERE datname = :'db';
SQL
)"
  case "$ANSWER" in
    "tesria-maintenance done ${REQUEST_ID}") break ;;
    "tesria-maintenance failed ${REQUEST_ID}"*)
      drop_restored
      die "the restored copy could not be brought up to date (${ANSWER#tesria-maintenance failed ${REQUEST_ID}: }); nothing was changed" ;;
  esac
  if declare -f restore_canceled >/dev/null && restore_canceled; then
    drop_restored
    die "canceled before the wiki was changed; nothing was replaced"
  fi
done
if [ "$ANSWER" != "tesria-maintenance done ${REQUEST_ID}" ]; then
  drop_restored
  die "the migrate service did not answer within ten minutes (is it running? docker compose ps migrate); nothing was changed"
fi
adm -c "COMMENT ON DATABASE \"${NEW_DB}\" IS NULL;" >/dev/null 2>&1 || true
log "the restored copy is up to date and the app may read it"

# --- 4. The last moment anyone can call this off ---------------------------

if declare -f restore_canceled >/dev/null && restore_canceled; then
  adm -c "DROP DATABASE \"${NEW_DB}\";" >/dev/null
  die "canceled before the wiki was changed; nothing was replaced"
fi

# --- 4b. The attachments, unpacked and checked before anything changes -------

STAGED=0
if [ -f "$ARCHIVE" ] && [ -d "$UPLOADS" ]; then
  [ -w "$UPLOADS" ] || { drop_restored; die "${UPLOADS} is read-only in this container, so the attachments could not be restored; nothing was changed"; }
  # The staged copy sits beside the live files until the switch, so the
  # uploads volume needs room for both. Twice the archive is a generous
  # estimate of the unpacked size: attachments are mostly already compressed.
  NEED_FILES=$(( $(stat -c %s "$ARCHIVE" 2>/dev/null || echo 0) * 2 ))
  FREE_FILES="$(df -B1 --output=avail "$UPLOADS" 2>/dev/null | tail -1 | tr -d ' ')"
  if [ -n "${FREE_FILES:-}" ] && [ "$FREE_FILES" -lt "$NEED_FILES" ]; then
    drop_restored; die "not enough free space for the attachments: ${NEED_FILES} bytes are needed and ${FREE_FILES} are free; nothing was changed"
  fi
  step "unpacking the attachments beside the live ones, to check them"
  rm -rf "$STAGING" && mkdir -p "$STAGING" || { drop_restored; die "could not prepare ${STAGING}; nothing was changed"; }
  # An older archive may carry .pre-restore (the Undo copy at the time it was
  # taken); it is never unpacked, or it would replace the current Undo copy.
  if ! tar -xzf "$ARCHIVE" -C "$STAGING" --exclude='./.pre-restore' --exclude='./.restore-staging'; then
    rm -rf "$STAGING"; drop_restored
    die "the attachments archive $(basename "$ARCHIVE") could not be unpacked; nothing was changed"
  fi
  STAGED=1
else
  log "no uploads archive for this cycle; attachments will be left as they are"
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
# rather than half of each. The archive was unpacked and checked before the
# switch; if putting it in place fails now, the previous database and files
# go back and the restore fails (DATA-03): never a success with half a wiki.

# Nothing is deleted on the way back, only moved: FILES says how far the
# files got, so exactly the moves that happened are reversed. A move that
# stopped halfway leaves some files on each side, and both sides go home.
FILES=none
swap_back() {
  log "ERROR: $*; putting the previous wiki back"
  local others=( -mindepth 1 -maxdepth 1 ! -name '.pre-restore' ! -name '.restore-staging' )
  if [ "$FILES" = restored ]; then
    # The restored files that arrived go back to staging, where the rest are.
    find "$UPLOADS" "${others[@]}" -exec mv -t "$STAGING" {} + 2>/dev/null
  fi
  if [ "$FILES" = aside ] || [ "$FILES" = restored ]; then
    find "$KEPT_UPLOADS" -mindepth 1 -maxdepth 1 -exec mv -t "$UPLOADS" {} + 2>/dev/null \
      || log "ERROR: some previous attachments are still in ${KEPT_UPLOADS}"
  fi
  adm <<SQL || die "$* and the previous database could not be put back: ${KEPT_DB} holds it, rename it to ${LIVE_DB} by hand"
ALTER DATABASE "${LIVE_DB}" WITH ALLOW_CONNECTIONS false;
SELECT pg_terminate_backend(pid) FROM pg_stat_activity
 WHERE datname = '${LIVE_DB}' AND pid <> pg_backend_pid();
ALTER DATABASE "${LIVE_DB}" RENAME TO "${NEW_DB}";
ALTER DATABASE "${KEPT_DB}" RENAME TO "${LIVE_DB}";
ALTER DATABASE "${LIVE_DB}" WITH ALLOW_CONNECTIONS true;
SQL
  [ -n "$DIR" ] && rm -f "$DIR/committed"
  die "$*; the previous wiki was put back, and the restored copy is kept as ${NEW_DB} and ${STAGING}"
}

if [ "$STAGED" = 1 ]; then
  step "putting the attachments in place"
  # An Undo copy from an earlier restore is replaced here, as it always was:
  # the switch above already replaced its database.
  rm -rf "$KEPT_UPLOADS" && mkdir -p "$KEPT_UPLOADS" || swap_back "could not prepare ${KEPT_UPLOADS}"
  FILES=aside
  if [ -n "$(find "$UPLOADS" -mindepth 1 -maxdepth 1 ! -name '.pre-restore' ! -name '.restore-staging' -print -quit)" ]; then
    find "$UPLOADS" -mindepth 1 -maxdepth 1 ! -name '.pre-restore' ! -name '.restore-staging' -exec mv -t "$KEPT_UPLOADS" {} + \
      || swap_back "could not move the current attachments aside"
  fi
  FILES=restored
  if [ -n "$(find "$STAGING" -mindepth 1 -maxdepth 1 -print -quit)" ]; then
    find "$STAGING" -mindepth 1 -maxdepth 1 -exec mv -t "$UPLOADS" {} + \
      || swap_back "could not move the restored attachments into place"
  fi
  rm -rf "$STAGING"
  log "attachments restored from $(basename "$ARCHIVE")"
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

# No wait for migrations here any more: the copy was brought up to date
# before the switch (3b), so the carried rows fit it already.

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
