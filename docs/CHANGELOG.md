# Changelog

All notable changes to ConfluenceClone are recorded here.
Format loosely follows [Keep a Changelog](https://keepachangelog.com/).

## [Unreleased]

### Phase 1 — Foundation (2026-07-22)

Added:
- Project plan (`PLAN.md`) covering stack, feature scope, data model,
  backup/recovery strategy, and phased roadmap.
- ASP.NET Core (.NET 10) API scaffold with a vertical-slice layout and a
  `GET /api/health` liveness endpoint. Serves the built React SPA same-origin
  in production with SPA-route fallback.
- React 19 + TypeScript + Vite frontend that displays live API health; Vite dev
  server proxies `/api` to the backend.
- Multi-stage `Dockerfile` (build SPA → publish API → slim non-root runtime
  with a container health check).
- `docker-compose.yml` full stack: PostgreSQL 18, the app, Caddy reverse proxy
  with automatic HTTPS, and a scheduled backup sidecar. All state on named
  volumes.
- Logical backup system: scheduled `pg_dump` with retention, plus on-demand
  `backup.sh`, `restore.sh`, and `verify-backup.sh`.
- Developer docs: `docs/architecture.md`, `docs/backup-recovery.md`, and this
  changelog. `.env.example`, `.gitignore`, and `README.md`.

Notes:
- EF Core / PostgreSQL wiring, and pgBackRest point-in-time recovery, are
  scheduled for Phase 2 and Phase 3 respectively.
