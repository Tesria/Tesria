#!/usr/bin/env bash
# Verify the newest (or a named) backup by restoring it into a throwaway
# database and confirming it loads. An untested backup is not a backup.
#
#   docker compose exec backup /scripts/verify-backup.sh
set -euo pipefail

BACKUP_DIR="/backups"
VERIFY_DB="ccrestore_verify_$$"
log() { echo "[verify $(date -u +%FT%TZ)] $*"; }

ARG="${1:-}"
if [ -n "$ARG" ]; then
  DUMP="${BACKUP_DIR}/${ARG#"$BACKUP_DIR"/}"
else
  # shellcheck disable=SC2012  # controlled db-<timestamp>.dump names, mtime order is intended
  DUMP="$(ls -1t "${BACKUP_DIR}"/db-*.dump 2>/dev/null | head -n1 || true)"
fi

if [ -z "${DUMP:-}" ] || [ ! -f "$DUMP" ]; then
  log "ERROR: no backup file found"
  exit 1
fi

log "verifying ${DUMP} into throwaway db ${VERIFY_DB}"

cleanup() {
  psql --dbname="postgres" -v ON_ERROR_STOP=1 \
    -c "DROP DATABASE IF EXISTS \"${VERIFY_DB}\";" >/dev/null 2>&1 || true
}
trap cleanup EXIT

psql --dbname="postgres" -v ON_ERROR_STOP=1 \
  -c "CREATE DATABASE \"${VERIFY_DB}\";" >/dev/null

pg_restore --no-owner --no-privileges --dbname="${VERIFY_DB}" "${DUMP}"

TABLES="$(psql --dbname="${VERIFY_DB}" -tAc \
  "SELECT count(*) FROM information_schema.tables WHERE table_schema='public';")"

log "OK: restored cleanly, ${TABLES} public table(s) present"
