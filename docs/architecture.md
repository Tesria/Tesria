# Architecture

This document records how ConfluenceClone is put together and why. It is kept
current as features land (per the project's documentation rule).

## Overview

A single deployable web application plus supporting containers:

```
Browser ──HTTPS──► Caddy (auto-TLS) ──► app (ASP.NET Core .NET 10)
                                          │  serves REST API under /api
                                          │  serves built React SPA (wwwroot)
                                          ├──► PostgreSQL 18  (pgdata volume)
                                          └──► uploads volume (attachments)
                        backup sidecar ──► pg_dump ──► backups volume (+ S3, Phase 3)
```

## Backend (`src/Api`)

- ASP.NET Core minimal APIs, **.NET 10 (LTS)**.
- Vertical-slice layout: each feature owns its endpoints under
  `Features/<Feature>/`. `Program.cs` stays thin and just wires features in.
- `Domain/` holds entities/value objects; `Infrastructure/` holds EF Core,
  storage, and auth wiring (populated in Phase 2).
- The API also serves the compiled SPA from `wwwroot` and falls back to
  `index.html` for client-side routes, so the whole product is one origin in
  production (no CORS needed). CORS is enabled only in Development for the Vite
  dev server.

### Health

`GET /api/health` returns `{ status, service, version, utc }`. Used by the
container `HEALTHCHECK` and the SPA's status card. Phase 2 adds a database
readiness probe.

## Frontend (`src/web`)

- React 19 + TypeScript, built with Vite.
- In development, Vite serves the SPA on `:5173` and proxies `/api` to the API
  on `:5099`, so the frontend always uses same-origin relative URLs — identical
  to production.
- In production, `npm run build` output is copied into the API's `wwwroot`
  during the Docker build.

## Data & persistence

- **PostgreSQL 18** is the system of record (schema/EF Core migrations land in
  Phase 2). Content (page bodies) will be stored as ProseMirror JSON in `jsonb`.
- Docker named volumes hold all state: `pgdata` (database), `uploads`
  (attachments), `backups` (local backup copies), plus Caddy's cert store.
  Nothing durable lives in a container layer.

## Deployment (`deploy/`, `docker-compose.yml`)

- Multi-stage `Dockerfile`: build SPA → publish API (embedding SPA) → slim
  `aspnet:10` runtime running as a non-root user with a health check.
- `docker-compose.yml` runs `db`, `app`, `caddy`, and `backup`. Only Caddy
  publishes ports (80/443); the app and database are reachable only inside the
  compose network.
- **Caddy** terminates TLS and reverse-proxies to the app, obtaining and
  renewing certificates automatically.

## Backups

See [`backup-recovery.md`](./backup-recovery.md). Layered by design: logical
`pg_dump` now (Phase 1); pgBackRest continuous archiving + point-in-time
recovery and offsite S3 replication in Phase 3; plus in-app safety nets
(version history, trash) as features arrive.

## Decisions

- **.NET + React** over a single-language stack: strongest backend reliability
  and data tooling, which suits the data-safety priority. The one gap —
  real-time co-editing, whose ecosystem is JS-native — is deferred to Phase 5
  and will be isolated in a small Node/Hocuspocus sidecar rather than reshaping
  the main stack.
- **Same-origin SPA hosting** (API serves `wwwroot`) keeps deployment to a
  single app container and avoids CORS in production.
