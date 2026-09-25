# Tesria: project notes for Claude

A self-hosted Confluence-style wiki. ASP.NET Core (.NET 10) API + React 19/
TypeScript SPA (TipTap v3 editor) + PostgreSQL 18, deployed via Docker Compose
(Caddy auto-HTTPS, a Node/Hocuspocus collab sidecar, layered backups).

Read first, in this order:
- [`README.md`](./README.md): stack, quick start, repo layout. Its Quick
  start is the whole procedure for standing this up on a fresh clone /
  new machine (which `.env` values must be set, and the one-liner that
  brings the stack up correctly): start there rather than reconstructing
  it from `docker-compose.yml`.
- [`docs/architecture.md`](./docs/architecture.md): how it's put together and
  why, including a detailed editor-subsystem section (extensions.ts as the
  schema source of truth, node views, floating menus, the draft/publish page
  lifecycle). Kept current as features land.
- [`docs/CHANGELOG.md`](./docs/CHANGELOG.md): dated, most-recent-first record
  of what's been built. Check this before assuming something doesn't exist.
- [`docs/backup-recovery.md`](./docs/backup-recovery.md): the runbook if
  you're touching anything backup/restore-related.
- [`docs/tls-and-lan-access.md`](./docs/tls-and-lan-access.md): real-domain
  vs. no-domain/LAN HTTPS, and the `deploy/scripts/trust-ca.*` scripts.
- `PLAN.md`: the original founding design doc (phases 1–5). The editor
  overhaul that followed it is tracked in the CHANGELOG instead, not as a
  numbered PLAN.md phase.
- [`docs/security.md`](./docs/security.md): the threat model, what each
  hardening layer does and does not defend, the known gaps, and the
  internet-readiness checklist. Read before exposing an instance or
  touching auth, sessions, rate limits, the audit chain or egress.
- [`docs/dev-plan.md`](./docs/dev-plan.md): the **current sequenced plan**
  (written 2026-09-08): roles/admin, profiles and avatars, password
  recovery, space icons, the Confluence editor-parity audit, and the
  brand-page roadmap items, ordered by dependency. Start here for "what
  next"; it says which `roadmap.md` items it has scheduled.
- [`docs/roadmap.md`](./docs/roadmap.md): forward-looking feature ideas not
  yet scheduled or designed (MCP support, expanded API, Mermaid diagrams,
  portable space/site export). Add new ideas here as they come up.

## Working conventions established in this repo

- **Docker is the source of truth for manual verification.** After a backend
  or frontend change, rebuild and restart just the `app` service and check it
  live rather than trusting the build alone:
  ```bash
  docker compose build app && docker compose up -d app
  ```
  `dotnet test` (SQLite in-memory, no Docker needed) covers backend logic;
  `npm run build && npm run lint && npm test` covers the frontend. All should
  stay green, but none substitutes for looking at the running app for UI
  changes.
- **A path that must work under the app role's grants gets a test in
  `DatabaseRoleTests`** (added 2026-09-25 for the review's DATA-04; `MigrateWatchTests` beside it). SQLite
  has no roles, so every other test passes whether or not the app writes a
  table it may not. Those tests run on real PostgreSQL when
  `TESRIA_TEST_POSTGRES` is set, and are skipped otherwise; CI runs them
  against a postgres service. Locally, a throwaway server does it:
  `docker run -d --rm --name tesria-test-pg -p 127.0.0.1:55432:5432 -e POSTGRES_PASSWORD=tesria-test postgres:18`,
  then `TESRIA_TEST_POSTGRES='Host=127.0.0.1;Port=55432;Username=postgres;Password=tesria-test'`.
- **Frontend tests are for logic, never for rendering** (`npm test`, vitest,
  added 2026-09-20 for the 8.6 block diff). What belongs there is pure
  functions with edge cases a walk cannot cover honestly. Components,
  routing and layout do *not* get unit tests here: a mounted-and-asserted
  component passes while the real page is broken, which is exactly the
  failure mode the rule below exists for.
- **Routing and layout changes get a live walk: every time, in every
  state.** A regression shipped on 2026-09-09 because a route restructure was
  verified only along the paths it was *for* (anonymous reading); the nested
  `ProtectedRoute` had silently broken page editing, trash, permissions and
  webhooks for every signed-in user.
  After touching `main.tsx`, `Layout.tsx`, `ProtectedRoute`/`SessionGate`,
  or any `useOutletContext` consumer, open in the browser: as a **member**,
  `/spaces`, a space, a page, `/new` (create a page), `edit` (save it),
  `trash` (purge it), `settings`, `permissions`, `webhooks`, `/search`,
  `/labels/:name`, `/profile`, `/admin` (refusal); as an **admin**, every
  `/admin/*` tab; **signed out**, `/spaces`, a private space and page
  (masked), `/search`, `/profile` (redirect), `/login`, `/register`,
  `/reset`. Check the console for `Uncaught` after each. "It rendered" is
  not enough for the editor: create, save and purge a real page.
- **Browser-automation gotcha:** the key name for Enter is `Enter`, not
  `Return`. `Return` is not a DOM `key` value, so the page sees a keypress
  that matches nothing (no newline, no menu selection, nothing) and it
  looks exactly like a broken feature. This wasted a debugging pass on the
  slash and mention menus, both of which were fine.
- **Screenshots of the running app** are taken by the harness in
  [`scripts/screenshots/`](./scripts/screenshots/README.md), which runs
  from the **PDF sidecar's image**: it already carries a Chromium matched to
  its Playwright, so there is nothing to install. Run it inside **Caddy's**
  network namespace (`--network container:tesria-caddy-1`, base
  `https://tesria.localhost`, `ignoreHTTPSErrors`). Pointing it at the app
  container directly looks like it works and does not: the session cookie is
  `Secure`, so plain HTTP drops it and every shot is signed-out, and `/collab`
  is routed by Caddy, so the editor renders its toolbar over an empty
  document. Chromium also force-upgrades a named host to HTTPS regardless of
  `--disable-features=HttpsUpgrades`. Annotations (circles, arrows, labels)
  are drawn as a DOM overlay before the capture, not painted onto the PNG.

- **The app uses a data router** (`createBrowserRouter` in `main.tsx`,
  since 2026-09-16), not `<BrowserRouter>`: `PageEditor`'s leave prompt
  depends on `useBlocker`, which only a data router provides. Anything that
  needs the router (hooks like `useLocation`) must render inside the route
  tree; `Root` in `main.tsx` is where app-wide router-aware components go.
- **For an anonymous browser without signing anyone out**, use
  `https://tesria.localhost` instead of `https://localhost`. Caddy serves
  both and cookies are per-host, so the second hostname has no session
  while the first keeps yours. This is how the signed-out half of a live
  walk gets done without signing anyone out of the first hostname.
- **Screenshot-harness steps run *before* a shot's `settle` wait**, so a
  `probe` placed first reads the page before the session check has answered
  and every signed-in route looks like "Loading…". Put a `{ "wait": 2500 }`
  step ahead of any probe. `tap` needs `SHOT_MOBILE=1` (touch);
  `SHOT_BROWSER=webkit` runs Safari's engine. The debug account's sign-in is
  in the gitignored `.debug-credentials` at the repo root.

- **Dependency audit is a release gate.** `scripts/audit.sh` runs
  `npm audit` (web + collab) and `dotnet list package --vulnerable
  --include-transitive`; it must exit 0 before a release or after touching
  any package manifest. Note `npm audit fix --omit=dev` prunes
  devDependencies: follow it with a plain `npm install`.
- **Dependency manifest**: after touching any package manifest or base
  image, run `node scripts/deps/build-manifest.mjs` (after `npm ci` in
  `src/web`, `collab` and `pdf`, so license texts are found) and commit
  `src/Api/About/`. CI fails when it is stale.
- **EF Core migrations**: `dotnet-ef` is installed as a global tool. Add one
  with `dotnet ef migrations add <Name> --output-dir Infrastructure/Migrations`
  from `src/Api/`. Under Compose the one-shot `migrate` service applies
  them before the app starts (14.3; `docker compose logs migrate`), and a
  production app refuses to start with any pending. Outside production
  (tests, `dotnet run`) the app applies them itself.
- **Editor schema changes** (new TipTap node/mark type) go in
  `src/web/src/editor/extensions.ts`, never declared inline in `Editor.tsx` or
  `CollaborativeEditor.tsx` separately: see the architecture doc's editor
  section for why (Yjs schema-sharing requirement).
- **Any popover `<form>` rendered inside the editor** (bubble menus, etc.)
  must call `e.stopPropagation()` in its submit handler: see the
  architecture doc's "Gotcha" note. This bit a real feature once already.
- Git Bash on Windows mangles absolute container paths in
  `docker compose exec`/`cp` args (e.g. `/scripts/verify.sh` becomes a bogus
  `C:/Program Files/Git/scripts/verify.sh`). Prefix the command with
  `MSYS_NO_PATHCONV=1` when running those.
