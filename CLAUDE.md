# ConfluenceClone — project notes for Claude

A self-hosted Confluence-style wiki. ASP.NET Core (.NET 10) API + React 19/
TypeScript SPA (TipTap v3 editor) + PostgreSQL 18, deployed via Docker Compose
(Caddy auto-HTTPS, a Node/Hocuspocus collab sidecar, layered backups).

Read first, in this order:
- [`README.md`](./README.md) — stack, quick start, repo layout.
- [`docs/architecture.md`](./docs/architecture.md) — how it's put together and
  why, including a detailed editor-subsystem section (extensions.ts as the
  schema source of truth, node views, floating menus, the draft/publish page
  lifecycle). Kept current as features land.
- [`docs/CHANGELOG.md`](./docs/CHANGELOG.md) — dated, most-recent-first record
  of what's been built. Check this before assuming something doesn't exist.
- [`docs/backup-recovery.md`](./docs/backup-recovery.md) — the runbook if
  you're touching anything backup/restore-related.
- [`docs/tls-and-lan-access.md`](./docs/tls-and-lan-access.md) — real-domain
  vs. no-domain/LAN HTTPS, and the `deploy/scripts/trust-ca.*` scripts.
- `PLAN.md` — the original founding design doc (phases 1–5). The editor
  overhaul that followed it is tracked in the CHANGELOG instead, not as a
  numbered PLAN.md phase.

## Working conventions established in this repo

- **Docker is the source of truth for manual verification.** After a backend
  or frontend change, rebuild and restart just the `app` service and check it
  live rather than trusting the build alone:
  ```bash
  docker compose build app && docker compose up -d app
  ```
  `dotnet test` (SQLite in-memory, no Docker needed) covers backend logic;
  `npm run build && npm run lint` covers the frontend. Both should stay green,
  but neither substitutes for looking at the running app for UI changes.
- **EF Core migrations**: `dotnet-ef` is installed as a global tool. Add one
  with `dotnet ef migrations add <Name> --output-dir Infrastructure/Migrations`
  from `src/Api/`. Migrations run automatically on API startup.
- **Editor schema changes** (new TipTap node/mark type) go in
  `src/web/src/editor/extensions.ts`, never declared inline in `Editor.tsx` or
  `CollaborativeEditor.tsx` separately — see the architecture doc's editor
  section for why (Yjs schema-sharing requirement).
- **Any popover `<form>` rendered inside the editor** (bubble menus, etc.)
  must call `e.stopPropagation()` in its submit handler — see the
  architecture doc's "Gotcha" note. This bit a real feature once already.
- Git Bash on Windows mangles absolute container paths in
  `docker compose exec`/`cp` args (e.g. `/scripts/verify.sh` becomes a bogus
  `C:/Program Files/Git/scripts/verify.sh`). Prefix the command with
  `MSYS_NO_PATHCONV=1` when running those.
- This repo has **no git remote configured** (as of 2026-07-25) — the user is
  deliberately keeping it local pending a code audit before pushing anywhere.
  Don't suggest adding one unprompted.

## Environment note — Windows → Mac migration (completed 2026-07-25)

Migrated from a Windows desktop to an Apple Silicon (M2 Max) Mac. All Docker
base images in this stack (`postgres:18`, `node:22-slim`,
`mcr.microsoft.com/dotnet/*`, `caddy:2`, plus `pgbackrest` via apt) are
multi-arch and built/ran natively on arm64 with no emulation, as expected.
Data was restored from the logical dump + uploads tarball in
`../confluenceclone-migration-package/` (21 tables, 6 attachments — verified
against the migration package's own record) and a fresh Mac-native backup +
restore-test was taken immediately after. Two real issues turned up, both
now fixed:

- **Whole-project file permissions were 600/700 (owner-only), everywhere.**
  Not a Windows quirk — whatever copied the project folder to this Mac
  stripped all group/other bits repo-wide. This silently broke Docker
  multi-stage builds that `COPY` host files and then `USER <nonroot>` before
  running them (mode bits are preserved by `COPY`, so a root-owned 600 file
  becomes unreadable to a later non-root user): `tsc` in the web build stage,
  `collab/server.js`, and `src/Api/appsettings.json` in the app image all
  failed this way (`EACCES`/`UnauthorizedAccessException`). Fixed by
  normalizing the whole tree (`dirs 755`, `files 644`, `*.sh 755`, `.env`
  kept at `600`). If a future clone/copy of this repo reintroduces
  restrictive permissions, expect the same failure mode.
- **`docker compose up -d db` alone crash-loops the container every ~10s**
  on a fresh volume, because `archive_command` (pgBackRest) fails with no
  stanza yet — the `pgbackrest` sidecar (which runs `stanza-create`) isn't
  up. The failure escalates to a full postmaster restart, not a quiet retry.
  The migration package's own runbook says to bring up `db` alone before
  restoring; safer in practice is `docker compose up -d db pgbackrest`
  first, then restore once WAL archiving is confirmed stable (no restarts,
  clean `archive-push` completions in `docker compose logs pgbackrest`).
- Also added a repo-root `.dockerignore` (`node_modules`, `dist`, `bin`,
  `obj`, `.git`, `.env`) — there wasn't one before, so stale host build
  artifacts (also carried over from Windows: `src/web/node_modules`,
  `src/web/dist`, `src/Api/bin`/`obj`, `tests/Api.Tests/bin`/`obj`, all
  gitignored) were being pulled into image build contexts and clobbering
  fresh in-container installs. Those stale directories were deleted on the
  host; `dotnet-ef` was reinstalled as a global tool (PATH updated via
  `~/.zprofile`).
