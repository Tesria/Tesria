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
 WHERE "Agent" = :'agent' AND "Status" = 'running';
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
heartbeat() {
  touch /tmp/heartbeat
  local free total
  read -r free total < <(df -B1 --output=avail,size "$VOLUME" 2>/dev/null | tail -1)
  q -v free="${free:-}" -v total="${total:-}" >/dev/null 2>&1 <<'SQL' || true
UPDATE "BackupAgents"
   SET "LastSeenAt" = now(),
       "VolumeFreeBytes" = NULLIF(:'free', '')::bigint,
       "VolumeTotalBytes" = NULLIF(:'total', '')::bigint,
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

# Claims the oldest requested job for this agent. Prints `id|kind|target`.
claim_job() {
  q <<'SQL'
UPDATE "BackupJobs" SET "Status" = 'running', "StartedAt" = now()
 WHERE "Id" = (SELECT "Id" FROM "BackupJobs"
                WHERE "Agent" = :'agent' AND "Status" = 'requested'
                ORDER BY "RequestedAt" LIMIT 1
                  FOR UPDATE SKIP LOCKED)
RETURNING "Id" || '|' || "Kind" || '|' || coalesce("Target", '');
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

# Default hooks; the sidecars override what they need.
after_backup() { echo '{}'; }
restore_details() { :; }

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

        while job="$(claim_job)" && [ -n "$job" ]; do
          IFS='|' read -r id kind target <<<"$job"
          if [ "$kind" = "restore-test" ]; then
            run_restore_job "$id" "$target"
          else
            log "backup requested"
            run_backup_job "$id"
          fi
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
