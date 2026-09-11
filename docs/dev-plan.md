# Development plan — sequenced (2026-09-08)

The next body of work, ordered by dependency rather than by the order it was
asked for. Written by Fable 5.1; **each item names the model that should
execute it** (see "Model gate" — read that section before starting
anything). Every item is grounded in the code as it stands today (see "What
exists"), not in assumptions.

Distinct from [`PLAN.md`](../PLAN.md) (the founding design, complete) and
[`roadmap.md`](./roadmap.md) (unscheduled ideas — the ones from there that
are now scheduled are pulled in here). Move items to the
[`CHANGELOG`](./CHANGELOG.md) as they ship.

## Model gate — read this first

Every item below carries a tag:

- **`Model: Opus`** — well-specified implementation. Opus 5 executes it.
- **`Model: Fable`** — a design or security-model decision that is expensive
  to reverse if wrong. Fable 5.1 executes it.
- **`Model: Fable → Opus`** — Fable writes the design (a spec section in this
  file or in `architecture.md`), then Opus implements against it. Two
  separate sittings; the handoff is the written spec.

**Before starting any item, compare its tag to the model you are running as
(the system prompt states it: "You are powered by the model named …"). If
they differ, STOP before any tool call that does work.** Tell the user which
model the plan asks for and in one line why, then offer exactly two options:
switch models, or override for this item. Do not proceed until they answer.
If they override, record it in that item's CHANGELOG entry ("executed by X,
plan asked for Y, user override"). This rule also lives in `CLAUDE.md`, so
it applies whether or not a session has read this file.

The assignment rule: Fable where a wrong call costs a migration, a security
hole, or a rewrite; Opus everywhere the spec is already precise enough that
the main risk is execution, which Opus handles well and cheaply.

## How to read this

Phases are ordered so that nothing needs something from a later phase.
Within a phase, items are ordered too. Sizes are relative (S / M / L / XL),
not estimates. **Three sequencing decisions matter most and are called out in
bold** — read those even if you skim the rest.

Conventions that apply to every item, from `CLAUDE.md`:

- Backend: `dotnet test` green (SQLite in-memory, no Docker); migrations via
  `dotnet ef migrations add <Name> --output-dir Infrastructure/Migrations`
  from `src/Api/`; they run automatically on API startup.
- Frontend: `npm run build && npm run lint` green.
- **Docker is the source of truth for manual verification** — rebuild `app`
  and check it live.
- Editor schema changes (any new node/mark) go in
  `src/web/src/editor/extensions.ts` only, never inline in an editor
  component — Yjs requires one shared schema. **Every new node/mark also
  needs a case in `ProseMirrorRenderer.cs`** (HTML and Markdown) and an
  `ExportTests` case, or it silently degrades to plain text on export.
- Any popover `<form>` inside the editor must `e.stopPropagation()` on submit.
- Dated CHANGELOG entry per item; keep `architecture.md` current.

## What exists (verified against the code, not the docs)

| Area | State today |
|---|---|
| Roles / admin | **None.** No `IsAdmin`, no role, no site-admin concept anywhere. |
| First user | Plain registration; nothing special about the first account. |
| Registration | **Open to anyone** who can reach `/register`. No toggle, no invites. |
| User model | `Email`, `DisplayName`, `PasswordHash`, `OidcSubject`, `Status`, `CreatedAt`. No avatar, no profile fields, no `LastSeenAt`. |
| Suspension | `UserStatus.Suspended` exists in the enum and login honours it — **but nothing in the codebase can set it.** |
| Site settings | No table, no concept. Config is env/appsettings only. |
| Email | **Nothing.** No SMTP, no sender abstraction, no MailKit package. |
| Password reset | None, of any kind. |
| Profile editing | No endpoint, no page. |
| Space model | `Key`, `Name`, `Description`, `Homepage`, `Archived`. No icon, no public flag. |
| Anonymous access | **None.** Every `/api` route except `/health` is `RequireAuthorization()`; the SPA's `ProtectedRoute` redirects to `/login`. |
| Storage | `IAttachmentStorage` (key-based, local disk, S3 slot reserved) — reusable for avatars/icons. |
| Telemetry | **None.** No page views, no login events, no last-seen. `AuditLogs` records mutations only. |
| KPI-able data | `Users.CreatedAt`, `Pages/PageVersions/Comments.CreatedAt`, `Attachment.Size` + `ContentType`, `AuditLogs.Action/CreatedAt`. Enough for *content* growth, not for *usage*. |
| Export | Markdown and print-ready HTML. **Not PDF** (the brand page says PDF — see Phase 8). |
| Editor nodes | paragraph, heading, bullet/ordered list, taskList, blockquote, codeBlock (lowlight), horizontalRule, image, table (+cell colours), panel. Marks: bold, italic, underline, strike, code, link, highlight (colours), comment. textAlign on heading/paragraph. |
| Notifications | In-app only, via `INotificationService`; the natural hook for email and for admin alerts. |
| Licence | **No `LICENSE` or `NOTICE` file in the repo.** The brand page claims Apache 2.0 — see 8.2. |

### Security baseline (audited 2026-09-08 — the findings Phase 3 exists to fix)

| Finding | Evidence | Severity for internet exposure |
|---|---|---|
| App never sees HTTPS; no forwarded-headers handling | Caddy → `http://+:8080`; no `UseForwardedHeaders` in `Program.cs` | **High.** `SecurePolicy = SameAsRequest` means the session cookie is set without `Secure`; and every request's `RemoteIpAddress` is Caddy's, so any per-IP rate limit would lock out *all* users at once. |
| No rate limiting, no lockout, no failed-login tracking | grep for `RateLimit`, `Lockout`, `RemoteIpAddress`: nothing | **High.** Unlimited online brute force against `/api/auth/login`. |
| App runs as the Postgres **superuser** | `ConnectionStrings__Default` uses `POSTGRES_USER`, which is the instance superuser | **High.** App compromise = full DB, including deleting audit rows. |
| Audit log is append-only in *application code* only | no `AuditLogs.Remove/Update` in code; nothing at the DB layer | Medium alone; **High** combined with the superuser role. |
| No security headers anywhere | nothing in `Program.cs` or `deploy/Caddyfile` | Medium. No HSTS, CSP, `nosniff`, frame-ancestors. |
| No CSRF token; SameSite=Lax is the only defence | no `AddAntiforgery`; one `DisableAntiforgery()` on upload | Low–Medium. Lax blocks cross-site POST from top-level navigations, not everything. |
| Webhooks can target any URL | no private-range check in `Infrastructure/Webhooks` | Medium. SSRF: an editor can make the server hit `169.254.169.254`, the DB, the collab sidecar. |
| Attachment content type trusted from the client and echoed on download | `ContentType = file.ContentType` → `Results.File(stream, a.ContentType, …)` | Low–Medium. `Content-Disposition: attachment` is set (good); no `nosniff`, no allowlist. |
| Caddy on-demand TLS catch-all on `:443` | `tls internal { on_demand }` | Medium on the internet: anyone can trigger cert minting for arbitrary SNI. A LAN feature, wrong for public hosting. |
| **38 npm vulnerabilities (2 high)** in `src/web` | `npm audit --omit=dev`; `react-router-dom` among them | Medium. Fixable. |
| Collab sidecar has **no lockfile** | `npm audit` → `ENOLOCK` | Medium: unauditable and unreproducible builds. |
| .NET packages | `dotnet list package --vulnerable --include-transitive`: clean | — |
| Argon2id via library defaults | `Argon2.Hash(password, HybridAddressing)`, no explicit params | Low. Defaults are sane; document them and pin them. |
| 30-day sliding cookie, no server-side revocation | `ExpireTimeSpan = 30d, SlidingExpiration` | Medium. Fixed by `SecurityStamp` in 1.1. |

---

## Phase 0 — Foundations (everything below depends on these)

**Sequencing decision #1: telemetry goes first, not last.** The dashboard
(2.5) is the last thing built, but its *usage* KPIs are only as good as the
data accumulated by then. Adding the recording now is cheap and means the
dashboard ships with weeks of real numbers instead of an empty chart. The
security phase (3.3) also needs it: "admin logged in from a new IP" is
undetectable without a login history.

### 0.1 Roles — `M` — Model: Fable → Opus — ✅ **shipped 2026-09-08**
- Add `Role` to `User` (`Member = 0, Admin = 1`) — an enum, not a bool, so
  a future `Viewer`/`Moderator` is a value, not a migration of a bool.
- **The first registered account becomes Admin** (`Users.CountAsync() == 0`
  inside the registration transaction). Document it in the README.
- `RequireAdmin` authorization policy; `CurrentUser.IsAdmin`.
- **Fable decided (2026-09-08): no silent bypass.** Admins see what their
  grants allow, plus an audited `recover-access` action that writes them an
  explicit space-admin grant — reusing the existing "explicit space admins
  bypass page restrictions" rule rather than adding a second code path.
  Full spec, including the first-user race handling, the migration
  backfill and the required tests: `architecture.md` → "Roles and
  administrators". **Fable half complete; Opus implements against it.**
- Tests: first-user-is-admin; non-admin gets 403 on an admin route; the
  existing tests keep passing (several register two users — check that
  "first user is admin" doesn't change their expectations).

### 0.2 Site settings — `S` — Model: Opus — ✅ **shipped 2026-09-09**
- `SiteSettings` table, single row, typed columns (not key/value — typed
  columns are validated by EF and readable in the admin UI without a
  parser). Start with: `InstanceName`, `AllowPublicRegistration`,
  `AllowPublicSpaces` (the instance-wide kill switch Phase 5 hangs off),
  SMTP fields (host, port, username, secret ref, from-address, TLS mode),
  `EmailEnabled`, `RequireTotpForAdmins`.
- `ISiteSettings` scoped service with a short in-memory cache and an
  invalidate-on-save.
- Secrets: store the SMTP password encrypted with the existing
  Data Protection stack (`DataProtectionKeys` is already in the DB).
- Endpoint: `GET/PUT /api/admin/settings` (admin only).
- Tests: registration respects `AllowPublicRegistration`; settings round-trip.

### 0.3 Usage telemetry — `M` — Model: Opus — ✅ **shipped 2026-09-09**
- `User.LastSeenAt` (bumped at most once per N minutes per request, via the
  `CurrentUser` accessor, to avoid a write per request).
- `user.login` audit action with actor, timestamp **and client IP** (needs
  3.0 for the IP to be real — record it anyway; it becomes correct the
  moment forwarded headers land). **Confirmed 2026-09-09 on the running
  stack: three requests from two clients all logged `172.18.0.7`, Caddy's
  container address. 3.0 is now a prerequisite in fact, not just on paper.** Failed logins: `user.login_failed` with
  IP and a count, never the attempted email in metadata.
- Page views: a `PageView` table (`PageId, UserId?, ViewedAt`) — `UserId`
  nullable from day one, because Phase 5 will write anonymous views.
  Written on `GET /pages/{id}` for browser sessions, not API tokens (tokens
  are scripts and would swamp the numbers). Index on `(PageId, ViewedAt)`.
- Nothing user-facing yet. This exists so 2.5 and 3.3 have data.

### 0.4 Profile media storage — `S` — Model: Opus — ✅ **shipped 2026-09-09**
- Reuse `IAttachmentStorage` with a distinct key namespace (`avatars/…`,
  `space-icons/…`) rather than a second storage abstraction.
- `GET /api/media/avatars/{userId}` and `/space-icons/{spaceId}` — long
  cache headers plus a content-hash query string for cache busting.
- Server-side validation: content-type allowlist (png/jpeg/webp), size cap
  (~1 MB), re-encode raster uploads to a fixed 256px square so the stored
  file is never the raw upload. **Reject SVG** for avatars outright — it can
  carry script, and there is no need for it here.
- **Shipped with the avatar upload/delete endpoints as well**, since the
  validation above has no other home. 1.2 is therefore the UI, the
  client-side crop and the generated defaults — not the server half.

---

## Phase 1 — Accounts and identity (user-facing)

### 1.1 Edit profile — `M` — Model: Opus — ✅ **shipped 2026-09-09**
- `PUT /api/auth/me` (display name); `PUT /api/auth/me/email` (requires
  current password; lower-cased, uniqueness check, 409 on collision);
  `PUT /api/auth/me/password` (current + new; **invalidates other
  sessions** — see below).
- OIDC-provisioned users (`PasswordHash == null`) can change display name
  and avatar but not email/password — the IdP owns those. Render those
  fields read-only with a note, don't just 400.
- **`SecurityStamp` on `User`**, embedded as a claim at sign-in and checked
  in `OnValidatePrincipal`; changing the password rotates it. Suspension
  (2.2), force-logout (3.3) and 2FA enrolment (3.5) all reuse this — build
  it here once.
- Frontend: `/profile` route, reachable from the username in the topbar.

### 1.2 Avatars — `M` — Model: Opus — ✅ **shipped 2026-09-09**
- Depends on 0.4.
- **Prebuilt set:** generate SVG avatars deterministically from the user
  id — initials on one of twelve backgrounds. (Shipped with its own palette
  in `avatarIdentity.ts` rather than the editor's `palette.ts`: those are
  light tints meant to sit *behind* dark body text, which is the opposite of
  what a coloured avatar with white initials needs.) The deterministic one is
  the default, so every user has an avatar from day one with zero storage.
- **Upload:** client-side square crop (a small canvas crop, no library); the
  server endpoint and re-encode already shipped in 0.4.
- Render in: topbar, comments, version history, the user directory/picker,
  and later mentions (7.C). One `<Avatar>` component, sizes 20/28/40.
  **Shipped in the topbar and profile only.** Comments and version history
  return just an `AuthorId` and render no author identity at all today, so
  avatars there need names added first — **scheduled as 1.5 below.** The people picker is a `<select>`, whose options cannot hold markup;
  `GET /api/users` already carries `avatarHash`/`avatarVariant` for 2.2's
  admin users list.

### 1.5 Author identity on comments and version history — `S` — Model: Opus — ✅ **shipped 2026-09-09**
- **Not a nice-to-have.** Comments today use `authorId` only to decide whether
  to show *your* edit/delete controls — no name is rendered anywhere, so a
  threaded discussion gives no way to tell who said what. Version history
  shows the version number, the "current" badge and the change comment, but
  not who made it, even though `PageVersion.AuthorId` has been stored since
  Phase 1. Both are visible gaps in shipped features, not new functionality.
- Root cause is the API: `CommentResponse` and `PageVersionResponse` return a
  bare `AuthorId`, so the client has nothing to render.
- Add `AuthorName`, `AuthorAvatarHash` and `AuthorAvatarVariant` to both
  responses, projected from the joined `User` — not a second round trip per
  comment, and not a client-side directory lookup, which would leak the whole
  user list to anyone who can read one page.
- A deleted author (2.2 anonymises rather than removes the row) must render as
  "Deleted user" and the generated avatar, never blank.
- Then render name + `<Avatar size={28}>` in `CommentsPanel` and
  `HistoryPanel`, which completes the render list 1.2 could not finish.
- Slotted here rather than folded into 1.2 because it needs an API change on
  two endpoints in a different feature slice, and because "show author names"
  is a user-visible behaviour change worth its own CHANGELOG entry.

### 1.3 Password recovery — offline (recovery codes) — `M` — Model: Opus — ✅ **shipped 2026-09-09**
- **Generated at registration**, as asked: 8 single-use codes
  (`xxxx-xxxx-xxxx`, crypto RNG), shown **once** on a post-registration
  screen with "download as text" and an "I've saved these" gate.
  **Shipped as SHA-256, not Argon2id:** Argon2's cost is for low-entropy
  secrets, and these are 60 bits of randomness — Argon2 would mean up to
  eight slow verifications per attempt, a free DoS lever. Same construction
  as `ApiToken`, fixed-time comparison included.
- `POST /api/auth/recover/code` → `{ email, code, newPassword }`. Constant
  response whether or not the email exists; mark the used code; rotate
  `SecurityStamp`; audit `user.password_recovered` (no code in metadata).
- Rate-limited. **Shipped keyed on email, not IP:** behind Caddy every
  request shares one address, so an IP limiter would throttle everyone at
  once. 3.2 adds real per-IP limiting once 3.0 lands.
- Regenerate from `/profile` (requires current password; invalidates all
  previous codes; shows the new set once). These same codes serve as 2FA
  backup codes in 3.5 — don't build a second set.
- Existing users have no codes — one-time banner on `/profile`, and admins
  can see who hasn't generated them (2.2).
- **Extended 2026-09-09:** a banner on a settings page nobody opens is not a
  recovery path, so accounts with zero codes are now prompted at sign-in and
  can generate a set in one click. The sign-in *is* the re-authentication, so
  no password is asked for within 15 minutes of it
  (`Auth:FreshLoginMinutes`); outside that window the prompt asks for the
  password in place rather than sending the person elsewhere. A supplied
  password is always verified, fresh session or not.
- **Admin-initiated reset** (`POST /api/admin/users/{id}/reset`) minting a
  one-time, 1-hour link an admin hands over out of band. For a self-hosted
  team this is the recovery path that will actually get used, and it needs
  no email. Depends on 0.1.
- Frontend: "Forgot password?" on the login page → "recovery code" (the
  "email" option appears only once 4.2 exists).

### 1.4 Registration control — `S` — Model: Opus — ✅ **shipped 2026-09-09**
- Depends on 0.2. When `AllowPublicRegistration` is off, `/register`
  returns 403 and the page says registration is by invitation.
- Invites: `POST /api/admin/invites` → single-use link with an expiry that
  bypasses the toggle. The only way a closed instance adds users without
  email.

---

## Phase 2 — Admin panel

### 2.1 Admin shell — `S` — Model: Opus — ✅ **shipped 2026-09-09**
- `/admin` route tree behind the admin role; a nav entry visible only to
  admins (in the topbar's More menu in the middle tier).
- Sections as sub-routes: Dashboard, Users, Spaces, Security (3.3),
  Settings. Groups stay at `/groups` but are linked from here.
- Reuse the existing tab/panel patterns; this is not a new design system.

### 2.2 Users — `M` — Model: Opus — ✅ **shipped 2026-09-09**
- List with search, role, status, `LastSeenAt`, created, last login IP,
  recovery-codes-generated, 2FA-enrolled (3.5).
- Actions: suspend/reactivate (rotate `SecurityStamp` so the session dies
  now), promote/demote admin (refuse to demote the last admin), admin
  reset (1.3), revoke all sessions, revoke all API tokens.
- Deletion: **soft**. Anonymise (email → tombstone, display name →
  "Deleted user", avatar removed) and keep the row — `PageVersion.AuthorId`,
  `Comment.AuthorId` and `AuditLog.ActorId` all reference it.

### 2.3 Settings — `S` — Model: Opus — ✅ **shipped 2026-09-09**
- The `SiteSettings` UI from 0.2: instance name, registration toggle,
  public-spaces kill switch, SMTP block with a **"Send test email"** button
  (live in 4.1; disabled with an explanation until then), TOTP-for-admins.

### 2.4 Spaces — `S` — Model: Opus — ✅ **shipped 2026-09-09**
- All spaces including archived; owner; page count; storage used
  (`SUM(Attachment.Size)` per space); **public or not** (Phase 5);
  archive/unarchive; purge (confirm by typing the key, audit it).

### 2.5 Dashboard — `L` — Model: Opus — ✅ **shipped 2026-09-09**
- Depends on 0.3 having run for a while.
- KPIs, each a stat tile with a sparkline where there's a time series:
  - **People:** total users, active 7d/30d (`LastSeenAt`), new this month,
    admins, suspended, logins per day, failed logins per day (a security
    signal that belongs on the front page).
  - **Content:** pages, versions, comments, attachments; pages created per
    week; storage used, per-space breakdown.
  - **Usage:** page views per day, split anonymous vs signed-in once Phase
    5 ships; most-viewed pages (30d); most active editors (30d).
  - **Health:** DB size, last logical backup, last pgBackRest backup (both
    already surfaced by scripts — see `backup-recovery.md`), open security
    alerts (3.3).
- One aggregate endpoint `GET /api/admin/dashboard?range=30d` computed
  server-side; the page must not fire fifteen queries.
- **Charts:** load the `dataviz` skill before writing any chart code. One
  small library or hand-rolled SVG; the bundle is already 1.1 MB.
- Themed — stat tiles must read in both themes and every accent.

---

## Phase 3 — Security hardening and threat detection

**Sequencing decision #2: this phase precedes public read mode (Phase 5),
and 3.0 precedes everything else in it.** Public mode is what turns a LAN
wiki into an internet target, so the hardening has to exist before the
toggle does. And 3.0 first because without real client IPs, 3.2's rate
limiter would rate-limit Caddy — i.e. everyone — and 3.3's detectors would
see one IP for the whole world. Alerts are delivered in-app here and gain
email in 4.3; do not block this phase on email.

### 3.0 Proxy trust and transport — `S` — Model: Opus — ✅ **shipped 2026-09-09** (run as Fable by user override)
- `UseForwardedHeaders` with `KnownNetworks` set to the compose network (not
  `KnownProxies` by IP — Caddy's container IP is not stable). Verify with a
  test that `RemoteIpAddress` and `Request.Scheme` reflect the client.
- Cookie `SecurePolicy = Always` in production (it is `SameAsRequest`
  today, and the request is never HTTPS as the app sees it).
- Security headers, set in Caddy so they cover the SPA and the API alike:
  HSTS (with a documented warning about `localhost`/LAN installs), `X-Content-
  Type-Options: nosniff`, `Referrer-Policy: strict-origin-when-cross-origin`,
  `Permissions-Policy`, `frame-ancestors 'none'` via CSP.
- **CSP:** the SPA has an inline theme script in `index.html` (added
  2026-09-08 to avoid the dark-mode flash). CSP must allow it by **hash**,
  not `'unsafe-inline'`; compute the hash at build time. Everything else can
  be `'self'` — no CDNs are used. Test the CSP in report-only mode first.
- Caddyfile: a second, documented variant for public hosting with
  `on_demand` **off** — the catch-all is a LAN convenience, not an internet
  feature. `tls-and-lan-access.md` gets a "hosting on the internet" section.
- **Shipped with two deviations.** Headers are set in app middleware, not
  Caddy: the CSP hash has to come from the `index.html` the app serves, and
  in-app headers hold behind any proxy and are testable. HSTS alone stays in
  Caddy (`Caddyfile.public` only). And trust is by private-range networks
  rather than "the compose network" — Docker's subnet is not fixed either;
  the safety argument is that port 8080 is never published.

### 3.1 Least-privilege DB role and tamper-evident audit log — `M` — Model: Fable — ✅ **shipped 2026-09-09**
- Two roles: a migrations/owner role (used only at startup to apply
  migrations) and a runtime `app` role with DML on every table **except**
  `UPDATE`/`DELETE` on `AuditLogs`, `SecurityEvents` and `PageViews`. Two
  connection strings; migrations run on the owner connection, then the app
  switches. `deploy/db` init script creates the roles; document rotation.
- **Hash chain** on `AuditLogs`: `PrevHash`, `Hash = SHA-256(PrevHash ‖
  canonical row)`. A `verify-audit-chain` admin action and CLI script walk
  the chain and report the first broken link. This makes deletion or
  edit *detectable* even by someone with the superuser password; the role
  split makes it *hard*; PITR (already shipped) makes it *recoverable*.
- Stream audit entries to stdout as structured JSON as well, so `docker
  logs` (and anything forwarding it) holds a copy that never touched the
  DB. Cheap, and the "attacker can't scrub logs" requirement is really
  "logs exist in more than one place".
- **Fable, because** the role split touches startup ordering, the test
  harness (SQLite has no roles — the split must be a no-op there), and
  backup/restore (`backup-recovery.md` restores as the superuser; make sure
  it still works). Getting this wrong bricks startup.
- **Shipped with one deviation:** the role is provisioned by the app at
  every startup, not by a `deploy/db` init script — init scripts only run
  on fresh volumes, so every existing install would have stayed on the
  superuser, and re-granting after `Migrate()` covers future tables. Also
  added: the daily monitor reports a chain that got *shorter*, which a
  chain cannot detect on its own; and every stored hash was recomputed
  independently in Python from a `psql` dump to prove the jsonb round trip.

### 3.2 Brute-force protection and rate limiting — `M` — Model: Opus — ✅ **shipped 2026-09-09** (run as Fable by user override)
- Depends on 3.0.
- `Microsoft.AspNetCore.RateLimiting` (built-in, no package): sliding
  window per IP on `/auth/login`, `/auth/register`, `/auth/recover/*`,
  `/api-tokens`; a global, generous per-IP limit on everything for
  anonymous callers (Phase 5 relies on this).
- Per-account failed-login counter with **exponential backoff, capped at a
  temporary lockout** — never permanent, or an attacker can lock any user
  out by trying their email. Same generic 401 whether locked or wrong.
  Successful login resets it. `Retry-After` on 429s.
- Every limiter is a `SiteSettings`-tunable, and the admin Security page
  shows the current counters.
- Tests: N failures → 429/backoff; success resets; two IPs don't share a
  bucket (proves 3.0 is working).
- **Shipped as specified**, plus: the Security page (planned for 3.3)
  exists now with the limits form, active lockouts and the 3.1 verify
  button; 3.3 adds events, alerts and mitigations to it. Recovery and admin
  unlock also clear a lockout.

### 3.3 Threat detection and admin alerting — `L` — Model: Fable → Opus — ✅ **shipped 2026-09-09** (both halves as Fable by user override)
- **Fable designs** the signal set, thresholds, and the alert lifecycle;
  **Opus implements.** The design goes into `architecture.md` first.
- `SecurityEvent` table (kind, severity, actor?, IP, targetId?, metadata,
  `AcknowledgedById?`), written by detectors that run inline on the
  triggering request (cheap checks) or on a short timer (window counts).
- Signals, initial set: failed-login burst per IP; per account; many
  accounts from one IP (credential stuffing); 401/403 spike; mass
  purge/trash (> N pages in M minutes); API-token minting burst; admin
  promotion; **any** public-mode toggle (Phase 5); admin login from an IP
  never seen for that account (needs 0.3's login history); webhook created
  pointing at a private range (3.4 should block it, alert anyway); audit
  chain verification failure (3.1); registration burst when open.
- An event above a threshold becomes an **alert**: one notification to
  every Admin-role user via `INotificationService` (email in 4.3), with a
  link to the Security page. Cooldown per (kind, key) to prevent alert
  storms.
- Security page (admin): event timeline with filters, open alerts, the
  rate-limit counters, and **mitigation actions** — each one click, each
  audited: block IP/CIDR (an in-memory blocklist middleware backed by a
  `BlockedNetworks` table; expiry optional), suspend user, force logout
  (rotate stamp), revoke that user's tokens, **disable all public spaces**
  (the `AllowPublicSpaces` kill switch from 0.2), close registration,
  require TOTP for admins now. Acknowledge/resolve with a note.
- Tests: each detector fires on a synthetic burst and not below threshold;
  cooldown suppresses duplicates; a blocked IP gets 403 before auth runs.
- **Shipped with two deviations.** Events and alerts are separate tables:
  events are append-only (the runtime role cannot touch them) and alerts
  carry the mutable acknowledge/resolve state, so acting on an alert never
  needs an UPDATE on the append-only table. And thresholds are constants in
  `SecurityThresholds`, not settings — see the design in architecture.md
  for why. Rate-limit counters were already on the Security page from 3.2.

### 3.4 Egress and input hardening — `M` — Model: Opus — ✅ **shipped 2026-09-09** (run as Fable by user override)
- **SSRF guard**, one implementation shared by webhooks (today) and link
  previews (7.E): deny loopback, private, link-local and the cloud metadata
  address; resolve DNS *then* connect to the resolved IP (no rebinding);
  cap redirects at 3; 5 s timeout; only `http(s)`. Applied at webhook
  *creation* and at *delivery*.
- Attachments: content-type allowlist on upload (or sniff and override);
  `nosniff` on download (3.0 covers it globally, assert it here too);
  never serve `text/html`, `image/svg+xml` or anything scriptable inline —
  force `application/octet-stream` for those.
- Antiforgery: keep SameSite=Lax and add a double-submit token for
  cookie-authenticated **state-changing** requests; API-token requests are
  exempt (no cookie, no CSRF). The upload endpoint's `DisableAntiforgery()`
  goes away.
- Request size limits confirmed at both Caddy (100 MB) and Kestrel.
- **Shipped with one deviation:** CSRF is a required custom header
  (`X-Requested-With: Tesria`) on cookie-authenticated state changes, not
  a double-submit token — equivalent protection, no token plumbing, and
  it covers the multipart uploads. The framework's `DisableAntiforgery()`
  stays on the two `IFormFile` endpoints because the framework attaches
  its own form-token requirement to them by default.

### 3.5 Sessions, 2FA and admin safety — `M` — Model: Opus — ✅ **shipped 2026-09-09** (run as Fable by user override)
- Idle timeout (e.g. 14 days) and an absolute lifetime (e.g. 90 days) on
  top of `SecurityStamp`; a "sessions" list on `/profile` with revoke.
- **TOTP 2FA** (`Otp.NET`): opt-in per user, enforceable for admins via
  `RequireTotpForAdmins`. Enrol with QR + the 1.3 recovery codes as backup
  codes. Verify at login; rotate stamp on enrol/disable.
- **Sudo mode:** destructive admin actions (purge space, demote admin,
  disable public spaces, DB role rotation) re-prompt for password (or TOTP)
  within a 5-minute window.
- Pin Argon2id parameters explicitly (time, memory, lanes) and document
  them; add a rehash-on-login path so parameters can be raised later.
- **Shipped as specified.** Sudo covers admin role changes (both
  directions), the public-spaces switch, page purge and blocklist removal;
  "DB role rotation" is not an endpoint (it is `.env` + restart). A wrong
  TOTP code or re-auth answer counts toward the 3.2 lockout.

### 3.6 Dependency hygiene — `S` — Model: Opus — ✅ **shipped 2026-09-09** (run as Fable by user override)
- Fix the 38 npm findings (`react-router` upgrade first — check the 7.x
  changelog for breaking changes to `NavLink`/`useLocation`, both used).
- Add `collab/package-lock.json` and make the collab Dockerfile use
  `npm ci`.
- A `scripts/audit.sh` running `npm audit --omit=dev` (web and collab) and
  `dotnet list package --vulnerable --include-transitive`; document it as
  a release gate. Renovate or Dependabot config if the repo goes public.
- **Shipped as specified.** The 38 findings were react-router (7.18.1 →
  7.18.3, patch-level, no API change) and every `@tiptap/*` package
  (3.28.0 → 3.31.3; the packages peer-depend on each other at exact
  versions, so they have to move together — `npm audit fix` alone cannot
  do it). Editor verified live after the upgrade.

### 3.7 Security review and internet-readiness gate — `M` — Model: Fable — ✅ **shipped 2026-09-09**
- Write `docs/security.md`: threat model (who attacks a public wiki and
  why), what each item above defends, what it does not, and the operator's
  **internet-readiness checklist** — the thing Phase 5's toggle links to.
- Run the `security-review` skill against the phase's branch and fix what
  it finds before merging.
- Decide and document the disclosure/contact path (a `SECURITY.md`) if the
  repo goes public.
- **Shipped with one deviation:** no `security-review` skill exists in
  this environment, so the review was a manual pass — every registered
  route inventoried for authorization, the credential endpoints for rate
  limiting, the OIDC return URL, token listing, and settings responses.
  Its surviving findings are `security.md`'s "Known gaps" list.

---

## Phase 4 — Email

After the admin panel because SMTP configuration lives in 2.3; after
security because 4.2 needs 3.2's limiter and 4.3 needs 3.3's alerts.

### 4.1 Sender + SMTP — `S` — Model: Opus — ✅ **shipped 2026-09-09** (run as Fable by user override)
- `IEmailSender`; `SmtpEmailSender` via MailKit; `NullEmailSender` when
  `EmailEnabled` is false. Plain text plus a minimal HTML wrapper; no
  template engine. "Send test email" in 2.3 goes live. Delivery failures
  → `email.failed` audit entry (no body in metadata).
- **Shipped with one deviation:** one sender class, not two — with
  `EmailEnabled` off (or settings incomplete) `SmtpEmailSender` declines
  and says why, so there is a single code path. Also added: an
  `EmailEnabled` toggle and "Send test email to me" on the Settings page
  (neither existed), and a `BaseUrl` setting (default `https://$DOMAIN`)
  so emailed links know the instance's address.

### 4.2 Password recovery — email — `M` — Model: Opus — ✅ **shipped 2026-09-09** (run as Fable by user override)
- `POST /api/auth/recover/email` → always 202, same body either way.
  Token: 32 random bytes, stored hashed, 1-hour expiry, single-use,
  invalidated by a newer request. `/reset?token=…` → new password → rotate
  stamp. The login page's "Forgot password?" now offers both paths; the
  email option only appears when `EmailEnabled`.
- **Shipped as specified.** The token is 1.3's admin reset token with a
  nullable issuer, so both links share one redemption path; the email
  path is throttled per address by 1.3's limiter and per client address
  by 3.2's.

### 4.3 Email delivery for alerts and notifications — `M` — Model: Opus — ✅ **shipped 2026-09-09** (run as Fable by user override)
- Security alerts (3.3) go to admins by email as well as in-app — this is
  the "email the admin group" requirement, and it is deliberately the
  *first* email notification wired up.
- Then user notifications for those who opt in (`/profile` → preferences).
  Immediate for mentions and alerts; daily digest for watches by default.
- **Shipped as an outbox.** `Notification.EmailedAt` marks what has been
  sent; `NotificationEmailService` polls every minute. Security alerts to
  administrators always go immediately; each person chooses Off (default —
  opt-in, as the plan says), Immediate, or Daily digest. Mentions do not
  exist until Phase 7, so "immediate for mentions" waits for them. Nothing
  older than a day is ever emailed, so turning email on does not flood
  inboxes with history.

---

## Phase 5 — Public read mode (anonymous access per space)

**Sequencing decision #3: this is gated on Phase 3, in code, not just in
this document.** The per-space toggle is only offered when the instance-wide
`AllowPublicSpaces` switch is on, and the settings page shows the
internet-readiness checklist (3.7) next to that switch. Someone can still
flip both on a LAN box — but they will have read what they are skipping.

The use case is exactly the one asked for: someone builds a game wiki and
hosts it for everyone. It pairs with wiki packs (8.5) — build it, export
it, and others can host their own copy publicly too.

### 5.1 Permission model for anonymous readers — `M` — Model: Fable — ✅ **shipped 2026-09-09**
- `Space.IsPublic`, `Space.PublicSince`, `Space.PublicComments` (default
  off). Toggling is a **site-admin** action (exposing content to the
  internet is an instance-level risk, not a space-owner one), audited as
  `space.published`/`space.unpublished`, and always raises a 3.3 event.
- An **anonymous principal** in `IPermissionService`: can view a page iff
  its space is public **and** the page carries no restriction **and** the
  page is `Current` (never drafts, never trash). `ViewableSpaceIdsAsync`
  for anonymous = public spaces only. Restricted pages inside a public
  space are invisible — 404, never 403, per the masking rule that already
  exists for authenticated users.
- **The leak matrix** — this is why it is a Fable item. Every read endpoint
  × anonymous × {public space, private space, restricted page in public
  space, draft, trashed page, attachment of restricted page, search hit,
  label listing, export, version history, tree}. Write it as a test class
  before the endpoints are opened; the endpoints are opened only until the
  matrix is green.
- Decisions to write down: version history stays authenticated (edit
  history of a public page can leak withdrawn content — recommend closed);
  comments hidden unless `PublicComments`; collab tokens, watches, drafts,
  the user directory, groups, and labels-across-spaces stay authenticated.
- **Shipped with one tightening:** anonymous readers are hidden from by
  *any* restriction in a page's ancestry (View or Edit), not only View —
  "not for everyone" now includes the internet. The matrix is
  `PublicReadTests` (nine tests over the full grid) and was written first.

### 5.2 Server: opening the read endpoints — `M` — Model: Opus — ✅ **shipped 2026-09-09** (run as Fable by user override)
- Replace `RequireAuthorization()` on read routes with a policy that admits
  anonymous callers and lets the permission service decide (5.1). Write
  routes stay `RequireAuthorization()`; anonymous gets 401 there.
- Endpoints admitted: space by key, page tree (filtered), page, page labels,
  attachments **download** (permission-checked through the page), search
  (scoped to public spaces), export (Markdown/HTML — "take your docs with
  you" should hold for readers too).
- Anonymous requests: `Cache-Control: public, max-age=60` plus an ETag on
  page GETs (unpublishing a space must take effect within that window;
  60 s is acceptable, document it); the 3.2 anonymous limiter applies;
  `PageView` rows with `UserId = null`.
- `sitemap.xml` and `robots.txt` for public spaces (allow `/spaces/{key}`
  for public keys, disallow `/api`). Serve basic `<title>`, description and
  Open Graph tags for public page URLs by injecting them into `index.html`
  at request time — the API already serves the SPA shell, so this is a
  small middleware, not SSR. Full SSR is out of scope; note it as a later
  option if search indexing matters.
- **Shipped as specified.** The meta middleware decides through the
  anonymous check whoever is asking, so a private title never reaches a
  link preview even for a signed-in admin's request.

### 5.3 SPA: a read-only public experience — `M` — Model: Opus — ✅ **shipped 2026-09-09** (run as Fable by user override)
- Route split: public space routes render outside `ProtectedRoute`;
  `AuthContext` already models `user === null`, so components branch on it
  rather than assuming a session.
- Anonymous chrome: brand, search (scoped), theme menu (client-side, works
  without a session), and a **Sign in** button where the username was. No
  edit/new/comment/watch controls; the page action bar collapses to
  Export and Full width.
- Spaces index for anonymous visitors lists public spaces only.
- A visible "Public" badge on public spaces for signed-in users, so nobody
  edits a public page thinking it's internal.

### 5.4 Operator controls — `S` — Model: Opus — ✅ **shipped 2026-09-09** (run as Fable by user override)
- The 2.4 Spaces admin page: public column; toggle with a confirmation
  that names what becomes visible (page count, attachment count).
- The 2.3 kill switch and the 3.3 "disable all public spaces" mitigation
  both set `AllowPublicSpaces = false`; the per-space flags are preserved
  so re-enabling restores the previous state.

---

## Phase 6 — Space icons — `S` — Model: Opus — ✅ **shipped 2026-09-10**

- Depends on 0.4 and reuses the 1.2 upload/crop pipeline unchanged.
- `Space.IconKind` (`None | Emoji | Image`), `IconValue`, `IconColor`.
  Default when unset: the key's first letter on an accent tile — same trick
  as avatars, so every space has an icon from day one.
- Render in space cards, sidebar head, breadcrumb, the public-space listing
  (5.3), and the wiki-pack manifest (8.5). Edit from the space's settings.
- **Shipped with one addition:** there was no space settings page at all —
  `PUT /api/spaces/{key}` had existed since Phase 2 with nothing in the UI
  reaching it — so `/spaces/{key}/settings` now carries the icon picker and
  the name/description form that endpoint was always waiting for.
- The emoji rule is deliberately about *shape*, not a list: short, no
  control characters, at least one non-ASCII character. A list of valid
  emoji goes stale with every Unicode release; this admits future ones and
  keycaps (which really do contain an ASCII digit) and refuses prose and
  markup. Pictures cannot be set through the JSON update — only through the
  upload route, which has the bytes and re-encodes them.

---

## Phase 7 — Editor parity with Confluence (the audit)

> **Scope added 2026-09-10 (done first):** editor chrome to match
> Confluence — a borderless, continuous page and title; a single-row,
> edge-to-edge toolbar; block elements behind a **+ Insert** menu that
> shares the slash catalogue; measured overflow into that menu instead of
> wrapping. See the CHANGELOG entry.

The catalogue below is from Atlassian's own Confluence Cloud documentation
("Add elements to a page", the macro index, the formatting guide and the
layouts doc), checked against `extensions.ts`. Each wave is independently
shippable; the order is by value-per-effort and by what later waves build on.

**Wave D (dynamic blocks) gets one generic mechanism, built once.** Children
display, Recently updated, Content by label, Excerpt include, Include page,
Attachments list, Contributors and Page properties report are all "a block
whose content comes from a query at render time". Build one `dynamicBlock`
node (`kind` + `params`) with one fetching node view and one server-side
snapshot for export, then add kinds. Eight separate node types would be
eight times the schema, renderer and export work.

### Element audit

| Confluence element | Have? | Wave |
|---|---|---|
| Headings, lists, bold/italic/underline/strike, code, quote, divider, tables, images, code snippet, panel, action items (task list), highlight, alignment | ✅ | — |
| **Table of contents** | ❌ | A |
| **Expand** (collapsible section) | ❌ | A |
| **Status** lozenge | ❌ | A |
| **Date** | ❌ | A |
| **Decision** | ❌ | A |
| **Anchor** / link to heading | ❌ | A |
| **Layouts** (columns) | ❌ | A |
| Text colour | ❌ | B |
| Subscript / superscript | ❌ | B |
| Indent / outdent | ❌ | B |
| Clear formatting | ❌ | B |
| Keyboard shortcuts (audit against Confluence's set) | partial | B |
| Markdown-style autoformat (`[ ]`, `//` date, `#` headings…) | partial | B |
| **Mention** (@user, notifies) | ❌ | C |
| **Emoji** picker | ❌ | C |
| Action item **assignee** | ❌ | C |
| Children display | ❌ | D |
| Recently updated | ❌ | D |
| Content by label | ❌ | D |
| Excerpt + Excerpt include | ❌ | D |
| Include page | ❌ | D |
| Attachments list | ❌ | D |
| Contributors / contributors summary | ❌ | D |
| Page properties + report | ❌ | D |
| Labels list / popular / related labels | ❌ | D |
| Task report | ❌ | D |
| Page tree / page index | ❌ | D |
| Change history | ❌ | D (cheap: versions already exist) |
| Smart links (inline / card / embed) | ❌ | E |
| iFrame / embed (allowlisted) | ❌ | E |
| Widget connector (YouTube etc.) | ❌ | E (same mechanism as embed) |
| Video / file attachment block | ❌ | E |
| PDF viewer | ❌ | E |
| Image gallery | ❌ | E |
| **Mermaid** diagrams (roadmap) | ❌ | F |
| Math (LaTeX) | ❌ | F |
| Chart (from a table) | ❌ | F |
| Tabs / synced blocks | ❌ | later — Tabs is app-provided in Confluence; synced blocks overlap Excerpt include |
| Jira macros, Office/OneDrive, Marketplace | n/a | not applicable to a self-hosted wiki |
| Blog posts (per-space blog) | ❌ | **not an editor element — a content type.** Decide separately; listed so it isn't forgotten. |
| Live search, User list, Profile picture, Spaces list, Create-from-template, Network | ❌ | low value; revisit after D |

### Wave A — structural blocks (frontend + renderer) — `L` — Model: Opus — ✅ **shipped 2026-09-10** (started as Fable by user override, finished as Opus)
- **Anchor first**: stable `id` attr on headings (slugified, de-duplicated);
  the link popover gets a "link to heading" list. TOC depends on it.
- **Table of contents**: a node with no stored content; the node view
  computes from headings live; export renders a real nested list of links.
- **Expand**: `expand` node, `title` attr, `block+` content, open state not
  persisted. Export: `<details>`; Markdown: bold title + indented body.
- **Status**: inline atom, `text` + `color` (grey/red/yellow/green/blue/
  purple — Confluence's set, mapped to the palette). Export: styled span.
- **Date**: inline atom, ISO `date` attr, rendered in the viewer's locale.
- **Decision**: block like panel with a fixed icon; Markdown `**Decision:**`.
- **Layouts**: `layoutSection` containing 2–3 `layoutColumn` nodes; presets
  two equal, three equal, left sidebar, right sidebar, three with sidebars.
  Not nestable (Confluence doesn't), but sections stack. Each section
  carries a width — centred / wide / full — mapped onto the existing
  page-level full-width mechanism (`.page-wrap--full`, the `--page-pad`
  breakout); reuse it per section. **Full-width tables inside a column must
  not break out** — scope the breakout rule to direct children of the
  content root. Export HTML: flex row; Markdown: columns in order.
- Slash-menu and toolbar entries for each, sharing `PANEL_TYPES`-style
  constants so the two can't drift.

### Wave B — formatting marks and input rules — `M` — Model: Opus — ✅ **shipped 2026-09-10**
- Text colour mark (palette-limited, reusing `ColorPalette` and the
  dark-mode ink-pinning approach). Subscript/superscript. Paragraph indent
  (`textIndent` attr, capped at ~4). Clear formatting. Shortcut audit
  against Confluence's list; verify StarterKit's `**`, `__`, `` ` `` rules.

### Wave C — people — `M` — Model: Opus
- **Mention**: `@` suggestion on the same `@tiptap/suggestion` primitive
  the slash menu uses; `mention` inline node with `userId`; on save, diff
  mentions and notify new ones (`user.mentioned`). Renders with the avatar.
- **Emoji**: `:` suggestion over a bundled list; stored as the literal
  character, nothing new in the schema or renderer.
- **Action item assignee**: `assigneeId` on `taskItem`; Task report (D)
  queries it.

### Wave D — dynamic blocks — `L` — Model: Fable → Opus
- **Fable designs** the `dynamicBlock` contract (attrs, the per-kind
  endpoint shape, export snapshotting, permission filtering) and writes it
  into `architecture.md`; **Opus adds kinds** against it.
- Server: one `GET /api/pages/{id}/blocks/{kind}?params` per kind,
  permission-filtered (a Children display must not leak restricted pages —
  reuse the search endpoint's two-pass filtering; and for public spaces,
  5.1's anonymous rules). Export: the renderer calls the same service to
  snapshot the block as static HTML/Markdown at export time.
- Kinds, in order: Children, Recently updated, Content by label,
  Attachments, Change history, Contributors, Excerpt/Excerpt include,
  Include page, Page properties (+ report), Labels lists, Task report,
  Page tree.

### Wave E — media and embeds — `M` — Model: Opus
- Embed node with a **server-enforced allowlist** of hosts (editable in
  admin settings). Never render an arbitrary iframe.
- Smart links: server-side Open Graph fetch through the 3.4 SSRF guard,
  cached. Inline / card display modes.
- Video and generic file blocks on existing attachments; PDF via the
  browser's viewer; gallery as a layout over image nodes.

### Wave F — technical content — `M` — Model: Opus
- **Mermaid** (from `roadmap.md`): a `mermaid` code-block language rendered
  by a node view; HTML export ships the source plus a client-side render
  script; Markdown export is a fenced block.
- Math via KaTeX (inline + block). Chart from a table's data — after D, a
  block can point at a table by id.

---

## Phase 8 — Brand page: claims and roadmap

The page at brianintheloop.com/tesria was audited against the product.
Every present-tense claim holds **except one**; the four roadmap items map
onto `roadmap.md` and are sequenced here.

### 8.1 PDF export — the one claim that isn't true yet — `M` — Model: Opus
- The page says "export any page as Markdown or a PDF". The product exports
  Markdown or **print-ready HTML**. Recommend a Playwright sidecar (the stack
  already has one Node sidecar, so the pattern exists) rendering the HTML
  export to PDF on request; keep the HTML export too. **After Wave A**,
  since layouts and TOC change what "print-ready" means.

### 8.2 Licence — `S` — Model: Opus
- The page says Apache 2.0 and open source. **The repo has no `LICENSE`
  file** (verified 2026-09-08), and `CLAUDE.md` says it is private pending
  an audit — which is now Phase 3.7. Add the Apache 2.0 text as `LICENSE`
  and a `NOTICE`; SPDX identifiers in `package.json` and the `.csproj`.
  Public visibility is the user's call — but the licence file should exist
  before it flips, not after.

### 8.3 API: OpenAPI + documentation — `M` — Model: Opus
- The REST API and webhooks exist and are documented in the **API space**
  (23 pages, created 2026-09-08). Missing: a machine-readable spec. Add
  `Microsoft.AspNetCore.OpenApi` at `/api/openapi.json` with the endpoint
  records as schemas; Scalar UI at `/api/docs`. **Before MCP** — tools are
  easiest to define from the spec. Keep the API space in step.

### 8.4 MCP server — `L` — Model: Fable → Opus
- **Fable designs** the tool surface and the auth model; **Opus implements.**
  Load the `claude-api` skill before designing tool definitions.
- A separate process (Node sidecar, like collab) or a .NET endpoint speaking
  MCP over HTTP; authenticates with an API token. Tools: search, get page,
  list space tree, get labels, create/update page — writes opt-in per
  token, **which is where token scopes finally land**: add a `ReadOnly`
  flag to `ApiToken` (it has no scopes today; 3.3's "revoke tokens" also
  benefits).
- Depends on 8.3 and benefits from Phase 7 stabilising the content schema.

### 8.5 Wiki packs — space/site export and import — `XL` — Model: Fable → Opus
- **Fable designs the format** (and the import's id-remapping rules);
  **Opus implements** export then import. The hard half is import: id
  remapping, attachment re-keying, permission principals that don't exist
  on the target, tree order, labels, templates, the space icon (6), and
  whether a pack carries `IsPublic` (recommend: no — the importer decides).
- Format: a zip with `manifest.json` (format version, source instance,
  counts), one JSON per page including versions, attachments as files,
  `spaces.json`. Version the format from day one.
- **Last, on purpose:** every earlier phase adds fields the pack must
  carry. Building it earlier means rebuilding it.

---

## Order of execution, flattened

1. **0.1** Roles (Fable→Opus) → **0.2** Settings → **0.3** Telemetry → **0.4** Media storage
2. **1.1** Profile → **1.2** Avatars → **1.3** Recovery codes + admin reset → **1.4** Registration control → **1.5** Author identity
3. **2.1** Admin shell → **2.2** Users → **2.3** Settings UI → **2.4** Spaces → **2.5** Dashboard
4. **3.0** Proxy trust & headers → **3.1** DB role + audit chain (Fable) → **3.2** Rate limiting → **3.3** Detection & alerts (Fable→Opus) → **3.4** Egress/input → **3.5** Sessions & 2FA → **3.6** Dependencies → **3.7** Review & gate (Fable)
5. **4.1** SMTP → **4.2** Email recovery → **4.3** Alert & notification email
6. **5.1** Anonymous permission model + leak matrix (Fable) → **5.2** Server → **5.3** SPA → **5.4** Operator controls
7. **6** Space icons
8. **7.A** → **7.B** → **7.C** → **7.D** (Fable→Opus) → **7.E** → **7.F**
9. **8.1** PDF (after 7.A) → **8.2** Licence (any time) → **8.3** OpenAPI → **8.4** MCP (Fable→Opus) → **8.5** Wiki packs (Fable→Opus)

Phases 6 and 8.2 are floaters — small, no dependents — and can fill gaps.
3.6 (dependency fixes) can also be pulled forward at any time; the npm
findings don't get better by waiting.

## Things this plan deliberately does not decide

- Whether admins bypass restrictions (0.1) — recommended yes; a Fable call.
- Whether to log search queries (2.5) — a privacy trade-off.
- Whether public pages expose version history (5.1) — recommended no.
- Blog posts as a content type (Phase 7 table) — a product decision.
- Public repo visibility (8.2).
- Immediate vs. digest for email notifications (4.3).
