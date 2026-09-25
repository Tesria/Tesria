#!/usr/bin/env bash
# Take a single archive of the attachments (uploads) volume: backup Layer 3
# (PLAN §5), keeping files and database backups on the same schedule.
# Usable on demand: docker compose exec backup /scripts/backup-files.sh
set -euo pipefail

BACKUP_DIR="/backups"
SRC="/data/uploads"
# The sidecar passes one stamp to both scripts so a dump and its uploads
# archive share it (dev-plan 9.1); run by hand, each takes its own.
STAMP="${BACKUP_STAMP:-$(date -u +%Y%m%dT%H%M%SZ)}"
OUT="${BACKUP_DIR}/uploads-${STAMP}.tar.gz"
# An interrupted run must not leave its .tmp behind (a 0-byte one never goes away).
trap 'rm -f "${OUT}.tmp"' EXIT

log() { echo "[backup $(date -u +%FT%TZ)] $*"; }

mkdir -p "$BACKUP_DIR"
if [ ! -d "$SRC" ]; then
  log "no uploads directory at ${SRC}; skipping file backup"
  exit 0
fi

log "archiving uploads ${SRC} -> ${OUT}"
# Not the restore's own working folders (the review's DATA-02, 2026-09-24):
# .pre-restore is the Undo copy of the files, and an archive that carried it
# brought an older Undo copy back over the current one on the next restore.
tar -czf "${OUT}.tmp" --exclude='./.pre-restore' --exclude='./.restore-staging' -C "$SRC" .
mv "${OUT}.tmp" "${OUT}"
log "OK ${OUT} ($(du -h "${OUT}" | cut -f1))"
