#!/usr/bin/env bash
# The `backup` sidecar (dev-plan 9.1): a pg_dump and an archive of the uploads
# volume, sharing one timestamp (a "cycle"). The loop, the schedule, the job
# queue and retention are in common.sh; this file is what is specific to
# logical backups. The scripts it calls still work by hand:
#   docker compose exec backup /scripts/backup.sh
#   docker compose exec backup /scripts/verify-backup.sh
AGENT=logical
VOLUME=/backups
BACKUP_DIR=/backups
# This sidecar's view of the live wiki: the attachments (dev-plan 9.3).
WIKI_PATH=/data/uploads
TOOL_VERSION="$(pg_dump --version 2>/dev/null)"

# shellcheck source=common.sh
. /opt/tesria/common.sh
# shellcheck source=../pgbackrest/offsite.sh
. /opt/tesria/offsite.sh
# shellcheck source=offsite-files.sh
. /scripts/offsite-files.sh
# shellcheck source=offsite-drill.sh
. /scripts/offsite-drill.sh

mkdir -p "$BACKUP_DIR"

do_backup() {
  local stamp
  stamp="$(date -u +%Y%m%dT%H%M%SZ)"
  BACKUP_STAMP="$stamp" /scripts/backup.sh && BACKUP_STAMP="$stamp" /scripts/backup-files.sh
}

legacy_backup() { do_backup; }

# A pg_dump that is interrupted leaves its .tmp behind. An hour is far longer
# than any dump this sidecar takes, so an older one belongs to no running job.
after_backup() {
  local removed
  removed="$(find "$BACKUP_DIR" -maxdepth 1 -type f -name '*.tmp' -mmin +60 -print -delete 2>/dev/null | wc -l | tr -d ' ')"
  [ "$removed" -gt 0 ] && note "removed $removed orphaned temporary file(s)"
  echo "{\"orphansRemoved\":$removed}"
}

do_restore_test() {
  /scripts/verify-backup.sh "db-$1.dump"
}

restore_details() {
  local tables
  tables="$(sed -n 's/.*restored cleanly, \([0-9][0-9]*\) public table.*/\1/p' "$1" | tail -n 1)"
  [ -n "$tables" ] && echo ",\"tablesRestored\":$tables"
}

# Mirrors the dumps and uploads archives on the volume into "Backups", one row
# per cycle. The listing goes through a file and \copy rather than a psql
# variable: kept forever, it outgrows a command-line argument.
sync_inventory() {
  [ -d "$BACKUP_DIR" ] || return 1
  find "$BACKUP_DIR" -maxdepth 1 -type f \( -name 'db-*.dump' -o -name 'uploads-*.tar.gz' \) \
    -printf '%f %s %T@\n' >/tmp/inventory.txt || return 1
  q -v reason="$1" <<'SQL'
CREATE TEMP TABLE listing (line text);
\copy listing FROM '/tmp/inventory.txt' WITH (FORMAT csv, DELIMITER E'\x01', QUOTE E'\x02')
WITH files AS (
  SELECT split_part(line, ' ', 1) AS f,
         split_part(line, ' ', 2)::bigint AS size,
         to_timestamp(split_part(line, ' ', 3)::double precision) AS modified
    FROM listing
   WHERE split_part(line, ' ', 1) ~ '^(db|uploads)-[0-9]{8}T[0-9]{6}Z\.(dump|tar\.gz)$'
), stamped AS (
  SELECT f, size, modified, f LIKE 'db-%' AS is_dump,
         substring(f FROM '([0-9]{8}T[0-9]{6}Z)') AS stamp,
         to_timestamp(substring(f FROM '([0-9]{8}T[0-9]{6})Z'), 'YYYYMMDD"T"HH24MISS') AS taken
    FROM files
), dumps AS (
  SELECT stamp, taken FROM stamped WHERE is_dump
), cycled AS (
  -- An archive belongs to the dump with its stamp. Before 9.1 the two were
  -- stamped separately, a second or two apart, so an archive with no exact
  -- match joins the latest dump taken up to five minutes before it.
  SELECT s.*,
         CASE WHEN s.is_dump THEN s.stamp ELSE coalesce(
           (SELECT d.stamp FROM dumps d WHERE d.stamp = s.stamp),
           (SELECT d.stamp FROM dumps d
             WHERE d.taken <= s.taken AND d.taken >= s.taken - interval '5 minutes'
             ORDER BY d.taken DESC LIMIT 1),
           s.stamp) END AS label
    FROM stamped s
), cycles AS (
  SELECT label,
         coalesce(min(taken) FILTER (WHERE is_dump), min(taken)) AS started,
         max(modified) AS completed,
         sum(size) AS size,
         bool_or(NOT is_dump) AS has_uploads,
         jsonb_agg(f ORDER BY f) AS files
    FROM cycled GROUP BY label
), upserted AS (
  INSERT INTO "Backups" ("Id", "Agent", "Label", "Type", "StartedAt", "CompletedAt", "SizeBytes",
                         "DetailJson", "HasUploads", "FirstSeenAt", "LastSeenAt")
  SELECT gen_random_uuid(), 'logical', label, 'dump', started, completed, size,
         jsonb_build_object('files', files), has_uploads, now(), now()
    FROM cycles
  ON CONFLICT ("Agent", "Label") DO UPDATE
     SET "LastSeenAt" = now(), "CompletedAt" = EXCLUDED."CompletedAt", "SizeBytes" = EXCLUDED."SizeBytes",
         "DetailJson" = EXCLUDED."DetailJson", "HasUploads" = EXCLUDED."HasUploads",
         "RemovedAt" = NULL, "RemovedReason" = NULL
  RETURNING 1
)
UPDATE "Backups" SET "RemovedAt" = now(), "RemovedReason" = :'reason'
 WHERE "Agent" = 'logical' AND "RemovedAt" IS NULL
   AND "Label" NOT IN (SELECT label FROM cycles);
SQL
}

# Removes whole cycles: the dump and its archive together.
apply_retention() {
  local labels label file
  labels="$(retention_candidates "$1" "$2")" || { note "retention: could not compute the plan; nothing removed"; return 1; }
  if [ -z "$labels" ]; then
    note "retention: nothing to remove"
    return 0
  fi
  for label in $labels; do
    if [ "$RETENTION_DRY_RUN" = 1 ]; then
      note "retention (dry run): would remove $label"
      continue
    fi
    q -v label="$label" <<'SQL' >/dev/null
UPDATE "Backups" SET "RemovedAt" = now(), "RemovedReason" = 'retention'
 WHERE "Agent" = 'logical' AND "Label" = :'label' AND "RemovedAt" IS NULL;
SQL
    for file in $(q -v label="$label" <<'SQL'
SELECT jsonb_array_elements_text("DetailJson" -> 'files') FROM "Backups"
 WHERE "Agent" = 'logical' AND "Label" = :'label';
SQL
); do
      # Only ever a name this sidecar writes, directly in the backup directory.
      if [[ "$file" =~ ^(db|uploads)-[0-9]{8}T[0-9]{6}Z\.(dump|tar\.gz)$ ]]; then
        rm -f -- "$BACKUP_DIR/$file" && note "retention: removed $file"
      fi
    done
  done
  # The copy kept by a restore ages out under the same policy (dev-plan 9.4).
  restore_apply_kept_retention "$1" "$2"
}

# --- Restoring the wiki (dev-plan 9.4) -------------------------------------

# Replace the wiki with an older copy. restore.sh does the work; this hands it
# the job's identity so the swap can be recorded, and reports the phases.
do_restore_wiki() {
  local id="$1" target="$2" options="$3" dir="$4"
  local dump="$target"
  # The page sends a cycle label (the stamp); restore.sh takes a file name.
  case "$dump" in
    db-*.dump) ;;
    "")        dump="" ;;
    *)         dump="db-${dump}.dump" ;;
  esac
  RESTORE_JOB_ID="$id" RESTORE_DIR="$dir" /scripts/restore.sh "$dump"
}

# Undo: the kept database and the kept uploads go back where they were. The
# same swap in reverse, and with its own safety backup first, because undoing
# a restore is itself a restore and the copy it replaces may be the one
# somebody actually wanted.
do_restore_undo() {
  local id="$1" target="$2" options="$3" dir="$4"
  local live="$PGDATABASE" kept="${PGDATABASE}_pre_restore" uploads="${WIKI_PATH}/.pre-restore"
  local aside="${PGDATABASE}_undone"

  psql -X -q -t -A -v ON_ERROR_STOP=1 --dbname=postgres \
    -c "SELECT 1 FROM pg_database WHERE datname = '${kept}';" | grep -q 1 \
    || { echo "ERROR: there is no kept copy named ${kept} to go back to"; return 1; }

  echo "[undo] taking a safety backup of the restored wiki before putting the previous one back"
  local stamp
  stamp="$(date -u +%Y%m%dT%H%M%SZ)"
  BACKUP_STAMP="$stamp" /scripts/backup.sh && BACKUP_STAMP="$stamp" /scripts/backup-files.sh \
    || { echo "ERROR: the safety backup failed; nothing has been changed"; return 1; }

  [ -n "$dir" ] && restore_export_carry "$dir"

  echo "[undo] switching back"
  psql -X -q -t -A -v ON_ERROR_STOP=1 --dbname=postgres <<SQL || { echo "ERROR: the switch back failed; the wiki is unchanged"; return 1; }
ALTER DATABASE "${live}" WITH ALLOW_CONNECTIONS false;
SELECT pg_terminate_backend(pid) FROM pg_stat_activity
 WHERE datname = '${live}' AND pid <> pg_backend_pid();
DROP DATABASE IF EXISTS "${aside}";
ALTER DATABASE "${live}" RENAME TO "${aside}";
ALTER DATABASE "${kept}" RENAME TO "${live}";
ALTER DATABASE "${live}" WITH ALLOW_CONNECTIONS true;
DROP DATABASE IF EXISTS "${aside}";
SQL

  if [ -d "$uploads" ] && [ -w "$WIKI_PATH" ]; then
    echo "[undo] putting the previous attachments back"
    find "$WIKI_PATH" -mindepth 1 -maxdepth 1 ! -name '.pre-restore' -exec rm -rf {} + 2>/dev/null
    find "$uploads" -mindepth 1 -maxdepth 1 -exec mv -t "$WIKI_PATH" {} + 2>/dev/null
    rmdir "$uploads" 2>/dev/null
  fi

  psql -X -q -t -A -v ON_ERROR_STOP=1 --dbname="$live" >/dev/null 2>&1 <<'SQL' || true
UPDATE "SiteSettings"
   SET "RestoreJobId" = NULL, "RestoreStartedAt" = NULL, "RestoreCancelRequestedAt" = NULL;
SQL
  [ -n "$dir" ] && restore_import_carry "$dir"
  # No kept copy after an undo: the wiki is the copy that was kept.
  restore_record_done "$id" "the copy kept before the last restore" ""
  [ -n "$dir" ] && printf '{"undone":true,"safetyBackup":"db-%s.dump"}' "$stamp" > "$dir/result"
  echo "[undo] done; the wiki is the copy that was kept before the restore"
}

# Remove the kept copy. The destruction of an undo, so it is only ever done
# because somebody asked or because the retention policy reached it.
do_restore_discard() {
  local target="$1" kept="${PGDATABASE}_pre_restore" uploads="${WIKI_PATH}/.pre-restore"
  echo "[discard] removing the kept copy ${kept}"
  psql -X -q -t -A -v ON_ERROR_STOP=1 --dbname=postgres \
    -c "DROP DATABASE IF EXISTS \"${kept}\";" >/dev/null || return 1
  [ -d "$uploads" ] && rm -rf "$uploads"
  q >/dev/null 2>&1 <<'SQL' || true
UPDATE "SiteSettings"
   SET "KeptCopyJson" = jsonb_set("KeptCopyJson"::jsonb, '{removedAt}', to_jsonb(now()))::text
 WHERE "KeptCopyJson" IS NOT NULL;
SQL
  echo "[discard] removed"
}

# The kept copy ages out under the retention policy, by the same rule as a
# backup (decided 2026-09-22): it is entered into the plan as if
# it were a backup taken at the moment of the restore, and removed when the
# plan would remove that backup. Retention off keeps it, as retention off
# keeps everything.
#
# Called from apply_retention, so it runs on the same pass and inherits the
# 24-hour grace on a stricter policy without needing its own copy of it.
restore_apply_kept_retention() {
  local keep_count="$1" keep_days="$2" due
  due="$(q -v kc="$keep_count" -v kd="$keep_days" 2>/dev/null <<'SQL'
WITH kept AS (
  SELECT ("KeptCopyJson"::jsonb ->> 'restoredAt')::timestamptz AS at,
         "KeptCopyJson"::jsonb ->> 'removedAt' AS removed
    FROM "SiteSettings" WHERE "KeptCopyJson" IS NOT NULL
), rank AS (
  -- Where a backup taken at that moment would sit in the newest-first list.
  SELECT kept.at,
         (SELECT count(*) FROM "Backups" b
           WHERE b."Agent" = 'logical' AND b."RemovedAt" IS NULL AND b."Error" IS NULL
             AND b."StartedAt" > kept.at) AS newer
    FROM kept WHERE kept.removed IS NULL
)
SELECT CASE WHEN newer >= :'kc'::int AND at < now() - make_interval(days => :'kd'::int)
            THEN 't' ELSE 'f' END
  FROM rank;
SQL
)" || return 0
  [ "$due" = t ] || return 0

  if [ "$RETENTION_DRY_RUN" = 1 ]; then
    note "retention (dry run): would remove the copy kept before the last restore"
    return 0
  fi
  note "retention: removing the copy kept before the last restore"
  local id
  id="$(q <<'SQL'
INSERT INTO "BackupJobs" ("Id", "Agent", "Kind", "Trigger", "Status", "RequestedAt", "StartedAt")
VALUES (gen_random_uuid(), 'logical', 'restore-discard', 'retention', 'running', now(), now())
RETURNING "Id";
SQL
)" || return 0
  local logf=/tmp/kept-retention.log
  if do_restore_discard "" >"$logf" 2>&1; then
    finish_job "$id" succeeded "" "$logf"
  else
    finish_job "$id" failed "$(first_error "$logf")" "$logf"
  fi
}

# The offsite copy of the files (dev-plan 9.2 step 2). Checked on every pass,
# but a scheduled slot is only copied to once per local cycle (see
# offsite_copy_due), so what is copied is a cycle that exists here first:
# local first, then replicate.
offsite_tick() {
  local line en kc kd
  # The same policy the local retention uses, read the same way, so every
  # copy expires together instead of drifting apart.
  if line="$(observe_policy)"; then
    IFS='|' read -r en kc kd _ <<<"$line"
  fi

  if offsite_cloud_enabled; then
    offsite_copy_slot cloud "${en:-f}" "${kc:-0}" "${kd:-0}"
  else
    offsite_files_disable cloud
  fi

  # The network and removable drives are copied to by services of their own,
  # backup-nas and backup-removable (deploy/backup/drive.sh). A drive that is
  # not there when the stack starts can stop the container it is mounted in
  # from starting at all (Docker Desktop cannot create the missing folder
  # under /Volumes), and before 0.8.2 that container was this one, so a
  # missing NAS took the local backups down with it (T8-026). What is left
  # here is noticing when one of those services is not running.
  drive_service_watch nas "${OFFSITE_NAS_PASSPHRASE:-}" "${OFFSITE_NAS_PATH:-}"
  drive_service_watch removable "${OFFSITE_REMOVABLE_PASSPHRASE:-}" "${OFFSITE_REMOVABLE_PATH:-}"

  # Last, and at most one per pass: proving a copy restores costs a full
  # read of it, and that must never delay the backups themselves.
  offsite_drill_tick cloud
}

# The drives' jobs are theirs to claim (see offsite_tick).
CLAIM_SKIP_SLOTS="nas removable"
AGENT_STARTED_AT="$(date +%s)"
# How long a drive's service may be silent before it counts as not running.
# Its heartbeat is a minute; this is three missed ones.
DRIVE_SILENT_SECONDS=180

# A drive's service writes its card's row at least once a minute, even in
# the middle of a long copy. A row that has gone quiet means the service is
# not running, which on Docker Desktop is what a drive missing at start
# does: the container is created and never starts. The card then says so,
# the drive counts as absent (an alert for a network drive, nothing for a
# removable one, as ever), and the jobs queued for it are answered here
# rather than left waiting for ever.
drive_service_watch() {
  local slot="$1" passphrase="$2" path="$3" stale service what msg
  [ -n "$passphrase" ] || return 0
  # Not in the first minutes after this one starts: the drive's service
  # starts at the same time and has not written yet.
  (( $(date +%s) - AGENT_STARTED_AT < DRIVE_SILENT_SECONDS )) && return 0
  stale="$(q -v slot="$slot" -v secs="$DRIVE_SILENT_SECONDS" 2>/dev/null <<'SQL'
SELECT CASE WHEN max("UpdatedAt") IS NULL
              OR max("UpdatedAt") < now() - make_interval(secs => :'secs'::int)
            THEN 't' ELSE 'f' END
  FROM "BackupTargets" WHERE "Slot" = :'slot' AND "Kind" = 'files';
SQL
)" || return 0
  [ "$stale" = t ] || return 0

  service="backup-$slot"
  if [ "$slot" = nas ]; then
    what="the network drive"
    msg="The service that copies to the network drive ($service) is not running, usually because the share at ${path:-OFFSITE_NAS_PATH} was not connected when Tesria started. Connect it, then run: docker compose up -d"
  else
    what="the removable drive"
    msg="The service that copies to this drive ($service) is not running, usually because the drive was not plugged in when Tesria started. Plug it in, then run: docker compose up -d"
  fi
  log "offsite: $service has not reported for over $(( DRIVE_SILENT_SECONDS / 60 )) minutes; marking $what absent"
  # UpdatedAt is left alone on purpose: it is the other service's heartbeat.
  q -v slot="$slot" -v msg="$msg" -v loc="$path" >/dev/null 2>&1 <<'SQL' || true
INSERT INTO "BackupTargets" ("Slot", "Kind", "Type", "Location", "Prefix", "Enabled", "Present", "Message", "UpdatedAt")
VALUES (:'slot', 'files', 'path', NULLIF(:'loc', ''), 'restic', true, false, :'msg', now())
ON CONFLICT ("Slot", "Kind") DO UPDATE
   SET "Enabled" = true, "Present" = false, "Message" = EXCLUDED."Message",
       "Location" = coalesce(EXCLUDED."Location", "BackupTargets"."Location");
UPDATE "BackupJobs"
   SET "Status" = 'failed', "StartedAt" = coalesce("StartedAt", now()), "FinishedAt" = now(),
       "Error" = left(:'msg', 500), "ResultJson" = jsonb_build_object('summary', left(:'msg', 500))
 WHERE "Agent" = 'logical' AND "Status" = 'requested'
   AND "Kind" IN ('copy-offsite', 'test-target') AND "Target" = :'slot';
SQL
}

# Putting back attachment files after a point-in-time restore (T8-017).
do_restore_uploads() {
  /scripts/restore-uploads.sh
}

run_agent
