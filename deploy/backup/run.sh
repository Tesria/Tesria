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
TOOL_VERSION="$(pg_dump --version 2>/dev/null)"

# shellcheck source=common.sh
. /opt/tesria/common.sh
# shellcheck source=../pgbackrest/offsite.sh
. /opt/tesria/offsite.sh
# shellcheck source=offsite-files.sh
. /scripts/offsite-files.sh

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
}

# The offsite copy of the files (dev-plan 9.2 step 2). Runs on the same pass
# as everything else, and only after the local backup has been taken, so what
# is copied is a cycle that exists here first: local first, then replicate.
offsite_tick() {
  local line en kc kd
  # The same policy the local retention uses, read the same way, so every
  # copy expires together instead of drifting apart.
  if line="$(observe_policy)"; then
    IFS='|' read -r en kc kd _ <<<"$line"
  fi

  if offsite_cloud_enabled; then
    restic_run_for cloud "${en:-f}" "${kc:-0}" "${kd:-0}" 1
  else
    offsite_files_disable cloud
  fi

  # The network drive (step 3). Scheduled like the cloud, and its absence is
  # worth an alert, because a share that should always be there and is not is
  # a problem rather than a fact of life.
  if [ -n "${OFFSITE_NAS_PASSPHRASE:-}" ]; then
    if offsite_path_present /mnt/nas; then
      restic_run_for nas "${en:-f}" "${kc:-0}" "${kd:-0}" 1
    else
      offsite_files_absent nas "The network drive is not mounted, or has not been claimed with claim-target.sh."
    fi
  else
    offsite_files_disable nas
  fi
}

run_agent
