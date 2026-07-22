# ConfluenceClone

A self-hosted, Docker-deployable knowledge base / wiki modeled on Atlassian
Confluence. Priorities: **data safety** (strong backup & recovery), a faithful
block-based editor, and one-command deployment.

See [`PLAN.md`](./PLAN.md) for the full design and roadmap.

**Status:** Phase 1 (Foundation) — a running skeleton: API health endpoint,
React SPA, and the full Docker stack (app + PostgreSQL 18 + Caddy auto-HTTPS +
scheduled backups). Content features (spaces, pages, editor) arrive in Phase 2.

## Tech stack

- **Backend:** ASP.NET Core (C#), .NET 10 (LTS)
- **Database:** PostgreSQL 18 (EF Core — added in Phase 2)
- **Frontend:** React 19 + TypeScript + Vite; TipTap editor (Phase 2)
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

Two terminals:

```bash
# API  (http://localhost:5099)
dotnet run --project src/Api

# Web  (http://localhost:5173, proxies /api to the API)
cd src/web && npm install && npm run dev
```

Open http://localhost:5173 — the page shows the live API health status.

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
src/Api/        ASP.NET Core API (serves the built SPA in production)
src/web/        React + Vite + TypeScript SPA
deploy/         Dockerfile, Caddyfile, backup scripts, pgBackRest (Phase 3)
docs/           architecture, backup-recovery runbook, CHANGELOG
docker-compose.yml   full stack
```
