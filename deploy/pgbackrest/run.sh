#!/usr/bin/env bash
# pgBackRest sidecar: creates the stanza, then runs scheduled physical backups
# (a full every 7th run, incrementals in between). Runs as the postgres user so
# it can read PGDATA (shared read-only) and connect over the shared socket.
set -euo pipefail

STANZA=main
INTERVAL_HOURS="${BACKUP_INTERVAL_HOURS:-24}"

# pgBackRest writes here; ensure the postgres user owns them on the shared repo.
mkdir -p /var/lib/pgbackrest /var/spool/pgbackrest /var/log/pgbackrest
chown -R postgres:postgres /var/lib/pgbackrest /var/spool/pgbackrest /var/log/pgbackrest

pgbr() { gosu postgres pgbackrest --stanza="$STANZA" "$@"; }

echo "[pgbackrest] waiting for postgres socket..."
until gosu postgres pg_isready -h /var/run/postgresql -q; do sleep 2; done

# Create the stanza (idempotent) and validate the archiving configuration.
echo "[pgbackrest] creating stanza (if needed) and checking config..."
pgbr stanza-create || true
pgbr check

count=0
while true; do
  if (( count % 7 == 0 )); then
    echo "[pgbackrest] full backup"
    pgbr backup --type=full
  else
    echo "[pgbackrest] incremental backup"
    pgbr backup --type=incr
  fi
  count=$((count + 1))
  sleep "$(( INTERVAL_HOURS * 3600 ))"
done
