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

## Offsite backups (dev-plan 9.2)

`offsite.sh` turns the `OFFSITE_CLOUD_*` slot in `.env` into pgBackRest's
`repo2`, as a drop-in at `/etc/pgbackrest/conf.d/repo2-cloud.conf`. Both the
`db` container (whose `archive_command` pushes WAL) and this sidecar write it
at start; when the slot is empty the file is absent and pgBackRest behaves as
it always did.

Two rules for anyone editing the configuration:

- **A drop-in must not repeat an option from `pgbackrest.conf`.** pgBackRest
  refuses with `option '...' cannot be set multiple times`. Everything
  generated is `repo2-*` or absent from the main file.
- **Never pass `PGBACKREST_REPO2_*` as environment variables.** A variable
  that is defined but empty is a hard error (`environment variable
  'repo2-type' must have a value`), and Compose cannot leave one out, so an
  instance with no offsite target would fail to archive WAL at all.

### Testing it without a cloud account

MinIO is an S3 server that runs on the laptop. It is in the compose file
behind a profile, so it is off unless asked for.

```bash
docker compose --profile offsite-test up -d minio
```

Create the bucket (the MinIO image carries the `mc` client):

```bash
docker compose exec minio mc alias set local http://localhost:9000 tesria-test tesria-test-secret
```

```bash
docker compose exec minio mc mb --ignore-existing local/tesria-backups
```

Then put this in `.env` and restart `db` and `pgbackrest`. Note
`URI_STYLE=path`: MinIO does not do host-style buckets.

```
OFFSITE_CLOUD_TYPE=s3
OFFSITE_CLOUD_ENDPOINT=minio:9000
OFFSITE_CLOUD_BUCKET=tesria-backups
OFFSITE_CLOUD_REGION=us-east-1
OFFSITE_CLOUD_URI_STYLE=path
OFFSITE_CLOUD_KEY=tesria-test
OFFSITE_CLOUD_SECRET=tesria-test-secret
OFFSITE_CLOUD_PASSPHRASE=a-different-passphrase-from-the-local-one
OFFSITE_CLOUD_VERIFY_TLS=n
```

`OFFSITE_CLOUD_VERIFY_TLS=n` is for MinIO over plain HTTP on the compose
network only. Never set it against a real provider: it turns off certificate
checking, and the backup is leaving the building.
