# Changelog

All notable changes to ConfluenceClone are recorded here.
Format loosely follows [Keep a Changelog](https://keepachangelog.com/).

## [Unreleased]

### Phase 5 — Advanced (2026-07-24)

Added:
- **Real-time collaborative editing.** A Node + Hocuspocus/Yjs sidecar
  (`collab/`) lets several people edit a page simultaneously, with live remote
  carets showing who is where. The editor engine is JS-only, so this is isolated
  in a small sidecar rather than reshaping the .NET stack (PLAN §1).
  - **Authorisation:** the sidecar cannot evaluate our permission model, so the
    API is the gatekeeper — it issues a short-lived HMAC-signed token only to
    users who may *edit* that page, and binds the token to that page id. The
    sidecar verifies signature, expiry, and document match.
  - **Persistence:** Yjs document state is stored in the main PostgreSQL
    database, so in-flight edits survive a restart and are covered by the
    existing backups. Saving still creates a normal `PageVersion`, preserving
    history and rollback.
  - **Optional:** with no `COLLAB_SHARED_SECRET` set, the API reports
    collaboration as disabled and the editor falls back to single-user mode.
  - Caddy proxies `/collab` websockets to the sidecar; Vite mirrors this in dev.
- **Page templates (blueprints).** Reusable starting points for new pages,
  either instance-wide or scoped to one space. Space-scoped templates require
  edit rights on the space (they affect everyone creating pages there);
  instance-wide templates can be deleted only by their author, matching the
  existing comment-ownership pattern. The new-page screen offers a "start from
  a template" picker, and any page can be saved as a template from its actions.
- **Notifications and watches.** Watch a page or a space to get notified about
  page edits, new comments, and (for spaces) new pages created in it. Shaped
  like the audit log (same Action/TargetType/MetadataJson convention) plus a
  recipient and read state, and queued on the same unit of work as the change
  that triggers it, so notifications commit atomically with it. Notifications
  never go to the person who made the change, and — reusing the same fix
  already applied to the audit log — are hidden if the recipient's access to
  the target is later revoked. SPA: a watch toggle on pages and spaces, and a
  bell in the top bar with unread count, a dropdown, and mark-as-read.
- **API tokens and webhooks — the public REST API.** Personal access tokens
  (`Authorization: Bearer <token>`) let scripts and integrations call the same
  REST API the SPA uses, without a browser session. A policy auth scheme picks
  cookie vs. bearer per request and populates the same claims either way, so
  every existing endpoint's permission checks work unchanged for token callers.
  Tokens are shown once at creation; only their SHA-256 hash is stored.
  Space-scoped, admin-managed webhooks POST an HMAC-SHA256-signed JSON payload
  (`X-Webhook-Signature`) to a URL for one or more events (`page.created`,
  `page.updated`, `comment.created`, or `*`). Delivery is queued onto an
  in-process channel and sent by a background service with retry/backoff, so a
  slow or unreachable receiver never blocks the request that triggered it.
  Verified with a real listener: signature checked valid, and an
  unsubscribed event correctly produced no delivery. SPA: an API Tokens page
  and a per-space Webhooks page.
- **OIDC / SSO — the last Phase 5 item.** Sign in via any standards-compliant
  OpenID Connect provider (Keycloak, Authentik, Google, ...) alongside local
  accounts, configured generically via `Oidc:Authority`/`ClientId`/
  `ClientSecret` (PLAN §1: "architected for OIDC/SSO later", pluggable). The
  `Smart` policy scheme now spans three auth methods (cookie / API token /
  OIDC-issued cookie), all converging on the same internal claim shape so every
  existing endpoint keeps working unchanged.
  - **Account resolution** (`IOidcUserProvisioner`, independently unit-tested):
    a returning subject signs in; a verified-email match links to an existing
    local account; an **unverified-email match is refused** — auto-linking it
    would let anyone claiming that address at the IdP take over an existing
    account; no match provisions a new passwordless account.
  - Optional: no `Oidc:Authority` means the app behaves exactly as
    local-accounts-only, unchanged.
  - SPA: a "Sign in with …" option on the login page, shown only when enabled;
    a full-page redirect (not a fetch), since the identity provider needs the
    browser's own address bar.
  - **Verified against a real Keycloak instance**, not mocks: the full
    authorization-code + PKCE redirect dance end-to-end (challenge → Keycloak
    login → callback → authenticated session), a second login resolving to the
    same account (idempotent), and — critically — an attacker registering at
    the IdP with a victim's email but *unverified* was cleanly refused with no
    session established. That run caught a real bug: `ctx.Fail()` inside
    `OnTicketReceived` didn't reliably stop sign-in from completing with the
    provider's raw, unmapped claims; fixed by writing the rejection response
    and calling `HandleResponse()` explicitly, the same pattern already used
    for provider-side failures. Migration: OidcSubjectIndex.

### Phase 4 — Fast-follow (2026-07-23)

Added:
- Labels/tags: instance-wide labels (names normalised to lower case) applied to
  pages, with add/remove per page, browse-by-label, and usage counts. Trashed
  pages drop out of label listings. SPA shows label chips on a page and a
  browse-by-label view.
- Page export (`GET /api/pages/{id}/export?format=…`) to Markdown or standalone,
  print-ready HTML, via a ProseMirror renderer that covers the editor's node and
  mark types and HTML-escapes all content. PDF is produced by printing the HTML
  export from the browser, avoiding a headless-browser dependency in the image.
  Download links added to the page view.
- Audit log: append-only record of who did what and when, written in the same
  transaction as the change it describes. Covers the page lifecycle
  (created / updated / trashed / restored / purged) and space create/archive,
  with `GET /api/audit` (filter by target, newest first) and an audit view.

- Groups: named sets of users (case-insensitive unique names) with membership
  management, plus a user directory endpoint for picking principals.
- Space permissions and page restrictions. Grants are made to a user *or* a
  group, for View / Edit / Admin on a space (higher implies lower) and
  View / Edit on a page.
  - **Default-open:** a space with no permission rows stays open to every
    authenticated user, so existing content keeps working; the first grant is
    what makes a space private.
  - **Page restrictions are inherited** by descendant pages, and only holders of
    an *explicit* space-admin grant bypass them.
  - **Anti-lockout:** whoever first restricts a space or page is guaranteed
    continued access, and the last space admin cannot be removed.
  - Enforced across spaces, pages, versions, tree, trash, comments,
    attachments, labels, export, and search — restricted content is hidden
    (404) rather than merely refused, so it is not discoverable.

- Management UI for the above: a Groups page (create/delete groups, manage
  membership from the user directory), a per-space Permissions page reached from
  the space sidebar, and a Restrictions tab on each page. Both grant flows share
  one principal picker, and each explains its current state — an open space says
  so, and warns that the first grant makes it private.

### Phase 3 — Search + backup system (2026-07-23)

Added:
- Data Protection keys are now persisted in the database (via `AppDbContext`)
  instead of the container filesystem, so signed auth cookies survive redeploys
  and work across replicas. Bumped .NET 10 packages to 10.0.10 to clear a
  critical Data Protection advisory (GHSA-9mv3-2cwr-p262).
- Soft-delete / trash for pages: deleting a page trashes it and its whole
  subtree (a global query filter hides trashed pages everywhere). New endpoints
  to list a space's trash, restore a trashed subtree, and permanently purge it,
  plus a Trash view in the SPA with restore / delete-permanently.
- Full-text search over page titles and content (`GET /api/search`). Pages keep
  a plain-text `SearchText` (title + extracted content) maintained on every
  save; on PostgreSQL this feeds a generated `tsvector` column with a GIN index
  and relevance ranking, with a portable `LIKE` fallback for the test provider.
  Trashed pages are excluded. SPA gains a top-bar search box and results page.
- pgBackRest physical backups + point-in-time recovery (backup Layer 1). The
  `db` image now bundles pgBackRest with continuous WAL archiving to an
  encrypted repository; a `pgbackrest` sidecar creates the stanza and runs
  scheduled full/incremental backups. Scripts for verification, an end-to-end
  PITR self-test, and disaster-recovery/PITR restore. Verified in Docker: a real
  recovery to an exact target time excluded post-target changes.
- Attachment (`uploads`) file backups added to the logical-backup sidecar
  (Layer 3), on the same schedule and retention as the `pg_dump` layer.
- `docs/backup-recovery.md` rewritten as a full runbook: in-app restore, PITR,
  disaster recovery, verification, and enabling S3 offsite. New
  `BACKUP_ENCRYPTION_KEY` in `.env`.

### Phase 2 — Core content (2026-07-22)

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
