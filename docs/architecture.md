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
- `Domain/` holds entities; `Infrastructure/` holds EF Core (`AppDbContext` +
  migrations), attachment storage, and auth wiring (Argon2id hashing, the
  current-user accessor).
- **Features:** `Auth` (register/login/logout/me, cookie sessions), `Spaces`,
  `Pages` (tree, versioning, rollback, move), `Attachments`, `Comments`.
- The API also serves the compiled SPA from `wwwroot` and falls back to
  `index.html` for client-side routes, so the whole product is one origin in
  production (no CORS needed). CORS is enabled only in Development for the Vite
  dev server.

### Health

`GET /api/health` returns `{ status, service, version, utc }` and includes a
database readiness probe (EF Core `DbContext` check). Used by the container
`HEALTHCHECK`.

### Auth

Three ways to authenticate, all resolving to the same claim shape so
`CurrentUser` and every permission check work identically regardless of which
was used:

- **Local accounts** — cookie-based sessions; passwords hashed with Argon2id.
- **API tokens** (`Authorization: Bearer <token>`) — for scripts/integrations
  (Features/ApiTokens). Only a SHA-256 hash is stored; the raw token is shown
  once, at creation.
- **OIDC/SSO** — optional, pluggable for any standards-compliant provider
  (Keycloak, Authentik, Google, ...) via `Oidc:Authority`/`ClientId`/
  `ClientSecret`. A first login provisions a passwordless local account; a
  verified-email match links to an existing local account; an unverified-email
  match is refused (would otherwise allow account takeover). With no Authority
  configured the app behaves exactly as local-accounts-only.

A `Smart` policy scheme picks Cookie vs. API-token per request based on the
`Authorization` header. Unauthenticated API calls receive `401` (no login
redirect), since the client is a SPA — except the OIDC login endpoint, which is
a real full-page redirect to the identity provider.

## Frontend (`src/web`)

- React 19 + TypeScript, built with Vite.
- In development, Vite serves the SPA on `:5173` and proxies `/api` to the API
  on `:5291`, so the frontend always uses same-origin relative URLs — identical
  to production.
- In production, `npm run build` output is copied into the API's `wwwroot`
  during the Docker build.
- Routing is React Router 7; a typed `api/client.ts` wraps all REST calls and
  an `AuthContext` holds the session.

### Responsive layout

Two breakpoints, documented as CSS custom properties in `index.css`'s
`:root` (`--bp-mobile: 640px`, `--bp-tablet: 1024px`) — CSS can't read a
custom property inside an `@media` condition, so each `@media` rule repeats
the raw number with a `/* keep in sync with --bp-mobile */` comment pointing
back to the documented source of truth. Below `--bp-mobile`: the topbar nav
collapses behind a hamburger, the space page-tree sidebar becomes an
off-canvas drawer, and the page's full-width toggle hides (a distinction
without a difference once the reading column already fills the viewport).
`useDismissable.ts` (outside-click/Escape dismissal) is shared by both the
sidebar drawer and `OverflowMenu`. `--page-pad` is the one custom property
worth being careful with: it's read both by `.paper`'s own padding and by
the full-width table breakout math (see the Editor section below) — change
it in one place, not both, or they drift apart and a full-width table
overflows the viewport by the difference.

### Editor (`src/web/src/editor`)

TipTap v3 (ProseMirror) provides the block WYSIWYG. Documents are stored as
ProseMirror JSON in `PageVersion.ContentJson`.

- **`extensions.ts` is the single source of truth for the schema** (node/mark
  types) — both `Editor.tsx` (single-user, and read-only rendering via
  `editable={false}`) and `CollaborativeEditor.tsx` (Yjs-backed) import from
  it rather than declaring their own extension list. This matters because Yjs
  requires every collaborator to share one exact ProseMirror schema — any new
  node/mark type is added here, once, never inline in either editor component.
- **Custom node views** (`CodeBlockView.tsx`) render a React component in
  place of a node — used for the syntax-highlighted code block's language
  picker/copy button/line-number gutter.
- **Floating menus** (`@tiptap/react/menus`'s `BubbleMenu`) — `LinkMenu.tsx`
  (editing an existing link), `SelectionBubbleMenu.tsx` (formatting a text
  selection, including the "Comment" action), `ImageHoverMenu.tsx` (border/
  shadow/comment on a selected image). Each needs a distinct `pluginKey` prop.
  **Gotcha:** these render inside the page's own save `<form>` in edit mode;
  any popover `<form>` inside one of them must call `e.stopPropagation()` in
  its submit handler, or the submit event bubbles through React's synthetic
  event system (which follows the component tree, not BubbleMenu's DOM
  portal) and also submits the outer page-save form.
- **Table hover controls** (`TableControls.tsx` for row/column insert-delete,
  `TableWidthControls.tsx` for the width edge-drag handle + full-width
  toggle) — fixed-position overlays that track proximity to each `<table>`
  in the document (not DOM ancestry, since the buttons render outside the
  table's own DOM), sharing one hover/selection-tracking hook,
  `useHoveredTable.ts`. `TableControls` uses `TableMap.positionAt()` (from
  `@tiptap/pm/tables`) to translate a clicked row/column index into the
  right ProseMirror cell position before running the standard add/delete
  row/column commands. Table width itself lives on the `table` node as
  `width` (px) and `layout: 'default' | 'full-width'` attrs (`extensions.ts`,
  same `.extend()`-and-disable-the-stock-one pattern as `CodeBlock`) —
  independent of column-border dragging (stock `prosemirror-tables`,
  unaffected) and of the page's own full-width setting (`Page.FullWidth`).
  **Gotcha:** prosemirror-tables' `TableView` (active whenever a table is
  `resizable`, which is always, in both edit and read-only rendering) only
  applies a node's rendered `style`/`data-*` attributes once, in its
  constructor — its own `update()` (used for every subsequent attribute
  change on an already-mounted table) recalculates the colgroup but never
  re-touches them. Schema `renderHTML` alone is only correct on a fresh
  mount (a page load, an export); anything that changes a table's attrs live
  (`TableWidthControls`) must also apply the same DOM effect directly right
  after the transaction commits, or the change is invisible until the next
  reload.
  On touch/no-hover input (`matchMedia('(hover: none) and (pointer:
  coarse)')`, not viewport width — a touchscreen laptop at desktop width has
  the same problem a phone does), `useHoveredTable` switches its reveal
  trigger from mouse proximity to "does the current selection sit inside a
  table," since hover doesn't exist there — tapping to place the cursor is
  the natural touch equivalent.
- **The slash command menu** (`slash/`) is a custom `Suggestion`-based
  extension (the same primitive `@tiptap/extension-mention` is built on) —
  there's no pre-built importable slash extension. Positioning, scroll/resize
  tracking, and outside-click dismissal are handled by `@tiptap/suggestion`'s
  own managed `mount()` API (Floating UI-based), which meant no separate
  positioning library (e.g. tippy.js) was needed.
- **Inline comments**: a `comment` mark (`commentMark.ts`) highlights a text
  range and links it to a real `Comment` row via a `commentId` attr; images
  can't carry marks, so an image comment has no in-document highlight.
- **Draft/publish**: a new page gets a real (invisible) `Page` row —
  `Status = PageStatus.Draft`, reusing an enum value that existed unused
  since Phase 2 — the moment the editor mounts, via `POST /pages/draft`. This
  gives image uploads (which need a real page id) somewhere to attach to
  before the user has saved anything. `POST /pages/{id}/publish` makes it
  real (fires the normal "page created" audit/notification/webhook side
  effects, exactly once — a retried publish is a safe no-op) and mutates the
  existing version 1 in place rather than creating a confusing empty-v1/
  real-v2 pair. The global EF Core query filter on `Page` excludes drafts
  (`Status != PageStatus.Draft`), matching the existing soft-delete filter
  pattern; permission checks already used `IgnoreQueryFilters()` for
  trash/restore, so they resolve drafts correctly with no extra code.

## Real-time collaboration (`collab/`)

A small **Node + Hocuspocus/Yjs** sidecar provides simultaneous editing. The
editor engine is JS-only, so this is the one piece deliberately kept outside the
.NET app (PLAN §1) rather than reshaping the main stack.

- **Authorisation.** The sidecar cannot evaluate the permission model, so the API
  is the gatekeeper: `GET /api/pages/{id}/collab-token` checks the caller may
  *edit* the page and returns a short-lived HMAC-signed token bound to that page
  id. The sidecar only verifies signature, expiry, and that the document being
  opened matches — so a forged token, or a valid token replayed against another
  page, is rejected.
- **Persistence.** Yjs state is written to the `CollabDocuments` table in the
  main database, so live edits survive a sidecar restart and fall under the
  existing backups. Saving a page still creates a regular `PageVersion`, so
  version history and rollback are unchanged.
- **Optional.** Without `COLLAB_SHARED_SECRET`, the API reports collaboration as
  disabled and the SPA silently uses the single-user editor.
- Caddy proxies `/collab` websocket traffic to the sidecar; the Vite dev server
  mirrors that route so development matches production.

## Data & persistence

- **PostgreSQL 18** is the system of record, via EF Core migrations applied
  automatically on startup. Page bodies are stored as ProseMirror JSON in
  `jsonb` columns. Every page save creates a new immutable `PageVersion`
  (history + rollback); comments carry an optional `jsonb` inline anchor.
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

See [`backup-recovery.md`](./backup-recovery.md). Layered by design:

- **pgBackRest** (Layer 1): the `db` image bundles pgBackRest with continuous
  WAL archiving to an encrypted repository (a `pgbackrest` sidecar runs
  scheduled full/incr backups), enabling point-in-time recovery.
- **Logical `pg_dump`** (Layer 2) and **`uploads` file archives** (Layer 3) on a
  schedule with retention, via the `backup` sidecar.
- **In-app safety nets**: page version history + rollback, and soft-delete/trash
  with restore.
- **Offsite S3** is supported but off by default (`BACKUP_S3_ENABLED`).

## Decisions

- **.NET + React** over a single-language stack: strongest backend reliability
  and data tooling, which suits the data-safety priority. The one gap —
  real-time co-editing, whose ecosystem is JS-native — is deferred to Phase 5
  and will be isolated in a small Node/Hocuspocus sidecar rather than reshaping
  the main stack.
- **Same-origin SPA hosting** (API serves `wwwroot`) keeps deployment to a
  single app container and avoids CORS in production.
