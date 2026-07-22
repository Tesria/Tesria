# Changelog

All notable changes to ConfluenceClone are recorded here.
Format loosely follows [Keep a Changelog](https://keepachangelog.com/).

## [Unreleased]

### Phase 2 — Core content (in progress, 2026-07-22)

Added:
- EF Core 10 + Npgsql data layer. Domain entities (`User`, `Space`, `Page`,
  `PageVersion`, `Attachment`, `Comment`) per PLAN §4, with page content stored
  as ProseMirror JSON in `jsonb` columns and every save creating a new version.
  Initial migration `InitialCreate`; migrations run automatically on startup.
- Local authentication: register / login / logout / me endpoints, cookie-based
  sessions, and Argon2id password hashing. Unauthenticated API calls get 401
  instead of a login redirect.
- Database health check wired into `GET /api/health`.
- Test project (`tests/Api.Tests`) running the API in-process against SQLite
  in-memory (no Docker needed); covers the password hasher and the full auth
  flow.
- Spaces: create / list / get / update, archive / unarchive, with unique,
  validated space keys.
- Pages: create / read / update with a hierarchical page tree, plus the full
  version model — every save appends a `PageVersion`, with version-history
  listing, single-version fetch, and restore (rollback appends a new version so
  nothing is lost). Move/reparent with cycle detection; delete guarded against
  orphaning child pages (soft-delete/trash arrives in Phase 3).
- Integration tests for spaces and pages (create/version/restore/tree/move/
  delete flows).
- Attachments: multipart upload, per-page listing, metadata, download, and
  delete. Bytes stored on the uploads volume behind a pluggable storage
  interface (local disk now, S3-compatible later); 25 MB per-file limit.
- Comments: footer and inline (anchored) comments with threaded replies. Edit
  and soft-delete restricted to the author; soft-delete keeps the row so reply
  threads survive.
- Integration tests for attachments (upload/download/delete) and comments
  (threads, validation, soft-delete, author-only edits).
- React 19 + TypeScript SPA (React Router 7): sign in / register, a spaces list
  with create, and a space view with a hierarchical page-tree sidebar.
- TipTap v3 block editor with a formatting toolbar for creating and editing
  pages; content round-trips as ProseMirror JSON. Read-only rendering reuses the
  same editor.
- Page view with tabbed footer: threaded comments (post/reply/edit/delete own),
  attachments (upload/download/delete), and version history (preview any version
  and restore it). Fixed the dev proxy port to match the API (5291).

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
