#!/usr/bin/env bash
# Take a single compressed logical backup (pg_dump custom format).
# Usable both from the sidecar loop and on demand:
#   docker compose exec backup /scripts/backup.sh
set -euo pipefail

BACKUP_DIR="/backups"
# The sidecar passes one stamp to both scripts so a dump and its uploads
# archive share it (dev-plan 9.1); run by hand, each takes its own.
STAMP="${BACKUP_STAMP:-$(date -u +%Y%m%dT%H%M%SZ)}"
OUT="${BACKUP_DIR}/db-${STAMP}.dump"
# An interrupted run must not leave its .tmp behind (a 0-byte one never goes away).
trap 'rm -f "${OUT}.tmp"' EXIT

log() { echo "[backup $(date -u +%FT%TZ)] $*"; }

mkdir -p "$BACKUP_DIR"
log "dumping ${PGDATABASE} -> ${OUT}"

# --format=custom: compressed, selective restore, works across PG versions.
pg_dump --format=custom --no-owner --no-privileges --file="${OUT}.tmp" \
  --dbname="${PGDATABASE}"

mv "${OUT}.tmp" "${OUT}"

# Integrity check: a readable table of contents proves the archive is valid.
if pg_restore --list "${OUT}" >/dev/null 2>&1; then
  SIZE="$(du -h "${OUT}" | cut -f1)"
  log "OK ${OUT} (${SIZE})"
else
  log "ERROR: produced archive failed integrity check: ${OUT}"
  exit 1
fi
