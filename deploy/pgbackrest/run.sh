#!/usr/bin/env bash
# The `pgbackrest` sidecar (dev-plan 9.1): physical backups for point-in-time
# recovery. Creates the stanza, then runs the loop in common.sh (schedule, job
# queue, retention); this file is what is specific to pgBackRest. Runs as root
# and calls pgBackRest as the postgres user, which can read PGDATA (shared
# read-only) and reach Postgres over the shared socket.
AGENT=physical
VOLUME=/var/lib/pgbackrest
STANZA=main
# This sidecar's view of the live wiki: the database itself (dev-plan 9.3).
WIKI_PATH=/var/lib/postgresql
FULL_EVERY_DAYS="${BACKUP_FULL_EVERY_DAYS:-7}"
TOOL_VERSION="$(pgbackrest version 2>/dev/null)"

# shellcheck source=../backup/common.sh
. /opt/tesria/common.sh
# shellcheck source=offsite.sh
. /scripts/offsite.sh
# shellcheck source=cipher.sh
. /scripts/cipher.sh

# The backup key (dev-plan 25.1): each container has its own filesystem, so
# the drop-in the db container wrote is not here.
log "$(cipher_write_conf)"

# The same drop-in the db container writes, written again here: each
# container has its own filesystem, and the sidecar needs repo2 to create
# the stanza on it, back up to it and verify it. Written before the stanza
# check below, because that check is what first touches repo2.
log "$(offsite_write_conf)"

# pgBackRest writes here; ensure the postgres user owns them on the shared repo.
mkdir -p /var/lib/pgbackrest /var/spool/pgbackrest /var/log/pgbackrest
chown -R postgres:postgres /var/lib/pgbackrest /var/spool/pgbackrest /var/log/pgbackrest

pgbr() { gosu postgres pgbackrest --stanza="$STANZA" "$@"; }

# pgBackRest with the local repository alone: the backup key, and not the
# cloud's drop-in. `check` and `stanza-create` take no --repo and fail as a
# whole when any one repository does, so without this a cloud target that is
# down, or a wrong cloud secret, stopped the local backups too (T8-008).
LOCAL_CONF_DIR=/etc/pgbackrest/local.d
mkdir -p "$LOCAL_CONF_DIR"
CIPHER_CONF="$LOCAL_CONF_DIR/tesria-cipher.conf" cipher_write_conf >/dev/null 2>&1 || true
pgbr_local() { gosu postgres pgbackrest --stanza="$STANZA" --config-include-path="$LOCAL_CONF_DIR" "$@"; }

# How long a backup waits for its last WAL segment to reach the local
# repository. pgBackRest's default is a minute, which is plenty while every
# repository takes WAL. While the cloud refuses it, Postgres retries
# archiving only once a minute after three failures, so the segment reaches
# the local repository up to a minute late, and every local backup failed
# with "was not archived before the 60000ms timeout" (found testing T8-008).
# Every local backup also names --repo=1: without it pgBackRest reads each
# repository's catalog first, and a cloud that refuses the key failed them.
ARCHIVE_WAIT=300

# One repository's status, from `info --output=json` already written to
# /tmp/repo-status.json, as `code|backups|message`. pgBackRest exits 0
# whatever happened to a repository and reports it here instead: 0 is fine,
# 1 is no stanza yet, 2 is a stanza with no backups yet, and anything else a
# repository it could not read. A wrong passphrase makes pgBackRest quote the
# undecryptable bytes, which are not UTF-8, so all but printable ASCII goes.
repo_status() {
  LC_ALL=C tr -d '\000-\037\177-\377' </tmp/repo-status.json >/tmp/repo-status.clean
  q -v key="$1" 2>/dev/null <<'SQL'
CREATE TEMP TABLE rs (doc text);
\copy rs FROM '/tmp/repo-status.clean' WITH (FORMAT csv, DELIMITER E'\x01', QUOTE E'\x02')
WITH s AS (SELECT (string_agg(doc, '')::jsonb) -> 0 AS j FROM rs)
SELECT (r -> 'status' ->> 'code') || '|'
       || (SELECT count(*) FROM jsonb_array_elements(coalesce(j -> 'backup', '[]'::jsonb)) AS b
            WHERE (b -> 'database' ->> 'repo-key')::int = :'key'::int) || '|'
       || translate(coalesce(r -> 'status' ->> 'message', ''), E'|\n', '  ')
  FROM s, jsonb_array_elements(coalesce(j -> 'repo', '[]'::jsonb)) AS r
 WHERE (r ->> 'key')::int = :'key'::int;
SQL
}

# Why the local repository cannot be used, in words for the Backups page;
# returns 1, printing nothing, when it can. The case that matters most is a
# backup key that does not open it (T1-030): every backup then fails, and
# before this the page said Healthy while the agent sat in its start-up check.
local_repo_problem() {
  local line code msg
  pgbr --repo=1 --log-level-console=off --output=json info >/tmp/repo-status.json 2>/dev/null
  line="$(repo_status 1)"
  [ -n "$line" ] || return 1
  IFS='|' read -r code _ msg <<<"$line"
  case "$code" in 0|1|2) return 1 ;; esac
  case "$msg" in
    *CryptoError*|*FormatError*|*"outside of section"*)
      echo "The backup key does not open the local backup repository: its backups were made with a different key. Put the right key in .env as BACKUP_ENCRYPTION_KEY (it is in backup-key.txt if Tesria made it), run docker compose up -d, then docker compose restart db pgbackrest." ;;
    *)
      echo "pgBackRest cannot read the local backup repository: ${msg:0:300}" ;;
  esac
  return 0
}

# A cloud failure in words for its card, from pgBackRest's own message or
# the whole of its output: the provider's reason (SignatureDoesNotMatch, say)
# is on a line of its own, below the one that says ERROR.
cloud_reason() {
  local msg first
  first="$(printf '%s\n' "$1" | grep -m1 -E 'ERROR|WARN' || true)"
  [ -n "$first" ] || first="$1"
  first="$(printf '%s' "$first" | LC_ALL=C tr -d '\000-\037\177-\377' | sed 's/^.*P00 *[A-Z]*: //')"
  msg="$(printf '%s' "$1" | LC_ALL=C tr -d '\000-\037\177-\377')"
  case "$msg" in
    *CryptoError*|*FormatError*|*"outside of section"*)
      echo "Reached the database repository, but the passphrase does not open it. Check OFFSITE_CLOUD_PASSPHRASE against the one it was created with." ;;
    *SignatureDoesNotMatch*)
      echo "The storage provider refused the secret for this key. Check OFFSITE_CLOUD_SECRET." ;;
    *InvalidAccessKeyId*)
      echo "The storage provider does not recognize the key. Check OFFSITE_CLOUD_KEY." ;;
    *"403"*|*AccessDenied*)
      echo "The storage provider refused the key or the secret (403). Check OFFSITE_CLOUD_KEY and OFFSITE_CLOUD_SECRET, and that the key may use ${OFFSITE_CLOUD_BUCKET:-the bucket}." ;;
    *"404"*|*NoSuchBucket*)
      echo "Bucket ${OFFSITE_CLOUD_BUCKET:-} was not found (404). Check OFFSITE_CLOUD_BUCKET; if it does exist, try the other OFFSITE_CLOUD_URI_STYLE." ;;
    *HostConnectError*|*"unable to get address"*|*"unable to connect"*)
      echo "Could not reach ${OFFSITE_CLOUD_ENDPOINT:-the storage provider}: ${first##*: }." ;;
    *"do not match the database"*)
      echo "The database repository at ${OFFSITE_CLOUD_PATH:-/tesria} belongs to a different database. Point OFFSITE_CLOUD_PATH somewhere new for this one."  ;;
    *)
      echo "Could not use the database repository: ${first:0:300}" ;;
  esac
}

# What the cloud database card has to say that pgBackRest's catalog does
# not: why the start-up check failed, why the last cloud backup or verify
# failed. Held in memory, so that the status sync on the next pass does not
# wipe it, and cleared by whatever proves it wrong.
CLOUD_START_PROBLEM=""
CLOUD_BACKUP_ERROR=""
CLOUD_VERIFY_ERROR=""
CLOUD_RETRY_AT=0

log "waiting for the postgres socket"
until gosu postgres pg_isready -h /var/run/postgresql -q; do sleep 2; done

# Create the stanza (idempotent). stanza-create covers every configured
# repository, so enabling an offsite target on a running instance creates its
# stanza here. It has to happen before archive_command's pushes to the new
# repository can succeed, and until they do, WAL is held: see the
# archive-push findings in the dev plan. When the cloud is what fails, the
# local stanza is still created, on its own.
pgbr stanza-create >/dev/null 2>&1 || pgbr_local stanza-create >/dev/null 2>&1 || true

# Validate archiving, without ever holding the agent (T8-008, T1-030). This
# used to loop until `check` passed, so a wrong cloud secret or a key that
# does not open the repository left the agent unregistered: no local
# backups, no answer to Test Connection, and a card that said Healthy until
# it said "Agent offline" a quarter of an hour later. Now a cloud failure is
# the cloud card's to report and the local backups carry on; a local failure
# is recorded straight away as the failed backup it is.
LOCAL_PROBLEM=""
startup_check() {
  local tries=0
  while :; do
    if pgbr check >/tmp/check.log 2>&1; then
      log "pgBackRest check passed"
      return 0
    fi
    if offsite_cloud_enabled && ! local_repo_problem >/dev/null; then
      # The local repository reads. If the cloud's does not, that is the
      # whole answer, found in seconds; if it does, the local repository gets
      # a check of its own, with room for Postgres's archiver, which retries
      # only once a minute while the cloud refuses WAL.
      local line code msg
      timeout 120 gosu postgres pgbackrest --stanza="$STANZA" --log-level-console=off --output=json --repo=2 info \
        >/tmp/repo-status.json 2>/dev/null
      line="$(repo_status 2)"
      IFS='|' read -r code _ msg <<<"$line"
      case "${code:-}" in
        0|2) pgbr_local check --archive-timeout=180 >/tmp/check-local.log 2>&1 && code=ok
             msg="$(cat /tmp/check.log)" ;;
        *)   code=ok
             [ -n "$msg" ] || msg="$(cat /tmp/check.log)" ;;
      esac
      if [ "$code" = ok ]; then
        CLOUD_START_PROBLEM="$(cloud_reason "$msg")"
        log "WARNING: the cloud repository failed pgBackRest's check; local backups carry on. $CLOUD_START_PROBLEM"
        return 0
      fi
    fi
    tries=$(( tries + 1 ))
    [ "$tries" -ge 3 ] && break
    log "ERROR: pgBackRest check failed; trying again in 20 seconds"
    sleep 20
  done
  LOCAL_PROBLEM="$(local_repo_problem)" \
    || LOCAL_PROBLEM="pgBackRest's check failed: $(grep -m1 ERROR /tmp/check.log | cut -c1-300)"
  log "ERROR: $LOCAL_PROBLEM"
  log "starting anyway, so the Backups page can say so; a backup is due now"
}
startup_check

# Called by the loop once this agent is registered. A local problem found at
# start is reported now, as a failed backup, rather than at the next
# scheduled run a day away; a cloud one goes on the cloud card.
agent_started_hook() {
  [ -n "$LOCAL_PROBLEM" ] && set_next_run 0
  return 0
}

# A full backup when there is none, or the newest is FULL_EVERY_DAYS old;
# an incremental otherwise. Read from the inventory rather than a counter
# held in memory: that counter reset on every restart, and every restart
# took a full backup and expired the oldest.
do_backup() {
  local type problem
  # Said in words before pgBackRest says it in bytes: a key that does not
  # open the repository fails every backup with a FormatError quoting the
  # undecryptable file.
  if problem="$(local_repo_problem)"; then
    echo "ERROR: $problem"
    return 1
  fi
  type="$(q -v full_every="$FULL_EVERY_DAYS" <<'SQL'
SELECT CASE WHEN max("StartedAt") IS NULL
              OR max("StartedAt") < now() - make_interval(days => :'full_every'::int)
            THEN 'full' ELSE 'incr' END
  FROM "Backups"
 WHERE "Agent" = 'physical' AND "Type" = 'full' AND "RemovedAt" IS NULL AND "Error" IS NULL;
SQL
)" || return 1
  echo "[physical] $type backup"
  pgbr --repo=1 backup --archive-timeout="$ARCHIVE_WAIT" --type="$type"
}

# Before the tables exist: a full when this process has taken none, as the
# old loop did. pgbackrest.conf's retention keeps everything.
LEGACY_COUNT=0
legacy_backup() {
  local type=incr
  (( LEGACY_COUNT % 7 == 0 )) && type=full
  LEGACY_COUNT=$(( LEGACY_COUNT + 1 ))
  pgbr --repo=1 backup --archive-timeout="$ARCHIVE_WAIT" --type="$type"
}

do_restore_test() {
  bash /scripts/verify.sh --set="$1"
}

# --- Point-in-time recovery from the admin page (dev-plan 9.4) -------------

RESTORE_ROOT=/var/run/postgresql/tesria-restore

# The newest good backup in the local repository that ended at or before a
# time, as `label stop-epoch time-epoch`, or nothing. A backup that ended
# after the time cannot be rolled forward to it.
restore_set_for() {
  pgbr --repo=1 --log-level-console=off --output=json info >/tmp/restore-sets.json 2>/dev/null || return 1
  q -v at="$1" 2>/dev/null <<'SQL'
CREATE TEMP TABLE sets (doc text);
\copy sets FROM '/tmp/restore-sets.json' WITH (FORMAT csv, DELIMITER E'\x01', QUOTE E'\x02')
WITH s AS (SELECT (string_agg(doc, '')::jsonb) -> 0 AS j FROM sets),
     b AS (SELECT x ->> 'label' AS label, (x -> 'timestamp' ->> 'stop')::bigint AS stop
             FROM s, jsonb_array_elements(coalesce(j -> 'backup', '[]'::jsonb)) AS x
            WHERE NOT coalesce((x ->> 'error')::boolean, false)),
     t AS (SELECT floor(extract(epoch FROM :'at'::timestamptz))::bigint AS at)
SELECT b.label || ' ' || b.stop || ' ' || t.at
  FROM b, t
 WHERE b.stop <= t.at
 ORDER BY b.stop DESC
 LIMIT 1;
SQL
}

# Roll the whole cluster back to a moment, or to the end of one backup.
#
# This sidecar cannot do the restore itself: pgBackRest writes into the data
# directory with Postgres stopped, and stopping Postgres from here would need
# the Docker socket. So the work is split. Everything that needs judgment
# happens here, where the job and the bounds are: the checks, the safety
# backup, the WAL switch, the carry-across. Then one request file goes onto
# the volume this container shares with `db`, and the supervisor there stops
# its own database, runs pgBackRest and starts it again.
do_restore_wiki() {
  local id="$1" target="$2" options="$3" dir="$4"
  local at args

  # The options are the app's: {"mode":"pitr","at":"..."} with the pipes
  # stripped by claim_job. A time means point-in-time; no time means the end
  # of the named backup, which is --type=immediate against that set.
  at="$(printf '%s' "$options" | sed -n 's/.*"at" *: *"\([^"]*\)".*/\1/p')"
  # An undo (do_restore_undo) goes back to the end of the timeline the last
  # restore replaced, rather than to a moment on it.
  local undo=""
  printf '%s' "$options" | grep -Eq '"undo" *: *true' && undo=1

  # Both values end up on pgBackRest's command line, and the job row they
  # come from is one the app's database role may write. So each must be
  # exactly the shape the app writes, a timestamp or a backup label, and
  # nothing that could become an extra option (the 14.1 review).
  if [ -n "$at" ] && ! printf '%s' "$at" | grep -Eq '^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}(\.[0-9]{1,7})?([+-][0-9]{2}:[0-9]{2}|Z)$'; then
    echo "ERROR: the restore time is not a timestamp; refusing it"
    return 1
  fi
  if [ -n "$target" ] && ! printf '%s' "$target" | grep -Eq '^[0-9]{8}-[0-9]{6}F(_[0-9]{8}-[0-9]{6}[DI])?$'; then
    echo "ERROR: the backup to restore is not a backup label; refusing it"
    return 1
  fi

  if [ -n "$at" ] && [ -n "$undo" ]; then
    # Undoing a point-in-time restore (t8-R01). The safety backup is the
    # latest one before the moment that restore began, and the WAL switch
    # after it put everything up to that moment in the archive. So the undo
    # restores that backup and replays its own timeline to the end of what
    # was archived. A time target cannot do this: nothing was written on
    # that timeline after the moment, so Postgres never sees the target and
    # stops with "recovery ended before configured recovery target was
    # reached". Without --target-timeline=current it followed the restore's
    # newer timeline instead, which forked earlier, and pgBackRest refused
    # with error [058]. (No --target-action: a recovery with no target
    # promotes by itself at the end.)
    local set_line set_label set_stop at_epoch
    set_line="$(restore_set_for "$at")"
    read -r set_label set_stop at_epoch <<<"$set_line"
    if [ -z "$set_label" ]; then
      echo "ERROR: no backup in the local repository ends at or before $at, so there is nothing to go back to"
      return 1
    fi
    args="--set=$set_label --type=default --target-timeline=current --delta"
    echo "[undo] restoring backup $set_label and replaying its timeline to the moment the restore began ($at)"
  elif [ -n "$at" ]; then
    # The backup set is chosen here, not by pgBackRest (KI-19, T8-002). Its
    # own choice parses the time, and it takes only "YYYY-MM-DD HH:MM:SS",
    # never the ISO 8601 the app sends: every point-in-time restore from the
    # page failed with error [029]. Given --set, pgBackRest hands the time to
    # Postgres untouched, and Postgres reads ISO 8601 as written. Choosing
    # also covers a time at the very end of a backup, which pgBackRest's own
    # choice refuses ("stop time less than").
    local set_line set_label set_stop at_epoch
    set_line="$(restore_set_for "$at")"
    read -r set_label set_stop at_epoch <<<"$set_line"
    if [ -z "$set_label" ]; then
      echo "ERROR: no backup in the local repository ends at or before $at, so there is nothing to roll forward from"
      return 1
    fi
    # Along the timeline of the backup restored from (t8-R01, the 0.8.3
    # retest). pgBackRest otherwise follows the newest timeline, and after a
    # point-in-time restore that one forked off earlier than the moments
    # before it, the undo's target among them: every undo failed with error
    # [058]. The backup chosen above is the latest one before the target, so
    # its timeline is the history the target belongs to.
    if [ $(( at_epoch - set_stop )) -lt 2 ]; then
      # The end of that backup, exactly. A time target this close to it
      # could fall before the point the backup is consistent at, and
      # Postgres refuses to open a cluster stopped short of that.
      args="--set=$set_label --type=immediate --target-timeline=current --target-action=promote --delta"
      echo "[restore] restoring to the end of backup $set_label ($at)"
    else
      args="--set=$set_label --type=time --target=$at --target-timeline=current --target-action=promote --delta"
      echo "[restore] point-in-time recovery to $at, from backup $set_label"
    fi
  elif [ -n "$target" ]; then
    args="--set=$target --type=immediate --target-timeline=current --target-action=promote --delta"
    echo "[restore] restoring to the end of backup $target"
  else
    echo "ERROR: neither a time nor a backup was given"
    return 1
  fi

  # The repository has to be sound before the cluster is replaced from it.
  echo "[restore] verifying the repository first"
  # The local one, which is the one restored from: a cloud target that is
  # down must not stand in the way.
  pgbr --repo=1 verify >/dev/null 2>&1 || { echo "ERROR: the backup repository did not verify; refusing to restore from it"; return 1; }

  if restore_canceled; then
    echo "ERROR: canceled before anything was changed"
    return 1
  fi

  # The safety backup, and then a WAL switch, so that everything up to this
  # moment is in the repository. Together they are what makes this restore
  # undoable: the undo is another point-in-time restore, to now.
  echo "[restore] taking a safety backup before the cluster is replaced"
  pgbr --repo=1 backup --archive-timeout="$ARCHIVE_WAIT" --type=incr || { echo "ERROR: the safety backup failed; nothing has been changed"; return 1; }
  q -c "SELECT pg_switch_wal();" >/dev/null 2>&1 || true
  sleep 5
  printf '%s' "$(date -u +%FT%TZ)" > "$dir/safety"

  [ -n "$dir" ] && restore_export_carry "$dir"

  if restore_canceled; then
    echo "ERROR: canceled before anything was changed"
    return 1
  fi

  # The point of no return. From here the cluster is being rewritten and the
  # only way back is another restore.
  date -u +%FT%TZ > "$dir/committed"
  echo "[restore] asking the database container to restore the cluster"
  mkdir -p "$RESTORE_ROOT"
  rm -f "$RESTORE_ROOT/done" "$RESTORE_ROOT/status"
  printf '%s\n%s\n' "$id" "$args" > "$RESTORE_ROOT/request.tmp"
  mv "$RESTORE_ROOT/request.tmp" "$RESTORE_ROOT/request"

  # Wait for it. Hours at most: a large cluster restores slowly, and there is
  # nothing useful to do in the meantime except report the phase.
  local waited=0 last=""
  while [ ! -f "$RESTORE_ROOT/done" ]; do
    sleep 5
    waited=$(( waited + 5 ))
    local now
    now="$(tail -n 1 "$RESTORE_ROOT/status" 2>/dev/null)"
    if [ -n "$now" ] && [ "$now" != "$last" ]; then
      echo "[restore] $now"
      last="$now"
    fi
    if [ "$waited" -gt 21600 ]; then
      echo "ERROR: the database container did not finish within six hours"
      return 1
    fi
  done

  if [ "$(cat "$RESTORE_ROOT/done")" != ok ]; then
    echo "ERROR: the database container could not restore the cluster; see its log"
    return 1
  fi

  echo "[restore] the cluster is back; waiting for it to accept connections"
  wait_for_db

  # A restore switches the timeline. pgBackRest can carry on incrementally
  # across one, but a full here is cheap and makes the new timeline's chain
  # stand on its own, which is what somebody restoring from it next month
  # actually needs.
  echo "[restore] taking a full backup on the new timeline"
  pgbr --repo=1 backup --archive-timeout="$ARCHIVE_WAIT" --type=full || echo "WARNING: the full backup failed; take one by hand"

  q >/dev/null 2>&1 <<'SQL' || true
UPDATE "SiteSettings"
   SET "RestoreJobId" = NULL, "RestoreStartedAt" = NULL, "RestoreCancelRequestedAt" = NULL;
SQL
  [ -n "$dir" ] && restore_import_carry "$dir"
  sync_inventory missing >/dev/null 2>&1 || true

  # The attachments (T8-017). A physical backup holds the database and
  # nothing else, so rolling it back brings back rows whose files are gone:
  # a deleted space came back with every picture broken. The backup agent,
  # which has the uploads and their archives, puts back every file the
  # restored database refers to and does not have. Queued rather than done
  # here, because this container has neither.
  echo "[restore] asking the dumps agent to put back any attachment files the restored wiki is missing"
  q >/dev/null 2>&1 <<'SQL' || echo "WARNING: could not queue the attachment check; run it by hand: docker compose exec backup /scripts/restore-uploads.sh"
INSERT INTO "BackupJobs" ("Id", "Agent", "Kind", "Trigger", "Status", "RequestedAt")
VALUES (gen_random_uuid(), 'logical', 'restore-uploads', 'restore', 'requested', now());
SQL

  # No kept database: a point-in-time restore replaces the cluster in place.
  # Its undo is another point-in-time restore, to the moment this one began,
  # which the safety backup and the WAL switch above made reachable.
  local kept
  kept="$(printf '{"jobId":"%s","mode":"pitr","restoredAt":"%s","database":null,"uploads":null,"restoredFrom":"%s","databaseBytes":null,"uploadsBytes":null,"removedAt":null}' \
    "$id" "$(cat "$dir/safety")" "${at:-$target}")"
  restore_record_done "$id" "${at:+$target at $at}${at:-$target}" "$kept"
  printf '{"restoredFrom":"%s","mode":"pitr","safetyAt":"%s"}' "${at:-$target}" "$(cat "$dir/safety")" > "$dir/result"
  echo "[restore] done"
}

# Undoing a point-in-time restore is another one, to the moment the first
# began. There is no kept cluster to swap back, which is why the safety
# backup and the WAL switch are not optional above.
do_restore_undo() {
  local id="$1" target="$2" options="$3" dir="$4"
  local at
  at="$(printf '%s' "$options" | sed -n 's/.*"at" *: *"\([^"]*\)".*/\1/p')"
  [ -n "$at" ] || { echo "ERROR: there is no moment to go back to"; return 1; }
  echo "[undo] rolling back to $at, the moment the restore began"
  do_restore_wiki "$id" "" "{\"mode\":\"pitr\",\"at\":\"$at\",\"undo\":true}" "$dir"
}

# --- The offsite repository (dev-plan 9.2) ---------------------------------

OFFSITE_BACKUP_EVERY_DAYS="${OFFSITE_CLOUD_BACKUP_EVERY_DAYS:-7}"

# Publishes what this instance is configured to do with the cloud slot, with
# every secret reduced to a fingerprint. This row is the only thing the app
# ever sees about an offsite target: the credentials stay in .env and are
# read here and nowhere else.
publish_cloud_target() {
  local enabled=false problem="" type="" endpoint="" bucket="" prefix="" keyfp="" passfp=""
  if [ -n "${OFFSITE_CLOUD_TYPE:-}" ]; then
    type="$OFFSITE_CLOUD_TYPE"
    endpoint="${OFFSITE_CLOUD_ENDPOINT:-}"
    bucket="${OFFSITE_CLOUD_BUCKET:-}"
    prefix="${OFFSITE_CLOUD_PATH:-/tesria}"
    keyfp="$(offsite_fingerprint "${OFFSITE_CLOUD_KEY:-}")"
    passfp="$(offsite_fingerprint "${OFFSITE_CLOUD_PASSPHRASE:-}")"
    if problem="$(offsite_cloud_problem)"; then enabled=false; else enabled=true; problem=""; fi
  fi
  q -v slot=cloud -v enabled="$enabled" -v problem="$problem" -v type="$type" \
    -v endpoint="$endpoint" -v bucket="$bucket" -v prefix="$prefix" \
    -v keyfp="$keyfp" -v passfp="$passfp" >/dev/null 2>&1 <<'SQL' || true
INSERT INTO "BackupTargets" ("Slot", "Kind", "Type", "Location", "Bucket", "Prefix", "Enabled", "Problem",
                             "KeyFingerprint", "PassphraseFingerprint", "UpdatedAt")
VALUES (:'slot', 'database', NULLIF(:'type',''), NULLIF(:'endpoint',''), NULLIF(:'bucket',''), NULLIF(:'prefix',''),
        :'enabled'::boolean, NULLIF(:'problem',''), NULLIF(:'keyfp',''), NULLIF(:'passfp',''), now())
ON CONFLICT ("Slot", "Kind") DO UPDATE
   SET "Type" = EXCLUDED."Type", "Location" = EXCLUDED."Location", "Bucket" = EXCLUDED."Bucket",
       "Prefix" = EXCLUDED."Prefix", "Enabled" = EXCLUDED."Enabled", "Problem" = EXCLUDED."Problem",
       "KeyFingerprint" = EXCLUDED."KeyFingerprint",
       "PassphraseFingerprint" = EXCLUDED."PassphraseFingerprint",
       "UpdatedAt" = now();
SQL
  publish_cloud_budget
}

# OFFSITE_CLOUD_BUDGET_GB, if somebody set one: how much the cloud slot is
# meant to hold, which is what gives its chart a "free" slice. Cloud storage
# has no size of its own, so this is the one number on that card a person
# chose rather than one that was measured, and it is shown as a budget, never
# as space. Written to both of the slot's rows, since either may be the one
# the screen reads first.
#
# Its own statement, so that a sidecar running ahead of the app's migration
# loses the budget and nothing else.
publish_cloud_budget() {
  local gb="${OFFSITE_CLOUD_BUDGET_GB:-}"
  if [ -n "$gb" ] && ! [[ "$gb" =~ ^[0-9]+([.][0-9]+)?$ && ! "$gb" =~ ^0+([.]0+)?$ ]]; then
    log "WARN: OFFSITE_CLOUD_BUDGET_GB='$gb' is not a positive number of gigabytes; ignoring it"
    gb=""
  fi
  [ -n "${OFFSITE_CLOUD_TYPE:-}" ] || gb=""
  q -v gb="$gb" >/dev/null 2>&1 <<'SQL' || true
UPDATE "BackupTargets"
   SET "BudgetBytes" = round(NULLIF(:'gb', '')::numeric * 1073741824)::bigint
 WHERE "Slot" = 'cloud'
   AND "BudgetBytes" IS DISTINCT FROM round(NULLIF(:'gb', '')::numeric * 1073741824)::bigint;
SQL
}

# What repo2 holds, from pgBackRest's own catalog, so the screen and the
# alerts read one source rather than guessing.
sync_cloud_status() {
  offsite_cloud_enabled || return 0
  local line code msg problem=""
  # Every repository, not just repo2: the offsite gap is found by comparing
  # what the two hold, which is more honest than any timestamp. With a time
  # limit, because an unreachable provider would otherwise hold the pass.
  if ! timeout 120 gosu postgres pgbackrest --stanza="$STANZA" --log-level-console=off --output=json info \
         >/tmp/repo-status.json 2>/dev/null; then
    cloud_set_message "The cloud repository could not be read: no answer from ${OFFSITE_CLOUD_ENDPOINT:-the storage provider} within two minutes."
    return 1
  fi
  # A repository pgBackRest could not read is reported here, per repository,
  # and not in the exit code; before this the card said Healthy through it.
  line="$(repo_status 2)"
  IFS='|' read -r code _ msg <<<"$line"
  case "${code:-}" in
    # What went wrong at start is over once the repository reads and WAL is
    # flowing again: a segment is acknowledged only when every repository
    # has it, so no backlog means the cloud is taking them.
    0|2) [ "${WAL_BACKLOG:-0}" -lt 3 ] && CLOUD_START_PROBLEM="" ;;
    1)   problem="There is no database repository at ${OFFSITE_CLOUD_PATH:-/tesria} yet. It is created when the pgbackrest service starts with this target configured: docker compose up -d pgbackrest." ;;
    "")  problem="The cloud repository could not be read." ;;
    *)   problem="$(cloud_reason "$msg")" ;;
  esac
  # The first thing that is wrong, in the order a person would fix them.
  if cloud_left_out; then
    problem="Left out of WAL archiving when the database started, because $(sed -n 2p "$OFFSITE_STATE_DIR/.tesria-cloud-archiving"), so the local backups could carry on. Fix it in .env, then run: docker compose up -d. A new full backup goes to the cloud once it is back."
  fi
  [ -n "$problem" ] || problem="$CLOUD_START_PROBLEM"
  [ -n "$problem" ] || problem="$CLOUD_BACKUP_ERROR"
  [ -n "$problem" ] || problem="$CLOUD_VERIFY_ERROR"
  local readable=f
  case "${code:-}" in 0|2) readable=t ;; esac
  q -v msg="$problem" -v readable="$readable" >/dev/null 2>&1 <<'SQL' || true
CREATE TEMP TABLE repo2 (doc text);
\copy repo2 FROM '/tmp/repo-status.clean' WITH (FORMAT csv, DELIMITER E'\x01', QUOTE E'\x02')
WITH s AS (SELECT (string_agg(doc, '')::jsonb) -> 0 AS j FROM repo2),
     -- Backups this repository holds: repo-key 2, the offsite one, and no
     -- other. Counting the local ones as well (T8-007) showed the local
     -- backup's time and size on the cloud card and, worse, made the cloud
     -- look as if it had a recent full, so it was never given one.
     b AS (SELECT x FROM s, jsonb_array_elements(coalesce(j -> 'backup', '[]'::jsonb)) AS x
            WHERE (x -> 'database' ->> 'repo-key')::int = 2),
     -- The newest WAL segment each repository has. pgBackRest names segments
     -- so that lexical order is time order, so a plain comparison is enough.
     arch AS (SELECT (a -> 'database' ->> 'repo-key')::int AS repo, a ->> 'max' AS maxwal
                FROM s, jsonb_array_elements(coalesce(j -> 'archive', '[]'::jsonb)) AS a)
UPDATE "BackupTargets" t
   -- What it held when it was last read, while it cannot be read now.
   SET "LastBackupAt" = CASE WHEN :'readable' = 't'
           THEN (SELECT max(to_timestamp((x -> 'timestamp' ->> 'stop')::bigint)) FROM b) ELSE t."LastBackupAt" END,
       "BytesStored"  = CASE WHEN :'readable' = 't'
           THEN (SELECT sum((x -> 'info' -> 'repository' ->> 'delta')::bigint) FROM b) ELSE t."BytesStored" END,
       -- Not a timestamp out of the catalog: pgBackRest does not record
       -- when a segment arrived, and pg_stat_archiver is no use because
       -- async archiving tells Postgres "archived" as soon as the segment is
       -- queued, whatever the repository did with it. So: WAL is current
       -- here when this repository's newest segment matches the local one,
       -- and the clock only moves while that holds.
       "LastWalAt"    = CASE
           WHEN (SELECT maxwal FROM arch WHERE repo = 2) IS NOT NULL
            AND (SELECT maxwal FROM arch WHERE repo = 2)
             >= coalesce((SELECT maxwal FROM arch WHERE repo = 1), '')
           THEN now() ELSE t."LastWalAt" END,
       "Message"      = NULLIF(left(:'msg', 2000), ''),
       "UpdatedAt"    = now()
 WHERE t."Slot" = 'cloud' AND t."Kind" = 'database';
SQL
  [ "$readable" = t ]
}

cloud_set_message() {
  q -v msg="$1" >/dev/null 2>&1 <<'SQL' || true
UPDATE "BackupTargets" SET "Message" = NULLIF(left(:'msg', 2000), ''), "UpdatedAt" = now()
 WHERE "Slot" = 'cloud' AND "Kind" = 'database';
SQL
}

# A full backup to the cloud on its own, slower schedule (decision 5): WAL
# streams there continuously, so recovery to a point in time does not wait
# for this. Keyed off what repo2 actually holds, not a timer in memory, so a
# restart does not take a fresh one every time. A failed one waits
# OFFSITE_RETRY_MINUTES, in memory, as the files copy does, rather than
# trying again every minute.
cloud_backup_due() {
  offsite_cloud_enabled || return 1
  [ "$CLOUD_RETRY_AT" -gt "$(date +%s)" ] && return 1
  local due
  due="$(q -v every="$OFFSITE_BACKUP_EVERY_DAYS" <<'SQL'
SELECT CASE WHEN "LastBackupAt" IS NULL
              OR "LastBackupAt" < now() - make_interval(days => :'every'::int)
            THEN 't' ELSE 'f' END
  FROM "BackupTargets" WHERE "Slot" = 'cloud' AND "Kind" = 'database';
SQL
)" || return 1
  [ "$due" = "t" ]
}

run_cloud_backup() {
  log "cloud: full backup to the offsite repository"
  if pgbr --repo=2 --type=full backup >/tmp/cloud-backup.log 2>&1; then
    CLOUD_BACKUP_ERROR=""
    CLOUD_RETRY_AT=0
    sync_cloud_status
    log "cloud: backup complete"
  else
    local err
    err="$(grep -m1 ERROR /tmp/cloud-backup.log || tail -1 /tmp/cloud-backup.log)"
    CLOUD_BACKUP_ERROR="The last offsite backup failed. $(cloud_reason "$(cat /tmp/cloud-backup.log)")"
    CLOUD_RETRY_AT=$(( $(date +%s) + ${OFFSITE_RETRY_MINUTES:-15} * 60 ))
    log "ERROR: cloud backup failed; next attempt in ${OFFSITE_RETRY_MINUTES:-15} minutes: $(printf '%s' "$err" | LC_ALL=C tr -d '\000-\037\177-\377' | cut -c1-300)"
    cloud_set_message "$CLOUD_BACKUP_ERROR"
    return 1
  fi
}

# Daily: manifests, WAL continuity and checksums, which is what catches a
# repository that is quietly rotting rather than one that is plainly down.
cloud_verify_due() {
  offsite_cloud_enabled || return 1
  local due
  due="$(q <<'SQL'
SELECT CASE WHEN "LastVerifyAt" IS NULL OR "LastVerifyAt" < now() - interval '1 day'
            THEN 't' ELSE 'f' END
  FROM "BackupTargets" WHERE "Slot" = 'cloud' AND "Kind" = 'database';
SQL
)" || return 1
  [ "$due" = "t" ]
}

run_cloud_verify() {
  log "cloud: verifying the offsite repository"
  local ok=t out
  out="$(pgbr --repo=2 verify 2>&1 | tail -3 | tr '\n' ' ')" || ok=f
  if [ "$ok" = t ]; then CLOUD_VERIFY_ERROR=""; else CLOUD_VERIFY_ERROR="Verify failed: $out"; fi
  q -v ok="$ok" -v msg="$out" >/dev/null 2>&1 <<'SQL' || true
UPDATE "BackupTargets"
   SET "LastVerifyAt" = CASE WHEN :'ok' = 't' THEN now() ELSE "LastVerifyAt" END,
       "Message" = CASE WHEN :'ok' = 't' THEN NULL ELSE left('Verify failed: ' || :'msg', 2000) END,
       "UpdatedAt" = now()
 WHERE "Slot" = 'cloud' AND "Kind" = 'database';
SQL
  [ "$ok" = t ] && log "cloud: verify passed" || log "ERROR: cloud verify failed: $out"
}

# Called by the loop in common.sh once each pass, after the local work.
offsite_tick() {
  publish_cloud_target
  offsite_cloud_enabled || return 0
  publish_wal_backlog
  # Nothing to back up to, or verify, while the repository cannot be read;
  # the card already says why.
  sync_cloud_status || return 0
  cloud_left_out && return 0
  if cloud_backup_due || cloud_gap_to_close; then
    run_cloud_backup && rm -f "$OFFSITE_STATE_DIR/.tesria-cloud-gap"
  fi
  cloud_verify_due && run_cloud_verify
  return 0
}

# Whether the db container left the cloud out of WAL archiving at start
# (offsite_cloud_refusal, in offsite.sh): the file is on the repository
# volume the two containers share.
cloud_left_out() {
  [ "$(sed -n 1p "$OFFSITE_STATE_DIR/.tesria-cloud-archiving" 2>/dev/null)" = excluded ]
}

# WAL from the time the cloud was left out never reached it, so its restore
# window has a hole there. A new full backup closes it, once the cloud is
# being archived to again.
cloud_gap_to_close() {
  [ -f "$OFFSITE_STATE_DIR/.tesria-cloud-gap" ] && [ "$CLOUD_RETRY_AT" -le "$(date +%s)" ]
}

# Test connection for the database repository (the Storage targets screen).
# `info` against repo2 alone: it reaches the bucket, reads the catalog and
# decrypts it with the passphrase, and changes nothing. `check` would test
# more (a WAL segment actually pushed) but forces a WAL switch to do it,
# which is not what somebody pressing a button to look expects.
#
# pgBackRest exits 0 whatever happened to the repository and reports it per
# repository in the JSON, so the answer is read from there.
do_test_target() {
  local slot="$1" problem json line code msg backups
  [ "$slot" = cloud ] || { echo "The database is only ever copied to the cloud target."; return 1; }
  if problem="$(offsite_cloud_problem)"; then echo "$problem."; return 1; fi
  [ -n "${OFFSITE_CLOUD_TYPE:-}" ] || { echo "No cloud target is configured."; return 1; }

  json="$(timeout 90 gosu postgres pgbackrest --stanza="$STANZA" --log-level-console=off \
            --output=json --repo=2 info 2>&1)" || {
    echo "$json" | tail -n 3
    echo "No answer from ${OFFSITE_CLOUD_ENDPOINT} within 90 seconds."
    return 1
  }
  # A wrong passphrase makes pgBackRest quote the undecryptable bytes in its
  # message, which are not UTF-8 and make the whole document unreadable to
  # Postgres. Its own words are ASCII, so everything else goes.
  printf '%s' "$json" | LC_ALL=C tr -d '\000-\037\177-\377' > /tmp/test-target.json
  line="$(q 2>/dev/null <<'SQL'
CREATE TEMP TABLE t (doc text);
\copy t FROM '/tmp/test-target.json' WITH (FORMAT csv, DELIMITER E'\x01', QUOTE E'\x02')
WITH s AS (SELECT (string_agg(doc, '')::jsonb) -> 0 AS j FROM t)
SELECT (r -> 'status' ->> 'code') || '|'
       || jsonb_array_length(coalesce(j -> 'backup', '[]'::jsonb)) || '|'
       || translate(r -> 'status' ->> 'message', E'|\n', '  ')
  FROM s, jsonb_array_elements(coalesce(j -> 'repo', '[]'::jsonb)) AS r
 WHERE (r ->> 'key')::int = 2;
SQL
)"
  if [ -z "$line" ]; then
    echo "$json" | tail -n 3
    echo "pgBackRest gave an answer that could not be read."
    return 1
  fi
  IFS='|' read -r code backups msg <<<"$line"
  echo "pgBackRest: code ${code}, ${msg:0:300}"

  case "$code" in
    0)
      echo "Connected. The passphrase opens the database repository in bucket ${OFFSITE_CLOUD_BUCKET}, which holds ${backups} backup$([ "$backups" = 1 ] || echo s)." ;;
    2)
      echo "Connected. The passphrase opens the database repository in bucket ${OFFSITE_CLOUD_BUCKET}; it has no backups yet." ;;
    1)
      # The stanza is created when this sidecar starts with the target set.
      echo "Reached bucket ${OFFSITE_CLOUD_BUCKET}, but there is no database repository at ${OFFSITE_CLOUD_PATH:-/tesria} yet. It is created when the pgbackrest service starts with this target configured: docker compose up -d pgbackrest."
      return 1 ;;
    *)
      cloud_reason "$msg"
      return 1 ;;
  esac
}

# How many segments Postgres has handed over that are not yet archived
# everywhere. This sidecar mounts PGDATA read-only, so it can count them.
publish_wal_backlog() {
  local n
  n="$(find /var/lib/postgresql/18/docker/pg_wal/archive_status -name '*.ready' 2>/dev/null | wc -l | tr -d ' ')"
  [ -z "$n" ] && return 0
  WAL_BACKLOG="$n"
  q -v n="$n" >/dev/null 2>&1 <<'SQL' || true
UPDATE "BackupTargets" SET "WalBacklogFiles" = :'n'::int, "UpdatedAt" = now() WHERE "Slot" = 'cloud' AND "Kind" = 'database';
SQL
}

# Mirrors pgBackRest's own catalog into "Backups". The JSON goes through a
# file and \copy: kept forever, it outgrows a command-line argument.
#
# The local repository's catalog only. The page's list, the full-or-incr
# decision, retention and restores are all about the backups on this
# machine; the cloud's are summed on its card instead (T8-007), and asking
# an unreachable cloud here would hold every pass.
sync_inventory() {
  if ! pgbr --repo=1 --log-level-console=off --output=json info >/tmp/inventory.json 2>/dev/null \
     || ! grep -q '"backup"' /tmp/inventory.json; then
    note "pgbackrest info failed; inventory not updated"
    return 1
  fi
  q -v reason="$1" <<'SQL'
CREATE TEMP TABLE info (doc text);
\copy info FROM '/tmp/inventory.json' WITH (FORMAT csv, DELIMITER E'\x01', QUOTE E'\x02')
WITH stanza AS (
  SELECT (string_agg(doc, '')::jsonb) -> 0 AS s FROM info
), backups AS (
  SELECT jsonb_array_elements(coalesce(s -> 'backup', '[]'::jsonb)) AS b FROM stanza
), upserted AS (
  INSERT INTO "Backups" ("Id", "Agent", "Label", "Type", "Prior", "StartedAt", "CompletedAt", "SizeBytes",
                         "DetailJson", "HasUploads", "Error", "FirstSeenAt", "LastSeenAt")
  SELECT gen_random_uuid(), 'physical', b ->> 'label', b ->> 'type', b ->> 'prior',
         to_timestamp((b -> 'timestamp' ->> 'start')::bigint),
         to_timestamp((b -> 'timestamp' ->> 'stop')::bigint),
         -- What this backup adds to the repository. For an incremental,
         -- repository.size would count the files it references too.
         coalesce((b -> 'info' -> 'repository' ->> 'delta')::bigint, 0),
         jsonb_build_object(
           'walStart', b -> 'archive' ->> 'start', 'walStop', b -> 'archive' ->> 'stop',
           'lsnStart', b -> 'lsn' ->> 'start', 'lsnStop', b -> 'lsn' ->> 'stop',
           'databaseBytes', (b -> 'info' ->> 'size')::bigint,
           'repositoryBytes', (b -> 'info' -> 'repository' ->> 'size')::bigint,
           'version', b -> 'backrest' ->> 'version'),
         false,
         CASE WHEN (b ->> 'error')::boolean THEN 'pgBackRest found checksum errors in this backup.' END,
         now(), now()
    FROM backups
  ON CONFLICT ("Agent", "Label") DO UPDATE
     SET "LastSeenAt" = now(), "SizeBytes" = EXCLUDED."SizeBytes", "DetailJson" = EXCLUDED."DetailJson",
         "Error" = EXCLUDED."Error", "RemovedAt" = NULL, "RemovedReason" = NULL
  RETURNING 1
)
UPDATE "Backups" SET "RemovedAt" = now(), "RemovedReason" = :'reason'
 WHERE "Agent" = 'physical' AND "RemovedAt" IS NULL
   AND "Label" NOT IN (SELECT b ->> 'label' FROM backups);
SQL
}

# pgBackRest keeps the newest K full backups (and what depends on them), so
# K is chosen to cover both limits: at least the count, and every full taken
# within the days. Never `expire --set`; the native count keeps the WAL
# rules pgBackRest's own. pgbackrest.conf's retention-full is 9999999, so
# nothing else expires anything.
apply_retention() {
  local keep fulls
  read -r keep fulls < <(q -v kc="$1" -v kd="$2" -F ' ' <<'SQL'
SELECT greatest(:'kc'::int, count(*) FILTER (WHERE "StartedAt" >= now() - make_interval(days => :'kd'::int))),
       count(*)
  FROM "Backups"
 WHERE "Agent" = 'physical' AND "Type" = 'full' AND "RemovedAt" IS NULL AND "Error" IS NULL;
SQL
)
  if [ -z "${keep:-}" ]; then
    note "retention: could not compute the plan; nothing removed"
    return 1
  fi
  if [ "$fulls" -le "$keep" ]; then
    note "retention: $fulls full backup(s), keeping $keep; nothing to remove"
    return 0
  fi
  if [ "$RETENTION_DRY_RUN" = 1 ]; then
    note "retention (dry run): would keep the newest $keep of $fulls full backups"
    return 0
  fi
  note "retention: keeping the newest $keep of $fulls full backups"
  # Every repository, so the cloud keeps to its own retention-full too; the
  # local one alone when the cloud cannot be reached.
  pgbr expire --repo1-retention-full="$keep" >&2 \
    || pgbr expire --repo=1 --repo1-retention-full="$keep" >&2
  sync_inventory retention >&2
}

run_agent
