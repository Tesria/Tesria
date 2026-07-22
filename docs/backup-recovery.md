# Backup & Recovery Runbook

Data safety is the top priority for this self-hosted app. Backups are layered
so a single failure never loses data.

## Layers

| Layer | What | Status |
|-------|------|--------|
| 1. Logical dumps | Scheduled `pg_dump` (custom format) with retention + verify | **Active (Phase 1)** |
| 2. Physical PITR | pgBackRest continuous WAL archiving → restore to any second | Planned (Phase 3) |
| 3. File backups | Attachment (`uploads`) backups alongside the database | Planned (Phase 3, with attachments) |
| 4. Offsite | S3-compatible replication of backups, encrypted | Planned (Phase 3) — `BACKUP_S3_ENABLED` |
| 5. In-app | Page version history + trash/soft-delete | Planned (Phase 2–3) |

## Layer 1 — logical backups (active now)

The `backup` container runs `deploy/backup/run.sh`: a backup on startup, then
every `BACKUP_INTERVAL_HOURS`, pruning dumps older than `BACKUP_RETENTION_DAYS`.
Dumps land on the `backups` volume as `db-<UTC-timestamp>.dump`.

### Commands

```bash
# Take a backup right now
docker compose exec backup /scripts/backup.sh

# Verify the newest backup by restoring it into a throwaway database
docker compose exec backup /scripts/verify-backup.sh

# Restore the newest backup (DESTRUCTIVE: drops & recreates the database)
docker compose exec backup /scripts/restore.sh

# Restore a specific backup
docker compose exec backup /scripts/restore.sh db-20260722T030000Z.dump
```

Each dump is integrity-checked (`pg_restore --list`) right after creation.
`verify-backup.sh` goes further and does a full test restore — run it on a
schedule you trust, because an unverified backup is not a backup.

### Copying backups off the box

Until offsite replication lands (Phase 3), copy the `backups` volume elsewhere:

```bash
docker run --rm -v confluenceclone_backups:/b -v "$PWD":/out alpine \
  tar czf /out/confluenceclone-backups.tgz -C /b .
```

## Recovery scenarios

**A. A user deleted/broke a page.**
Phase 2+: restore from in-app version history or trash — no ops needed. This
will be the common case and avoids touching database backups at all.

**B. Database corruption / bad migration — roll back to last good state.**
Restore the newest (or a chosen) logical dump with `restore.sh`. With Phase 3
pgBackRest you will instead restore to an exact point in time.

**C. Full disaster recovery on a new host.**
1. Install Docker, clone the repo, recreate `.env`.
2. Restore your latest `backups` archive into a fresh `backups` volume.
3. `docker compose up -d db backup`, then
   `docker compose exec backup /scripts/restore.sh`.
4. `docker compose up -d` to bring up the rest.

## Phase 3 preview — pgBackRest PITR

Configuration will live in `deploy/pgbackrest/`. Postgres will archive WAL to a
pgBackRest repository (local and/or S3), enabling `full`/`incr` backups and
restore to an arbitrary timestamp. See `deploy/pgbackrest/README.md`.
