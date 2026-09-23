#!/usr/bin/env bash
# Carrying the uploads and the logical dumps off this machine with restic
# (dev-plan 9.2 step 2). Sourced by the `backup` sidecar.
#
# What goes, and why it is not the tarball. restic reads the uploads volume
# and the dump directory **directly**. Shipping the nightly tar.gz instead
# would defeat deduplication entirely: a fresh archive is new bytes end to
# end every cycle, so every cycle would cost its full size again. Reading
# the files means an unchanged attachment is stored once, whatever it is
# stored in locally.
#
# This is also what satisfies 9.2's prerequisite that the logical dumps are
# encrypted before anything copies them off the box. restic encrypts
# client-side, always, with this slot's own passphrase. The local dumps on
# the `backups` volume stay as they are; what changes is that nothing
# leaves unencrypted.

RESTIC_UPLOADS=/data/uploads
RESTIC_DUMPS=/backups

# restic's cache is per-container and disposable; keeping it out of the
# backups volume stops it being backed up by the next run.
export RESTIC_CACHE_DIR=/tmp/restic-cache

# Everything restic needs for one slot, in this process only. Returns 1 when
# the slot is not usable, which is not an error: most instances have none.
restic_env_for() {
  local slot="$1"
  unset RESTIC_REPOSITORY RESTIC_PASSWORD AWS_ACCESS_KEY_ID AWS_SECRET_ACCESS_KEY AWS_DEFAULT_REGION
  case "$slot" in
    cloud)
      offsite_cloud_enabled || return 1
      # s3:endpoint/bucket/prefix. restic wants a scheme; pgBackRest took
      # the endpoint bare, so the two configurations read the same in .env.
      local endpoint="${OFFSITE_CLOUD_ENDPOINT}"
      case "$endpoint" in https://*|http://*) ;; *) endpoint="https://${endpoint}" ;; esac
      export RESTIC_REPOSITORY="s3:${endpoint}/${OFFSITE_CLOUD_BUCKET}${OFFSITE_CLOUD_PATH:-/tesria}/files"
      export RESTIC_PASSWORD="${OFFSITE_CLOUD_PASSPHRASE}"
      export AWS_ACCESS_KEY_ID="${OFFSITE_CLOUD_KEY}"
      export AWS_SECRET_ACCESS_KEY="${OFFSITE_CLOUD_SECRET}"
      export AWS_DEFAULT_REGION="${OFFSITE_CLOUD_REGION:-us-east-1}"
      ;;
    nas|removable)
      local path passvar path_ok
      if [ "$slot" = nas ]; then path=/mnt/nas; passvar="${OFFSITE_NAS_PASSPHRASE:-}"
      else path=/mnt/removable; passvar="${OFFSITE_REMOVABLE_PASSPHRASE:-}"; fi
      # No passphrase, no backup, and deliberately no falling back to
      # another slot's: a copy nobody can decrypt is not a copy, and one
      # encrypted with the wrong key is worse, because it looks like one.
      [ -z "$passvar" ] && return 1
      offsite_path_present "$path" || return 1
      export RESTIC_REPOSITORY="${path}/restic"
      export RESTIC_PASSWORD="$passvar"
      ;;
    *) return 1 ;;
  esac
  return 0
}

# Whether a mounted path is really the target, rather than the empty
# directory an absent mount leaves behind. The sentinel is written once by
# claim-target.sh and never by the sidecar: if the sidecar created it, an
# unmounted share would be claimed on its first pass and every backup after
# that would go to the boot disk.
offsite_path_present() {
  [ -d "${1:-}" ] && [ -f "${1}/.tesria-backup-target" ]
}

# restic refuses a self-signed certificate unless told; the test harness runs
# MinIO with one. Never set for a real provider: the backup is leaving.
restic_tls_flag() {
  case "${RESTIC_REPOSITORY:-}" in
    s3:*) [ "${OFFSITE_CLOUD_VERIFY_TLS:-y}" = "n" ] && echo "--insecure-tls" ;;
  esac
}

rst() { restic $(restic_tls_flag) "$@"; }

# Clears locks left behind by a run that did not finish.
#
# restic takes an exclusive lock while it works and releases it when it is
# done. A sidecar that is killed part way through (a container restart, a
# host reboot, a drive pulled early) never releases, and every later run
# then fails on a repository that looks broken but is only locked. `unlock`
# removes only locks restic considers stale, which is what this is: the
# process that took it is gone, and only this sidecar ever writes here.
restic_clear_stale_lock() {
  local held
  held="$(rst list locks 2>/dev/null | head -1)"
  [ -z "$held" ] && return 0
  note "offsite files: clearing a lock left by an earlier run"
  rst unlock >/dev/null 2>&1
}

# Creates the repository on first use. Anything else is left alone: an
# existing repository with a different passphrase must fail loudly rather
# than be replaced, because replacing it would discard every backup in it.
restic_ensure_repo() {
  restic_clear_stale_lock
  if rst cat config >/dev/null 2>&1; then return 0; fi

  # `cat config` failing does not mean the repository is missing: a lock, a
  # slow share or a dropped connection all look the same from here. So `init`
  # is attempted, and its own refusal is what tells the two apart. A
  # repository that already exists is *not* a failure; it means the read
  # failed for some other reason, and the next pass will try again rather
  # than this one reporting a broken target.
  note "offsite files: initializing the restic repository"
  local out
  if out="$(rst init 2>&1 | tail -2 | tr '\n' ' ')"; then return 0; fi
  case "$out" in
    *"config file already exists"*|*"repository master key and config already initialized"*)
      note "offsite files: the repository is there but could not be read this time; leaving it alone"
      RESTIC_LAST_ERROR=""
      return 1 ;;
  esac
  note "offsite files: could not open or create the repository: $out"
  RESTIC_LAST_ERROR="$out"
  return 1
}

# The admin page's policy, in restic's words. Mirrors ResticRetention in C#,
# which is where the rule is pinned by tests; change one, change both.
#
#   keep the newest N              --keep-last N
#   and everything from D days     --keep-within Dd
#
# A slot that is present only now and then (a drive: step 4) gets the count
# alone, because a time window would prune a repository that has simply not
# been plugged in. Retention off removes nothing at all.
restic_forget_args() {
  local enabled="$1" count="$2" days="$3" windowed="${4:-1}"
  [ "$enabled" != "t" ] && return 1
  printf -- '--keep-last %s' "$count"
  [ "$windowed" = "1" ] && printf -- ' --keep-within %sd' "$days"
}

# When a scheduled slot (cloud, NAS) is next copied to. Once per local
# cycle: a copy is due when a local backup has completed since this slot's
# last copy, or when the slot has never had one. Keyed off what the tables
# record rather than a timer in memory, so a restart does not copy again.
#
# This used to be every pass of the loop, which is once a minute: a snapshot
# a minute kept for the whole retention window, and a 5% read check a minute,
# which against a cloud provider downloads the repository several times an
# hour. The local cycle is the unit anyway: what is copied is a cycle that
# exists here first.
#
# A failed copy waits OFFSITE_RETRY_MINUTES before the next attempt, in
# memory, because a target that is down should not be hammered every minute
# and a restart trying once more is harmless.
OFFSITE_RETRY_MINUTES="${OFFSITE_RETRY_MINUTES:-15}"
declare -A OFFSITE_RETRY_AT=()

offsite_copy_due() {
  local slot="$1" due type loc bucket prefix keyfp passfp
  [ "${OFFSITE_RETRY_AT[$slot]:-0}" -gt "$(date +%s)" ] && return 1
  # A slot pointed somewhere new in .env (another bucket, path or passphrase)
  # has never been copied to, whatever the row says about the old one;
  # otherwise its first copy would wait for the next local backup.
  IFS='|' read -r type loc bucket prefix keyfp passfp <<<"$(offsite_files_identity "$slot")"
  due="$(q -v slot="$slot" -v loc="$loc" -v bucket="$bucket" -v prefix="$prefix" -v passfp="$passfp" <<'SQL'
WITH t AS (
  SELECT (SELECT CASE WHEN "Location" IS NOT DISTINCT FROM NULLIF(:'loc', '')
                       AND "Bucket" IS NOT DISTINCT FROM NULLIF(:'bucket', '')
                       AND "Prefix" IS NOT DISTINCT FROM NULLIF(:'prefix', '')
                       AND "PassphraseFingerprint" IS NOT DISTINCT FROM NULLIF(:'passfp', '')
                      THEN "LastBackupAt" END
            FROM "BackupTargets" WHERE "Slot" = :'slot' AND "Kind" = 'files') AS copied
)
SELECT CASE
         WHEN t.copied IS NULL THEN 't'
         WHEN EXISTS (SELECT 1 FROM "Backups" b
                       WHERE b."Agent" = 'logical' AND b."RemovedAt" IS NULL AND b."Error" IS NULL
                         AND coalesce(b."CompletedAt", b."StartedAt") > t.copied) THEN 't'
         ELSE 'f'
       END
  FROM t;
SQL
)" || return 1
  [ "$due" = t ]
}

# Copies to one scheduled slot if it is due, and backs off if it fails.
offsite_copy_slot() {
  local slot="$1"
  offsite_copy_due "$slot" || return 0
  if restic_run_for "$slot" "$2" "$3" "$4" 1; then
    unset 'OFFSITE_RETRY_AT[$slot]'
  else
    OFFSITE_RETRY_AT[$slot]=$(( $(date +%s) + OFFSITE_RETRY_MINUTES * 60 ))
    note "offsite files: the $slot copy did not complete; next attempt in ${OFFSITE_RETRY_MINUTES} minutes"
  fi
}

# One run for one slot: back up, apply retention, then a partial read check.
# Each step reports separately, because "the backup worked but verification
# failed" is a different thing to tell someone than "nothing was copied".
restic_run_for() {
  local slot="$1" enabled="$2" count="$3" days="$4" windowed="${5:-1}"
  restic_env_for "$slot" || return 1

  # Reach it first, with a time limit. Unreachable, restic would otherwise
  # retry for up to fifteen minutes, holding every job queued behind it.
  local probe
  probe="$(restic_probe "$slot")"
  if [ $? = 1 ]; then
    note "offsite files: $slot: $(printf '%s\n' "$probe" | tail -n 1)"
    offsite_files_status "$slot" "The last offsite copy could not start. $(printf '%s\n' "$probe" | tail -n 1)"
    return 1
  fi

  if ! restic_ensure_repo; then
    [ -n "${RESTIC_LAST_ERROR:-}" ] \
      && offsite_files_status "$slot" "The repository could not be opened or created. ${RESTIC_LAST_ERROR}"
    return 1
  fi

  local out
  if ! out="$(rst backup --tag tesria --tag "$AGENT" \
                 --exclude '*.tmp' --exclude 'lost+found' \
                 "$RESTIC_UPLOADS" "$RESTIC_DUMPS" 2>&1 | tail -3 | tr '\n' ' ')"; then
    note "offsite files: backup failed: $out"
    offsite_files_status "$slot" "The last offsite file backup failed: $out"
    return 1
  fi
  note "offsite files: $out"

  local args
  if args="$(restic_forget_args "$enabled" "$count" "$days" "$windowed")"; then
    # shellcheck disable=SC2086
    rst forget $args --prune >/dev/null 2>&1 \
      || note "offsite files: retention pass failed; nothing removed"
  fi

  # A subset rather than the whole repository: reading every byte back from
  # object storage every run would cost more than it is worth, and 5% of a
  # deduplicated repository still finds rot quickly.
  local verified=f
  rst check --read-data-subset=5% >/dev/null 2>&1 && verified=t

  offsite_files_publish "$slot" "$verified"
}

# A removable drive, on demand. Different from the scheduled targets in
# three ways, each because the drive is absent most of the time:
#
#   * it is never scheduled, only asked for;
#   * retention is a count with no time window, so a drive plugged in twice
#     a year is not pruned to nothing for having been in a drawer;
#   * it finishes with check, then sync, then says "safe to remove", because
#     the next thing that happens to this target is somebody pulling it out.
restic_run_removable() {
  local count="$1" enabled="$2"
  restic_env_for removable || return 1

  offsite_warn_filesystem /mnt/removable

  if ! restic_ensure_repo; then
    [ -n "${RESTIC_LAST_ERROR:-}" ] \
      && offsite_files_status removable "The repository on the drive could not be opened or created. ${RESTIC_LAST_ERROR}"
    return 1
  fi

  local out
  if ! out="$(rst backup --tag tesria --tag removable \
                 --exclude '*.tmp' --exclude 'lost+found' \
                 "$RESTIC_UPLOADS" "$RESTIC_DUMPS" 2>&1 | tail -3 | tr '\n' ' ')"; then
    note "removable: copy failed: $out"
    offsite_files_status removable "The last copy to the drive failed: $out"
    return 1
  fi
  note "removable: $out"

  # A count and no window: see above.
  if [ "$enabled" = t ]; then
    # shellcheck disable=SC2086
    rst forget --keep-last "$count" --prune >/dev/null 2>&1 \
      || note "removable: retention pass failed; nothing removed"
  fi

  local verified=f
  rst check --read-data-subset=5% >/dev/null 2>&1 && verified=t

  # Flush the kernel's buffers before anyone unplugs it. Without this the
  # copy can be reported finished while parts of it are still in memory, and
  # a drive pulled at that moment holds a repository that is missing pieces.
  sync
  offsite_files_publish removable "$verified"

  if [ "$verified" = t ]; then
    note "removable: copy verified and flushed to the drive; safe to remove"
    # "Safe to remove" is about the data, which sync has flushed: pulling
    # the drive now cannot lose any of it. It is not a promise that the
    # operating system will eject cleanly, because this container holds a
    # bind mount on the drive and that keeps it busy until the sidecar is
    # stopped. The runbook says which one you want.
    offsite_files_message removable "Copy complete and verified. The data is flushed, so the drive can be removed. To eject it cleanly first: docker compose stop backup."
  else
    note "removable: copied and flushed, but verification did not pass"
    offsite_files_message removable "Copied and flushed to the drive, but verification did not pass. The copy is on the drive; check it before relying on it."
  fi
}

# FAT32 cannot hold a file over 4GB. restic's packs stay well under that by
# default, so this is a warning rather than a refusal, but a dump restored
# onto such a drive by hand would hit it.
#
# Best effort, and it will not fire on macOS. Docker Desktop passes a bind
# mount through its own file sharing, so the container sees `fuse` whatever
# the drive really is; only on Linux, where a bind mount keeps the
# underlying type, does this report msdos or vfat. Better a warning that is
# sometimes silent than one that guesses.
offsite_warn_filesystem() {
  local fs
  fs="$(stat -f -c %T "${1:-}" 2>/dev/null)"
  case "$fs" in
    msdos|vfat)
      note "removable: the drive is formatted $fs, which cannot hold a file larger than 4GB"
      offsite_files_message removable "This drive is formatted $fs. restic works, but nothing larger than 4GB can be written; exFAT avoids the limit." ;;
  esac
}

# A line for the status card that is not an error.
offsite_files_message() {
  q -v slot="$1" -v msg="$2" >/dev/null 2>&1 <<'SQL' || true
UPDATE "BackupTargets" SET "Message" = left(:'msg', 2000), "UpdatedAt" = now()
 WHERE "Slot" = :'slot' AND "Kind" = 'files';
SQL
}

# What a slot is, as .env describes it: type|location|bucket|prefix|key|passphrase,
# the secrets as fingerprints. What is published, and what tells a copy to a
# target from a copy to one that has since been replaced.
offsite_files_identity() {
  local slot="$1" type="" loc="" bucket="" prefix="" keyfp="" passfp=""
  case "$slot" in
    cloud)
      type="${OFFSITE_CLOUD_TYPE:-}"; loc="${OFFSITE_CLOUD_ENDPOINT:-}"
      bucket="${OFFSITE_CLOUD_BUCKET:-}"; prefix="${OFFSITE_CLOUD_PATH:-/tesria}"
      keyfp="$(offsite_fingerprint "${OFFSITE_CLOUD_KEY:-}")"
      passfp="$(offsite_fingerprint "${OFFSITE_CLOUD_PASSPHRASE:-}")" ;;
    nas)
      # The host path, not the container's: /mnt/nas would mean nothing to
      # somebody reading the screen.
      type=path; loc="${OFFSITE_NAS_PATH:-}"; bucket=""; prefix=restic; keyfp=""
      passfp="$(offsite_fingerprint "${OFFSITE_NAS_PASSPHRASE:-}")" ;;
    removable)
      type=path; loc="${OFFSITE_REMOVABLE_PATH:-}"; bucket=""; prefix=restic; keyfp=""
      passfp="$(offsite_fingerprint "${OFFSITE_REMOVABLE_PASSPHRASE:-}")" ;;
  esac
  printf '%s|%s|%s|%s|%s|%s\n' "$type" "$loc" "$bucket" "$prefix" "$keyfp" "$passfp"
}

# What this slot holds, for the screen and the alerts.
offsite_files_publish() {
  local slot="$1" verified="$2" bytes="" snaps="" type loc bucket prefix keyfp passfp
  bytes="$(rst stats --mode raw-data --json 2>/dev/null | sed -n 's/.*"total_size":\([0-9]*\).*/\1/p')"
  snaps="$(rst snapshots --json 2>/dev/null | grep -o '"short_id"' | wc -l | tr -d ' ')"
  IFS='|' read -r type loc bucket prefix keyfp passfp <<<"$(offsite_files_identity "$slot")"
  q -v slot="$slot" -v verified="$verified" -v bytes="${bytes:-}" -v snaps="${snaps:-0}" \
    -v keyfp="$keyfp" -v passfp="$passfp" \
    -v type="$type" -v loc="$loc" -v bucket="$bucket" -v prefix="$prefix" \
    >/dev/null 2>&1 <<'SQL' || true
INSERT INTO "BackupTargets" ("Slot", "Kind", "Type", "Location", "Bucket", "Prefix", "Enabled",
                             "KeyFingerprint", "PassphraseFingerprint",
                             "LastBackupAt", "LastVerifyAt", "BytesStored", "Present", "Message", "UpdatedAt")
VALUES (:'slot', 'files', NULLIF(:'type',''), NULLIF(:'loc',''), NULLIF(:'bucket',''), NULLIF(:'prefix',''),
        true, NULLIF(:'keyfp',''), NULLIF(:'passfp',''),
        now(), CASE WHEN :'verified' = 't' THEN now() END,
        NULLIF(:'bytes','')::bigint, true, NULL, now())
ON CONFLICT ("Slot", "Kind") DO UPDATE
   SET "Type" = EXCLUDED."Type", "Location" = EXCLUDED."Location", "Bucket" = EXCLUDED."Bucket",
       "Prefix" = EXCLUDED."Prefix", "Enabled" = true,
       "KeyFingerprint" = EXCLUDED."KeyFingerprint",
       "PassphraseFingerprint" = EXCLUDED."PassphraseFingerprint",
       "LastBackupAt" = now(),
       "LastVerifyAt" = coalesce(EXCLUDED."LastVerifyAt", "BackupTargets"."LastVerifyAt"),
       "BytesStored" = EXCLUDED."BytesStored", "Present" = true,
       "Message" = NULL, "UpdatedAt" = now();
SQL
}

offsite_files_status() {
  q -v slot="$1" -v msg="$2" >/dev/null 2>&1 <<'SQL' || true
INSERT INTO "BackupTargets" ("Slot", "Kind", "Enabled", "Message", "UpdatedAt")
VALUES (:'slot', 'files', true, left(:'msg', 2000), now())
ON CONFLICT ("Slot", "Kind") DO UPDATE
   -- Enabled as well as the message: a slot that is configured and failing
   -- is still configured, and leaving this alone would take its card off the
   -- screen at exactly the moment somebody needs to read the error on it.
   SET "Enabled" = true, "Message" = left(:'msg', 2000), "UpdatedAt" = now();
SQL
}

# Configured, but not there right now. Kept enabled and dated, so the screen
# can still show what it last held and when: for a drive that is normally in
# a drawer (step 4) that is the useful answer, and for a NAS it is what makes
# the alert meaningful.
offsite_files_absent() {
  # The second argument is kept for the log only. The row records that the
  # target is not there, and the screen says so in its own words; writing a
  # sentence here as well would leave "not plugged in" on the card after the
  # drive came back, which is how this was found.
  note "offsite files: ${1} absent: ${2}"
  q -v slot="$1" >/dev/null 2>&1 <<'SQL' || true
INSERT INTO "BackupTargets" ("Slot", "Kind", "Enabled", "Present", "UpdatedAt")
VALUES (:'slot', 'files', true, false, now())
ON CONFLICT ("Slot", "Kind") DO UPDATE
   SET "Enabled" = true, "Present" = false, "UpdatedAt" = now();
SQL
}

# Present, but nothing was copied: a removable drive that is plugged in and
# waiting to be asked. Leaves every date alone, because none of them changed.
offsite_files_seen() {
  local slot="$1" loc passfp
  case "$slot" in
    nas)       loc="${OFFSITE_NAS_PATH:-}";       passfp="$(offsite_fingerprint "${OFFSITE_NAS_PASSPHRASE:-}")" ;;
    removable) loc="${OFFSITE_REMOVABLE_PATH:-}"; passfp="$(offsite_fingerprint "${OFFSITE_REMOVABLE_PASSPHRASE:-}")" ;;
    *) loc=""; passfp="" ;;
  esac
  # The dates are left alone: nothing was copied. Only what the target *is*
  # gets refreshed, so a path edited in .env shows correctly straight away
  # rather than after the next copy.
  q -v slot="$slot" -v loc="$loc" -v passfp="$passfp" >/dev/null 2>&1 <<'SQL' || true
INSERT INTO "BackupTargets" ("Slot", "Kind", "Type", "Location", "Prefix", "Enabled", "Present",
                             "PassphraseFingerprint", "UpdatedAt")
VALUES (:'slot', 'files', 'path', NULLIF(:'loc',''), 'restic', true, true,
        NULLIF(:'passfp',''), now())
ON CONFLICT ("Slot", "Kind") DO UPDATE
   SET "Type" = 'path', "Location" = EXCLUDED."Location", "Prefix" = 'restic',
       "Enabled" = true, "Present" = true,
       "PassphraseFingerprint" = EXCLUDED."PassphraseFingerprint",
       -- A target that has just come back keeps nothing it said while it was
       -- away; one that was here all along keeps its last run's message.
       "Message" = CASE WHEN "BackupTargets"."Present" IS DISTINCT FROM true
                        THEN NULL ELSE "BackupTargets"."Message" END,
       "UpdatedAt" = now();
SQL
}

# Marks a slot as off, so a target that is removed from .env stops showing.
offsite_files_disable() {
  q -v slot="$1" >/dev/null 2>&1 <<'SQL' || true
UPDATE "BackupTargets" SET "Enabled" = false, "UpdatedAt" = now()
 WHERE "Slot" = :'slot' AND "Kind" = 'files';
SQL
}

# How long a restic target gets to answer before it is treated as down.
# restic retries a refused key or an unreachable host quietly for up to about
# fifteen minutes, which is right in the middle of a backup and wrong
# everywhere else: before a scheduled copy it held the whole sidecar (every
# queued job, and the other targets) for as long as one target was down, and
# on Test connection it left a person watching a button. A healthy target
# answers in a second or two.
RESTIC_PROBE_SECONDS="${RESTIC_PROBE_SECONDS:-30}"

# How the card names the slot: "bucket X" / "in bucket X", or the drive.
restic_where() {
  case "$1" in
    cloud)     printf '%s|%s\n' "bucket ${OFFSITE_CLOUD_BUCKET:-}" "in bucket ${OFFSITE_CLOUD_BUCKET:-}" ;;
    nas)       printf '%s|%s\n' "the network drive" "on the network drive" ;;
    removable) printf '%s|%s\n' "the drive" "on the drive" ;;
  esac
}

# Opens the repository RESTIC_* points at, briefly and without a lock.
# Returns 0 when it opens, 2 when the location answered but holds no
# repository yet, 1 otherwise; on 1 and 2 the last line printed says why, in
# words for the card. restic's own output goes before it, for the log.
restic_probe() {
  local slot="$1" where inside out rc reason
  IFS='|' read -r where inside <<<"$(restic_where "$slot")"
  out="$(timeout "$RESTIC_PROBE_SECONDS" restic $(restic_tls_flag) --no-lock cat config 2>&1)"
  rc=$?
  [ "$rc" = 0 ] && return 0
  echo "$out" | tail -n 5

  case "$rc" in
    10)
      # The location answered and is empty, which is a new target that has
      # not had its first backup yet, or the wrong path. Only the person
      # reading this knows which.
      echo "Reached ${where}. There is no repository there yet: the first backup creates one. If backups have already been copied here, the path or bucket is wrong."
      return 2 ;;
    12)
      echo "Reached ${where}, but the passphrase does not open the repository there. Check the passphrase in .env against the one the repository was created with."
      return 1 ;;
  esac
  # restic says why only in the lines it prints while retrying; the final
  # line is a generic "unable to open config file".
  reason="$(printf '%s\n' "$out" | sed -n 's/.*returned error, retrying after [^:]*: //p' | head -n 1)"
  [ -n "$reason" ] || reason="$(printf '%s\n' "$out" | grep -m1 -E 'Fatal|error' || true)"
  reason="${reason#Stat: }"
  case "$reason" in
    *"Access Key Id"*|*InvalidAccessKeyId*)
      echo "The storage provider does not recognize the key. Check OFFSITE_CLOUD_KEY." ;;
    *"signature we calculated does not match"*|*SignatureDoesNotMatch*)
      echo "The storage provider refused the secret for this key. Check OFFSITE_CLOUD_SECRET." ;;
    *"Access Denied"*|*AccessDenied*|*"403"*)
      echo "The key was refused access to the bucket. Check that it is allowed to use ${OFFSITE_CLOUD_BUCKET:-the bucket}." ;;
    *"bucket does not exist"*|*NoSuchBucket*)
      echo "The bucket ${OFFSITE_CLOUD_BUCKET:-} does not exist. Check OFFSITE_CLOUD_BUCKET, or create it with the provider." ;;
    *"no such host"*|*"connection refused"*|*"i/o timeout"*|*"network is unreachable"*)
      echo "Could not reach ${OFFSITE_CLOUD_ENDPOINT:-the target}: ${reason##*: }." ;;
    *"permission denied"*)
      echo "${where^} is mounted, but the backup agent is not allowed to read it: ${reason}." ;;
    "")
      echo "No answer from ${where} within ${RESTIC_PROBE_SECONDS} seconds." ;;
    *)
      echo "Could not open the repository ${inside}: ${reason}" ;;
  esac
  return 1
}

# Test connection for the files repository (the Storage targets screen).
# Reaches the slot and opens its repository with this slot's passphrase,
# which proves the location, the credentials and the passphrase together.
# Changes nothing: no lock is taken, nothing is created, no stale lock is
# cleared. The last line printed is the answer shown on the card.
do_test_target() {
  local slot="$1" path="" where inside problem rc
  case "$slot" in
    cloud)
      if problem="$(offsite_cloud_problem)"; then echo "$problem."; return 1; fi
      [ -n "${OFFSITE_CLOUD_TYPE:-}" ] || { echo "No cloud target is configured."; return 1; } ;;
    nas)
      [ -n "${OFFSITE_NAS_PASSPHRASE:-}" ] || { echo "No network drive is configured."; return 1; }
      path=/mnt/nas ;;
    removable)
      [ -n "${OFFSITE_REMOVABLE_PASSPHRASE:-}" ] || { echo "No removable drive is configured."; return 1; }
      path=/mnt/removable ;;
    *) echo "There is no target called '$slot'."; return 1 ;;
  esac
  IFS='|' read -r where inside <<<"$(restic_where "$slot")"

  if [ -n "$path" ] && ! offsite_path_present "$path"; then
    # The sentinel is what tells a mounted target from the empty directory
    # an absent one leaves behind, so its absence is the whole answer.
    if [ "$slot" = removable ]; then
      echo "The drive is not plugged in, or has not been claimed with claim-target.sh."
    else
      echo "Nothing is mounted at the network drive's path, or it has not been claimed with claim-target.sh."
    fi
    return 1
  fi

  restic_env_for "$slot" || { echo "The settings for ${where} could not be read."; return 1; }
  restic_probe "$slot"
  rc=$?
  # No repository yet is a working target that has not had its first copy.
  [ "$rc" = 2 ] && return 0
  [ "$rc" = 0 ] || return 1

  # A mounted share can be readable and not writable, which a backup finds
  # out only when it tries. Asked of the filesystem, never by writing a file.
  if [ -n "$path" ] && [ ! -w "$path/restic" ]; then
    echo "Opened the repository ${inside}, but the backup agent cannot write to it. Check the share's permissions."
    return 1
  fi

  local snaps
  snaps="$(timeout "$RESTIC_PROBE_SECONDS" restic $(restic_tls_flag) --no-lock snapshots --json 2>/dev/null \
             | grep -o '"short_id"' | wc -l | tr -d ' ')"
  echo "Connected. The passphrase opens the repository ${inside}, which holds ${snaps:-0} snapshot$([ "${snaps:-0}" = 1 ] || echo s)."
}
