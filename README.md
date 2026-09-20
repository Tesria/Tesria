# Tesria

A self-hosted, Docker-deployable knowledge base / wiki modeled on Atlassian
Confluence. Priorities: **data safety** (strong backup & recovery), a faithful
block-based editor, and one-command deployment.

See [`PLAN.md`](./PLAN.md) for the full design and roadmap.

**Status:** Phases 1–5 complete (the full original roadmap), plus a
ground-up editor UX overhaul beyond it. A working wiki — local accounts,
spaces, pages in a hierarchical tree, a TipTap block editor with version
history and rollback, attachments, threaded footer/inline comments,
full-text search, and soft-delete/trash — on the full Docker stack (app +
PostgreSQL 18 + Caddy auto-HTTPS). Data safety is covered by pgBackRest
point-in-time recovery plus logical and file backups. Phase 4 added labels,
page export, an audit log, and groups with space permissions / page
restrictions. Phase 5 added real-time collaborative editing, page templates,
notifications/watches, a public REST API (tokens + webhooks), and OIDC/SSO.
The editor overhaul added syntax-highlighted code blocks, tables/task lists
with hover-triggered controls, images (with a draft/publish page lifecycle
so uploads work on unsaved pages), inline/anchored comments, a slash-command
menu, and a per-page full-width layout toggle — see
[`docs/CHANGELOG.md`](./docs/CHANGELOG.md) for the full list.

## Tech stack

- **Backend:** ASP.NET Core (C#), .NET 10 (LTS); EF Core 10
- **Database:** PostgreSQL 18 (Npgsql); page content stored as ProseMirror JSON
- **Frontend:** React 19 + TypeScript + Vite; TipTap v3 block editor
- **Auth:** local accounts (Argon2id), API tokens, and optional OIDC/SSO —
  cookie sessions, all converging on the same permission model
- **Collaboration:** Node + Hocuspocus/Yjs sidecar for simultaneous editing
- **PDF export:** Node + Playwright sidecar rendering the print-ready HTML export
- **Deploy:** Docker Compose — Caddy reverse proxy with automatic HTTPS
- **Backups:** pgBackRest continuous WAL archiving + point-in-time recovery,
  scheduled `pg_dump` + `uploads` archives, and in-app version history / trash

## Quick start (Docker)

```bash
cp .env.example .env        # then edit the values below
docker compose up -d --build
```

Set these in `.env` before the first start — `.env.example` ships
placeholders, not blanks, so nothing fails loudly if you skip one:

| Variable | |
|---|---|
| `POSTGRES_PASSWORD` | Any long random string. |
| `APP_DB_PASSWORD` | Any long random string, different from the above (`openssl rand -hex 24`). The app creates a least-privilege `tesria_app` role with it at startup and runs as that role; it cannot alter or delete audit rows. Empty runs the app as the database owner — acceptable on a LAN, not on the internet. |
| `BACKUP_ENCRYPTION_KEY` | **Required.** Encrypts the pgBackRest repository (`openssl rand -hex 32`). Backups made with it are unrecoverable without it, so keep it somewhere safe — and *don't* reuse a key from another install unless you intend to restore that install's backups. |
| `DOMAIN`, `ACME_EMAIL` | `localhost` is fine for a laptop. |
| `COLLAB_SHARED_SECRET` | Optional (`openssl rand -hex 32`). Empty disables real-time co-editing; the editor falls back to single-user. |
| `PDF_SHARED_SECRET` | Optional (`openssl rand -hex 32`). Empty disables PDF export; `?format=pdf` then answers 503 telling the user to print the HTML export. |

Everything else — schema included — sets itself up: the API runs EF Core
migrations on startup, and the pgBackRest sidecar creates its stanza on
first boot. Register the first account at `https://<domain>/register`; a
fresh database has no users.

> **Bring the whole stack up together** (`docker compose up -d`), not
> `docker compose up -d db` on its own. On a fresh volume the database
> crash-loops every ~10s if started alone, because WAL archiving fails
> until the `pgbackrest` sidecar has created the stanza. If you do need the
> database by itself, start `db pgbackrest` together.

- Real domain: set `DOMAIN=wiki.example.com` and Caddy fetches a Let's Encrypt
  cert automatically. Visit `https://wiki.example.com`.
- Local test: keep `DOMAIN=localhost` and visit `https://localhost` (Caddy uses
  a self-signed cert, so the browser will warn once).
- The app is also reachable from other devices on your LAN (including phones)
  by IP or hostname, no extra config needed. To make that access — and the
  `localhost` warning above — go away for good on a given device, run
  `deploy/scripts/trust-ca.sh` (macOS/Linux) or `trust-ca.ps1` (Windows) once;
  see [`docs/tls-and-lan-access.md`](./docs/tls-and-lan-access.md) for details
  and the real-domain-without-public-exposure option.

Check health directly: `curl -k https://localhost/api/health`.

## Local development (without Docker)

The API needs a PostgreSQL database. The simplest option is to run just the
database from the compose stack and point the API at it:

```bash
docker compose up -d db      # Postgres on localhost:5432 (per your .env)
```

Then, in two terminals:

```bash
# API  (http://localhost:5291) — reads ConnectionStrings:Default; the default
# targets Host=localhost;Database=confluence;Username=confluence
dotnet run --project src/Api

# Web  (http://localhost:5173, proxies /api to the API)
cd src/web && npm install && npm run dev
```

Open http://localhost:5173, create an account, and start a space. Migrations run
automatically on API startup. (Tests, by contrast, need no database — see below.)

## Tests

```bash
dotnet test
```

The API test suite boots the app in-process against SQLite in-memory, so it runs
without Docker or a live PostgreSQL.

## Backups

Two sidecars back the instance up every `BACKUP_INTERVAL_HOURS`: `backup`
(a `pg_dump` plus an archive of uploads, on the `backups` volume) and
`pgbackrest` (physical backups and continuous WAL archiving for point-in-time
recovery). **Administration → Backups** shows both, runs a backup or a restore
test on demand, and sets the retention policy (keep the newest *N* and the last
*D* days, or keep everything). `BACKUP_RETENTION_DAYS` only seeds that policy
on the first start after upgrading.

```bash
docker compose exec backup /scripts/backup.sh          # backup now
docker compose exec backup /scripts/verify-backup.sh   # restore-test newest
docker compose exec backup /scripts/restore.sh         # restore newest (destructive)
```

Full details and disaster-recovery runbook: [`docs/backup-recovery.md`](./docs/backup-recovery.md).

## Roles

Three roles ship: **User**, **Administrator** and **Owner**. The owner is the
one account that owns the instance; only it changes roles or hands the
instance on. **Administration → Roles** is the matrix of what each role may
do, from creating spaces to changing the backup retention policy.
Administrators may shape user roles; only the owner may change what
administrators can do.

## Repository layout

```
src/Api/        ASP.NET Core API (Domain / Infrastructure / Features slices)
src/web/        React + Vite + TypeScript SPA (TipTap editor)
collab/         Real-time collaboration sidecar (Node + Hocuspocus/Yjs)
pdf/            PDF rendering sidecar (Node + Playwright/Chromium)
tests/          API integration tests (in-process, SQLite in-memory)
deploy/         Dockerfile, Caddyfile, backup scripts, pgBackRest (Phase 3)
docs/           architecture, backup-recovery runbook, CHANGELOG
docker-compose.yml   full stack
```
