# pgBackRest: physical backups + point-in-time recovery

This is backup **Layer 1** (PLAN §5): continuous WAL archiving and the ability
to restore the database to any moment (e.g. "just before the accidental delete
at 14:32").

## How it fits together

- The `db` image (`deploy/db/Dockerfile`) is PostgreSQL 18 with pgBackRest
  installed. `docker-compose.yml` enables archiving via
  `archive_command = pgbackrest --stanza=main archive-push %p`.
- `pgbackrest.conf` is mounted into both the `db` container (which archives WAL)
  and the `pgbackrest` sidecar (which creates the stanza and runs backups). The
  repository lives on the `pgbackrest` volume; the sidecar reaches Postgres over
  the shared `pgsocket` volume and reads PGDATA via the shared `pgdata` volume.
- The repository is encrypted (AES-256-CBC) using `BACKUP_ENCRYPTION_KEY`; the
  connection user comes from `POSTGRES_USER`. Both are passed as `PGBACKREST_*`
  environment variables so no secrets live in this file.

## Scripts

| Script | Purpose |
|--------|---------|
| `run.sh` | Sidecar entrypoint: stanza-create + check, then the shared loop in `deploy/backup/common.sh` (schedule, job queue, retention, reporting to the app; dev-plan 9.1). |
| `verify.sh` | Restore the latest backup, or `--set=LABEL`, to a throwaway dir and sanity-check it. The admin page's Test restore runs this. |
| `pitr-selftest.sh` | Prove PITR end-to-end: recover to a target time and check the result. |
| `restore.sh` | Disaster-recovery / PITR restore into the live data dir (Postgres stopped). |

See [`docs/backup-recovery.md`](../../docs/backup-recovery.md) for the full
runbook, including point-in-time recovery. Retention is set on the admin page, not in `pgbackrest.conf`.
