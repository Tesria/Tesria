#!/usr/bin/env bash
# Restore the database from a logical backup. DESTRUCTIVE: it drops and
# recreates the target database.
#
#   docker compose exec backup /scripts/restore.sh                # newest dump
#   docker compose exec backup /scripts/restore.sh db-2026....dump # specific
#
# For point-in-time recovery (restore to an exact moment), see Phase 3 /
# docs/backup-recovery.md — that uses pgBackRest, not this script.
set -euo pipefail

BACKUP_DIR="/backups"
log() { echo "[restore $(date -u +%FT%TZ)] $*"; }

ARG="${1:-}"
if [ -n "$ARG" ]; then
  DUMP="${BACKUP_DIR}/${ARG#"$BACKUP_DIR"/}"
else
  # shellcheck disable=SC2012  # controlled db-<timestamp>.dump names, mtime order is intended
  DUMP="$(ls -1t "${BACKUP_DIR}"/db-*.dump 2>/dev/null | head -n1 || true)"
fi

if [ -z "${DUMP:-}" ] || [ ! -f "$DUMP" ]; then
  log "ERROR: no backup file found (looked for ${ARG:-newest db-*.dump})"
  exit 1
fi

log "restoring ${PGDATABASE} from ${DUMP}"
log "WARNING: this drops and recreates database '${PGDATABASE}' in 5s. Ctrl-C to abort."
sleep 5

# Recreate the target database, then restore into it. Connect via the default
# 'postgres' database to be able to drop the target.
psql --dbname="postgres" -v ON_ERROR_STOP=1 <<SQL
SELECT pg_terminate_backend(pid) FROM pg_stat_activity
  WHERE datname = '${PGDATABASE}' AND pid <> pg_backend_pid();
DROP DATABASE IF EXISTS "${PGDATABASE}";
CREATE DATABASE "${PGDATABASE}";
SQL

pg_restore --no-owner --no-privileges --dbname="${PGDATABASE}" "${DUMP}"
log "restore complete from ${DUMP}"
