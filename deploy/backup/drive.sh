#!/usr/bin/env bash
# One path target in a service of its own (dev-plan 24.2, T8-026): the
# network drive (`backup-nas`) or the removable drive (`backup-removable`).
#
# Why a service each. A path target is a bind mount, and on Docker Desktop
# for Mac a bind mount whose folder is not there stops the container from
# starting at all ("error while creating mount source path ... permission
# denied": it cannot create a folder under /Volumes). Until 0.8.2 both drives
# were mounted into the `backup` service, so a NAS that was not connected
# when the stack started took the local dumps down with it, and a removable
# drive, which is in a drawer most of the time, would have done the same.
# Now a missing drive stops only its own service. The `backup` service
# notices that this one has gone quiet and says so on the drive's card
# (drive_service_watch in run.sh), and `docker compose up -d` once the drive
# is back starts this one.
#
# What it does is what `backup` did for the slot before: the network drive
# is copied to once per local backup and drilled monthly; the removable
# drive is only reported, and copied to when someone chooses Copy Now. It
# answers the slot's Copy Now and Test Connection jobs, which stay the
# logical agent's jobs; `backup` leaves them for this service.
#
# It is not an agent of its own: no BackupAgents row, no backups. It writes
# only its slot's BackupTargets row.
AGENT=logical
SLOT="${OFFSITE_SLOT:-}"
case "$SLOT" in
  nas)       MOUNT=/mnt/nas;       PASSPHRASE="${OFFSITE_NAS_PASSPHRASE:-}" ;;
  removable) MOUNT=/mnt/removable; PASSPHRASE="${OFFSITE_REMOVABLE_PASSPHRASE:-}" ;;
  *) echo "drive.sh: OFFSITE_SLOT must be nas or removable, not '${SLOT}'" >&2; exit 2 ;;
esac

# shellcheck source=common.sh
. /opt/tesria/common.sh
# shellcheck source=../pgbackrest/offsite.sh
. /opt/tesria/offsite.sh
# shellcheck source=offsite-files.sh
. /scripts/offsite-files.sh
# shellcheck source=offsite-drill.sh
. /scripts/offsite-drill.sh

log() { echo "[$SLOT $(date -u +%FT%TZ)] $*"; }

# The retention the logical agent last applied, which is what the local
# dumps are kept by, so the copies expire together. Read, never decided
# here: deciding writes the agent's row, which is the other service's.
drive_policy() {
  q 2>/dev/null <<'SQL'
SELECT CASE WHEN "AppliedRetentionEnabled" THEN 't' ELSE 'f' END || '|'
       || coalesce("AppliedKeepCount", 0) || '|' || coalesce("AppliedKeepDays", 0)
  FROM "BackupAgents" WHERE "Name" = 'logical';
SQL
}

# The card's row, touched every minute from the background even while a long
# copy runs: it is how `backup` knows this service is running.
drive_heartbeat() {
  while true; do
    touch /tmp/heartbeat
    if [ -n "$PASSPHRASE" ]; then
      q -v slot="$SLOT" >/dev/null 2>&1 <<'SQL' || true
UPDATE "BackupTargets" SET "UpdatedAt" = now() WHERE "Slot" = :'slot' AND "Kind" = 'files';
SQL
    fi
    sleep "$POLL_SECONDS"
  done
}

# Once, at start: whatever `backup` said while this service was not running
# is over now. A copy or test this service was running when it stopped did
# not finish; `backup` leaves this slot's jobs alone, so they are closed here.
drive_started() {
  q -v slot="$SLOT" >/dev/null 2>&1 <<'SQL' || true
UPDATE "BackupTargets" SET "Message" = NULL, "UpdatedAt" = now()
 WHERE "Slot" = :'slot' AND "Kind" = 'files' AND "Message" LIKE 'The service that copies to%';
UPDATE "BackupJobs"
   SET "Status" = 'failed', "FinishedAt" = now(),
       "Error" = 'Interrupted: the drive''s service stopped before this finished, so how it ended was not recorded.'
 WHERE "Agent" = 'logical' AND "Status" = 'running'
   AND "Kind" IN ('copy-offsite', 'test-target') AND "Target" = :'slot';
SQL
}

drive_tick() {
  local line en kc kd
  if [ -z "$PASSPHRASE" ]; then
    offsite_files_disable "$SLOT"
    return 0
  fi
  if ! offsite_path_present "$MOUNT"; then
    # A network drive that should always be there is a problem, and raises
    # an alert; a removable drive that is not plugged in is in a drawer.
    if [ "$SLOT" = nas ]; then
      offsite_files_absent nas "The network drive is not mounted, or has not been claimed with claim-target.sh."
    else
      offsite_files_absent removable "The drive is not plugged in. The last copy it holds is shown above."
    fi
    return 0
  fi
  # Checked every minute, so a share that comes back shows as back within a
  # minute rather than at the next copy (the second half of dev-plan 24.2).
  offsite_files_seen "$SLOT"
  [ "$SLOT" = nas ] || return 0

  if line="$(drive_policy)"; then
    IFS='|' read -r en kc kd <<<"$line"
  fi
  offsite_copy_slot nas "${en:-f}" "${kc:-0}" "${kd:-0}"
  offsite_drill_tick nas
}

# The job the Copy Now button queues (dev-plan 9.2 step 4).
do_copy_offsite() {
  local slot="$1" line en kc kd
  if [ "$slot" != removable ]; then
    echo "Only a removable target is copied on demand."
    return 1
  fi
  if ! offsite_path_present /mnt/removable; then
    echo "The drive is not plugged in, or has not been claimed with claim-target.sh."
    return 1
  fi
  if line="$(drive_policy)"; then
    IFS='|' read -r en kc kd <<<"$line"
  fi
  restic_run_removable "${kc:-10}" "${en:-f}"
}

# This slot's Copy Now and Test Connection jobs, and nothing else.
claim_drive_job() {
  q -v slot="$SLOT" <<'SQL'
UPDATE "BackupJobs" SET "Status" = 'running', "StartedAt" = now()
 WHERE "Id" = (SELECT "Id" FROM "BackupJobs"
                WHERE "Agent" = 'logical' AND "Status" = 'requested'
                  AND "Kind" IN ('copy-offsite', 'test-target') AND "Target" = :'slot'
                ORDER BY "RequestedAt" LIMIT 1
                  FOR UPDATE SKIP LOCKED)
RETURNING "Id" || '|' || "Kind" || '|' || coalesce("Target", '');
SQL
}

trap 'kill "$HB_PID" 2>/dev/null; exit 0' TERM INT
wait_for_db
drive_heartbeat &
HB_PID=$!
log "started for $MOUNT"

started=0
while true; do
  if tables_ready; then
    if [ "$started" = 0 ]; then
      drive_started
      started=1
    fi
    drive_tick
    while job="$(claim_drive_job)" && [ -n "$job" ]; do
      IFS='|' read -r id kind target <<<"$job"
      case "$kind" in
        copy-offsite) log "copy requested"; run_copy_job "$id" "$target" ;;
        test-target)  log "connection test requested"; run_test_target_job "$id" "$target" ;;
      esac
    done
  fi
  sleep "$POLL_SECONDS" &
  wait $!
done
