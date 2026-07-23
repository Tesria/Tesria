#!/usr/bin/env bash
# Take a single archive of the attachments (uploads) volume — backup Layer 3
# (PLAN §5), keeping files and database backups on the same schedule.
# Usable on demand: docker compose exec backup /scripts/backup-files.sh
set -euo pipefail

BACKUP_DIR="/backups"
SRC="/data/uploads"
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
OUT="${BACKUP_DIR}/uploads-${STAMP}.tar.gz"

log() { echo "[backup $(date -u +%FT%TZ)] $*"; }

mkdir -p "$BACKUP_DIR"
if [ ! -d "$SRC" ]; then
  log "no uploads directory at ${SRC}; skipping file backup"
  exit 0
fi

log "archiving uploads ${SRC} -> ${OUT}"
tar -czf "${OUT}.tmp" -C "$SRC" .
mv "${OUT}.tmp" "${OUT}"
log "OK ${OUT} ($(du -h "${OUT}" | cut -f1))"
