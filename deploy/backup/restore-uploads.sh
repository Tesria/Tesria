#!/usr/bin/env bash
# Puts back the attachment files the database refers to and the uploads
# volume does not have, from the uploads archives (T8-017).
#
#   docker compose exec backup /scripts/restore-uploads.sh
#   docker compose exec -e RESTORE_DRY_RUN=1 backup /scripts/restore-uploads.sh
#
# Why it exists. A point-in-time recovery rolls back the database and
# nothing else: pgBackRest's backups hold the cluster, not the uploads
# volume. So after one, the database can refer to files that are gone. The
# case that found it: a space was deleted (which deletes its files), then
# recovered to the minute before, and came back with every picture broken.
#
# What it does. For each file the database names (attachments, avatars,
# image space icons) that is not on the volume, it looks through the uploads
# archives, newest first, and extracts that one file. It never overwrites a
# file and never deletes one, so it is safe to run at any time, as often as
# you like. The admin page runs it by itself after every point-in-time
# restore. A file that was added and removed again between two archives was
# in none of them, and is reported as missing for good.
set -uo pipefail
# comm and sort must agree on the order.
export LC_ALL=C

UPLOADS="${UPLOADS_PATH:-/data/uploads}"
BACKUP_DIR="${BACKUP_DIR:-/backups}"
DRY="${RESTORE_DRY_RUN:-0}"
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

log() { echo "[restore-uploads $(date -u +%FT%TZ)] $*"; }

[ -d "$UPLOADS" ] || { echo "ERROR: there is no uploads directory at $UPLOADS"; exit 1; }

# Every file the database refers to. Keys are flat names on the volume (the
# app keeps only the last path segment), so only those are asked for, and
# only names made of the characters the app itself writes.
if ! psql -X -q -t -A -v ON_ERROR_STOP=1 >"$WORK/referenced" 2>"$WORK/psql.err" <<'SQL'
SELECT "StorageKey" FROM "Attachments"
UNION SELECT "AvatarKey" FROM "Users" WHERE "AvatarKey" IS NOT NULL
UNION SELECT 'space-icons/' || "Id" || '.webp' FROM "Spaces" WHERE "IconKind" = 2;
SQL
then
  echo "ERROR: could not read the attachment list from the database: $(head -1 "$WORK/psql.err")"
  exit 1
fi

: >"$WORK/missing"
while IFS= read -r key; do
  name="${key##*/}"
  [[ "$name" =~ ^[A-Za-z0-9._-]+$ ]] || continue
  [ -e "$UPLOADS/$name" ] || echo "$name" >>"$WORK/missing"
done <"$WORK/referenced"
sort -u -o "$WORK/missing" "$WORK/missing"

referenced="$(grep -c . "$WORK/referenced" || true)"
missing="$(grep -c . "$WORK/missing" || true)"
log "the database refers to $referenced file(s); $missing of them are not on the uploads volume"
if [ "$missing" -eq 0 ]; then
  echo "Every attachment file the wiki refers to is present. Nothing to put back."
  exit 0
fi

restored=0
# Newest first, by the stamp in the name, which is when it was taken.
for archive in $(find "$BACKUP_DIR" -maxdepth 1 -type f -name 'uploads-*.tar.gz' -printf '%f\n' 2>/dev/null | sort -r); do
  [ -s "$WORK/missing" ] || break
  # The archive's members are ./<name>; keep only those still missing.
  tar -tzf "$BACKUP_DIR/$archive" 2>/dev/null | sed -n 's|^\./||p' | sort -u >"$WORK/members"
  comm -12 "$WORK/missing" "$WORK/members" >"$WORK/found"
  n="$(grep -c . "$WORK/found" || true)"
  [ "$n" -gt 0 ] || continue
  if [ "$DRY" = 1 ]; then
    log "DRY RUN: would put back $n file(s) from $archive"
  else
    sed 's|^|./|' "$WORK/found" >"$WORK/names"
    # --skip-old-files: a file that is there already is left exactly as it is.
    if ! tar -xzf "$BACKUP_DIR/$archive" -C "$UPLOADS" --skip-old-files -T "$WORK/names"; then
      log "WARNING: $archive could not be read completely; trying the older archives for the rest"
      continue
    fi
    log "put back $n file(s) from $archive"
  fi
  restored=$(( restored + n ))
  comm -23 "$WORK/missing" "$WORK/found" >"$WORK/rest" && mv "$WORK/rest" "$WORK/missing"
done

left="$(grep -c . "$WORK/missing" || true)"
if [ "$left" -gt 0 ]; then
  log "not in any archive: $(head -20 "$WORK/missing" | tr '\n' ' ')$([ "$left" -gt 20 ] && echo "and $(( left - 20 )) more")"
  echo "ERROR: put back $restored attachment file(s); $left are in no uploads archive and cannot be restored (they were added after the newest archive, or between two)."
  exit 1
fi
if [ "$DRY" = 1 ]; then
  echo "DRY RUN: would put back all $restored missing attachment file(s)."
else
  echo "Put back all $restored missing attachment file(s) from the uploads archives."
fi
