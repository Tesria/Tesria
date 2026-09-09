# Backup & Recovery Runbook

Data safety is the top priority for this self-hosted app. Backups are layered
so a single failure never loses data.

## Layers

| Layer | What | Status |
|-------|------|--------|
| 1. Physical PITR | pgBackRest continuous WAL archiving → restore to any second | **Active** |
| 2. Logical dumps | Scheduled `pg_dump` (custom format) with retention + verify | **Active** |
| 3. File backups | Attachment (`uploads`) archives on the same schedule | **Active** |
| 4. Offsite | S3-compatible replication, encrypted | Optional — off by default |
| 5. In-app | Page version history + trash / soft-delete | **Active** |

The pgBackRest repository is **encrypted at rest** (AES-256-CBC) with the
passphrase from `BACKUP_ENCRYPTION_KEY`. **Keep that key safe and off-box** — the
repository cannot be restored without it.

---

## Layer 1 — pgBackRest (physical backups + point-in-time recovery)

The `db` image bundles pgBackRest and archives every WAL segment
(`archive_command`) to the `pgbackrest` repository volume. The `pgbackrest`
sidecar creates the stanza and runs scheduled backups (a full every 7th run,
incrementals in between) via `deploy/pgbackrest/run.sh`.

```bash
# Show repository status, backup list, and WAL range
docker compose exec pgbackrest gosu postgres pgbackrest --stanza=main info

# Take a backup right now (full, or use --type=incr / --type=diff)
docker compose exec pgbackrest gosu postgres pgbackrest --stanza=main backup --type=full

# Verify: restore the latest backup to a throwaway dir and sanity-check it
docker compose exec pgbackrest bash /scripts/verify.sh

# Deep end-to-end check: prove PITR replays to an exact target time
docker compose exec pgbackrest bash /scripts/pitr-selftest.sh
```

Run `verify.sh` on a schedule you trust — an unverified backup is not a backup.

---

## Layer 2 — logical dumps, and Layer 3 — file backups

The `backup` container (`deploy/backup/run.sh`) takes a compressed `pg_dump` and
a `tar` of the `uploads` volume on startup and every `BACKUP_INTERVAL_HOURS`,
pruning both older than `BACKUP_RETENTION_DAYS`. Artifacts land on the `backups`
volume as `db-<timestamp>.dump` and `uploads-<timestamp>.tar.gz`.

```bash
docker compose exec backup /scripts/backup.sh          # DB dump now
docker compose exec backup /scripts/backup-files.sh    # uploads archive now
docker compose exec backup /scripts/verify-backup.sh   # test-restore newest dump
```

Copy the whole `backups` volume off the box periodically (or enable offsite,
below):

```bash
docker run --rm -v tesria_backups:/b -v "$PWD":/out alpine \
  tar czf /out/tesria-backups.tgz -C /b .
```

---

## The runtime database role and restores

Since dev-plan 3.1 the app runs as `tesria_app`, a least-privilege role it
creates itself at startup from the owner connection. Nothing in this
runbook changes: backups and restores keep using the owner (`POSTGRES_USER`),
`pg_restore` already runs `--no-privileges`, and the role is re-provisioned
the next time the app starts — including on a brand-new host where it did
not exist. Two things worth knowing:

* Start `app` before `collab` after a restore (compose does: collab depends
  on app being healthy), because collab signs in as the role the app creates.
* After any restore, run `scripts/verify-audit-chain.sh` (or Admin →
  Security → Verify). A point-in-time restore legitimately shortens the
  audit chain to the target time; the chain will verify, and the in-process
  monitor's "shorter than last time" warning on the next daily run is
  expected once. A restore should never produce a *broken* chain — if it
  does, the backup itself was taken from an already-tampered database.

## Recovery scenarios

### A. A user deleted or broke a page (the common case)

No ops required — use the in-app safety nets:

- **Bad edit:** open the page → **History** tab → preview a prior version →
  **Restore** (rollback appends a new version; nothing is lost).
- **Deleted page:** the space sidebar → **Trash** → **Restore** (brings the page
  and its sub-pages back).

### B. Point-in-time recovery (bad migration, mass delete, corruption)

Roll the database back to an exact moment — e.g. just before a bad change at
`14:32`.

```bash
# 1. Stop everything that talks to the database.
docker compose stop app pgbackrest db

# 2. Restore in place to the target time (omit the timestamp to roll forward
#    to the very latest state instead).
docker compose run --rm --no-deps --entrypoint bash db \
  /scripts/restore.sh "2026-07-23 14:32:00+00"

# 3. Start the database; Postgres replays WAL and promotes at the target.
docker compose start db
docker compose logs -f db      # watch for "database system is ready"

# 4. Bring the rest back up.
docker compose up -d
```

Timestamps use Postgres syntax with a timezone offset (e.g. `+00` for UTC).

### C. Full disaster recovery on a new host

1. Install Docker, clone the repo, and recreate `.env` — **including the same
   `BACKUP_ENCRYPTION_KEY`** as the original instance.
2. Restore the pgBackRest repository (and `uploads`) onto the new host. If you
   kept an offsite copy of the `pgbackrest` volume, load it into a fresh volume;
   if you use an S3 repo (below), it is already reachable.
3. Recreate the stanza owner metadata and restore:
   ```bash
   docker compose up -d db      # starts empty; stop Postgres before restoring
   docker compose stop db
   docker compose run --rm --no-deps --entrypoint bash db /scripts/restore.sh
   docker compose start db
   ```
4. `docker compose up -d` to bring up the whole stack.

If you only have the logical dumps, restore the newest instead:

```bash
docker compose up -d db backup
docker compose exec backup /scripts/restore.sh          # newest dump (destructive)
# then restore attachments:
docker run --rm -v tesria_uploads:/u -v tesria_backups:/b alpine \
  sh -c 'cd /u && tar xzf /b/uploads-<timestamp>.tar.gz'
```

---

## Offsite backups (optional, off by default)

Set `BACKUP_S3_ENABLED=true` and the `S3_*` values in `.env`, then choose one or
both:

- **pgBackRest S3 repository (recommended for the database).** pgBackRest has
  native, encrypted, deduplicated S3 support. Point the repository at S3 by
  supplying these to the `db` and `pgbackrest` services (via `.env` →
  compose `environment`) instead of the local repo:
  ```
  PGBACKREST_REPO1_TYPE=s3
  PGBACKREST_REPO1_S3_ENDPOINT=${S3_ENDPOINT}
  PGBACKREST_REPO1_S3_BUCKET=${S3_BUCKET}
  PGBACKREST_REPO1_S3_REGION=${S3_REGION}
  PGBACKREST_REPO1_S3_KEY=${S3_ACCESS_KEY}
  PGBACKREST_REPO1_S3_KEY_SECRET=${S3_SECRET_KEY}
  ```
  Continuous WAL and full/incr backups then land offsite automatically, still
  encrypted with `BACKUP_ENCRYPTION_KEY`.
- **File replication for the logical dumps + uploads archives.** Sync the
  `backups` volume to your bucket with a tool of choice (`rclone`, `aws s3
  sync`) from a scheduled job or a small sidecar.

Test a restore from the offsite copy the same way as Scenario C — untested
offsite backups are not backups either.
