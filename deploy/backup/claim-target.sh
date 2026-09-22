#!/usr/bin/env bash
# Claims a mounted path as a Tesria backup target (dev-plan 9.2 step 3).
#
#   docker compose exec backup /scripts/claim-target.sh nas
#   docker compose exec backup /scripts/claim-target.sh removable
#
# Writes .tesria-backup-target into the mount, and that file is the whole
# safety mechanism. A network share that is offline, or a drive that is
# unplugged, leaves an ordinary empty directory behind at the mount point.
# Backing up into that would not fail: it would quietly write to the boot
# disk until it filled. A sentinel on the target itself cannot be faked by
# an absent mount, and a mount-point check cannot do this job from inside a
# container, where a bind mount is always a mount point.
#
# Run once per target. It refuses to overwrite one that already exists, so
# pointing two instances at one directory is caught rather than silently
# shared.
set -uo pipefail

SLOT="${1:-}"
case "$SLOT" in
  nas)       MOUNT=/mnt/nas ;;
  removable) MOUNT=/mnt/removable ;;
  *) echo "usage: claim-target.sh nas|removable" >&2; exit 2 ;;
esac
# The variables are spelled in upper case; the slot is not.
SLOT_UPPER="$(printf '%s' "$SLOT" | tr '[:lower:]' '[:upper:]')"

SENTINEL="$MOUNT/.tesria-backup-target"

if [ ! -d "$MOUNT" ]; then
  echo "There is no $MOUNT in this container. Set OFFSITE_${SLOT_UPPER}_PATH in .env" >&2
  echo "and restart the backup sidecar." >&2
  exit 1
fi

# An empty directory here is the dangerous case, so say so plainly rather
# than claiming it: it is what an unmounted share looks like.
if [ -z "$(ls -A "$MOUNT" 2>/dev/null)" ]; then
  echo "WARNING: $MOUNT is empty."
  echo "If your share or drive should already have files on it, it is probably"
  echo "not mounted, and claiming it now would point backups at the boot disk."
  printf "Claim it anyway? [y/N] "
  read -r reply
  case "$reply" in y|Y|yes|YES) ;; *) echo "Nothing was written."; exit 1 ;; esac
fi

if [ -e "$SENTINEL" ]; then
  echo "This target is already claimed:"
  sed 's/^/  /' "$SENTINEL"
  exit 0
fi

instance="$(hostname)"
cat > "$SENTINEL" <<SENT || { echo "Could not write to $MOUNT (read-only?)" >&2; exit 1; }
# Written by Tesria (dev-plan 9.2) to mark this directory as a backup target.
# The backup sidecar refuses to write here unless this file is present, which
# is how it tells a mounted share from an empty mount point.
# Deleting it stops backups to this target; it does not delete any backup.
slot=$SLOT
claimed=$(date -u +%FT%TZ)
by=$instance
SENT

echo "Claimed $MOUNT for the $SLOT slot."
echo "Backups to it will start on the next pass, if OFFSITE_${SLOT_UPPER}_PASSPHRASE is set."
