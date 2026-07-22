#!/usr/bin/env bash
# Backup sidecar entrypoint: takes a logical backup on startup, then every
# BACKUP_INTERVAL_HOURS, pruning dumps older than RETENTION_DAYS.
#
# This is the Phase 1 logical-backup layer. Phase 3 adds pgBackRest continuous
# archiving + point-in-time recovery as a stronger, independent layer.
set -euo pipefail

INTERVAL_HOURS="${BACKUP_INTERVAL_HOURS:-24}"
RETENTION_DAYS="${RETENTION_DAYS:-14}"
BACKUP_DIR="/backups"

log() { echo "[backup $(date -u +%FT%TZ)] $*"; }

mkdir -p "$BACKUP_DIR"
log "sidecar started: interval=${INTERVAL_HOURS}h retention=${RETENTION_DAYS}d target=${PGDATABASE}@${PGHOST}"

while true; do
  if /scripts/backup.sh; then
    log "pruning dumps older than ${RETENTION_DAYS} days"
    find "$BACKUP_DIR" -maxdepth 1 -name 'db-*.dump' -type f -mtime "+${RETENTION_DAYS}" -print -delete || true

    if [ "${BACKUP_S3_ENABLED:-false}" = "true" ]; then
      log "offsite upload is enabled but not yet implemented (Phase 3). Skipping."
      # Phase 3: sync "$BACKUP_DIR" to the configured S3-compatible bucket.
    fi
  else
    log "ERROR: backup failed; will retry next cycle"
  fi

  log "sleeping ${INTERVAL_HOURS}h until next backup"
  sleep "$(( INTERVAL_HOURS * 3600 ))"
done
