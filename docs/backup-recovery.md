# Backup & Recovery Runbook

Data safety is the top priority for this self-hosted app. Backups are layered
so a single failure never loses data.

## Layers

| Layer | What | Status |
|-------|------|--------|
| 1. Physical PITR | pgBackRest continuous WAL archiving → restore to any second | **Active** |
| 2. Logical dumps | Scheduled `pg_dump` (custom format) with retention + verify | **Active** |
| 3. File backups | Attachment (`uploads`) archives on the same schedule | **Active** |
| 4. Offsite | A copy off this machine | **Not implemented** (dev-plan 9.2) |
| 5. In-app | Page version history + trash / soft-delete | **Active** |

Layers 1 to 3 report to the app and are managed from **Administration →
Backups** (dev-plan 9.1): status, a retention policy that covers all three,
**Back up now**, and **Test restore**. Start there; the commands below are for
when the app is down or you are working on the host.

The pgBackRest repository is **encrypted at rest** (AES-256-CBC) with the
passphrase from `BACKUP_ENCRYPTION_KEY`. **Keep that key safe and off-box**: the
repository cannot be restored without it.

---

## Administration → Backups

- **Status.** One card per backup agent: last backup, next run, how far back
  a restore reaches (for pgBackRest, the window you can restore to any moment
  in), disk free, the last restore test, and the success rate over 30 days.
  An agent that stops checking in, falls behind, fails a backup or a restore
  test, or runs low on disk raises an alert to every administrator (Security
  tab, and email when email is configured).
- **Retention policy.** Either keep every backup forever, or keep the newest
  *N* backups and everything from the last *D* days: a backup is removed only
  when it is outside both. The policy applies to the database dumps with their
  uploads archives, and to pgBackRest's full backups with everything that
  depends on them. It affects nothing but backups. A change is previewed
  first, needs your password, and is audited. **A change that could remove
  more waits 24 hours before it takes effect**, and every administrator is
  alerted when it is saved; loosening applies at once.
- **Back up now** queues a backup on both agents; each picks it up within a
  minute. **Test restore** restores that backup somewhere throwaway (a scratch
  database, or a scratch directory for pgBackRest) and records the result.
- **Upgrading from before 9.1:** the first start seeds the policy from
  `BACKUP_RETENTION_DAYS` (days) with *N* = 3, and the agents then wait their
  24 hours before removing anything. After that the variable is not read.

---

## Layer 1: pgBackRest (physical backups + point-in-time recovery)

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

Run `verify.sh` on a schedule you trust (or press **Test restore**); an
unverified backup is not a backup. `verify.sh --set=LABEL` tests one backup.

Retention is not in `pgbackrest.conf` any more: `repo1-retention-full=9999999`
there only stops the expire that follows every backup from removing anything.
The sidecar expires according to the admin page's policy after each scheduled
or requested backup. **A backup you take by hand with the command above never
expires anything.** A full backup is taken when the newest is
`BACKUP_FULL_EVERY_DAYS` (default 7) old; a restart no longer forces one.

---

## Layer 2 (logical dumps, and Layer 3) file backups

The `backup` container (`deploy/backup/run.sh`) takes a compressed `pg_dump` and
a `tar` of the `uploads` volume every `BACKUP_INTERVAL_HOURS`, sharing one
timestamp, and applies the retention policy from the admin page. A failed run
is retried after 15 minutes (doubling to at most 6 hours) instead of waiting a
whole interval. Artifacts land on the `backups` volume as `db-<timestamp>.dump`
and `uploads-<timestamp>.tar.gz`.

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
the next time the app starts, including on a brand-new host where it did
not exist. Two things worth knowing:

* Start `app` before `collab` after a restore (compose does: collab depends
  on app being healthy), because collab signs in as the role the app creates.
* After any restore, run `scripts/verify-audit-chain.sh` (or Admin →
  Security → Verify). A point-in-time restore legitimately shortens the
  audit chain to the target time; the chain will verify, and the in-process
  monitor's "shorter than last time" warning on the next daily run is
  expected once. A restore should never produce a *broken* chain, if it
  does, the backup itself was taken from an already-tampered database.

## Recovery scenarios

### A. A user deleted or broke a page (the common case)

No ops required: use the in-app safety nets:

- **Bad edit:** open the page → **History** tab → preview a prior version →
  **Restore** (rollback appends a new version; nothing is lost).
- **Deleted page:** the space sidebar → **Trash** → **Restore** (brings the page
  and its sub-pages back).

### A2. A space was deleted

Deleting a space (dev-plan 11.3) is not a trash operation: the pages,
versions, comments, attachments and history are gone from the database, and
the Trash cannot bring them back. Only a backup taken **before** the deletion
still holds them, so this is scenario **B** if the instance is otherwise
healthy and you want the moment just before it, or **C** if you are rebuilding
anyway.

The audit log survives, and it is where to start: `space.deleted` records the
key, the name, the page and attachment counts, the bytes and who did it, which
gives you both the timestamp for a point-in-time target and a way to check
afterwards that everything came back.

**Orphaned files.** Attachment bytes are deleted after the database commits,
best effort. A file that will not delete is logged with its storage key
(`Orphaned attachment file after deleting space …`), and a crash between the
commit and the sweep leaves the same thing behind. These are harmless but they
occupy the uploads volume; `grep Orphaned` in the app logs lists them for
removal from `Storage:UploadsPath`. The failure is always in this direction:
never a half-deleted space, only bytes with nothing pointing at them.

### B. Point-in-time recovery (bad migration, mass delete, corruption)

Roll the database back to an exact moment: e.g. just before a bad change at
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

1. Install Docker, clone the repo, and recreate `.env`, **including the same
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

## Offsite backups: the cloud repository (dev-plan 9.2, step 1)

A second pgBackRest repository, `repo2`, on S3-compatible storage. WAL
streams to it continuously alongside the local repository, and a full backup
goes weekly, so point-in-time recovery exists off this machine. Backblaze B2
is the documented default; anything with an S3 API works.

**Configuration is in `.env` only**, under `OFFSITE_CLOUD_*`, and is read by
the backup sidecars alone. It is deliberately not in the admin page: Data
Protection keys live in the database, so a storage key held there would
travel inside every backup along with the means to decrypt it. The admin
page shows fingerprints, which is all the app is ever given.

`OFFSITE_CLOUD_PASSPHRASE` is **separate from `BACKUP_ENCRYPTION_KEY`**, so
a leaked remote passphrase cannot read the local repository. Escrow both off
this machine. A lost passphrase is an unreadable copy with no way back.

### What a dead remote does, and why the queue limit matters

This is the part to understand before turning it on, and it was confirmed by
experiment on pgBackRest 2.59.1 rather than assumed:

- A WAL segment is acknowledged to Postgres only once **every** repository
  has it. If the cloud goes away, WAL is not acknowledged, `.ready` files
  pile up in `pg_wal/archive_status`, and `pg_wal` grows.
- `archive-push-queue-max` (set from `OFFSITE_ARCHIVE_QUEUE_MAX`, default
  16GiB) is what stops that filling the disk. Past the limit pgBackRest tells
  Postgres the segment is archived and **drops it**.
- **A dropped segment is lost from the local repository too**, not just the
  offsite one. So an unattended cloud outage does not merely cost the offsite
  copy: left long enough, it breaks point-in-time recovery locally as well.
- Which is why the **`backup.offsite_archive_gap` alert fires on a backlog of
  three segments**, long before the limit trips. Treat it as urgent: either
  fix the remote or clear the slot from `.env` and restart `db`, and then
  **take a full backup**, because recovery before the gap is broken either way.
- pgBackRest 2.59 is the floor for this. Before it, the queue limit did not
  take effect while archive-push was erroring (pgbackrest#2629), which is
  precisely when it is needed. `deploy/db/Dockerfile` pins it.

### Turning it on for an instance that is already running

Set the `OFFSITE_CLOUD_*` block in `.env`, then restart both containers that
speak pgBackRest:

```bash
docker compose up -d db pgbackrest
```

The sidecar runs `stanza-create`, which creates the stanza on the new
repository; until that has happened, WAL for it is held. Then check the
status card, or:

```bash
docker compose exec pgbackrest gosu postgres pgbackrest --stanza=main check
```

### Restoring from the cloud copy

The same restore as Layer 1, with the repository chosen explicitly. The
passphrase must be in the environment, since it is what decrypts the copy.

```bash
docker compose exec pgbackrest gosu postgres pgbackrest --stanza=main --repo=2 info
```

Testing it without a cloud account: see `deploy/pgbackrest/README.md`, which
runs MinIO locally behind the `offsite-test` profile.

### Still local only

The logical dumps and the uploads are **not** copied offsite yet, and the
dumps are still plaintext: that is step 2, where restic replaces the tarball
and encrypts client-side. Until then, copy the `backups` volume off the box
yourself (the `docker run` line under Layer 2) and store it accordingly.
Network drives and removable disks are steps 3 and 4.
