# Tesria — Project Plan

**Status:** Draft for approval
**Date:** 2026-07-22
**Owner:** Brian

A self-hosted, Docker-deployable knowledge base / wiki modeled on Atlassian Confluence. Priorities, in order: (1) data safety — strong backup and recovery, since this is self-hosted and data loss is unacceptable; (2) a faithful block-based editing experience; (3) simple single-command deployment.

---

## 1. Decisions locked

| Area | Decision | Rationale |
|------|----------|-----------|
| Backend | ASP.NET Core (C#), **.NET 10 (LTS)** | Strongly typed, reliable, first-class Docker + data tooling; best fit for the reliability/backup emphasis. |
| Data layer | EF Core 10 + **PostgreSQL 18** (Npgsql) | Mature, transactional, excellent backup/PITR story. |
| Frontend | **React 19 + TypeScript + Vite** | Required regardless of backend — the editor engine is JS-only. |
| Editor | **TipTap v3** (ProseMirror) block WYSIWYG, storing structured JSON | This is the defining Confluence feature. |
| Real-time co-editing | Deferred to a later phase; when added, a small **Node + Hocuspocus/Yjs** sidecar bridges to TipTap | The only weakness of a .NET backend; isolating it keeps the main stack clean. |
| Auth | Local accounts (email + hashed password) now, **architected for OIDC/SSO** later | Simplest for small teams; pluggable for Keycloak/Authentik/Google later. |
| v1 goal | **Lean MVP** that runs in Docker with backups working from day one | Ship something real, iterate. |

---

## 2. Confluence core features (research) and how we prioritize them

Confluence's model, distilled from Atlassian's documentation, is: **Spaces** (top-level containers, e.g. per team/project) → **Pages** organized in a **hierarchical tree** → a **block WYSIWYG editor** → **page version history** → **comments** (inline + footer) → **attachments** → **labels** → **search** → a two-level **permission model** (space permissions + per-page restrictions) → **user/group management**.

We map that to phases:

**MVP (Phase 1–3):** Users/auth, Spaces, Pages + page tree, TipTap editor, page version history, attachments, full-text search, **comments (footer + inline)**, Docker deploy, and the full backup/recovery system. *(Comments were promoted into the MVP per your decision.)*

**Fast-follow (Phase 4):** Labels/tags, page restrictions, space permissions, page export (PDF/HTML/Markdown), audit log.

**Later (Phase 5+):** Real-time collaborative editing (Hocuspocus sidecar), OIDC/SSO, templates and blueprints, macros/embeds, page trees drag-reorder, notifications/watches, REST API + webhooks, full-text search upgrade (Postgres FTS → optionally Meilisearch/OpenSearch).

Sources reviewed: Atlassian Confluence Data Center docs (spaces, permissions & restrictions), plus comparison of self-hosted peers (BookStack, Outline, Wiki.js, XWiki) to calibrate MVP scope.

---

## 3. Architecture

```
                        ┌─────────────────────────────┐
   Browser ──HTTPS──►   │   Reverse proxy (Caddy)      │  automatic TLS
                        │   - terminates TLS           │
                        └───────────────┬─────────────┘
                                        │
                        ┌───────────────▼─────────────┐
                        │   app  (ASP.NET Core .NET 10)│
                        │   - REST API                 │
                        │   - serves built React SPA   │
                        │   - auth, RBAC               │
                        │   - attachment storage        │
                        └───────┬──────────────┬───────┘
                                │              │
                   ┌────────────▼───┐   ┌──────▼─────────┐
                   │ postgres:18    │   │ uploads volume │
                   │ (pgdata volume)│   │ (attachments)  │
                   └──────┬─────────┘   └────────────────┘
                          │ WAL + dumps
                   ┌──────▼──────────────────────────────┐
                   │ backup sidecar (pgBackRest + cron)   │
                   │ → local backups volume + offsite S3  │
                   └──────────────────────────────────────┘

   (Phase 5) collab sidecar: Node + Hocuspocus/Yjs  ← websocket ← Browser
```

**Repository layout (monorepo):**

```
Tesria/
├─ src/
│  ├─ Api/                 # ASP.NET Core project (.NET 10)
│  │  ├─ Domain/           # entities, value objects
│  │  ├─ Infrastructure/   # EF Core, Npgsql, storage, auth
│  │  ├─ Features/         # vertical slices (Spaces, Pages, Search…)
│  │  └─ Program.cs
│  └─ web/                 # React 19 + Vite + TypeScript SPA
│     ├─ src/editor/       # TipTap setup
│     └─ src/features/
├─ deploy/
│  ├─ Dockerfile           # multi-stage: build web → build api → runtime
│  ├─ docker-compose.yml   # app, db, proxy, backup
│  ├─ docker-compose.prod.yml
│  ├─ pgbackrest/          # config
│  └─ scripts/             # backup.sh, restore.sh, verify-backup.sh
├─ docs/                   # developer docs (kept up to date per project rules)
│  ├─ architecture.md
│  ├─ backup-recovery.md   # the runbook
│  └─ CHANGELOG.md
├─ tests/
├─ PLAN.md                 # this file
└─ README.md
```

---

## 4. Data model (initial)

Core entities (EF Core, Postgres):

- **User** — id, email, display name, password hash (Argon2id), status, created_at. (OIDC subject id nullable, for later.)
- **Group** and **UserGroup** — for group-based permissions.
- **Space** — id, key, name, description, homepage_id, created_by, archived flag.
- **Page** — id, space_id, parent_page_id (self-referencing → tree), title, current_version_id, position (ordering), status (draft/current/archived), created_by, timestamps.
- **PageVersion** — id, page_id, version_number, content (ProseMirror JSON, `jsonb`), content_html (rendered cache), author_id, change_comment, created_at. **Every save creates a new version** → history + rollback.
- **Attachment** — id, page_id, filename, content_type, size, storage_key, uploaded_by, version. Stored on the uploads volume (S3-compatible optional later).
- **Comment** (Phase 4) — id, page_id, parent_comment_id, anchor (for inline), body, author, timestamps.
- **Label** + **PageLabel** (Phase 4).
- **SpacePermission** / **PageRestriction** (Phase 4) — principal (user/group) × operation (view/edit/admin).
- **AuditLog** — actor, action, target, timestamp, metadata (jsonb).

Design notes: content stored as `jsonb` (queryable, diff-able); a rendered HTML cache column avoids re-rendering on every read; full-text search via a Postgres `tsvector` generated column + GIN index for the MVP.

---

## 5. Backup & recovery strategy (primary requirement)

Defense in depth — three independent layers so a single failure never loses data.

**Layer 1 — Continuous physical backup + Point-in-Time Recovery (PITR).**
`pgBackRest` runs in a sidecar container with WAL archiving enabled on Postgres. This gives full + incremental backups and the ability to restore to *any second* in time (e.g. "just before the accidental delete at 14:32"). This is the strongest protection and the industry standard for self-hosted Postgres.

**Layer 2 — Nightly logical dumps.**
Scheduled `pg_dump` (custom format, compressed) as a portable, version-independent snapshot that can be restored onto any Postgres instance — useful for migrations and as a belt-and-suspenders alongside pgBackRest.

**Layer 3 — Attachment/file backups.**
The uploads volume is backed up on the same schedule (restic or rsync to the backups volume), so files and database stay consistent.

**Cross-cutting requirements:**
- **Offsite copy:** all backups optionally replicated to S3-compatible storage (Backblaze B2, MinIO, AWS S3) — configurable via `.env`. Local-only is supported too.
- **Encryption at rest:** backups encrypted (pgBackRest native encryption / restic).
- **Retention policy:** configurable (e.g. keep 7 daily, 4 weekly, 6 monthly).
- **Automated verification:** a scheduled job restores the latest backup into a throwaway container and runs a sanity check, so we know backups actually work — untested backups are not backups.
- **One-command operations:** `deploy/scripts/backup.sh` (on-demand full backup), `restore.sh` (guided restore, incl. PITR to a timestamp), `verify-backup.sh`.
- **Documented runbook:** `docs/backup-recovery.md` with exact restore steps for three scenarios: (a) full disaster recovery on a new host, (b) point-in-time rollback after bad edit/delete, (c) single-page recovery from version history (in-app, no ops needed).
- **In-app safety nets:** page version history with rollback, and soft-delete / trash with a retention window before hard delete — so most "oops" recoveries never require touching backups.

---

## 6. Docker & deployment

- **Single multi-stage `Dockerfile`:** stage 1 builds the React SPA (Node), stage 2 publishes the .NET app, final runtime stage is a slim `aspnet:10` image serving both API and static SPA. Runs as non-root, with a healthcheck.
- **`docker-compose.yml`** brings up the whole stack with one command: `app`, `postgres`, `caddy` (auto-TLS reverse proxy), `backup` sidecar. Optional profiles for `collab` and `minio`.
- **Named volumes:** `pgdata` (database), `uploads` (attachments), `backups` (local backup copies). All persistence lives in volumes, nothing in the container layer.
- **Config via `.env`:** DB credentials, base URL, backup schedule/retention, S3 offsite settings, SMTP (later). A `.env.example` is committed.
- **Migrations** run automatically on startup (EF Core), guarded so they're safe to re-run.
- Target: `git clone` → set `.env` → `docker compose up -d` → working instance with backups scheduled.

---

## 7. Build roadmap (phases)

Each phase ends in a working, committed, documented state (per project rules: document changes, commit after every feature).

1. **Foundation** — repo scaffold, .NET solution, React app, Postgres, Docker compose that boots (incl. Caddy auto-HTTPS); healthcheck; test project in place.
2. **Core content** — Users + local auth; Spaces CRUD; Pages CRUD with tree; TipTap editor wired to save/load ProseMirror JSON; page version history + rollback; attachments; **comments (footer + inline)**.
3. **Search + backup system** — Postgres full-text search; pgBackRest PITR; nightly dumps; file backups; S3-compatible offsite support (present but disabled until credentials set); scripts; verification job; `backup-recovery.md` runbook; soft-delete/trash.
4. **Fast-follow** — labels, space permissions + page restrictions, export (PDF/HTML/MD), audit log.
5. **Advanced** — real-time co-editing (Hocuspocus sidecar), OIDC/SSO, templates, notifications/watches, public REST API.

MVP = phases 1–3 (comments included).

---

## 8. Engineering practices

- Developer docs in `docs/` kept current as features land; `CHANGELOG.md` updated per change.
- A git commit after every completed feature, with a clear message.
- Automated tests for domain logic and critical API paths; the backup/restore path gets an explicit tested procedure.
- Verification step at the end of each phase (build + tests + a manual smoke check of the running container).

---

## 9. Confirmed configuration

- **Offsite backups:** S3-compatible support is built in but **disabled by default**; local-volume backups work out of the box. Enable offsite by setting credentials in `.env`.
- **TLS:** compose ships **Caddy with automatic HTTPS**.
- **v1 scope addition:** **comments** are included in the MVP (see phases above).
- Real-time co-editing and OIDC/SSO remain Phase 5 (later).
