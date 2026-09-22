#!/usr/bin/env bash
# The offsite restore drill (dev-plan 9.2 step 6).
#
# Every other check in this system asks whether a copy exists and whether it
# passes its own integrity check. This one asks the only question that
# matters after the machine is gone: does the offsite copy still turn back
# into a working database?
#
# It restores the newest dump out of an offsite restic repository, loads it
# into a throwaway database, counts the tables, and drops it again. Nothing
# live is touched, and the drill is scheduled rather than left to somebody
# remembering, because a backup nobody has restored is a backup nobody knows
# about.

# How often each target is drilled. Monthly: a full restore reads the whole
# dump back out of the target, which on a metered cloud or a slow share is
# not something to do nightly, and a copy that was good last month and is
# broken today is caught by the far cheaper integrity check that runs after
# every backup.
OFFSITE_DRILL_DAYS="${OFFSITE_DRILL_DAYS:-30}"

offsite_drill_due() {
  local slot="$1" due
  due="$(q -v slot="$slot" -v every="$OFFSITE_DRILL_DAYS" <<'SQL'
SELECT CASE WHEN "LastDrillAt" IS NULL
              OR "LastDrillAt" < now() - make_interval(days => :'every'::int)
            THEN 't' ELSE 'f' END
  FROM "BackupTargets" WHERE "Slot" = :'slot' AND "Kind" = 'files';
SQL
)" || return 1
  [ "$due" = "t" ]
}

# Restores the newest dump this target holds into a throwaway database.
# Prints its own log; the caller decides what to do with the outcome.
# Cleaning up is not optional: the restored copy is the size of the database
# and the throwaway database is another one, so leaving either behind would
# fill the disk a drill at a time. Done explicitly rather than with a RETURN
# trap, which reads tidily and does not work: a trap fires after the function
# has returned, its locals are gone, and under `set -u` the trap body dies on
# the first one it touches, silently skipping the cleanup it exists for.
offsite_drill_cleanup() {
  rm -rf "${1:-/nonexistent}"
  [ -n "${2:-}" ] && psql --dbname=postgres -v ON_ERROR_STOP=1 \
    -c "DROP DATABASE IF EXISTS \"$2\";" >/dev/null 2>&1
  return 0
}

offsite_drill_run() {
  local slot="$1" db="tesria_drill_$$" target=/tmp/offsite-drill newest tables
  restic_env_for "$slot" || { echo "The $slot target is not usable."; return 1; }

  rm -rf "$target"; mkdir -p "$target"

  echo "Restoring the newest dump from the $slot target..."
  if ! rst restore latest --target "$target" --include /backups >/dev/null 2>&1; then
    echo "FAILED: the offsite copy could not be restored."
    offsite_drill_cleanup "$target" ""; return 1
  fi

  newest="$(find "$target" -name 'db-*.dump' -type f 2>/dev/null | sort | tail -1)"
  if [ -z "$newest" ]; then
    echo "FAILED: the offsite copy holds no database dump."
    offsite_drill_cleanup "$target" ""; return 1
  fi
  echo "Restored $(basename "$newest") ($(du -h "$newest" | cut -f1))."

  # Dropped first in case an earlier drill was killed before it could.
  psql --dbname=postgres -v ON_ERROR_STOP=1 -c "DROP DATABASE IF EXISTS \"$db\";" >/dev/null 2>&1
  psql --dbname=postgres -v ON_ERROR_STOP=1 -c "CREATE DATABASE \"$db\";" >/dev/null || {
    echo "FAILED: could not create a throwaway database for the drill."
    offsite_drill_cleanup "$target" ""; return 1
  }
  if ! pg_restore --no-owner --no-privileges --dbname="$db" "$newest" >/dev/null 2>&1; then
    echo "FAILED: the dump restored from the $slot target would not load."
    offsite_drill_cleanup "$target" "$db"; return 1
  fi

  tables="$(psql --dbname="$db" -tAc \
    "SELECT count(*) FROM information_schema.tables WHERE table_schema='public';" 2>/dev/null)"
  # A dump that loads but holds nothing is the failure this catches: an
  # empty database restores perfectly and is worth nothing.
  if [ -z "$tables" ] || [ "$tables" -lt 1 ]; then
    echo "FAILED: the restored database has no tables in it."
    offsite_drill_cleanup "$target" "$db"; return 1
  fi

  echo "OK: the $slot copy restored cleanly, $tables public table(s) present."
  offsite_drill_cleanup "$target" "$db"
  return 0
}

offsite_drill_record() {
  q -v slot="$1" -v ok="$2" -v note="$3" >/dev/null 2>&1 <<'SQL' || true
UPDATE "BackupTargets"
   SET "LastDrillAt" = now(), "LastDrillOk" = :'ok'::boolean,
       "Message" = CASE WHEN :'ok' = 'false' THEN left(:'note', 2000) ELSE "Message" END,
       "UpdatedAt" = now()
 WHERE "Slot" = :'slot' AND "Kind" = 'files';
SQL
}

# Called once per pass. At most one drill per pass, so a slow restore never
# delays the next backup by more than one target's worth.
offsite_drill_tick() {
  local slot logf ok
  for slot in cloud nas; do
    restic_env_for "$slot" >/dev/null 2>&1 || continue
    offsite_drill_due "$slot" || continue

    logf="$(mktemp)"
    log "restore drill: $slot"
    if offsite_drill_run "$slot" >"$logf" 2>&1; then ok=true; else ok=false; fi
    cat "$logf"
    offsite_drill_record "$slot" "$ok" "$(tail -1 "$logf")"
    [ "$ok" = true ] \
      && log "restore drill: the $slot copy is restorable" \
      || log "ERROR: restore drill: the $slot copy did NOT restore"
    rm -f "$logf"
    return 0
  done
}
