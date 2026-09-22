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
    *) return 1 ;;
  esac
  return 0
}

# restic refuses a self-signed certificate unless told; the test harness runs
# MinIO with one. Never set for a real provider: the backup is leaving.
restic_tls_flag() {
  [ "${OFFSITE_CLOUD_VERIFY_TLS:-y}" = "n" ] && echo "--insecure-tls"
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

# What this slot holds, for the screen and the alerts.
offsite_files_publish() {
  local slot="$1" verified="$2" bytes="" snaps=""
  bytes="$(rst stats --mode raw-data --json 2>/dev/null | sed -n 's/.*"total_size":\([0-9]*\).*/\1/p')"
  snaps="$(rst snapshots --json 2>/dev/null | grep -o '"short_id"' | wc -l | tr -d ' ')"
  q -v slot="$slot" -v verified="$verified" -v bytes="${bytes:-}" -v snaps="${snaps:-0}" \
    -v keyfp="$(offsite_fingerprint "${OFFSITE_CLOUD_KEY:-}")" \
    -v passfp="$(offsite_fingerprint "${OFFSITE_CLOUD_PASSPHRASE:-}")" \
    -v type="${OFFSITE_CLOUD_TYPE:-}" -v loc="${OFFSITE_CLOUD_ENDPOINT:-}" \
    -v bucket="${OFFSITE_CLOUD_BUCKET:-}" -v prefix="${OFFSITE_CLOUD_PATH:-/tesria}" \
    >/dev/null 2>&1 <<'SQL' || true
INSERT INTO "BackupTargets" ("Slot", "Kind", "Type", "Location", "Bucket", "Prefix", "Enabled",
                             "KeyFingerprint", "PassphraseFingerprint",
                             "LastBackupAt", "LastVerifyAt", "BytesStored", "Message", "UpdatedAt")
VALUES (:'slot', 'files', NULLIF(:'type',''), NULLIF(:'loc',''), NULLIF(:'bucket',''), NULLIF(:'prefix',''),
        true, NULLIF(:'keyfp',''), NULLIF(:'passfp',''),
        now(), CASE WHEN :'verified' = 't' THEN now() END,
        NULLIF(:'bytes','')::bigint, NULL, now())
ON CONFLICT ("Slot", "Kind") DO UPDATE
   SET "Type" = EXCLUDED."Type", "Location" = EXCLUDED."Location", "Bucket" = EXCLUDED."Bucket",
       "Prefix" = EXCLUDED."Prefix", "Enabled" = true,
       "KeyFingerprint" = EXCLUDED."KeyFingerprint",
       "PassphraseFingerprint" = EXCLUDED."PassphraseFingerprint",
       "LastBackupAt" = now(),
       "LastVerifyAt" = coalesce(EXCLUDED."LastVerifyAt", "BackupTargets"."LastVerifyAt"),
       "BytesStored" = EXCLUDED."BytesStored", "Message" = NULL, "UpdatedAt" = now();
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

# Marks a slot as off, so a target that is removed from .env stops showing.
offsite_files_disable() {
  q -v slot="$1" >/dev/null 2>&1 <<'SQL' || true
UPDATE "BackupTargets" SET "Enabled" = false, "UpdatedAt" = now()
 WHERE "Slot" = :'slot' AND "Kind" = 'files';
SQL
}
