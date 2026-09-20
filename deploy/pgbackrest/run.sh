#!/usr/bin/env bash
# The `pgbackrest` sidecar (dev-plan 9.1): physical backups for point-in-time
# recovery. Creates the stanza, then runs the loop in common.sh (schedule, job
# queue, retention); this file is what is specific to pgBackRest. Runs as root
# and calls pgBackRest as the postgres user, which can read PGDATA (shared
# read-only) and reach Postgres over the shared socket.
AGENT=physical
VOLUME=/var/lib/pgbackrest
STANZA=main
FULL_EVERY_DAYS="${BACKUP_FULL_EVERY_DAYS:-7}"
TOOL_VERSION="$(pgbackrest version 2>/dev/null)"

# shellcheck source=../backup/common.sh
. /opt/tesria/common.sh

# pgBackRest writes here; ensure the postgres user owns them on the shared repo.
mkdir -p /var/lib/pgbackrest /var/spool/pgbackrest /var/log/pgbackrest
chown -R postgres:postgres /var/lib/pgbackrest /var/spool/pgbackrest /var/log/pgbackrest

pgbr() { gosu postgres pgbackrest --stanza="$STANZA" "$@"; }

log "waiting for the postgres socket"
until gosu postgres pg_isready -h /var/run/postgresql -q; do sleep 2; done

# Create the stanza (idempotent) and validate archiving. A failure used to
# exit the container; now it waits and tries again.
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

# Mirrors pgBackRest's own catalogue into "Backups". The JSON goes through a
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
