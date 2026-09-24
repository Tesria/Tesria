# Tesria

A self-hosted, Docker-deployable knowledge base / wiki modeled on Atlassian
Confluence. Priorities: **data safety** (strong backup & recovery), a faithful
block-based editor, and one-command deployment.

**Status:** 0.6.0, the first public release. Website: [tesria.com](https://tesria.com).
What Tesria does today:

- **Writing:** spaces of pages in a tree, a block editor (tables, panels,
  code, diagrams, math, charts, layouts, live blocks that list pages and
  tasks), templates, labels, and real-time co-editing with drafts, history
  and rollback.
- **Working together:** threaded and inline comments, mentions, watches and
  notifications, full-text search.
- **Sharing:** export a page as Markdown, HTML or PDF, a whole space as a
  static website, or a space as a portable wiki pack that another Tesria can
  import. Spaces can be published for anonymous reading.
- **Running it:** a setup wizard, roles and rights, groups, space
  permissions and page restrictions, invitations, two-factor sign-in,
  single sign-on (OIDC, beta), branding, email through SMTP, Gmail or
  Microsoft 365, and optional Tailscale for private access from anywhere.
- **Keeping it safe:** backups with point-in-time recovery, offsite copies,
  and restore and undo from the admin page; a tamper-evident audit log,
  security alerts, rate limits, a least-privilege database role, and a
  dependency list with a vulnerability check.
- **Connecting to it:** a REST API with tokens and webhooks, and MCP for AI
  assistants.

What changed in each version is in [`docs/CHANGELOG.md`](./docs/CHANGELOG.md);
what comes next is in [`docs/roadmap.md`](./docs/roadmap.md) and
[`docs/dev-plan.md`](./docs/dev-plan.md). [`PLAN.md`](./PLAN.md) is the
original design the project started from.

## Documentation

- **Using and running Tesria:** the docs at
  [tesria.com/docs](https://tesria.com/docs). Each release also carries
  them as downloads: `docs-pack.zip` to import into your own Tesria (Spaces,
  then **Import a pack**), and `docs-site.zip` to read offline.
- **How it is built:** [`docs/architecture.md`](./docs/architecture.md),
  and the threat model and known gaps in [`docs/security.md`](./docs/security.md).
- **Backups and disaster recovery:** [`docs/backup-recovery.md`](./docs/backup-recovery.md).

## Tech stack

- **Backend:** ASP.NET Core (C#), .NET 10 (LTS); EF Core 10
- **Database:** PostgreSQL 18 (Npgsql); page content stored as ProseMirror JSON
- **Frontend:** React 19 + TypeScript + Vite; TipTap v3 block editor
- **Auth:** local accounts (Argon2id), API tokens, and optional OIDC/SSO (beta),
  cookie sessions, all converging on the same permission model
- **Collaboration:** Node + Hocuspocus/Yjs sidecar for simultaneous editing
- **PDF export:** Node + Playwright sidecar rendering the print-ready HTML export
- **Deploy:** Docker Compose, Caddy reverse proxy with automatic HTTPS
- **Backups:** pgBackRest continuous WAL archiving + point-in-time recovery,
  scheduled `pg_dump` + `uploads` archives, and in-app version history / trash

## Quick start (Docker)

```bash
cp .env.example .env        # then edit the values below
docker compose up -d --build
```

Set these in `.env` before the first start: `.env.example` ships
placeholders, not blanks, so nothing fails loudly if you skip one:

| Variable | |
|---|---|
| `POSTGRES_PASSWORD` | Any long random string. |
| `APP_DB_PASSWORD` | Any long random string, different from the above (`openssl rand -hex 24`). **Required.** A one-shot `migrate` service creates a least-privilege `tesria_app` role with it before the app starts, and the app runs only as that role; it cannot alter or delete audit rows, and the app never sees the owner password. |
| `BACKUP_ENCRYPTION_KEY` | **Required.** Encrypts the pgBackRest repository (`openssl rand -hex 32`). Backups made with it are unrecoverable without it, so keep it somewhere safe, and *don't* reuse a key from another install unless you intend to restore that install's backups. |
| `DOMAIN`, `ACME_EMAIL` | `localhost` is fine for a laptop. |
| `COLLAB_SHARED_SECRET` | Optional (`openssl rand -hex 32`). Empty disables real-time co-editing; the editor falls back to single-user. |
| `PDF_SHARED_SECRET` | Optional (`openssl rand -hex 32`). Empty disables PDF export; `?format=pdf` then answers 503 telling the user to print the HTML export. |

Everything else, schema included, sets itself up: a one-shot `migrate`
service updates the database before the app starts, and the pgBackRest
sidecar creates its stanza on first boot. Then open `https://<domain>/` and the setup wizard takes it from
there: it creates the owner account, names the instance, and walks you
through who can join, what each role may do, and how much backup history to
keep. A fresh database has no users, so the first account to be created owns
the instance.

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
  by IP or hostname, no extra config needed. To make that access, and the
  `localhost` warning above, go away for good on a given device, run
  `deploy/scripts/trust-ca.sh` (macOS/Linux) or `trust-ca.ps1` (Windows) once,
  or open `http://<server>/trust` on that device for a guided version;
  see [`docs/tls-and-lan-access.md`](./docs/tls-and-lan-access.md) for details
  and the real-domain-without-public-exposure option.

Check health directly: `curl -k https://localhost/api/health` (it gives the
version only to a signed-in caller).

## Development

Tesria is developed the way it runs, with Docker: change the code, rebuild
the part you changed, and look at it in the browser.

```bash
docker compose up -d --build app   # after changing the API or the web app
docker compose up -d --build collab   # or pdf, after changing those services
```

See [`CONTRIBUTING.md`](./CONTRIBUTING.md) for the conventions and how a
change is proposed, and the **Developers** section of the docs for the
architecture.

## Tests

```bash
dotnet test tests/Api.Tests                       # the server
cd src/web && npm ci && npm run build && npm run lint && npm test   # the web app
```

The API test suite boots the app in-process against SQLite in-memory, so it runs
without Docker or a live PostgreSQL. GitHub Actions runs both on every push and
pull request.

## Backups

Two sidecars back the instance up every `BACKUP_INTERVAL_HOURS`: `backup`
(a `pg_dump` plus an archive of uploads, on the `backups` volume) and
`pgbackrest` (physical backups and continuous WAL archiving for point-in-time
recovery). **Administration → Backups** shows both, runs a backup or a restore
test on demand, and sets the retention policy (keep the newest *N* and the last
*D* days, or keep everything). `BACKUP_RETENTION_DAYS` only seeds that policy
on the first start after upgrading. The same page restores a backup (with an
undo), and can send copies offsite to cloud storage, a network drive or a
removable drive.

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

You can add your own roles alongside the three, in either the user or the
administrator tier: **New role** copies an existing one and you edit the
copy in the matrix. A role is a set of rights, not a rank, so moving
someone between roles of the same tier never promotes them.

Administrators and the owner can also **delete a space**, from its settings
page. That destroys every page in it and cannot be undone from inside Tesria,
so it asks for the space key typed back and your password in the same step.
Archiving is the reversible alternative, and a space's own administrator can
do that without holding the right.

## Repository layout

```
src/Api/        ASP.NET Core API (Domain / Infrastructure / Features slices)
src/web/        React + Vite + TypeScript SPA (TipTap editor)
collab/         Real-time collaboration sidecar (Node + Hocuspocus/Yjs)
pdf/            PDF rendering sidecar (Node + Playwright/Chromium)
tests/          API integration tests (in-process, SQLite in-memory)
deploy/         Dockerfile, Caddyfile, backup scripts, pgBackRest (Phase 3)
docs/           architecture, security, backup-recovery runbook, CHANGELOG, plans
scripts/        the docs' publisher (scripts/docs), demo data, screenshots,
                the dependency manifest, the audit gate
docker-compose.yml   full stack
```

## Project

Tesria is free and open source under the [Apache License 2.0](./LICENSE)
([`NOTICE`](./NOTICE) has the attributions). How to take part:
[`CONTRIBUTING.md`](./CONTRIBUTING.md), [`CODE_OF_CONDUCT.md`](./CODE_OF_CONDUCT.md)
and [`GOVERNANCE.md`](./GOVERNANCE.md). Getting help: [`SUPPORT.md`](./SUPPORT.md).
Reporting a vulnerability: [`SECURITY.md`](./SECURITY.md).
