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

## Offsite backups: the uploads and the dumps (dev-plan 9.2, step 2)

The `backup` sidecar copies the **uploads volume and the logical dumps** to
the same slot with restic, which encrypts client-side with that slot's own
passphrase. So the dumps, which sit in plaintext on the local volume, do not
leave this machine in plaintext.

It reads the files directly rather than shipping the nightly tarball:
deduplication then means an unchanged attachment costs nothing on the next
run. Retention is the admin page's policy translated into restic's
(`--keep-last N --keep-within Dd`), so the local and offsite copies expire
together. Every run ends with `check --read-data-subset=5%`.

### Restoring files from the offsite copy

Everything restic needs is in `.env`. From the backup sidecar:

```bash
docker compose exec backup bash -lc 'export RESTIC_REPOSITORY="s3:https://$OFFSITE_CLOUD_ENDPOINT/$OFFSITE_CLOUD_BUCKET$OFFSITE_CLOUD_PATH/files" RESTIC_PASSWORD="$OFFSITE_CLOUD_PASSPHRASE" AWS_ACCESS_KEY_ID="$OFFSITE_CLOUD_KEY" AWS_SECRET_ACCESS_KEY="$OFFSITE_CLOUD_SECRET"; restic snapshots'
```

Then `restic restore latest --target /tmp/restored` and take the dump or the
attachments from there. A restic repository is self-contained: the binary
and the passphrase are enough to read it on any machine, with no Tesria.

## Offsite backups: a network drive (dev-plan 9.2, step 3)

The uploads and the dumps can also go to a NAS on the LAN, as a restic
repository on a mounted path, encrypted with that slot's own passphrase.

**Tesria does not mount anything.** Mount the share on the host, where your
system already handles credentials, reconnects and reboots, and give Tesria
the host path in `OFFSITE_NAS_PATH`.

- macOS: mount it in Finder; the path is then under `/Volumes`. Docker
  Desktop must also be allowed to share that directory. The first container
  that touches it prompts, and **until you allow it the mount hangs rather
  than failing**, which looks like the backup being stuck.
- Linux: an `/etc/fstab` line with `_netdev,nofail` so a missing share
  delays neither boot nor the backup sidecar.

Then claim it once:

```bash
docker compose exec backup /scripts/claim-target.sh nas
```

### The sentinel, and why it exists

That command writes `.tesria-backup-target` into the share, and the sidecar
refuses to write to a path that does not carry it.

The reason is the failure it prevents. When a network share is not mounted,
the mount point is still there: an ordinary empty directory on the boot
disk. Backing up into that would not fail; it would quietly fill the boot
disk while appearing to work, and the NAS would hold nothing. A file on the
far side cannot be faked by an absent mount, and a mount-point check cannot
do this from inside a container, where a bind mount is always a mount point.

This is also why the share is a **bind mount** rather than a Docker
`cifs`/`nfs` volume. A named network volume stops the container starting
while the share is down, which would take the *local* backups down with it.

Deleting the sentinel stops backups to that target and deletes nothing.

### Point-in-time recovery on a NAS: the SFTP recipe

The database does **not** go to a mounted path, because `archive_command`
runs inside the `db` container where nothing can check the share is really
there, and pgBackRest would happily build a fresh repository in the empty
directory an unmounted share leaves behind.

If you want PITR on the NAS rather than only the files, use pgBackRest's
SFTP repository type, which is a network connection rather than a mount, so
the NAS being down is an error instead of a silent local write. Enable SSH
on the NAS, then add a third repository to
`deploy/pgbackrest/pgbackrest.conf`:

```ini
repo3-type=sftp
repo3-path=/volume1/tesria/pgbackrest
repo3-sftp-host=nas.lan
repo3-sftp-host-user=tesria
repo3-sftp-private-key-file=/etc/pgbackrest/nas_ed25519
repo3-cipher-type=aes-256-cbc
```

Mount the key into both the `db` and `pgbackrest` containers, supply
`repo3-cipher-pass` the way `repo2` gets its passphrase, and read the
archive-push warnings above first: a third repository is a third one that
must accept every WAL segment before Postgres is told the segment is
archived.

### Immutability on a NAS

Use the NAS's own snapshot schedule, which every mainstream model has, and
point it at the directory holding the restic repository. A snapshot the
Tesria host cannot delete is what protects the backups from a compromise of
the host. The stronger alternative, if the NAS can run it, is restic's
`rest-server --append-only`, which accepts new data and refuses deletions;
retention then has to be run separately with a key that can delete.

### Restoring from the network drive

```bash
docker compose exec backup bash -lc 'export RESTIC_REPOSITORY=/mnt/nas/restic RESTIC_PASSWORD="$OFFSITE_NAS_PASSPHRASE"; restic snapshots'
```

Then `restic restore latest --target /tmp/restored`.

## Offsite backups: a removable drive (dev-plan 9.2, step 4)

The classic offline copy: a disk you plug in, copy to, and take away again.
It uses the same mechanism as a network drive, and differs in three ways,
each because the drive is absent most of the time.

- **It is never scheduled.** Nothing is copied until somebody asks, with
  Copy now on the backups page. Its absence raises no alert: a drive in a
  drawer has not failed.
- **Retention is a count with no time window.** A drive plugged in twice a
  year would otherwise be pruned to nothing for having been in a drawer.
- **It finishes with `check`, then `sync`, then says so**, because the next
  thing that happens to this target is somebody pulling it out.

Set `OFFSITE_REMOVABLE_PATH` and `OFFSITE_REMOVABLE_PASSPHRASE`, then claim
the drive once, exactly as for a network drive:

```bash
docker compose exec backup /scripts/claim-target.sh removable
```

### "Safe to remove" means the data, not the eject

When the copy reports safe to remove, `sync` has flushed every byte to the
drive: pulling it out at that point cannot lose any of the backup.

It is **not** a promise that the operating system will eject it cleanly. The
backup sidecar holds a bind mount on the drive, which keeps it busy, so
Finder or `umount` will refuse until that is released:

```bash
docker compose stop backup
```

Then eject, then `docker compose up -d backup`. If you would rather just
unplug it, the data is already safe.

### One warning that does not work everywhere

A drive formatted FAT32 cannot hold a file larger than 4GB. restic's own
files stay well under that, so it is a warning rather than a refusal, and
you would only meet the limit restoring a large dump onto the drive by hand.

The check is best effort and **is silent on macOS**: Docker Desktop passes
the drive through its own file sharing, so the container sees a generic
filesystem type whatever the drive really is. On Linux it reports properly.
exFAT avoids the limit entirely and is a better choice for a backup drive.

### An offline copy is not a schedule

If a removable drive is the **only** offsite target configured, the backups
page says in words that the instance has no offsite backup, and raises
`backup.offsite_manual_only`. Between the times somebody remembers to plug
the drive in, that is simply true, and a reassuring green card would not be.

### Restoring from the drive

```bash
docker compose exec backup bash -lc 'export RESTIC_REPOSITORY=/mnt/removable/restic RESTIC_PASSWORD="$OFFSITE_REMOVABLE_PASSPHRASE"; restic snapshots'
```

### Still to come

The Storage targets screen is step 5, and the restore drills and the
"the machine is gone" chapter are step 6.
