#!/usr/bin/env bash
# Shared by both backup sidecars (dev-plan 9.1): the `backup` sidecar
# (deploy/backup/run.sh, pg_dump and the uploads archive) and the
# `pgbackrest` sidecar (deploy/pgbackrest/run.sh). Mounted into both at
# /opt/tesria/common.sh.
#
# The database is the contract with the app. Each sidecar writes its heartbeat
# and schedule to "BackupAgents", mirrors what it has on disk into "Backups",
# and appends and runs "BackupJobs". The app reads those tables, stores the
# retention policy on "SiteSettings", and queues work by appending a
# `requested` job. The app never touches a backup file.
#
# The sourcing script sets, before calling run_agent:
#   AGENT            logical | physical
#   VOLUME           the directory whose free space is reported
#   TOOL_VERSION     shown on the status card
#   FULL_EVERY_DAYS  physical only
# and defines:
#   do_backup            take one backup; output is the job's log
#   do_restore_test L    restore backup L somewhere throwaway and check it
#   sync_inventory R     mirror the backups on disk into "Backups"; rows
#                        whose backups are gone get "RemovedReason" = R
#   apply_retention N D  remove what is outside both the newest N and the
#                        last D days, then sync with reason `retention`
#   legacy_backup        a backup for when the tables do not exist yet
#
# Nothing here runs under `set -e`. A failed backup is a row, not an exit:
# before 9.1 a failure exited the pgbackrest container, and the restart took
# another full backup.

set -uo pipefail
export PGTZ=UTC

POLL_SECONDS="${BACKUP_POLL_SECONDS:-60}"
INTERVAL_HOURS="${BACKUP_INTERVAL_HOURS:-24}"
# Printing what retention would remove, and removing nothing, for checking
# this file's SQL against the C# copy of the rule (BackupRetention.cs).
RETENTION_DRY_RUN="${RETENTION_DRY_RUN:-0}"

log() { echo "[$AGENT $(date -u +%FT%TZ)] $*"; }
# For functions whose stdout is data: the message goes to the job log instead.
note() { log "$@" >&2; }

# psql with the script on stdin: unaligned, tuples only, stop on error.
# Connection settings come from PGHOST/PGUSER/PGPASSWORD/PGDATABASE.
q() { psql -X -q -t -A -v ON_ERROR_STOP=1 -v agent="$AGENT" "$@"; }

wait_for_db() {
  until pg_isready -q; do
    log "waiting for the database"
    sleep 5
  done
}

tables_ready() {
  local ready
  ready="$(q 2>/dev/null <<'SQL'
SELECT to_regclass('public."BackupJobs"') IS NOT NULL
   AND EXISTS (SELECT 1 FROM information_schema.columns
                WHERE table_name = 'SiteSettings' AND column_name = 'BackupPolicyChangedAt');
SQL
)"
  [ "$ready" = "t" ]
}

set_message() {
  q -v msg="$1" >/dev/null 2>&1 <<'SQL' || true
UPDATE "BackupAgents" SET "Message" = left(:'msg', 2000) WHERE "Name" = :'agent';
SQL
}

# Registers this agent. Jobs this agent was running when it stopped cannot
# still be running, so they are failed. The schedule is kept: a restart no
# longer means an immediate backup unless one is due.
agent_start() {
  q -v interval="$INTERVAL_HOURS" -v full_every="${FULL_EVERY_DAYS:-}" -v version="$TOOL_VERSION" <<'SQL'
UPDATE "BackupJobs"
   SET "Status" = 'failed', "FinishedAt" = now(),
       "Error" = 'The backup agent restarted while this job was running.'
 WHERE "Agent" = :'agent' AND "Status" = 'running'
   -- Not a restore. A backup that was interrupted is simply a failed backup,
   -- but a restore may have been interrupted *after* its point of no return,
   -- in which case the wiki has already been replaced and this row is the
   -- only thing that knows. Its truth is the restore directory on this
   -- sidecar's own volume, which restore_reconcile_on_start reads before
   -- anything else runs (dev-plan 9.4).
   AND "Kind" NOT IN ('restore', 'restore-undo');
INSERT INTO "BackupAgents" ("Name", "StartedAt", "LastSeenAt", "IntervalHours", "FullEveryDays", "ToolVersion", "Message")
VALUES (:'agent', now(), now(), :'interval'::int, NULLIF(:'full_every', '')::int, :'version', 'Started.')
ON CONFLICT ("Name") DO UPDATE
   SET "StartedAt" = now(), "LastSeenAt" = now(),
       "IntervalHours" = EXCLUDED."IntervalHours", "FullEveryDays" = EXCLUDED."FullEveryDays",
       "ToolVersion" = EXCLUDED."ToolVersion", "Message" = EXCLUDED."Message",
       -- A shorter interval takes effect now rather than after the old one.
       "NextRunAt" = LEAST("BackupAgents"."NextRunAt", now() + make_interval(hours => EXCLUDED."IntervalHours"));
SQL
}

# Runs in the background for the life of the container, so a long backup
# does not make the agent look offline. Also feeds the compose healthcheck.
# What this agent's own backups occupy, and which filesystem they sit on
# (dev-plan 9.3). `df` already gives free and total; the missing number is
# how much of the used space is *ours*, which is what makes "backups vs
# everything else vs free" answerable rather than guessed.
#
# Measured on its own clock rather than every heartbeat: `du` walks the whole
# backup directory, which is cheap on a few gigabytes and not free on a large
# repository, and the number moves slowly. The filesystem id comes from `df`,
# and is what lets two agents sharing one disk draw one chart instead of two
# of the same disk.
MEASURE_EVERY="${BACKUP_MEASURE_SECONDS:-600}"
MEASURED_AT=0
MEASURED_BYTES=""
MEASURED_WIKI=""
MEASURED_FS=""
MEASURED_FREE=""
MEASURED_TOTAL=""

# A path that is a bind mount from the host, so that `df` on it reports the
# *host's* disk. Both sidecars already mount their scripts this way.
HOST_REF="${BACKUP_HOST_REF:-/scripts}"

measure_volume() {
  local now
  now="$(date +%s)"
  (( now - MEASURED_AT < MEASURE_EVERY )) && return 0
  MEASURED_AT="$now"
  MEASURED_BYTES="$(du -sb "$VOLUME" 2>/dev/null | cut -f1)"
  # What the live wiki itself occupies: the uploads for one sidecar, the
  # database directory for the other. Each reports its own part.
  MEASURED_WIKI=""
  [ -n "${WIKI_PATH:-}" ] && [ -d "$WIKI_PATH" ] \
    && MEASURED_WIKI="$(du -sb "$WIKI_PATH" 2>/dev/null | cut -f1)"

  # The disk that actually constrains this machine.
  #
  # `df` on the container's own volume is not it. Under Docker Desktop that
  # volume lives on a *sparse* virtual disk which reports the size it may
  # grow to, not the space the host can still give it: on a Mac with 700GB
  # free it will happily claim 1.7TB. Backups then fill the real disk long
  # before any warning here would fire, which is the one failure this
  # measurement exists to prevent.
  #
  # A host bind mount is passed through the host's filesystem, so `df` on it
  # reports the host's real figures. On a Linux host the two are usually the
  # same filesystem anyway and this changes nothing.
  read -r MEASURED_FREE MEASURED_TOTAL < <(df -B1 --output=avail,size "$HOST_REF" 2>/dev/null | tail -1)
  MEASURED_FS="$(df --output=source "$HOST_REF" 2>/dev/null | tail -1 | tr -d ' ')"
}

heartbeat() {
  touch /tmp/heartbeat
  measure_volume
  q -v free="${MEASURED_FREE:-}" -v total="${MEASURED_TOTAL:-}" \
    -v used="${MEASURED_BYTES:-}" -v wiki="${MEASURED_WIKI:-}" -v fs="${MEASURED_FS:-}" >/dev/null 2>&1 <<'SQL' || true
UPDATE "BackupAgents"
   SET "LastSeenAt" = now(),
       "VolumeFreeBytes" = coalesce(NULLIF(:'free', '')::bigint, "VolumeFreeBytes"),
       "VolumeTotalBytes" = coalesce(NULLIF(:'total', '')::bigint, "VolumeTotalBytes"),
       "VolumeWikiBytes" = coalesce(NULLIF(:'wiki', '')::bigint, "VolumeWikiBytes"),
       -- Left alone when the measurement has not run this pass, so the last
       -- known figure stays on the screen rather than blinking to nothing.
       "VolumeBackupBytes" = coalesce(NULLIF(:'used', '')::bigint, "VolumeBackupBytes"),
       "VolumeFilesystem" = coalesce(NULLIF(:'fs', ''), "VolumeFilesystem"),
       -- How far forward point-in-time recovery reaches: the last segment
       -- Postgres handed to pgBackRest. archive-push with archive-async
       -- only reports success once the segment is in the repository.
       "WalArchivedAt" = CASE WHEN "Name" = 'physical'
                              THEN (SELECT last_archived_time FROM pg_stat_archiver)
                              ELSE "WalArchivedAt" END
 WHERE "Name" = :'agent';
SQL
}

heartbeat_loop() {
  while true; do
    heartbeat
    sleep "$POLL_SECONDS"
  done
}

# Reads the saved policy and decides what this agent enforces now, recording
# the decision on its "BackupAgents" row. Prints `enabled|count|days|pending`
# (pending: when a waiting stricter policy takes effect), or nothing when no
# policy has been set, which removes nothing.
#
# A policy that could remove more than the applied one (retention turned on,
# a smaller count, fewer days, or no policy ever applied) waits 24 hours from
# when this agent first sees it. Until then the agent keeps anything either
# policy keeps. A change after the agent looked restarts the clock. A looser
# policy applies at once. Mirrors BackupRetention.Enforced in C#.
observe_policy() {
  q <<'SQL'
WITH s AS (
  SELECT "BackupRetentionEnabled" AS en, "BackupKeepCount" AS kc, "BackupKeepDays" AS kd,
         "BackupPolicyChangedAt" AS ch
    FROM "SiteSettings" WHERE "BackupPolicyChangedAt" IS NOT NULL LIMIT 1
), c AS (
  SELECT s.*, a."AppliedRetentionEnabled" AS aen, a."AppliedKeepCount" AS akc,
         a."AppliedKeepDays" AS akd, a."PolicyObservedAt" AS obs,
         (s.en AND (a."AppliedRetentionEnabled" IS NOT TRUE
                    OR s.kc < a."AppliedKeepCount" OR s.kd < a."AppliedKeepDays")) AS stricter
    FROM s CROSS JOIN "BackupAgents" a
   WHERE a."Name" = :'agent'
), d AS (
  SELECT c.*,
         CASE WHEN stricter AND (obs IS NULL OR obs < ch) THEN now() ELSE obs END AS seen
    FROM c
), e AS (
  SELECT d.*, (NOT stricter OR seen + interval '24 hours' <= now()) AS apply_now FROM d
), u AS (
  UPDATE "BackupAgents" ag
     SET "AppliedRetentionEnabled" = CASE WHEN e.apply_now THEN e.en ELSE ag."AppliedRetentionEnabled" END,
         "AppliedKeepCount"        = CASE WHEN e.apply_now THEN e.kc ELSE ag."AppliedKeepCount" END,
         "AppliedKeepDays"         = CASE WHEN e.apply_now THEN e.kd ELSE ag."AppliedKeepDays" END,
         "PolicyObservedAt"        = CASE WHEN e.apply_now THEN NULL ELSE e.seen END
    FROM e
   WHERE ag."Name" = :'agent'
  RETURNING e.*
)
SELECT CASE
         WHEN apply_now THEN
           (CASE WHEN en THEN 't' ELSE 'f' END) || '|' || kc || '|' || kd || '|'
         WHEN aen IS NOT TRUE THEN
           'f|' || kc || '|' || kd || '|' || to_char(seen + interval '24 hours', 'YYYY-MM-DD"T"HH24:MI:SS"Z"')
         ELSE
           't|' || greatest(kc, akc) || '|' || greatest(kd, akd) || '|'
                || to_char(seen + interval '24 hours', 'YYYY-MM-DD"T"HH24:MI:SS"Z"')
       END
  FROM u;
SQL
}

# Applies the policy after a successful backup. Prints a JSON object for the
# job's result.
run_retention() {
  local line en kc kd pending
  if ! line="$(observe_policy)"; then
    note "retention: could not read the policy; nothing removed"
    echo '{"retention":{"applied":false,"reason":"policy unreadable"}}'
    return 0
  fi
  IFS='|' read -r en kc kd pending <<<"$line"
  local pending_json="null"
  [ -n "${pending:-}" ] && pending_json="\"$pending\""

  if [ -z "${en:-}" ]; then
    note "retention: no policy has been set; nothing removed"
    echo '{"retention":{"applied":false,"reason":"no policy"}}'
    return 0
  fi
  if [ "$en" != "t" ]; then
    if [ -n "${pending:-}" ]; then
      note "retention: a new policy takes effect at $pending; nothing removed before then"
    else
      note "retention: keeping every backup"
    fi
    echo "{\"retention\":{\"applied\":true,\"enabled\":false,\"pendingUntil\":$pending_json}}"
    return 0
  fi

  note "retention: keep the newest $kc and everything from the last $kd days${pending:+ (a stricter policy takes effect at $pending)}"
  apply_retention "$kc" "$kd"
  echo "{\"retention\":{\"applied\":true,\"enabled\":true,\"keepCount\":$kc,\"keepDays\":$kd,\"dryRun\":$([ "$RETENTION_DRY_RUN" = 1 ] && echo true || echo false),\"pendingUntil\":$pending_json}}"
}

# Oldest-first labels of this agent's present, successful backups that are
# outside both limits. The logical rule; physical works in whole fulls
# (see deploy/pgbackrest/run.sh).
retention_candidates() {
  q -v kc="$1" -v kd="$2" <<'SQL'
SELECT "Label"
  FROM (SELECT "Label", "StartedAt",
               row_number() OVER (ORDER BY "StartedAt" DESC) - 1 AS i
          FROM "Backups"
         WHERE "Agent" = :'agent' AND "RemovedAt" IS NULL AND "Error" IS NULL) x
 WHERE i >= :'kc'::int AND "StartedAt" < now() - make_interval(days => :'kd'::int)
 ORDER BY "StartedAt";
SQL
}

backup_due() {
  local due
  due="$(q <<'SQL'
SELECT coalesce("NextRunAt" <= now(), true) FROM "BackupAgents" WHERE "Name" = :'agent';
SQL
)"
  [ "$due" = "t" ]
}

# Claims the oldest requested job for this agent. Prints
# `id|kind|target|options`. The options are the job's OptionsJson with the
# pipes stripped, since this is a pipe-delimited line and a restore's options
# are a small flat object (dev-plan 9.4).
claim_job() {
  q <<'SQL'
UPDATE "BackupJobs" SET "Status" = 'running', "StartedAt" = now()
 WHERE "Id" = (SELECT "Id" FROM "BackupJobs"
                WHERE "Agent" = :'agent' AND "Status" = 'requested'
                ORDER BY "RequestedAt" LIMIT 1
                  FOR UPDATE SKIP LOCKED)
RETURNING "Id" || '|' || "Kind" || '|' || coalesce("Target", '')
       || '|' || translate(coalesce("OptionsJson", ''), '|', ' ');
SQL
}

# A job this sidecar does not know how to run. Never silently ignored and
# never treated as a backup: an unrecognized kind means the app is newer than
# this image, and the person waiting deserves to be told exactly that.
fail_unknown_job() {
  q -v id="$1" -v kind="$2" >/dev/null 2>&1 <<'SQL' || true
UPDATE "BackupJobs"
   SET "Status" = 'failed', "FinishedAt" = now(),
       "Error" = 'This backup agent does not know how to run a ' || :'kind'
                 || ' job. Its image is older than the application; rebuild the backup sidecars.'
 WHERE "Id" = :'id'::uuid;
SQL
}

create_job() {
  q -v trigger="$1" <<'SQL'
INSERT INTO "BackupJobs" ("Id", "Agent", "Kind", "Trigger", "Status", "RequestedAt", "StartedAt")
VALUES (gen_random_uuid(), :'agent', 'backup', :'trigger', 'running', now(), now())
RETURNING "Id";
SQL
}

# finish_job ID STATUS ERROR LOGFILE [EXTRA_JSON]
# The result lists what this run produced and what retention removed, read
# back from the inventory so it is what actually happened.
finish_job() {
  local tail
  tail="$(tail -n 40 "$4" | cut -c1-500)"
  q -v id="$1" -v status="$2" -v error="$3" -v log="$tail" -v extra="${5:-}" >/dev/null <<'SQL'
UPDATE "BackupJobs" j
   SET "Status" = :'status', "FinishedAt" = now(),
       "Error" = NULLIF(left(:'error', 2000), ''),
       "LogTail" = :'log',
       "ResultJson" = jsonb_build_object(
         'produced', (SELECT coalesce(jsonb_agg(jsonb_build_object('label', b."Label", 'type', b."Type", 'sizeBytes', b."SizeBytes")
                                               ORDER BY b."StartedAt"), '[]'::jsonb)
                        FROM "Backups" b
                       WHERE j."Kind" = 'backup' AND b."Agent" = j."Agent" AND b."FirstSeenAt" >= j."StartedAt"),
         'removed',  (SELECT coalesce(jsonb_agg(b."Label" ORDER BY b."StartedAt"), '[]'::jsonb)
                        FROM "Backups" b
                       WHERE b."Agent" = j."Agent" AND b."RemovedReason" = 'retention' AND b."RemovedAt" >= j."StartedAt")
       ) || coalesce(NULLIF(:'extra', '')::jsonb, '{}'::jsonb)
 WHERE "Id" = :'id'::uuid;
SQL
}

set_next_run() {
  q -v secs="$1" >/dev/null <<'SQL'
UPDATE "BackupAgents" SET "NextRunAt" = now() + make_interval(secs => :'secs'::int) WHERE "Name" = :'agent';
SQL
}

# The first line of the log that says what went wrong, or its last line.
first_error() {
  local line
  line="$(grep -m1 -iE 'error|fatal|fail' "$1" || true)"
  [ -n "$line" ] || line="$(tail -n 1 "$1")"
  echo "${line:-The backup failed without output.}"
}

FAILURES=0

run_backup_job() {
  local id="$1" logf retention after extra
  logf="$(mktemp)"
  if do_backup >"$logf" 2>&1; then
    sync_inventory missing >>"$logf" 2>&1 || echo "WARN: inventory sync failed after the backup" >>"$logf"
    # Retention and the after-backup hook log to stderr (into the job log)
    # and print one JSON object on stdout.
    retention="$(run_retention 2>>"$logf" | tail -n 1)"
    after="$(after_backup 2>>"$logf" | tail -n 1)"
    extra="$(merge_json "$retention" "$after" 2>>"$logf")"
    cat "$logf"
    finish_job "$id" succeeded "" "$logf" "$extra" || log "WARN: could not record the job result"
    set_next_run "$(( INTERVAL_HOURS * 3600 ))"
    FAILURES=0
    set_message "Backup succeeded at $(date -u +%FT%TZ)."
    log "backup succeeded"
  else
    cat "$logf"
    local err backoff
    err="$(first_error "$logf")"
    finish_job "$id" failed "$err" "$logf" || log "WARN: could not record the job result"
    FAILURES=$(( FAILURES + 1 ))
    # 15 minutes, doubling, at most 6 hours. Before 9.1 a failure waited a
    # whole interval (a day by default).
    backoff=$(( 900 * (1 << (FAILURES > 5 ? 5 : FAILURES - 1)) ))
    [ "$backoff" -gt 21600 ] && backoff=21600
    set_next_run "$backoff"
    set_message "Backup failed: $err"
    log "ERROR: backup failed; retrying in $(( backoff / 60 )) minutes"
  fi
  rm -f "$logf"
}

run_restore_job() {
  local id="$1" label="$2" logf ok extra
  logf="$(mktemp)"
  if [ -z "$label" ]; then
    label="$(q <<'SQL'
SELECT "Label" FROM "Backups" WHERE "Agent" = :'agent' AND "RemovedAt" IS NULL AND "Error" IS NULL
 ORDER BY "StartedAt" DESC LIMIT 1;
SQL
)"
  fi
  log "restore test of $label"
  if [ -n "$label" ] && do_restore_test "$label" >"$logf" 2>&1; then
    ok=true
  else
    ok=false
  fi
  cat "$logf"
  q -v label="$label" -v ok="$ok" >/dev/null <<'SQL' || true
UPDATE "Backups" SET "LastVerifiedAt" = now(), "LastVerifyOk" = :'ok'::boolean
 WHERE "Agent" = :'agent' AND "Label" = :'label';
SQL
  extra="{\"label\":\"$label\",\"ok\":$ok$(restore_details "$logf")}"
  if [ "$ok" = true ]; then
    finish_job "$id" succeeded "" "$logf" "$extra"
    set_message "Restore test of $label passed."
  else
    finish_job "$id" failed "$( [ -n "$label" ] && first_error "$logf" || echo 'There is no backup to restore.')" "$logf" "$extra"
    set_message "Restore test of ${label:-the newest backup} FAILED."
  fi
  rm -f "$logf"
}

# --- Restoring the wiki (dev-plan 9.4) -------------------------------------
#
# The hardest fact about a restore is that the job's own status lives in the
# database being replaced. So every restore has a directory on this sidecar's
# own volume, and that directory is the truth: the phases, the log, the
# carried-across backup history, and enough about the swap to finish or
# reverse it after a restart. The database rows are written from it, not the
# other way round.
#
# Each sidecar defines do_restore_wiki / do_restore_undo for its own kind; the
# default refuses clearly rather than doing something approximate.
RESTORE_ROOT="${BACKUP_RESTORE_ROOT:-/backups/restores}"
# Inherited, never reset. restore.sh sources this file and is handed its
# directory in the environment, so a bare `RESTORE_DIR=""` here silently
# emptied it and the phases, the carried-across history and the result file
# were all written nowhere. The restore still worked, which is what made it
# hard to see.
RESTORE_DIR="${RESTORE_DIR:-}"
RESTORE_DRY_RUN="${RESTORE_DRY_RUN:-0}"

do_restore_wiki() { echo "This agent cannot restore the wiki."; return 1; }
do_restore_undo() { echo "This agent cannot undo a restore."; return 1; }
do_restore_discard() { echo "This agent has no kept copy to remove."; return 1; }

# Everything about one restore, on disk, outside the database it replaces.
restore_dir_for() {
  RESTORE_DIR="$RESTORE_ROOT/$1"
  mkdir -p "$RESTORE_DIR"
  echo "$RESTORE_DIR"
}

# The phase a person sees on the page. Written to the directory first, because
# during the swap the database cannot be written at all; the row is a
# best-effort mirror of the file.
restore_phase() {
  local phase="$1"
  [ -n "$RESTORE_DIR" ] && printf '%s %s\n' "$(date -u +%FT%TZ)" "$phase" >>"$RESTORE_DIR/phases"
  log "restore: $phase"
  q -v msg="$phase" >/dev/null 2>&1 <<'SQL' || true
UPDATE "BackupAgents" SET "Message" = left('Restore: ' || :'msg', 2000) WHERE "Name" = :'agent';
SQL
}

# Has somebody asked to stop? Checked at every step up to the point of no
# return and never after it, which is what makes the answer to "can I cancel"
# honest rather than hopeful.
#
# Two ways to ask. A cancel while the job runs sets RestoreCancelRequestedAt.
# A cancel while it was still queued, and the app giving up on a job nobody
# claimed, clear RestoreJobId instead, because the app may not write the job
# row (BackupJobs is append-only for its role, the review's DATA-04): a
# restore runs only while the wiki is waiting for that very job.
# RESTORE_JOB_ID is the job being run; without it only the flag counts.
restore_canceled() {
  local v
  v="$(q -v job="${RESTORE_JOB_ID:-}" <<'SQL' 2>/dev/null
SELECT CASE WHEN "RestoreCancelRequestedAt" IS NOT NULL
              OR (:'job' <> '' AND "RestoreJobId"::text IS DISTINCT FROM :'job')
            THEN 't' ELSE 'f' END
  FROM "SiteSettings" LIMIT 1;
SQL
)" || return 1
  [ "$v" = t ]
}

# The tables that describe the disk rather than the wiki: which backups exist,
# what ran, and what the offsite targets look like. They are carried across a
# restore because a dump from last week would otherwise make the page forget
# every backup taken since, including the safety backup this restore just
# took, which is the one somebody would need next.
RESTORE_CARRY_TABLES='Backups BackupJobs BackupTargets BackupAgents'

restore_export_carry() {
  local dir="$1" table
  for table in $RESTORE_CARRY_TABLES; do
    q -c "\\copy (SELECT * FROM \"$table\") TO '$dir/carry-$table.csv' WITH (FORMAT csv, HEADER)" \
      >/dev/null 2>&1 || note "could not export $table; it will not be carried across"
  done
}

# Imported with ON CONFLICT DO NOTHING via a staging table: the restored
# database has its own rows for the same backups, and those are the ones a
# person was looking at a moment ago. This adds what is missing rather than
# overwriting what is there.
#
# With one exception, for jobs. The database being swapped in is a snapshot,
# and its jobs are frozen as they were then: the job that took a dump is
# "running" inside that dump, and the kept copy an undo puts back holds the
# restore it was kept by as "running". Left alone, those rows stayed running
# for ever (five of them did, from the 9.4 walk) and kept the backups page
# polling. Worse, a job that was "requested" in the snapshot would be picked
# up and run again. So a job row that the carried copy shows further along
# (requested, then running, then finished) takes the carried copy's outcome.
restore_import_carry() {
  local dir="$1" table extra
  for table in $RESTORE_CARRY_TABLES; do
    [ -f "$dir/carry-$table.csv" ] || continue
    extra=""
    [ "$table" = BackupJobs ] && extra='
UPDATE "BackupJobs" j
   SET "Status" = c."Status", "StartedAt" = c."StartedAt", "FinishedAt" = c."FinishedAt",
       "Error" = c."Error", "LogTail" = c."LogTail", "ResultJson" = c."ResultJson"
  FROM carry_stage c
 WHERE c."Id" = j."Id"
   AND (CASE c."Status" WHEN '"'requested'"' THEN 0 WHEN '"'running'"' THEN 1 ELSE 2 END)
     > (CASE j."Status" WHEN '"'requested'"' THEN 0 WHEN '"'running'"' THEN 1 ELSE 2 END);'
    # BEGIN/COMMIT is not tidiness: psql runs each statement in its own
    # transaction by default, so ON COMMIT DROP would drop the staging table
    # the instant it was created and every \copy into it would fail with
    # "relation does not exist". That is exactly what happened the first time
    # this ran, silently, because the failure is only a note.
    q >/dev/null 2>&1 <<SQL || note "could not carry $table across"
BEGIN;
CREATE TEMP TABLE carry_stage (LIKE "$table" INCLUDING DEFAULTS) ON COMMIT DROP;
\copy carry_stage FROM '$dir/carry-$table.csv' WITH (FORMAT csv, HEADER)
INSERT INTO "$table" SELECT * FROM carry_stage ON CONFLICT DO NOTHING;
$extra
COMMIT;
SQL
  done
}

# Runs a restore, whatever kind. The sidecar's do_restore_wiki does the work
# and prints a JSON object describing what it did; everything around it here
# is the same for both kinds: the directory, the phases, the log, and the row
# that is written afterwards from the restored database.
# A queued restore the wiki stopped waiting for (canceled, or abandoned by
# the app when no agent came) is ended here instead of run. Nothing was
# touched, and maintenance belongs to whatever the wiki is waiting for now,
# so it is left alone.
restore_withdrawn() {
  local id="$1"
  RESTORE_JOB_ID="$id" restore_canceled || return 1
  log "restore job $id was withdrawn before it started; not running it"
  q -v id="$id" >/dev/null 2>&1 <<'SQL' || true
UPDATE "BackupJobs"
   SET "Status" = 'failed', "FinishedAt" = now(),
       "Error" = 'Canceled, or no backup agent picked it up in time, before it started. Nothing was changed.'
 WHERE "Id" = :'id'::uuid;
SQL
  # A cancel that arrived as the flag, for the job the wiki is waiting for,
  # still leaves maintenance on: that one is this job's to clear.
  RESTORE_JOB_ID="$id" restore_clear_maintenance
  return 0
}

run_restore_wiki_job() {
  local id="$1" target="$2" options="${3:-}" dir logf status=succeeded err="" extra
  restore_withdrawn "$id" && return 0
  export RESTORE_JOB_ID="$id"
  dir="$(restore_dir_for "$id")"
  logf="$dir/log"
  : > "$logf"
  printf '%s' "$options" > "$dir/options"
  printf '%s' "$target" > "$dir/target"
  date -u +%FT%TZ > "$dir/started"

  log "RESTORE requested: $target ${options}"
  if do_restore_wiki "$id" "$target" "$options" "$dir" >>"$logf" 2>&1; then
    extra="$(cat "$dir/result" 2>/dev/null || echo '{}')"
  else
    status=failed
    err="$(first_error "$logf")"
    extra="$(cat "$dir/result" 2>/dev/null || echo '{}')"
  fi
  cat "$logf"
  finish_job "$id" "$status" "$err" "$logf" "$extra"
  if [ "$status" = succeeded ]; then
    set_message "Restored from $target."
  else
    set_message "Restore from $target FAILED: $err"
    restore_clear_maintenance
  fi
  unset RESTORE_JOB_ID
}

run_restore_undo_job() {
  local id="$1" target="$2" options="${3:-}" dir logf status=succeeded err="" extra
  restore_withdrawn "$id" && return 0
  export RESTORE_JOB_ID="$id"
  dir="$(restore_dir_for "$id")"
  logf="$dir/log"
  : > "$logf"
  log "RESTORE UNDO requested"
  if do_restore_undo "$id" "$target" "$options" "$dir" >>"$logf" 2>&1; then
    extra="$(cat "$dir/result" 2>/dev/null || echo '{}')"
  else
    status=failed
    err="$(first_error "$logf")"
    extra='{}'
    restore_clear_maintenance
  fi
  cat "$logf"
  finish_job "$id" "$status" "$err" "$logf" "$extra"
  unset RESTORE_JOB_ID
}

run_restore_discard_job() {
  local id="$1" target="$2" options="${3:-}" logf=/tmp/discard.log status=succeeded err=""
  : > "$logf"
  if ! do_restore_discard "$target" "$options" >>"$logf" 2>&1; then
    status=failed
    err="$(first_error "$logf")"
  fi
  cat "$logf"
  finish_job "$id" "$status" "$err" "$logf"
}

# Gives the wiki back when a restore did not happen. The app clears this too
# when it sees the job fail; doing it here as well means a failure that the
# app never sees (it was restarting) still ends the read-only state.
# Only while the wiki is waiting for this job (or for none): a failed or
# withdrawn job must not end the maintenance of a newer restore.
restore_clear_maintenance() {
  q -v job="${RESTORE_JOB_ID:-}" >/dev/null 2>&1 <<'SQL' || true
UPDATE "SiteSettings"
   SET "RestoreJobId" = NULL, "RestoreStartedAt" = NULL, "RestoreCancelRequestedAt" = NULL
 WHERE :'job' = '' OR "RestoreJobId" IS NULL OR "RestoreJobId"::text = :'job';
SQL
}

# Records that a restore finished, in the database that now exists. This is
# what the app polls for, and what makes it restart into the restored wiki.
restore_record_done() {
  local id="$1" from="$2" kept="$3"
  q -v id="$id" -v from="$from" -v kept="$kept" >/dev/null <<'SQL'
UPDATE "SiteSettings"
   SET "RestoreJobId" = NULL, "RestoreStartedAt" = NULL, "RestoreCancelRequestedAt" = NULL,
       "LastRestoredAt" = now(), "LastRestoreJobId" = :'id'::uuid,
       "LastRestoreFrom" = :'from',
       "KeptCopyJson" = NULLIF(:'kept', '');
SQL
}

# Closes jobs this agent left running. An agent runs one job at a time, in
# this loop, so at the top of a pass nothing of its own can really be
# running: a row that says so belongs to a run that was interrupted (the
# container stopped mid-job) or to a database that was swapped in with the
# row frozen inside it. Either way the page would poll it for ever.
#
# One is left alone: the restore the database records as in progress. A
# point-in-time restore is carried out by the db container, and this
# sidecar restarting while it waits must not end maintenance under a
# cluster that is still being rewritten. If that column does not exist yet
# (a database from before 9.4), the query fails and nothing is touched.
#
# A restore or an undo says how it ended in its directory: a result file, or
# "restore complete" in the log of one from before the result file existed.
# Anything else is recorded as interrupted, which is the truth: nobody knows
# how it ended, and the log in the directory is where to look.
reconcile_stale_jobs() {
  local rows line id kind dir status err extra at
  rows="$(q 2>/dev/null <<'SQL'
SELECT j."Id" || '|' || j."Kind"
  FROM "BackupJobs" j
 WHERE j."Agent" = :'agent' AND j."Status" = 'running'
   AND j."Id" IS DISTINCT FROM (SELECT "RestoreJobId" FROM "SiteSettings" LIMIT 1);
SQL
)" || return 0
  [ -z "$rows" ] && return 0
  while IFS='|' read -r id kind; do
    [ -n "$id" ] || continue
    status=failed extra="" at=""
    err="Interrupted: the backup agent stopped before this finished, so how it ended was not recorded."
    dir="$RESTORE_ROOT/$id"
    case "$kind" in
      restore|restore-undo)
        if [ -s "$dir/result" ]; then
          status=succeeded err="" extra="$(cat "$dir/result")"
          at="$(date -u -r "$dir/result" +%FT%TZ 2>/dev/null)"
        elif [ -f "$dir/log" ] && grep -qE 'restore complete|\[undo\] done|\[restore\] done' "$dir/log"; then
          status=succeeded err=""
          at="$(date -u -r "$dir/log" +%FT%TZ 2>/dev/null)"
        elif [ -d "$dir" ]; then
          err="Interrupted: the backup agent stopped before this finished, so how it ended was not recorded. Its log is in $dir."
        fi ;;
    esac
    log "closing $kind job $id left running: $status"
    q -v id="$id" -v status="$status" -v err="$err" -v extra="$extra" -v at="$at" >/dev/null 2>&1 <<'SQL' || true
UPDATE "BackupJobs"
   SET "Status" = :'status',
       "FinishedAt" = coalesce(NULLIF(:'at', '')::timestamptz, now()),
       "Error" = NULLIF(:'err', ''),
       "ResultJson" = coalesce(NULLIF(:'extra', '')::jsonb, "ResultJson")
 WHERE "Id" = :'id'::uuid AND "Status" = 'running';
SQL
  done <<<"$rows"
}

# Default hooks; the sidecars override what they need.
after_backup() { echo '{}'; }
# Offsite targets (dev-plan 9.2). Each sidecar defines what it can do.
offsite_tick() { :; }
do_copy_offsite() { echo "This agent has no target to copy to."; return 1; }

# Copying to a target that is only there sometimes (9.2 step 4). Reported
# like any other job, so it shows in the same list with the same log.
run_copy_job() {
  local id="$1" slot="$2" log=/tmp/copy-job.log status=succeeded err=""
  : > "$log"
  if ! do_copy_offsite "$slot" >>"$log" 2>&1; then
    status=failed
    err="$(tail -n 1 "$log" | cut -c1-500)"
  fi
  finish_job "$id" "$status" "$err" "$log"
}
restore_details() { :; }

# Test connection (the Storage targets screen): reach a slot and open the
# repository there, changing nothing. Each sidecar tests the repository it
# writes; the cloud has one of each, so it is asked of both.
do_test_target() { echo "This agent writes to no storage target."; return 1; }

# The test's last line is its answer, in words for the card, and is kept as
# the job's summary whether it passed or not.
run_test_target_job() {
  local id="$1" slot="$2" log=/tmp/test-target.log status=succeeded err="" summary extra
  : > "$log"
  if ! do_test_target "$slot" >>"$log" 2>&1; then
    status=failed
    err="$(tail -n 1 "$log" | cut -c1-500)"
  fi
  summary="$(tail -n 1 "$log")"
  extra="$(q -v s="$summary" <<'SQL'
SELECT jsonb_build_object('summary', left(:'s', 500))::text;
SQL
)" || extra=""
  finish_job "$id" "$status" "$err" "$log" "$extra"
}

# Shallow-merges two JSON objects written by these scripts (no nesting of
# the same key). Done in Postgres; there is no jq in either image.
merge_json() {
  q -v a="${1:-}" -v b="${2:-}" <<'SQL'
SELECT (coalesce(NULLIF(:'a', '')::jsonb, '{}') || coalesce(NULLIF(:'b', '')::jsonb, '{}'))::text;
SQL
}

run_agent() {
  trap 'kill "$HB_PID" 2>/dev/null; exit 0' TERM INT
  wait_for_db
  heartbeat_loop &
  HB_PID=$!

  local started=0 first=1 legacy_next=0 job id kind target trigger
  while true; do
    if tables_ready; then
      if [ "$started" = 0 ]; then
        if agent_start; then
          started=1
          log "registered; interval ${INTERVAL_HOURS}h"
        fi
      fi
      if [ "$started" = 1 ]; then
        observe_policy >/dev/null || log "WARN: could not read the retention policy"
        sync_inventory missing || log "WARN: inventory sync failed"
        offsite_tick || log "WARN: offsite tick failed"
        reconcile_stale_jobs

        while job="$(claim_job)" && [ -n "$job" ]; do
          IFS='|' read -r id kind target options <<<"$job"
          case "$kind" in
            backup)          log "backup requested"; run_backup_job "$id" ;;
            restore-test)    run_restore_job "$id" "$target" ;;
            copy-offsite)    run_copy_job "$id" "$target" ;;
            test-target)     run_test_target_job "$id" "$target" ;;
            restore)         run_restore_wiki_job "$id" "$target" "$options" ;;
            restore-undo)    run_restore_undo_job "$id" "$target" "$options" ;;
            restore-discard) run_restore_discard_job "$id" "$target" "$options" ;;
            # Anything else is a kind this sidecar predates. Failing loudly is
            # the whole point: until 9.4 an unknown kind fell through to
            # `run_backup_job`, so an older sidecar handed a `restore` would
            # have taken a backup and reported success, which is the most
            # dangerous possible answer to "please restore".
            *)
              log "ERROR: unknown job kind '$kind'"
              fail_unknown_job "$id" "$kind"
              ;;
          esac
        done

        if backup_due; then
          trigger=scheduled
          [ "$first" = 1 ] && trigger=startup
          if id="$(create_job "$trigger")" && [ -n "$id" ]; then
            log "$trigger backup"
            run_backup_job "$id"
          fi
        fi
        first=0
      fi
    else
      # The app has not migrated yet (first boot, or an older app). Back up
      # on the interval as before 9.1, and remove nothing.
      if [ "$(date +%s)" -ge "$legacy_next" ]; then
        log "backup tables not found; taking a backup without recording it"
        if legacy_backup; then
          legacy_next=$(( $(date +%s) + INTERVAL_HOURS * 3600 ))
        else
          log "ERROR: backup failed; retrying in 15 minutes"
          legacy_next=$(( $(date +%s) + 900 ))
        fi
      fi
    fi
    sleep "$POLL_SECONDS" &
    wait $!
  done
}
