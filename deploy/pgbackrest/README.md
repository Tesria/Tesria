# pgBackRest (Phase 3)

This directory will hold the pgBackRest configuration that adds the strongest
backup layer: **continuous WAL archiving + point-in-time recovery (PITR)** —
the ability to restore the database to any moment (e.g. "just before the
accidental delete at 14:32").

Planned in Phase 3:

- `pgbackrest.conf` — repository, retention, and encryption settings.
- Postgres configured with `archive_mode = on` and `archive_command` pointing
  at pgBackRest.
- A `pgbackrest` service (or extension of the `backup` service) running
  scheduled `full` and `incr` backups.
- Optional S3-compatible offsite repository (see `BACKUP_S3_ENABLED` in `.env`).
- Restore runbook covering full disaster recovery and PITR in
  `docs/backup-recovery.md`.

Until then, the Phase 1 logical backup layer (`deploy/backup/`, nightly
`pg_dump` with retention and a verify script) protects your data.
