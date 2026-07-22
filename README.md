# ConfluenceClone

A self-hosted, Docker-deployable knowledge base / wiki modeled on Atlassian
Confluence. Priorities: **data safety** (strong backup & recovery), a faithful
block-based editor, and one-command deployment.

See [`PLAN.md`](./PLAN.md) for the full design and roadmap.

**Status:** Phase 2 (Core content) — a working wiki: local accounts, spaces,
pages in a hierarchical tree, a TipTap block editor with full version history
and rollback, page attachments, and threaded footer/inline comments. Runs on the
full Docker stack (app + PostgreSQL 18 + Caddy auto-HTTPS + scheduled backups).
Full-text search and pgBackRest point-in-time recovery arrive in Phase 3.

## Tech stack

- **Backend:** ASP.NET Core (C#), .NET 10 (LTS); EF Core 10
- **Database:** PostgreSQL 18 (Npgsql); page content stored as ProseMirror JSON
- **Frontend:** React 19 + TypeScript + Vite; TipTap v3 block editor
- **Auth:** local accounts, cookie sessions, Argon2id password hashing
- **Deploy:** Docker Compose — Caddy reverse proxy with automatic HTTPS
- **Backups:** scheduled `pg_dump` with retention now; pgBackRest point-in-time
  recovery in Phase 3

## Quick start (Docker)

```bash
cp .env.example .env        # then edit POSTGRES_PASSWORD, DOMAIN, ACME_EMAIL
docker compose up -d --build
```

- Real domain: set `DOMAIN=wiki.example.com` and Caddy fetches a Let's Encrypt
  cert automatically. Visit `https://wiki.example.com`.
- Local test: keep `DOMAIN=localhost` and visit `https://localhost` (Caddy uses
  a self-signed cert, so the browser will warn once).

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

The `backup` container takes a compressed logical backup on startup and every
`BACKUP_INTERVAL_HOURS`, keeping `BACKUP_RETENTION_DAYS` of history on the
`backups` volume.

```bash
docker compose exec backup /scripts/backup.sh          # backup now
docker compose exec backup /scripts/verify-backup.sh   # restore-test newest
docker compose exec backup /scripts/restore.sh         # restore newest (destructive)
```

Full details and disaster-recovery runbook: [`docs/backup-recovery.md`](./docs/backup-recovery.md).

## Repository layout

```
src/Api/        ASP.NET Core API (Domain / Infrastructure / Features slices)
src/web/        React + Vite + TypeScript SPA (TipTap editor)
tests/          API integration tests (in-process, SQLite in-memory)
deploy/         Dockerfile, Caddyfile, backup scripts, pgBackRest (Phase 3)
docs/           architecture, backup-recovery runbook, CHANGELOG
docker-compose.yml   full stack
```
