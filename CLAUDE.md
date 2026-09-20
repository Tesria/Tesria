# Tesria — project notes for Claude

A self-hosted Confluence-style wiki. ASP.NET Core (.NET 10) API + React 19/
TypeScript SPA (TipTap v3 editor) + PostgreSQL 18, deployed via Docker Compose
(Caddy auto-HTTPS, a Node/Hocuspocus collab sidecar, layered backups).

Read first, in this order:
- [`README.md`](./README.md) — stack, quick start, repo layout. Its Quick
  start is the whole procedure for standing this up on a fresh clone /
  new machine (which `.env` values must be set, and the one-liner that
  brings the stack up correctly) — start there rather than reconstructing
  it from `docker-compose.yml`.
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
- [`docs/security.md`](./docs/security.md) — the threat model, what each
  hardening layer does and does not defend, the known gaps, and the
  internet-readiness checklist. Read before exposing an instance or
  touching auth, sessions, rate limits, the audit chain or egress.
- [`docs/dev-plan.md`](./docs/dev-plan.md) — the **current sequenced plan**
  (written 2026-09-08): roles/admin, profiles and avatars, password
  recovery, space icons, the Confluence editor-parity audit, and the
  brand-page roadmap items, ordered by dependency. Start here for "what
  next"; it says which `roadmap.md` items it has scheduled.
- [`docs/roadmap.md`](./docs/roadmap.md) — forward-looking feature ideas not
  yet scheduled or designed (MCP support, expanded API, Mermaid diagrams,
  portable space/site export). Add new ideas here as they come up.

## Model gate — check before starting any dev-plan item

[`docs/dev-plan.md`](./docs/dev-plan.md) tags every item with the model
that should execute it: **Opus** (well-specified implementation), **Fable**
(design or security-model decisions that are expensive to reverse), or
**Fable → Opus** (Fable writes the spec, Opus implements it).

**Before starting an item, compare its tag to the model you are running as**
— the system prompt states it ("You are powered by the model named …").
If they differ, **stop before any tool call that does work.** Say which model
the plan asks for and, in one line, why; then offer exactly two options:
switch models, or override for this item. Wait for the answer. If the user
overrides, note it in the item's CHANGELOG entry. The point is to avoid
burning a large model's tokens on routine implementation, or a smaller
one's on a decision it shouldn't be making — either way, silently.

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
- **There are no frontend tests, so routing and layout changes get a live
  walk — every time, in every state.** A regression shipped on 2026-09-09
  because a route restructure was verified only along the paths it was
  *for* (anonymous reading); the nested `ProtectedRoute` had silently broken
  page editing, trash, permissions and webhooks for every signed-in user.
  After touching `main.tsx`, `Layout.tsx`, `ProtectedRoute`/`SessionGate`,
  or any `useOutletContext` consumer, open in the browser: as a **member** —
  `/spaces`, a space, a page, `/new` (create a page), `edit` (save it),
  `trash` (purge it), `settings`, `permissions`, `webhooks`, `/search`,
  `/labels/:name`, `/profile`, `/admin` (refusal); as an **admin** — every
  `/admin/*` tab; **signed out** — `/spaces`, a private space and page
  (masked), `/search`, `/profile` (redirect), `/login`, `/register`,
  `/reset`. Check the console for `Uncaught` after each. "It rendered" is
  not enough for the editor: create, save and purge a real page.
- **Browser-automation gotcha:** the key name for Enter is `Enter`, not
  `Return`. `Return` is not a DOM `key` value, so the page sees a keypress
  that matches nothing — no newline, no menu selection, nothing — and it
  looks exactly like a broken feature. This wasted a debugging pass on the
  slash and mention menus, both of which were fine.
- **Screenshots of the running app** are taken by the harness in
  [`scripts/screenshots/`](./scripts/screenshots/README.md), which runs
  from the **PDF sidecar's image** — it already carries a Chromium matched to
  its Playwright, so there is nothing to install. Run it inside **Caddy's**
  network namespace (`--network container:tesria-caddy-1`, base
  `https://tesria.localhost`, `ignoreHTTPSErrors`). Pointing it at the app
  container directly looks like it works and does not: the session cookie is
  `Secure`, so plain HTTP drops it and every shot is signed-out, and `/collab`
  is routed by Caddy, so the editor renders its toolbar over an empty
  document. Chromium also force-upgrades a named host to HTTPS regardless of
  `--disable-features=HttpsUpgrades`. Annotations (circles, arrows, labels)
  are drawn as a DOM overlay before the capture, not painted onto the PNG.

- **Driving the iOS Simulator, learned the hard way (2026-09-14).** Boot
  exactly one device and wait for `xcrun simctl bootstatus -b` before
  anything else — this machine is tight on memory, and a second boot takes
  it down. Caddy's CA goes in with `xcrun simctl keychain <udid>
  add-root-cert`, then `https://localhost` in Safari is the instance with no
  warning. Disconnect the hardware keyboard (`defaults write
  com.apple.iphonesimulator ConnectHardwareKeyboard -bool false`, relaunch
  the app) or the software keyboard never appears. Injected taps do **not**
  reliably move focus between web form fields, and the software keyboard
  drops shift (`@` types as `2`); ask the person to focus the field and
  then type. Never tap right after a swipe — the page is still moving.
  `xcrun simctl openurl` and `xcrun simctl io <udid> screenshot` need no
  panel access and are the reliable half.
  Switching `xcode-select` to Xcode also routes `git` and `python3` through
  Xcode's tools, and both refuse to run until `sudo xcodebuild -license
  accept` — a patch script then prints the licence notice instead of
  running, and looks like success unless its output is read. Do not use the
  simulator on this machine at all unless asked: it is an 8 GB Mac.

- **The app uses a data router** (`createBrowserRouter` in `main.tsx`,
  since 2026-09-16), not `<BrowserRouter>` — `PageEditor`'s leave prompt
  depends on `useBlocker`, which only a data router provides. Anything that
  needs the router (hooks like `useLocation`) must render inside the route
  tree; `Root` in `main.tsx` is where app-wide router-aware components go.
- **For an anonymous browser without signing anyone out**, use
  `https://tesria.localhost` instead of `https://localhost`. Caddy serves
  both and cookies are per-host, so the second hostname has no session
  while the first keeps yours. This is how the signed-out half of a live
  walk gets done without asking the owner to sign back in afterwards.
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
  devDependencies — follow it with a plain `npm install`.
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
- **Remote**: `origin` → [`Tesria/Tesria`](https://github.com/Tesria/Tesria),
  **private**, pushed 2026-09-08 (before that the repo was deliberately local
  only, pending a code audit). Default branch is `main` (renamed from
  `master` on 2026-09-08; the old branch is gone from both ends, so a clone
  predating that rename needs `git branch -m master main` plus a re-point at
  the new upstream). Keep it
  private — it isn't the open-source release, and that audit still hasn't
  happened. The history was scanned for secrets before the first push (`.env`
  is gitignored and was never committed; `.env.example` is placeholders only).

## Session handoff — 2026-08-03

Everything through this date is committed (this repo had ~2 weeks of
uncommitted work sitting in the working tree; it's now split into four
commits: the ConfluenceClone→Tesria rename, the drag-and-drop page tree
feature + a search bug fix, the password-visibility toggle, and this doc
update). `dotnet test` (111 tests) and `npm run build && npm run lint` were
both green as of the last commit; the Docker `app` image was rebuilt and
the features were verified live in-browser (desktop + mobile viewports)
before committing — see `docs/CHANGELOG.md`'s dated entries for what
"verified" covered for each one.

Two pieces of throwaway test data are sitting in the live app, left
alone deliberately (permanent deletion isn't something this assistant
does unprompted) — safe to remove or ignore:
- A **"DnD Tester"** test account with a **"Drag and Drop Test" (`DND`)**
  space, created to verify the drag-and-drop tree feature without touching
  real content.
- A **"Trash Test Page"** sitting in the real **"App Design"** space's
  Trash, from an earlier trash/restore verification pass.
- An **"API Docs Bot"** account (`api-docs-bot@tesria.local`) and the
  **"API"** space it authored, created 2026-09-08 to document the REST API
  end-to-end and exercise the editor's full feature set against real content
  (23 pages, 8 labels, panels, coloured tables, a saved template). The space
  is real documentation worth keeping; the bot account is a fixture and can
  be deleted once its pages are reassigned or the space is re-owned.
- A **"Manual Bot"** account (`manual-bot@tesria.local`) and the **"Tesria
  User Manual"** (`MANUAL`) space it authored, created 2026-09-11: 47 pages
  and 86 screenshots documenting the product for end users. Same arrangement
  as the API bot — the space is real documentation, the account is a fixture.
  Its password is **not** in the repo; regenerate it (or reset from the admin
  area) if the screenshot harness needs to run again.
  It was **promoted to instance administrator** on 2026-09-11, at the owner's
  explicit request, so that the manual could document the admin area. That is
  a standing admin account and therefore a standing risk: demote it from
  Administration → Users once the documentation is settled.

If a new session picks up UI work in the "App Design" space (the
dogfooding space documenting Tesria's own architecture), note it's real,
intentional content — not test data to clean up.

## Environment note — Windows → Mac migration (completed 2026-07-25)

Migrated from a Windows desktop to an Apple Silicon (M2 Max) Mac. All Docker
base images in this stack (`postgres:18`, `node:22-slim`,
`mcr.microsoft.com/dotnet/*`, `caddy:2`, plus `pgbackrest` via apt) are
multi-arch and built/ran natively on arm64 with no emulation, as expected.
Data was restored from the logical dump + uploads tarball in
`../tesria-migration-package/` (21 tables, 6 attachments — verified
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
