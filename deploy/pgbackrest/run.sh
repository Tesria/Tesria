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

# The same drop-in the db container writes, written again here: each
# container has its own filesystem, and the sidecar needs repo2 to create
# the stanza on it, back up to it and verify it. Written before the stanza
# check below, because that check is what first touches repo2.
log "$(offsite_write_conf)"

# pgBackRest writes here; ensure the postgres user owns them on the shared repo.
mkdir -p /var/lib/pgbackrest /var/spool/pgbackrest /var/log/pgbackrest
chown -R postgres:postgres /var/lib/pgbackrest /var/spool/pgbackrest /var/log/pgbackrest

pgbr() { gosu postgres pgbackrest --stanza="$STANZA" "$@"; }

log "waiting for the postgres socket"
until gosu postgres pg_isready -h /var/run/postgresql -q; do sleep 2; done

# Create the stanza (idempotent) and validate archiving. A failure used to
# exit the container; now it waits and tries again.
# stanza-create covers every configured repository, so enabling an offsite
# target on a running instance creates its stanza here. It has to happen
# before archive_command's pushes to the new repository can succeed, and
# until they do, WAL is held: see the archive-push findings in the dev plan.
pgbr stanza-create >/dev/null 2>&1 || true
until pgbr check; do
  log "ERROR: pgBackRest check failed; retrying in 60 seconds"
  sleep 60
done

# A full backup when there is none, or the newest is FULL_EVERY_DAYS old;
# an incremental otherwise. Read from the inventory rather than a counter
# held in memory: that counter reset on every restart, and every restart
# took a full backup and expired the oldest.
do_backup() {
  local type
  type="$(q -v full_every="$FULL_EVERY_DAYS" <<'SQL'
SELECT CASE WHEN max("StartedAt") IS NULL
              OR max("StartedAt") < now() - make_interval(days => :'full_every'::int)
            THEN 'full' ELSE 'incr' END
  FROM "Backups"
 WHERE "Agent" = 'physical' AND "Type" = 'full' AND "RemovedAt" IS NULL AND "Error" IS NULL;
SQL
)" || return 1
  echo "[physical] $type backup"
  pgbr backup --type="$type"
}

# Before the tables exist: a full when this process has taken none, as the
# old loop did. pgbackrest.conf's retention keeps everything.
LEGACY_COUNT=0
legacy_backup() {
  local type=incr
  (( LEGACY_COUNT % 7 == 0 )) && type=full
  LEGACY_COUNT=$(( LEGACY_COUNT + 1 ))
  pgbr backup --type="$type"
}

do_restore_test() {
  bash /scripts/verify.sh --set="$1"
}

# --- Point-in-time recovery from the admin page (dev-plan 9.4) -------------

RESTORE_ROOT=/var/run/postgresql/tesria-restore

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
  if [ -n "$at" ]; then
    args="--type=time --target=$at --target-action=promote --delta"
    echo "[restore] point-in-time recovery to $at"
  elif [ -n "$target" ]; then
    args="--set=$target --type=immediate --target-action=promote --delta"
    echo "[restore] restoring to the end of backup $target"
  else
    echo "ERROR: neither a time nor a backup was given"
    return 1
  fi

  # The repository has to be sound before the cluster is replaced from it.
  echo "[restore] verifying the repository first"
  pgbr verify >/dev/null 2>&1 || { echo "ERROR: the backup repository did not verify; refusing to restore from it"; return 1; }

  if restore_canceled; then
    echo "ERROR: canceled before anything was changed"
    return 1
  fi

  # The safety backup, and then a WAL switch, so that everything up to this
  # moment is in the repository. Together they are what makes this restore
  # undoable: the undo is another point-in-time restore, to now.
  echo "[restore] taking a safety backup before the cluster is replaced"
  pgbr backup --type=incr || { echo "ERROR: the safety backup failed; nothing has been changed"; return 1; }
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
  pgbr backup --type=full || echo "WARNING: the full backup failed; take one by hand"

  q >/dev/null 2>&1 <<'SQL' || true
UPDATE "SiteSettings"
   SET "RestoreJobId" = NULL, "RestoreStartedAt" = NULL, "RestoreCancelRequestedAt" = NULL;
SQL
  [ -n "$dir" ] && restore_import_carry "$dir"
  sync_inventory missing >/dev/null 2>&1 || true

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
  do_restore_wiki "$id" "" "{\"mode\":\"pitr\",\"at\":\"$at\"}" "$dir"
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
  local json backup_at wal_at bytes
  # Every repository, not just repo2: the offsite gap is found by comparing
  # what the two hold, which is more honest than any timestamp.
  json="$(pgbr --log-level-console=off --output=json info 2>/dev/null)" || {
    q -v msg="The cloud repository could not be read." >/dev/null 2>&1 <<'SQL' || true
UPDATE "BackupTargets" SET "Message" = :'msg', "UpdatedAt" = now() WHERE "Slot" = 'cloud' AND "Kind" = 'database';
SQL
    return 1
  }
  printf '%s' "$json" > /tmp/repo2-info.json
  q >/dev/null 2>&1 <<'SQL' || true
CREATE TEMP TABLE repo2 (doc text);
\copy repo2 FROM '/tmp/repo2-info.json' WITH (FORMAT csv, DELIMITER E'\x01', QUOTE E'\x02')
WITH s AS (SELECT (string_agg(doc, '')::jsonb) -> 0 AS j FROM repo2),
     -- Backups this repository holds. repo-key 2 is the offsite one.
     b AS (SELECT jsonb_array_elements(coalesce(j -> 'backup', '[]'::jsonb)) AS x FROM s),
     -- The newest WAL segment each repository has. pgBackRest names segments
     -- so that lexical order is time order, so a plain comparison is enough.
     arch AS (SELECT (a -> 'database' ->> 'repo-key')::int AS repo, a ->> 'max' AS maxwal
                FROM s, jsonb_array_elements(coalesce(j -> 'archive', '[]'::jsonb)) AS a)
UPDATE "BackupTargets" t
   SET "LastBackupAt" = (SELECT max(to_timestamp((x -> 'timestamp' ->> 'stop')::bigint)) FROM b),
       "BytesStored"  = (SELECT sum((x -> 'info' -> 'repository' ->> 'delta')::bigint) FROM b),
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
       "Message"      = NULL,
       "UpdatedAt"    = now()
 WHERE t."Slot" = 'cloud' AND t."Kind" = 'database';
SQL
}

# A full backup to the cloud on its own, slower schedule (decision 5): WAL
# streams there continuously, so recovery to a point in time does not wait
# for this. Keyed off what repo2 actually holds, not a timer in memory, so a
# restart does not take a fresh one every time.
cloud_backup_due() {
  offsite_cloud_enabled || return 1
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
    sync_cloud_status
    log "cloud: backup complete"
  else
    local tail_out; tail_out="$(tail -3 /tmp/cloud-backup.log | tr '\n' ' ')"
    log "ERROR: cloud backup failed: $tail_out"
    q -v msg="The last offsite backup failed: $tail_out" >/dev/null 2>&1 <<'SQL' || true
UPDATE "BackupTargets" SET "Message" = left(:'msg', 2000), "UpdatedAt" = now() WHERE "Slot" = 'cloud' AND "Kind" = 'database';
SQL
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
  sync_cloud_status
  publish_wal_backlog
  cloud_backup_due && run_cloud_backup
  cloud_verify_due && run_cloud_verify
  return 0
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
      case "$msg" in
        *CryptoError*|*FormatError*)
          echo "Reached the database repository, but the passphrase does not open it. Check OFFSITE_CLOUD_PASSPHRASE against the one it was created with." ;;
        *"403"*)
          echo "The storage provider refused the key or the secret (403). Check OFFSITE_CLOUD_KEY and OFFSITE_CLOUD_SECRET, and that the key may use ${OFFSITE_CLOUD_BUCKET}." ;;
        *"404"*)
          echo "Bucket ${OFFSITE_CLOUD_BUCKET} was not found (404). Check OFFSITE_CLOUD_BUCKET; if it does exist, try the other OFFSITE_CLOUD_URI_STYLE." ;;
        *HostConnectError*|*"unable to get address"*|*"unable to connect"*)
          echo "Could not reach ${OFFSITE_CLOUD_ENDPOINT}: ${msg##*: }." ;;
        *)
          echo "Could not open the database repository: ${msg:0:300}" ;;
      esac
      return 1 ;;
  esac
}

# How many segments Postgres has handed over that are not yet archived
# everywhere. This sidecar mounts PGDATA read-only, so it can count them.
publish_wal_backlog() {
  local n
  n="$(find /var/lib/postgresql/18/docker/pg_wal/archive_status -name '*.ready' 2>/dev/null | wc -l | tr -d ' ')"
  [ -z "$n" ] && return 0
  q -v n="$n" >/dev/null 2>&1 <<'SQL' || true
UPDATE "BackupTargets" SET "WalBacklogFiles" = :'n'::int, "UpdatedAt" = now() WHERE "Slot" = 'cloud' AND "Kind" = 'database';
SQL
}

# Mirrors pgBackRest's own catalog into "Backups". The JSON goes through a
# file and \copy: kept forever, it outgrows a command-line argument.
sync_inventory() {
  if ! pgbr --log-level-console=off --output=json info >/tmp/inventory.json 2>/dev/null \
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
  pgbr expire --repo1-retention-full="$keep" >&2
  sync_inventory retention >&2
}

run_agent
