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

# Creates the repository on first use. Anything else is left alone: an
# existing repository with a different passphrase must fail loudly rather
# than be replaced, because replacing it would discard every backup in it.
restic_ensure_repo() {
  if rst cat config >/dev/null 2>&1; then return 0; fi
  note "offsite files: initialising the restic repository"
  rst init >/dev/null 2>&1
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

# One run for one slot: back up, apply retention, then a partial read check.
# Each step reports separately, because "the backup worked but verification
# failed" is a different thing to tell someone than "nothing was copied".
restic_run_for() {
  local slot="$1" enabled="$2" count="$3" days="$4" windowed="${5:-1}"
  restic_env_for "$slot" || return 1

  if ! restic_ensure_repo; then
    offsite_files_status "$slot" "The restic repository could not be opened or created."
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
    offsite_files_status removable "The restic repository on the drive could not be opened or created."
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

# What this slot holds, for the screen and the alerts.
offsite_files_publish() {
  local slot="$1" verified="$2" bytes="" snaps="" type loc bucket prefix keyfp passfp
  bytes="$(rst stats --mode raw-data --json 2>/dev/null | sed -n 's/.*"total_size":\([0-9]*\).*/\1/p')"
  snaps="$(rst snapshots --json 2>/dev/null | grep -o '"short_id"' | wc -l | tr -d ' ')"
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
   SET "Message" = left(:'msg', 2000), "UpdatedAt" = now();
SQL
}

# Configured, but not there right now. Kept enabled and dated, so the screen
# can still show what it last held and when: for a drive that is normally in
# a drawer (step 4) that is the useful answer, and for a NAS it is what makes
# the alert meaningful.
offsite_files_absent() {
  q -v slot="$1" -v msg="$2" >/dev/null 2>&1 <<'SQL' || true
INSERT INTO "BackupTargets" ("Slot", "Kind", "Enabled", "Present", "Message", "UpdatedAt")
VALUES (:'slot', 'files', true, false, left(:'msg', 2000), now())
ON CONFLICT ("Slot", "Kind") DO UPDATE
   SET "Enabled" = true, "Present" = false,
       "Message" = left(:'msg', 2000), "UpdatedAt" = now();
SQL
}

# Present, but nothing was copied: a removable drive that is plugged in and
# waiting to be asked. Leaves every date alone, because none of them changed.
offsite_files_seen() {
  q -v slot="$1" >/dev/null 2>&1 <<'SQL' || true
INSERT INTO "BackupTargets" ("Slot", "Kind", "Enabled", "Present", "UpdatedAt")
VALUES (:'slot', 'files', true, true, now())
ON CONFLICT ("Slot", "Kind") DO UPDATE
   SET "Enabled" = true, "Present" = true, "UpdatedAt" = now();
SQL
}

# Marks a slot as off, so a target that is removed from .env stops showing.
offsite_files_disable() {
  q -v slot="$1" >/dev/null 2>&1 <<'SQL' || true
UPDATE "BackupTargets" SET "Enabled" = false, "UpdatedAt" = now()
 WHERE "Slot" = :'slot' AND "Kind" = 'files';
SQL
}
