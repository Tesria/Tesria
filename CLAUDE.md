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

## Environment note (2026-07-25)

The user is migrating their dev environment from a Windows desktop to an
Apple Silicon (M2 Max) Mac to free up machine resources. All Docker base
images in this stack (`postgres:18`, `node:22-slim`,
`mcr.microsoft.com/dotnet/*`, `caddy:2`, plus `pgbackrest` via apt) are
multi-arch, so the stack should build and run natively on arm64 with no
emulation. If a fresh session starts on the Mac and something in this stack
doesn't behave identically, that's worth flagging as a possible
platform-specific issue rather than assuming user error.
