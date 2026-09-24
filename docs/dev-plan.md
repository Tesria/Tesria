# Development plan: sequenced (2026-09-08)

The next body of work, ordered by dependency rather than by the order it was
asked for. Written by Fable 5.1; **each item names the model that should
execute it** (see "Model gate": read that section before starting
anything). Every item is grounded in the code as it stands today (see "What
exists"), not in assumptions.

Distinct from [`PLAN.md`](../PLAN.md) (the founding design, complete) and
[`roadmap.md`](./roadmap.md) (unscheduled ideas: the ones from there that
are now scheduled are pulled in here). Move items to the
[`CHANGELOG`](./CHANGELOG.md) as they ship.

## Model gate: read this first

**Since 2026-09-22, Opus 5.5 designs and implements every item.** The owner
switched on trial after Anthropic's launch page reported Opus 5.5 at Fable
5.1's level on most work, for less. New items are tagged
**`Model: Opus 5.5`**. Tags on earlier items are kept as history:

- **`Model: Opus`**: well-specified implementation, executed by Opus 5.
- **`Model: Fable`**: a design or security-model decision, executed by Fable 5.1.
- **`Model: Fable → Opus`**: Fable wrote the spec, then Opus implemented it,
  in two sittings with the written spec as the handoff.

**Before starting any item, check you are running as Opus 5.5** (the system
prompt states it: "You are powered by the model named …"). **If not, STOP
before any tool call that does work.** Tell the user the plan expects Opus
5.5, then offer exactly two options: switch models, or override for this
item. Do not proceed until they answer. If they override, record it in that
item's CHANGELOG entry ("executed by X, plan asked for Opus 5.5, user
override"). This rule also lives in `CLAUDE.md`, so it applies whether or
not a session has read this file.

Fable stays available as an optional second opinion. A design names its
security-model and hard-to-reverse decisions so the owner can ask for one.
Because this is a trial, anything an Opus 5.5 design missed that surfaces
later is recorded in that item's CHANGELOG entry.

## How to read this

Phases are ordered so that nothing needs something from a later phase.
Within a phase, items are ordered too. Sizes are relative (S / M / L / XL),
not estimates. **Three sequencing decisions matter most and are called out in
bold**: read those even if you skim the rest.

Conventions that apply to every item, from `CLAUDE.md`:

- Backend: `dotnet test` green (SQLite in-memory, no Docker); migrations via
  `dotnet ef migrations add <Name> --output-dir Infrastructure/Migrations`
  from `src/Api/`; they run automatically on API startup.
- Frontend: `npm run build && npm run lint` green.
- **Docker is the source of truth for manual verification**: rebuild `app`
  and check it live.
- Editor schema changes (any new node/mark) go in
  `src/web/src/editor/extensions.ts` only, never inline in an editor
  component: Yjs requires one shared schema. **Every new node/mark also
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
| Suspension | `UserStatus.Suspended` exists in the enum and login honors it, **but nothing in the codebase can set it.** |
| Site settings | No table, no concept. Config is env/appsettings only. |
| Email | **Nothing.** No SMTP, no sender abstraction, no MailKit package. |
| Password reset | None, of any kind. |
| Profile editing | No endpoint, no page. |
| Space model | `Key`, `Name`, `Description`, `Homepage`, `Archived`. No icon, no public flag. |
| Anonymous access | **None.** Every `/api` route except `/health` is `RequireAuthorization()`; the SPA's `ProtectedRoute` redirects to `/login`. |
| Storage | `IAttachmentStorage` (key-based, local disk, S3 slot reserved): reusable for avatars/icons. |
| Telemetry | **None.** No page views, no login events, no last-seen. `AuditLogs` records mutations only. |
| KPI-able data | `Users.CreatedAt`, `Pages/PageVersions/Comments.CreatedAt`, `Attachment.Size` + `ContentType`, `AuditLogs.Action/CreatedAt`. Enough for *content* growth, not for *usage*. |
| Export | Markdown and print-ready HTML. **Not PDF** (the brand page says PDF: see Phase 8). |
| Editor nodes | paragraph, heading, bullet/ordered list, taskList, blockquote, codeBlock (lowlight), horizontalRule, image, table (+cell colors), panel. Marks: bold, italic, underline, strike, code, link, highlight (colors), comment. textAlign on heading/paragraph. |
| Notifications | In-app only, via `INotificationService`; the natural hook for email and for admin alerts. |
| License | **No `LICENSE` or `NOTICE` file in the repo.** The brand page claims Apache 2.0: see 8.2. |

### Security baseline (audited 2026-09-08, the findings Phase 3 exists to fix)

| Finding | Evidence | Severity for internet exposure |
|---|---|---|
| App never sees HTTPS; no forwarded-headers handling | Caddy → `http://+:8080`; no `UseForwardedHeaders` in `Program.cs` | **High.** `SecurePolicy = SameAsRequest` means the session cookie is set without `Secure`; and every request's `RemoteIpAddress` is Caddy's, so any per-IP rate limit would lock out *all* users at once. |
| No rate limiting, no lockout, no failed-login tracking | grep for `RateLimit`, `Lockout`, `RemoteIpAddress`: nothing | **High.** Unlimited online brute force against `/api/auth/login`. |
| App runs as the Postgres **superuser** | `ConnectionStrings__Default` uses `POSTGRES_USER`, which is the instance superuser | **High.** App compromise = full DB, including deleting audit rows. |
| Audit log is append-only in *application code* only | no `AuditLogs.Remove/Update` in code; nothing at the DB layer | Medium alone; **High** combined with the superuser role. |
| No security headers anywhere | nothing in `Program.cs` or `deploy/Caddyfile` | Medium. No HSTS, CSP, `nosniff`, frame-ancestors. |
| No CSRF token; SameSite=Lax is the only defense | no `AddAntiforgery`; one `DisableAntiforgery()` on upload | Low–Medium. Lax blocks cross-site POST from top-level navigations, not everything. |
| Webhooks can target any URL | no private-range check in `Infrastructure/Webhooks` | Medium. SSRF: an editor can make the server hit `169.254.169.254`, the DB, the collab sidecar. |
| Attachment content type trusted from the client and echoed on download | `ContentType = file.ContentType` → `Results.File(stream, a.ContentType, …)` | Low–Medium. `Content-Disposition: attachment` is set (good); no `nosniff`, no allowlist. |
| Caddy on-demand TLS catch-all on `:443` | `tls internal { on_demand }` | Medium on the internet: anyone can trigger cert minting for arbitrary SNI. A LAN feature, wrong for public hosting. |
| **38 npm vulnerabilities (2 high)** in `src/web` | `npm audit --omit=dev`; `react-router-dom` among them | Medium. Fixable. |
| Collab sidecar has **no lockfile** | `npm audit` → `ENOLOCK` | Medium: unauditable and unreproducible builds. |
| .NET packages | `dotnet list package --vulnerable --include-transitive`: clean |: |
| Argon2id via library defaults | `Argon2.Hash(password, HybridAddressing)`, no explicit params | Low. Defaults are sane; document them and pin them. |
| 30-day sliding cookie, no server-side revocation | `ExpireTimeSpan = 30d, SlidingExpiration` | Medium. Fixed by `SecurityStamp` in 1.1. |

---

## Phase 0: Foundations (everything below depends on these)

**Sequencing decision #1: telemetry goes first, not last.** The dashboard
(2.5) is the last thing built, but its *usage* KPIs are only as good as the
data accumulated by then. Adding the recording now is cheap and means the
dashboard ships with weeks of real numbers instead of an empty chart. The
security phase (3.3) also needs it: "admin logged in from a new IP" is
undetectable without a login history.

### 0.1 Roles · `M` · Model: Fable → Opus · ✅ **shipped 2026-09-08**
- Add `Role` to `User` (`Member = 0, Admin = 1`), an enum, not a bool, so
  a future `Viewer`/`Moderator` is a value, not a migration of a bool.
- **The first registered account becomes Admin** (`Users.CountAsync() == 0`
  inside the registration transaction). Document it in the README.
- `RequireAdmin` authorization policy; `CurrentUser.IsAdmin`.
- **Fable decided (2026-09-08): no silent bypass.** Admins see what their
  grants allow, plus an audited `recover-access` action that writes them an
  explicit space-admin grant: reusing the existing "explicit space admins
  bypass page restrictions" rule rather than adding a second code path.
  Full spec, including the first-user race handling, the migration
  backfill and the required tests: `architecture.md` → "Roles and
  administrators". **Fable half complete; Opus implements against it.**
- Tests: first-user-is-admin; non-admin gets 403 on an admin route; the
  existing tests keep passing (several register two users: check that
  "first user is admin" doesn't change their expectations).

### 0.2 Site settings · `S` · Model: Opus · ✅ **shipped 2026-09-09**
- `SiteSettings` table, single row, typed columns (not key/value, typed
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

### 0.3 Usage telemetry · `M` · Model: Opus · ✅ **shipped 2026-09-09**
- `User.LastSeenAt` (bumped at most once per N minutes per request, via the
  `CurrentUser` accessor, to avoid a write per request).
- `user.login` audit action with actor, timestamp **and client IP** (needs
  3.0 for the IP to be real: record it anyway; it becomes correct the
  moment forwarded headers land). **Confirmed 2026-09-09 on the running
  stack: three requests from two clients all logged `172.18.0.7`, Caddy's
  container address. 3.0 is now a prerequisite in fact, not just on paper.** Failed logins: `user.login_failed` with
  IP and a count, never the attempted email in metadata.
- Page views: a `PageView` table (`PageId, UserId?, ViewedAt`): `UserId`
  nullable from day one, because Phase 5 will write anonymous views.
  Written on `GET /pages/{id}` for browser sessions, not API tokens (tokens
  are scripts and would swamp the numbers). Index on `(PageId, ViewedAt)`.
- Nothing user-facing yet. This exists so 2.5 and 3.3 have data.

### 0.4 Profile media storage · `S` · Model: Opus · ✅ **shipped 2026-09-09**
- Reuse `IAttachmentStorage` with a distinct key namespace (`avatars/…`,
  `space-icons/…`) rather than a second storage abstraction.
- `GET /api/media/avatars/{userId}` and `/space-icons/{spaceId}`: long
  cache headers plus a content-hash query string for cache busting.
- Server-side validation: content-type allowlist (png/jpeg/webp), size cap
  (~1 MB), re-encode raster uploads to a fixed 256px square so the stored
  file is never the raw upload. **Reject SVG** for avatars outright: it can
  carry script, and there is no need for it here.
- **Shipped with the avatar upload/delete endpoints as well**, since the
  validation above has no other home. 1.2 is therefore the UI, the
  client-side crop and the generated defaults, not the server half.

---

## Phase 1: Accounts and identity (user-facing)

### 1.1 Edit profile · `M` · Model: Opus · ✅ **shipped 2026-09-09**
- `PUT /api/auth/me` (display name); `PUT /api/auth/me/email` (requires
  current password; lower-cased, uniqueness check, 409 on collision);
  `PUT /api/auth/me/password` (current + new; **invalidates other
  sessions**, see below).
- OIDC-provisioned users (`PasswordHash == null`) can change display name
  and avatar but not email/password: the IdP owns those. Render those
  fields read-only with a note, don't just 400.
- **`SecurityStamp` on `User`**, embedded as a claim at sign-in and checked
  in `OnValidatePrincipal`; changing the password rotates it. Suspension
  (2.2), force-logout (3.3) and 2FA enrollment (3.5) all reuse this: build
  it here once.
- Frontend: `/profile` route, reachable from the username in the topbar.

### 1.2 Avatars · `M` · Model: Opus · ✅ **shipped 2026-09-09**
- Depends on 0.4.
- **Prebuilt set:** generate SVG avatars deterministically from the user
  id: initials on one of twelve backgrounds. (Shipped with its own palette
  in `avatarIdentity.ts` rather than the editor's `palette.ts`: those are
  light tints meant to sit *behind* dark body text, which is the opposite of
  what a colored avatar with white initials needs.) The deterministic one is
  the default, so every user has an avatar from day one with zero storage.
- **Upload:** client-side square crop (a small canvas crop, no library); the
  server endpoint and re-encode already shipped in 0.4.
- Render in: topbar, comments, version history, the user directory/picker,
  and later mentions (7.C). One `<Avatar>` component, sizes 20/28/40.
  **Shipped in the topbar and profile only.** Comments and version history
  return just an `AuthorId` and render no author identity at all today, so
  avatars there need names added first: **scheduled as 1.5 below.** The people picker is a `<select>`, whose options cannot hold markup;
  `GET /api/users` already carries `avatarHash`/`avatarVariant` for 2.2's
  admin users list.

### 1.5 Author identity on comments and version history · `S` · Model: Opus · ✅ **shipped 2026-09-09**
- **Not a nice-to-have.** Comments today use `authorId` only to decide whether
  to show *your* edit/delete controls, no name is rendered anywhere, so a
  threaded discussion gives no way to tell who said what. Version history
  shows the version number, the "current" badge and the change comment, but
  not who made it, even though `PageVersion.AuthorId` has been stored since
  Phase 1. Both are visible gaps in shipped features, not new functionality.
- Root cause is the API: `CommentResponse` and `PageVersionResponse` return a
  bare `AuthorId`, so the client has nothing to render.
- Add `AuthorName`, `AuthorAvatarHash` and `AuthorAvatarVariant` to both
  responses, projected from the joined `User`, not a second round trip per
  comment, and not a client-side directory lookup, which would leak the whole
  user list to anyone who can read one page.
- A deleted author (2.2 anonymises rather than removes the row) must render as
  "Deleted user" and the generated avatar, never blank.
- Then render name + `<Avatar size={28}>` in `CommentsPanel` and
  `HistoryPanel`, which completes the render list 1.2 could not finish.
- Slotted here rather than folded into 1.2 because it needs an API change on
  two endpoints in a different feature slice, and because "show author names"
  is a user-visible behavior change worth its own CHANGELOG entry.

### 1.3 Password recovery: offline (recovery codes) · `M` · Model: Opus · ✅ **shipped 2026-09-09**
- **Generated at registration**, as asked: 8 single-use codes
  (`xxxx-xxxx-xxxx`, crypto RNG), shown **once** on a post-registration
  screen with "download as text" and an "I've saved these" gate.
  **Shipped as SHA-256, not Argon2id:** Argon2's cost is for low-entropy
  secrets, and these are 60 bits of randomness: Argon2 would mean up to
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
  backup codes in 3.5: don't build a second set.
- Existing users have no codes, one-time banner on `/profile`, and admins
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

### 1.4 Registration control · `S` · Model: Opus · ✅ **shipped 2026-09-09**
- Depends on 0.2. When `AllowPublicRegistration` is off, `/register`
  returns 403 and the page says registration is by invitation.
- Invites: `POST /api/admin/invites` → single-use link with an expiry that
  bypasses the toggle. The only way a closed instance adds users without
  email.

---

## Phase 2: Admin panel

### 2.1 Admin shell · `S` · Model: Opus · ✅ **shipped 2026-09-09**
- `/admin` route tree behind the admin role; a nav entry visible only to
  admins (in the topbar's More menu in the middle tier).
- Sections as sub-routes: Dashboard, Users, Spaces, Security (3.3),
  Settings. Groups stay at `/groups` but are linked from here.
- Reuse the existing tab/panel patterns; this is not a new design system.

### 2.2 Users · `M` · Model: Opus · ✅ **shipped 2026-09-09**
- List with search, role, status, `LastSeenAt`, created, last login IP,
  recovery-codes-generated, 2FA-enrolled (3.5).
- Actions: suspend/reactivate (rotate `SecurityStamp` so the session dies
  now), promote/demote admin (refuse to demote the last admin), admin
  reset (1.3), revoke all sessions, revoke all API tokens.
- Deletion: **soft**. Anonymise (email → tombstone, display name →
  "Deleted user", avatar removed) and keep the row: `PageVersion.AuthorId`,
  `Comment.AuthorId` and `AuditLog.ActorId` all reference it.

### 2.3 Settings · `S` · Model: Opus · ✅ **shipped 2026-09-09**
- The `SiteSettings` UI from 0.2: instance name, registration toggle,
  public-spaces kill switch, SMTP block with a **"Send test email"** button
  (live in 4.1; disabled with an explanation until then), TOTP-for-admins.

### 2.4 Spaces · `S` · Model: Opus · ✅ **shipped 2026-09-09**
- All spaces including archived; owner; page count; storage used
  (`SUM(Attachment.Size)` per space); **public or not** (Phase 5);
  archive/unarchive; purge (confirm by typing the key, audit it).

### 2.5 Dashboard · `L` · Model: Opus · ✅ **shipped 2026-09-09**
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
    already surfaced by scripts: see `backup-recovery.md`), open security
    alerts (3.3).
    *Shipped without the backup tiles: nothing in the app could see the
    sidecars. They landed with 9.1 (2026-09-17).*
- One aggregate endpoint `GET /api/admin/dashboard?range=30d` computed
  server-side; the page must not fire fifteen queries.
- **Charts:** load the `dataviz` skill before writing any chart code. One
  small library or hand-rolled SVG; the bundle is already 1.1 MB.
- Themed: stat tiles must read in both themes and every accent.

---

## Phase 3: Security hardening and threat detection

**Sequencing decision #2: this phase precedes public read mode (Phase 5),
and 3.0 precedes everything else in it.** Public mode is what turns a LAN
wiki into an internet target, so the hardening has to exist before the
toggle does. And 3.0 first because without real client IPs, 3.2's rate
limiter would rate-limit Caddy: i.e. everyone, and 3.3's detectors would
see one IP for the whole world. Alerts are delivered in-app here and gain
email in 4.3; do not block this phase on email.

### 3.0 Proxy trust and transport · `S` · Model: Opus · ✅ **shipped 2026-09-09** (run as Fable by user override)
- `UseForwardedHeaders` with `KnownNetworks` set to the compose network (not
  `KnownProxies` by IP, Caddy's container IP is not stable). Verify with a
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
  be `'self'`: no CDNs are used. Test the CSP in report-only mode first.
- Caddyfile: a second, documented variant for public hosting with
  `on_demand` **off**: the catch-all is a LAN convenience, not an internet
  feature. `tls-and-lan-access.md` gets a "hosting on the internet" section.
- **Shipped with two deviations.** Headers are set in app middleware, not
  Caddy: the CSP hash has to come from the `index.html` the app serves, and
  in-app headers hold behind any proxy and are testable. HSTS alone stays in
  Caddy (`Caddyfile.public` only). And trust is by private-range networks
  rather than "the compose network": Docker's subnet is not fixed either;
  the safety argument is that port 8080 is never published.

### 3.1 Least-privilege DB role and tamper-evident audit log · `M` · Model: Fable · ✅ **shipped 2026-09-09**
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
  harness (SQLite has no roles: the split must be a no-op there), and
  backup/restore (`backup-recovery.md` restores as the superuser; make sure
  it still works). Getting this wrong bricks startup.
- **Shipped with one deviation:** the role is provisioned by the app at
  every startup, not by a `deploy/db` init script: init scripts only run
  on fresh volumes, so every existing install would have stayed on the
  superuser, and re-granting after `Migrate()` covers future tables. Also
  added: the daily monitor reports a chain that got *shorter*, which a
  chain cannot detect on its own; and every stored hash was recomputed
  independently in Python from a `psql` dump to prove the jsonb round trip.

### 3.2 Brute-force protection and rate limiting · `M` · Model: Opus · ✅ **shipped 2026-09-09** (run as Fable by user override)
- Depends on 3.0.
- `Microsoft.AspNetCore.RateLimiting` (built-in, no package): sliding
  window per IP on `/auth/login`, `/auth/register`, `/auth/recover/*`,
  `/api-tokens`; a global, generous per-IP limit on everything for
  anonymous callers (Phase 5 relies on this).
- Per-account failed-login counter with **exponential backoff, capped at a
  temporary lockout**, never permanent, or an attacker can lock any user
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

### 3.3 Threat detection and admin alerting · `L` · Model: Fable → Opus · ✅ **shipped 2026-09-09** (both halves as Fable by user override)
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
  rate-limit counters, and **mitigation actions**: each one click, each
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
  `SecurityThresholds`, not settings: see the design in architecture.md
  for why. Rate-limit counters were already on the Security page from 3.2.

### 3.4 Egress and input hardening · `M` · Model: Opus · ✅ **shipped 2026-09-09** (run as Fable by user override)
- **SSRF guard**, one implementation shared by webhooks (today) and link
  previews (7.E): deny loopback, private, link-local and the cloud metadata
  address; resolve DNS *then* connect to the resolved IP (no rebinding);
  cap redirects at 3; 5 s timeout; only `http(s)`. Applied at webhook
  *creation* and at *delivery*.
- Attachments: content-type allowlist on upload (or sniff and override);
  `nosniff` on download (3.0 covers it globally, assert it here too);
  never serve `text/html`, `image/svg+xml` or anything scriptable inline:
  force `application/octet-stream` for those.
- Antiforgery: keep SameSite=Lax and add a double-submit token for
  cookie-authenticated **state-changing** requests; API-token requests are
  exempt (no cookie, no CSRF). The upload endpoint's `DisableAntiforgery()`
  goes away.
- Request size limits confirmed at both Caddy (100 MB) and Kestrel.
- **Shipped with one deviation:** CSRF is a required custom header
  (`X-Requested-With: Tesria`) on cookie-authenticated state changes, not
  a double-submit token: equivalent protection, no token plumbing, and
  it covers the multipart uploads. The framework's `DisableAntiforgery()`
  stays on the two `IFormFile` endpoints because the framework attaches
  its own form-token requirement to them by default.

### 3.5 Sessions, 2FA and admin safety · `M` · Model: Opus · ✅ **shipped 2026-09-09** (run as Fable by user override)
- Idle timeout (e.g. 14 days) and an absolute lifetime (e.g. 90 days) on
  top of `SecurityStamp`; a "sessions" list on `/profile` with revoke.
- **TOTP 2FA** (`Otp.NET`): opt-in per user, enforceable for admins via
  `RequireTotpForAdmins`. Enroll with QR + the 1.3 recovery codes as backup
  codes. Verify at login; rotate stamp on enroll/disable.
- **Sudo mode:** destructive admin actions (purge space, demote admin,
  disable public spaces, DB role rotation) re-prompt for password (or TOTP)
  within a 5-minute window.
- Pin Argon2id parameters explicitly (time, memory, lanes) and document
  them; add a rehash-on-login path so parameters can be raised later.
- **Shipped as specified.** Sudo covers admin role changes (both
  directions), the public-spaces switch, page purge and blocklist removal;
  "DB role rotation" is not an endpoint (it is `.env` + restart). A wrong
  TOTP code or re-auth answer counts toward the 3.2 lockout.

### 3.6 Dependency hygiene · `S` · Model: Opus · ✅ **shipped 2026-09-09** (run as Fable by user override)
- Fix the 38 npm findings (`react-router` upgrade first, check the 7.x
  changelog for breaking changes to `NavLink`/`useLocation`, both used).
- Add `collab/package-lock.json` and make the collab Dockerfile use
  `npm ci`.
- A `scripts/audit.sh` running `npm audit --omit=dev` (web and collab) and
  `dotnet list package --vulnerable --include-transitive`; document it as
  a release gate. Renovate or Dependabot config if the repo goes public.
- **Shipped as specified.** The 38 findings were react-router (7.18.1 →
  7.18.3, patch-level, no API change) and every `@tiptap/*` package
  (3.28.0 → 3.31.3; the packages peer-depend on each other at exact
  versions, so they have to move together: `npm audit fix` alone cannot
  do it). Editor verified live after the upgrade.

### 3.7 Security review and internet-readiness gate · `M` · Model: Fable · ✅ **shipped 2026-09-09**
- Write `docs/security.md`: threat model (who attacks a public wiki and
  why), what each item above defends, what it does not, and the operator's
  **internet-readiness checklist**, the thing Phase 5's toggle links to.
- Run the `security-review` skill against the phase's branch and fix what
  it finds before merging.
- Decide and document the disclosure/contact path (a `SECURITY.md`) if the
  repo goes public.
- **Shipped with one deviation:** no `security-review` skill exists in
  this environment, so the review was a manual pass: every registered
  route inventoried for authorization, the credential endpoints for rate
  limiting, the OIDC return URL, token listing, and settings responses.
  Its surviving findings are `security.md`'s "Known gaps" list.

---

## Phase 4: Email

After the admin panel because SMTP configuration lives in 2.3; after
security because 4.2 needs 3.2's limiter and 4.3 needs 3.3's alerts.

### 4.1 Sender + SMTP · `S` · Model: Opus · ✅ **shipped 2026-09-09** (run as Fable by user override)
- `IEmailSender`; `SmtpEmailSender` via MailKit; `NullEmailSender` when
  `EmailEnabled` is false. Plain text plus a minimal HTML wrapper; no
  template engine. "Send test email" in 2.3 goes live. Delivery failures
  → `email.failed` audit entry (no body in metadata).
- **Shipped with one deviation:** one sender class, not two, with
  `EmailEnabled` off (or settings incomplete) `SmtpEmailSender` declines
  and says why, so there is a single code path. Also added: an
  `EmailEnabled` toggle and "Send test email to me" on the Settings page
  (neither existed), and a `BaseUrl` setting (default `https://$DOMAIN`)
  so emailed links know the instance's address.

### 4.2 Password recovery: email · `M` · Model: Opus · ✅ **shipped 2026-09-09** (run as Fable by user override)
- `POST /api/auth/recover/email` → always 202, same body either way.
  Token: 32 random bytes, stored hashed, 1-hour expiry, single-use,
  invalidated by a newer request. `/reset?token=…` → new password → rotate
  stamp. The login page's "Forgot password?" now offers both paths; the
  email option only appears when `EmailEnabled`.
- **Shipped as specified.** The token is 1.3's admin reset token with a
  nullable issuer, so both links share one redemption path; the email
  path is throttled per address by 1.3's limiter and per client address
  by 3.2's.

### 4.3 Email delivery for alerts and notifications · `M` · Model: Opus · ✅ **shipped 2026-09-09** (run as Fable by user override)
- Security alerts (3.3) go to admins by email as well as in-app, this is
  the "email the admin group" requirement, and it is deliberately the
  *first* email notification wired up.
- Then user notifications for those who opt in (`/profile` → preferences).
  Immediate for mentions and alerts; daily digest for watches by default.
- **Shipped as an outbox.** `Notification.EmailedAt` marks what has been
  sent; `NotificationEmailService` polls every minute. Security alerts to
  administrators always go immediately; each person chooses Off (default:
  opt-in, as the plan says), Immediate, or Daily digest. Mentions do not
  exist until Phase 7, so "immediate for mentions" waits for them. Nothing
  older than a day is ever emailed, so turning email on does not flood
  inboxes with history.

---

## Phase 5: Public read mode (anonymous access per space)

**Sequencing decision #3: this is gated on Phase 3, in code, not just in
this document.** The per-space toggle is only offered when the instance-wide
`AllowPublicSpaces` switch is on, and the settings page shows the
internet-readiness checklist (3.7) next to that switch. Someone can still
flip both on a LAN box, but they will have read what they are skipping.

The use case is exactly the one asked for: someone builds a game wiki and
hosts it for everyone. It pairs with wiki packs (8.5): build it, export
it, and others can host their own copy publicly too.

### 5.1 Permission model for anonymous readers · `M` · Model: Fable · ✅ **shipped 2026-09-09**
- `Space.IsPublic`, `Space.PublicSince`, `Space.PublicComments` (default
  off). Toggling is a **site-admin** action (exposing content to the
  internet is an instance-level risk, not a space-owner one), audited as
  `space.published`/`space.unpublished`, and always raises a 3.3 event.
- An **anonymous principal** in `IPermissionService`: can view a page iff
  its space is public **and** the page carries no restriction **and** the
  page is `Current` (never drafts, never trash). `ViewableSpaceIdsAsync`
  for anonymous = public spaces only. Restricted pages inside a public
  space are invisible: 404, never 403, per the masking rule that already
  exists for authenticated users.
- **The leak matrix**: this is why it is a Fable item. Every read endpoint
  × anonymous × {public space, private space, restricted page in public
  space, draft, trashed page, attachment of restricted page, search hit,
  label listing, export, version history, tree}. Write it as a test class
  before the endpoints are opened; the endpoints are opened only until the
  matrix is green.
- Decisions to write down: version history stays authenticated (edit
  history of a public page can leak withdrawn content: recommend closed);
  comments hidden unless `PublicComments`; collab tokens, watches, drafts,
  the user directory, groups, and labels-across-spaces stay authenticated.
- **Shipped with one tightening:** anonymous readers are hidden from by
  *any* restriction in a page's ancestry (View or Edit), not only View:
  "not for everyone" now includes the internet. The matrix is
  `PublicReadTests` (nine tests over the full grid) and was written first.

### 5.2 Server: opening the read endpoints · `M` · Model: Opus · ✅ **shipped 2026-09-09** (run as Fable by user override)
- Replace `RequireAuthorization()` on read routes with a policy that admits
  anonymous callers and lets the permission service decide (5.1). Write
  routes stay `RequireAuthorization()`; anonymous gets 401 there.
- Endpoints admitted: space by key, page tree (filtered), page, page labels,
  attachments **download** (permission-checked through the page), search
  (scoped to public spaces), export (Markdown/HTML: "take your docs with
  you" should hold for readers too).
- Anonymous requests: `Cache-Control: public, max-age=60` plus an ETag on
  page GETs (unpublishing a space must take effect within that window;
  60 s is acceptable, document it); the 3.2 anonymous limiter applies;
  `PageView` rows with `UserId = null`.
- `sitemap.xml` and `robots.txt` for public spaces (allow `/spaces/{key}`
  for public keys, disallow `/api`). Serve basic `<title>`, description and
  Open Graph tags for public page URLs by injecting them into `index.html`
  at request time: the API already serves the SPA shell, so this is a
  small middleware, not SSR. Full SSR is out of scope; note it as a later
  option if search indexing matters.
- **Shipped as specified.** The meta middleware decides through the
  anonymous check whoever is asking, so a private title never reaches a
  link preview even for a signed-in admin's request.

### 5.3 SPA: a read-only public experience · `M` · Model: Opus · ✅ **shipped 2026-09-09** (run as Fable by user override)
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

### 5.4 Operator controls · `S` · Model: Opus · ✅ **shipped 2026-09-09** (run as Fable by user override)
- The 2.4 Spaces admin page: public column; toggle with a confirmation
  that names what becomes visible (page count, attachment count).
- The 2.3 kill switch and the 3.3 "disable all public spaces" mitigation
  both set `AllowPublicSpaces = false`; the per-space flags are preserved
  so re-enabling restores the previous state.

### 5.5 Anonymous access is opt-in twice · `S` · Model: Opus · ✅ **shipped 2026-09-20**

*Added 2026-09-20 at the owner's request, specified by Fable 5.1. Sequenced
before the onboarding wizard (10.2), which gains the switch.*

**The problem.** With **Allow public spaces** off (the default), an
anonymous visitor to `/` still gets the public shell: a Spaces page saying
"Nothing is published for public reading. Sign in to see more." The same
happens with the switch on and no space marked public. An instance that
publishes nothing should look like one: a sign-in page.

**The rule.** Anonymous reading exists only when **both** the instance
switch (`AllowPublicSpaces`) is on **and** at least one non-archived space
is public. The server already enforces this per space
(`PermissionService.IsPubliclyViewableSpaceAsync`); this item makes the
SPA's landing behavior match it.

- `GET /api/instance` (anonymous, rate-limited with the anonymous policy;
  10.2's `GET /api/setup` folds into it): `{ instanceName, needsOwner,
  publicReading, allowPublicRegistration }`. `publicReading` is the rule
  above, computed from the cached settings and one indexed query. This is
  the one place the SPA learns anything before a session exists; 10.2
  reads `needsOwner` from it, and the login page reads
  `allowPublicRegistration` to show or hide **Create one** (an invite
  token in the URL shows the register page regardless).
- `SessionGate`: when the session check answers "no user" and
  `publicReading` is false, every shell route (`/`, `/spaces`, a space, a
  page, `/search`, `/labels/:name`) sends the visitor to `/login` with
  `from` set, as it did before Phase 5. When `publicReading` is true the
  public shell renders as today. A deep link to a public page on an
  instance that publishes nothing therefore lands on sign-in, which is
  correct: nothing is public.
- The login page, when `publicReading` is true, offers "Browse what is
  public" under the form, so a visitor who arrived at `/login` by habit
  is not stranded.
- Signed-in behavior does not change. The "Nothing is published" copy
  stays for the one case it still describes: a signed-in user with no
  spaces visible to them.

**Wizard (10.2, step 3).** Below the registration cards, a switch **Allow
anonymous reading**, off by default, with: "Off: every visitor must sign
in. On: spaces you mark public can be read without an account; nothing is
public until you mark a space." The step is already required (the
registration choice); the switch has a default, so it needs no choice.
Turning it on is sudo territory (3.5), which the fresh sign-in covers.

**Docs and tests.** `docs/security.md`'s public-read paragraph gains the
two-switch rule; `architecture.md`'s Phase 5 section too. Tests
(`PublicReadTests`): `publicReading` is false with the switch off, false
with it on and no public space, false with only an archived public space,
true otherwise; `needsOwner` flips on the first account;
`allowPublicRegistration` follows the setting. The redirect is SPA
behavior, so it is walked live: signed out with the switch off (login),
with it on and no public space (login), with a public space (the shell);
a public page's deep link in each state; and then the full walk, since
`SessionGate` changes.

**As built (2026-09-20), where it differs from the above.**
- **No new rate-limit policy.** `RateLimits` already applies its global
  limiter to every caller without a session, which is exactly the
  anonymous policy this asked for; `/api/instance` inherits it by being
  anonymous. Adding a named policy would have been a second name for the
  same limit.
- **No index, deliberately.** The spec said "one indexed query". `Spaces`
  has no index on `IsPublic` and does not want one: an instance has tens
  of spaces, not thousands, so the planner scans a page or two and an
  index would be pure write overhead. Revisit only if a space list ever
  gets long enough to measure.
- **Nothing to fold in from 10.2.** `GET /api/setup` does not exist yet,
  so `/api/instance` is new rather than a rename.
- **One test beyond the list:** the response is asserted to be *exactly*
  the four documented fields, with no space key or address anywhere in
  the body. It is the only endpoint that answers an anonymous caller
  about the instance, so what it does not say is worth pinning down.
- **The signed-out walk used a second hostname.** Caddy serves both
  `localhost` and `tesria.localhost`; cookies are per-host, so the second
  one is an anonymous browser without disturbing a signed-in session in
  the first. That is the cheap way to walk anonymous states on a live
  instance, and it is worth remembering for 5.x work generally.

---

## Phase 6: Space icons · `S` · Model: Opus · ✅ **shipped 2026-09-10**

- Depends on 0.4 and reuses the 1.2 upload/crop pipeline unchanged.
- `Space.IconKind` (`None | Emoji | Image`), `IconValue`, `IconColor`.
  Default when unset: the key's first letter on an accent tile: same trick
  as avatars, so every space has an icon from day one.
- Render in space cards, sidebar head, breadcrumb, the public-space listing
  (5.3), and the wiki-pack manifest (8.5). Edit from the space's settings.
- **Shipped with one addition:** there was no space settings page at all,
  `PUT /api/spaces/{key}` had existed since Phase 2 with nothing in the UI
  reaching it, so `/spaces/{key}/settings` now carries the icon picker and
  the name/description form that endpoint was always waiting for.
- The emoji rule is deliberately about *shape*, not a list: short, no
  control characters, at least one non-ASCII character. A list of valid
  emoji goes stale with every Unicode release; this admits future ones and
  keycaps (which really do contain an ASCII digit) and refuses prose and
  markup. Pictures cannot be set through the JSON update: only through the
  upload route, which has the bytes and re-encodes them.

---

## Phase 7: Editor parity with Confluence (the audit) · ✅ **complete 2026-09-10**

> **Scope added 2026-09-10 (done first):** editor chrome to match
> Confluence: a borderless, continuous page and title; a single-row,
> edge-to-edge toolbar; block elements behind a **+ Insert** menu that
> shares the slash catalog; measured overflow into that menu instead of
> wrapping. See the CHANGELOG entry.

The catalog below is from Atlassian's own Confluence Cloud documentation
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
| Headings, lists, bold/italic/underline/strike, code, quote, divider, tables, images, code snippet, panel, action items (task list), highlight, alignment | ✅ |: |
| **Table of contents** | ❌ | A |
| **Expand** (collapsible section) | ❌ | A |
| **Status** lozenge | ❌ | A |
| **Date** | ❌ | A |
| **Decision** | ❌ | A |
| **Anchor** / link to heading | ❌ | A |
| **Layouts** (columns) | ❌ | A |
| Text color | ❌ | B |
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
| Tabs / synced blocks | ❌ | later, Tabs is app-provided in Confluence; synced blocks overlap Excerpt include |
| Jira macros, Office/OneDrive, Marketplace | n/a | not applicable to a self-hosted wiki |
| Blog posts (per-space blog) | ❌ | **not an editor element, a content type.** Decide separately; listed so it isn't forgotten. |
| Live search, User list, Profile picture, Spaces list, Create-from-template, Network | ❌ | low value; revisit after D |

### Wave A: structural blocks (frontend + renderer) · `L` · Model: Opus · ✅ **shipped 2026-09-10** (started as Fable by user override, finished as Opus)
- **Anchor first**: stable `id` attr on headings (slugified, de-duplicated);
  the link popover gets a "link to heading" list. TOC depends on it.
- **Table of contents**: a node with no stored content; the node view
  computes from headings live; export renders a real nested list of links.
- **Expand**: `expand` node, `title` attr, `block+` content, open state not
  persisted. Export: `<details>`; Markdown: bold title + indented body.
- **Status**: inline atom, `text` + `color` (gray/red/yellow/green/blue/
  purple: Confluence's set, mapped to the palette). Export: styled span.
- **Date**: inline atom, ISO `date` attr, rendered in the viewer's locale.
- **Decision**: block like panel with a fixed icon; Markdown `**Decision:**`.
- **Layouts**: `layoutSection` containing 2–3 `layoutColumn` nodes; presets
  two equal, three equal, left sidebar, right sidebar, three with sidebars.
  Not nestable (Confluence doesn't), but sections stack. Each section
  carries a width, centered / wide / full, mapped onto the existing
  page-level full-width mechanism (`.page-wrap--full`, the `--page-pad`
  breakout); reuse it per section. **Full-width tables inside a column must
  not break out**: scope the breakout rule to direct children of the
  content root. Export HTML: flex row; Markdown: columns in order.
- Slash-menu and toolbar entries for each, sharing `PANEL_TYPES`-style
  constants so the two can't drift.

### Wave B: formatting marks and input rules · `M` · Model: Opus · ✅ **shipped 2026-09-10**
- Text color mark (palette-limited, reusing `ColorPalette` and the
  dark-mode ink-pinning approach). Subscript/superscript. Paragraph indent
  (`textIndent` attr, capped at ~4). Clear formatting. Shortcut audit
  against Confluence's list; verify StarterKit's `**`, `__`, `` ` `` rules.

### Wave C: people · `M` · Model: Opus · ✅ **shipped 2026-09-10**
- **Mention**: `@` suggestion on the same `@tiptap/suggestion` primitive
  the slash menu uses; `mention` inline node with `userId`; on save, diff
  mentions and notify new ones (`user.mentioned`). Renders with the avatar.
- **Emoji**: `:` suggestion over a bundled list; stored as the literal
  character, nothing new in the schema or renderer.
- **Action item assignee**: `assigneeId` on `taskItem`; Task report (D)
  queries it.

### Wave D: dynamic blocks · `L` · Model: Fable → Opus · ✅ **shipped 2026-09-10** (contract + mechanism as Fable; all twelve kinds as Opus)
- **Fable designs** the `dynamicBlock` contract (attrs, the per-kind
  endpoint shape, export snapshotting, permission filtering) and writes it
  into `architecture.md`; **Opus adds kinds** against it.
- Server: one `GET /api/pages/{id}/blocks/{kind}?params` per kind,
  permission-filtered (a Children display must not leak restricted pages:
  reuse the search endpoint's two-pass filtering; and for public spaces,
  5.1's anonymous rules). Export: the renderer calls the same service to
  snapshot the block as static HTML/Markdown at export time.
- All twelve kinds shipped: Children, Recently updated, Content by label,
  Attachments, Change history, Contributors, Include page, Excerpt include,
  Page properties report, Labels list, Task report, Page tree. The contract,
  the per-kind params and the three tests each kind owes are in
  `architecture.md`, "Dynamic blocks": adding a thirteenth is one class,
  one DI line, one catalog entry and three tests.

### Wave E: media and embeds · `M` · Model: Opus · ✅ **shipped 2026-09-10**
- Embed node with a **server-enforced allowlist** of hosts (editable in
  admin settings). Never render an arbitrary iframe.
- Smart links: server-side Open Graph fetch through the 3.4 SSRF guard,
  cached. Inline / card display modes.
- Video and generic file blocks on existing attachments; PDF via the
  browser's viewer; gallery as a layout over image nodes.

### Wave F: technical content · `M` · Model: Opus · ✅ **shipped 2026-09-10**
- **Mermaid** (from `roadmap.md`): a `mermaid` code-block language rendered
  by a node view; HTML export ships the source plus a client-side render
  script; Markdown export is a fenced block.
- Math via KaTeX (inline + block). Chart from a table's data, after D, a
  block can point at a table by id.

---

## Phase 8 · Brand page: claims and roadmap

The project's original brand page was audited against the product.
Every present-tense claim holds **except one**; the four roadmap items map
onto `roadmap.md` and are sequenced here.

### 8.1 PDF export: the one claim that isn't true yet · `M` · Model: Opus · ✅ **shipped 2026-09-10**
- The page says "export any page as Markdown or a PDF". The product exports
  Markdown or **print-ready HTML**. Recommend a Playwright sidecar (the stack
  already has one Node sidecar, so the pattern exists) rendering the HTML
  export to PDF on request; keep the HTML export too. **After Wave A**,
  since layouts and TOC change what "print-ready" means.

### 8.2 License · `S` · Model: Opus · ✅ **shipped 2026-09-10**
- The page says Apache 2.0 and open source. **The repo has no `LICENSE`
  file** (verified 2026-09-08), and `CLAUDE.md` says it is private pending
  an audit, which is now Phase 3.7. Add the Apache 2.0 text as `LICENSE`
  and a `NOTICE`; SPDX identifiers in `package.json` and the `.csproj`.
  Public visibility is the user's call, but the license file should exist
  before it flips, not after.

### 8.3 API: OpenAPI + documentation · `M` · Model: Opus · ✅ **shipped 2026-09-10**
- The REST API and webhooks exist and are documented in the **API space**
  (23 pages, created 2026-09-08). Missing: a machine-readable spec. Add
  `Microsoft.AspNetCore.OpenApi` at `/api/openapi.json` with the endpoint
  records as schemas; Scalar UI at `/api/docs`. **Before MCP**: tools are
  easiest to define from the spec. Keep the API space in step.

### 8.4 MCP server · `L` · Model: Fable → Opus · ✅ **shipped 2026-09-11** (contract and token scopes as Fable; the ten tools, `PageWriter` and the Markdown converter as Opus)
- **Fable designs** the tool surface and the auth model; **Opus implements.**
  Load the `claude-api` skill before designing tool definitions.
- A separate process (Node sidecar, like collab) or a .NET endpoint speaking
  MCP over HTTP; authenticates with an API token. Tools: search, get page,
  list space tree, get labels, create/update page: writes opt-in per
  token, **which is where token scopes finally land**: add a `ReadOnly`
  flag to `ApiToken` (it has no scopes today; 3.3's "revoke tokens" also
  benefits).
- Depends on 8.3 and benefits from Phase 7 stabilizing the content schema.

### 8.5 Wiki packs: space export and import · `XL` · Model: Fable → Opus · ✅ **designed and shipped 2026-09-21** (Fable designed, Opus implemented)

**What it is for.** A pack is how a wiki survives its instance. The manual
written on 2026-09-11 was lost with the database it lived in, and the owner
has since decided the rebuilt manual's source of truth is the wiki, not the
repository. So the pack is the thing 10.5 commits: export the manual, keep
the export in git, import it anywhere. The second use is moving a space
between two instances. It is **not** a sync, a merge, or a backup of the
instance; Phase 9 does backups, and merging is deliberately out of scope
(see "Not decided").

**The shape of the problem.** Every row a space is made of carries ids that
mean nothing on another instance: page ids, attachment storage keys,
comment ids, and above all user and group ids as authors and as permission
principals. A pack therefore carries *content and structure* and lets the
importer re-mint every identity, and it carries *no permissions and no
identities*, because those are the two things that would let a zip file
grant access. The inventory at the end of this item is what the decisions
below were made against.

**The format, version 1.** A zip:

```
manifest.json          format, generator, exportedAt, source, space {key, name},
                       counts, omitted (see export), restrictions (counts only)
space.json             key, name, description, homepage (page id), icon
                       {kind, value, file?}, templates [{name, description, content}]
authors.json           { "<id>": { "displayName": "..." } }   attribution record only
pages/<id>.json        one per page, in tree order (below)
attachments/<id>       the bytes; filename and type live in the page's json
space-icon.webp        only when the space has an uploaded icon
```

A page file:

```
{ "id", "parent", "position", "title", "fullWidth", "createdAt", "createdBy",
  "current": <version number>,
  "versions": [ { "number", "createdAt", "author", "comment", "content": <ProseMirror JSON> } ],
  "labels": [ "name", ... ],
  "attachments": [ { "id", "filename", "contentType", "size", "file": "attachments/<id>" } ],
  "comments": [ { "id", "parent", "author", "body", "anchor", "createdAt", "updatedAt", "deletedAt" } ] }
```

Ids inside the pack are the source instance's ids, used only as join keys
between files; an importer always mints new ones. **Output is canonical**:
JSON pretty-printed with sorted keys, pages ordered by tree position, zip
entries in a fixed order, no timestamps in the zip headers. The reason is
10.5: the manual's pack will be committed unzipped, and a re-export after a
one-word edit must diff as a one-word edit, not as forty-seven rewritten
files. `format` is an integer, `1`; adding an optional field does not bump
it, changing the meaning of one does, and an importer refuses a pack whose
format is newer than it knows ("made by a newer Tesria") rather than
guessing.

**What travels, and what does not.** Each of these is a decision.

1. **Pages: current, published pages only.** Drafts (`Status = Draft`) are an
   unfinished edit belonging to a session, not the space. The trash does not
   travel either: a deleted page is not part of the space, and restoring it
   before export is the way to say otherwise.
2. **Full version history travels.** The manual's edit history is part of the
   record, the sketch asked for it, and the cost is bounded. `ContentHtml`
   never travels; it has been dead since 12.1.
3. **Attachments travel as bytes**, and are re-keyed on import through
   `IAttachmentStorage` with a freshly minted storage key. Their content type
   is re-derived from the bytes (`ContentTypes.Resolve`) exactly as an upload
   is, never trusted from the manifest, and each is subject to the same 25 MB
   cap an upload has.
4. **Labels travel by name.** They are instance-wide and lower-cased, so an
   import finds-or-creates by name, which is what `AddToPage` already does.
5. **Templates travel**, since they are per space and are content.
6. **Comments travel**, with threads and tombstones (`DeletedAt` kept, so a
   deleted parent does not orphan replies). The anchor payload travels as-is;
   what actually ties an inline comment to text is the `commentId` on the
   comment mark inside the document, and that is rewritten with the rest of
   the content (below).
7. **The space icon travels**: emoji and color as values, an uploaded icon as
   a file, re-keyed on import through the same media service.
8. **`IsPublic`, `PublicComments`, `PublicSince` do not travel.** An imported
   space is private, and publishing it is 5.5's two-step opt-in, which a zip
   file must not be able to bypass. `Archived` does not travel either; an
   imported space is live.
9. **Permissions do not travel: neither `SpacePermissions` nor
   `PageRestrictions`.** Their principals are ids on another instance.
   Carrying names instead would invite matching by name, and matching by
   name is how a stranger with the right display name ends up with access.
   An imported space starts as open as a new one (open to members, closed to
   the internet), and the importer restricts it afterwards. So that the
   importer knows to, the manifest records *that* restrictions existed, as
   counts, and the import's response says so in words.
10. **Watches, webhooks, notifications, page views and collaboration drafts
    do not travel.** The first four point outward or at people; the last is
    a session, and carries a `meta.version` bound to the source instance.
11. **Attribution goes to the importer.** Every `CreatedById`, `AuthorId` and
    `UploadedById` on the target is the importing user. The original authors
    are recorded by display name only in `authors.json`, and the import's
    audit event carries the same record, so who wrote what is not lost; it is
    just not asserted against the target's accounts. Display names only:
    no email addresses go into a file that will be committed to a repository.
    Matching identities across instances is an identity decision, and
    attributing words to the wrong real person is worse than attributing
    them to the importer.

**Rewriting content on import.** One pure function, `PackRewriter`, takes a
document and the id maps and returns the document as it must be on the
target. It is a pure function so it can be tested against fixtures, the way
8.6's diff is.

- Page links `/spaces/<key>/pages/<id>[#anchor]` (12.2's regex) become the
  target key and the new id when the page is in the pack. A link to a page
  that is *not* in the pack is **left unchanged**: on a same-instance
  re-import it may still be valid, and a link that 404s honestly beats one
  silently destroyed. The site export's "#" rule is right for a static site
  and wrong here.
- Attachment links `/api/attachments/<id>/download` become the new ids.
- Comment marks' `commentId` become the new comment ids.
- Mentions keep their `label` and have `userId` set to null; task assignees
  keep `assigneeName` and have `assigneeId` set to null. The mention node
  already renders from the label when there is no user behind it, which is
  what 12.1's fixture found and fixed.
- `homepage` and `parent` are mapped through the page id map.
- Every document then goes through `PageContent.TryNormalize`, which is the
  one door every stored page passes through: it validates the JSON and, since
  8.6, strips tracked-change marks. A pack cannot smuggle either.

**Export.** `GET /api/spaces/{key}/export/pack`, requiring `PagesExport` and
view rights on the space. It exports **everything the caller can see**: this
is preservation, not publishing, so the audience is the exporter, not the
anonymous reader 12.2 defaults to. Pages the caller cannot view are omitted,
and the manifest's `omitted` says how many, without titles. It **streams**
the zip to the response rather than buffering it: 12.2 caps a site at 300
pages because each page is a browser capture, and a pack has no such cost,
so it has no such cap. Audited as `space.exported`.

**Import.** `POST /api/spaces/import`, multipart: the zip, a target `key`,
and an optional `name`. Requires `SpacesCreate`, because that is what it
does, and a full (not read-only) token if it comes through the API.

- The key must not exist; 409 otherwise, and no merge in version 1. A pack
  imported twice is two spaces under two keys.
- **Atomic.** All rows in one transaction; attachment and icon bytes written
  to storage before the commit and deleted again on rollback, best effort.
  A failed import leaves no half-space, which is 12.1's lesson about page
  creation applied to a thousand rows at once.
- **Untrusted input, throughout.** The manifest's `format` is checked first.
  Entry names are validated against the expected paths and refused on `..`,
  a leading `/`, or anything unexpected (zip slip). The zip has an entry
  count cap and a total uncompressed size cap (500 MB), and each attachment
  the upload cap. Every page document goes through `TryNormalize`; every
  attachment's type is re-derived from its bytes. The page tree is checked
  for cycles and dangling parents (a dangling parent lifts the page to the
  root, as 12.2 does). Positions are renormalized, and version numbers are
  renormalized to 1..n in `createdAt` order. `source` in the manifest is
  shown to the importer and used for nothing else.
- Rate limited per user, like token minting (ten imports an hour is plenty
  for anyone who is not a script).
- `SearchText` is rebuilt on import, not carried. No `CollabDocuments` are
  created; the first person to open an imported page seeds the shared
  document from it, which is 8.6 step 3 doing its ordinary job.
- Audited as `space.imported`, with counts, the source instance name and the
  `authors.json` record.

**Where it lives in the UI.** Space settings already has "Export as a site"
for readers; it gains "Export as a pack" beside it, with one sentence on the
difference (a site is for people, a pack is for Tesria). The spaces list
gains "Import a pack" next to "New space", for anyone with `SpacesCreate`:
choose the file, give it a key, done, and the result says what came in and
whether the source had restrictions the importer should now set.

**How 10.5 uses it.** The manual's pack is committed **unzipped** under
`docs/manual/pack/`, so a page edit diffs as a page edit, and a two-line
script zips it back for import. The canonical-output rule above is what
makes that work; it is a format requirement, not a nicety.

**Opus implements, in this order, each step shippable alone:**
1. ✅ **shipped 2026-09-21.** `WikiPack.cs`: the model, a canonical writer and
   a validating reader, as pure code, with 27 tests.
   As built: the reader's name check is an allow-list of the shapes the format
   defines rather than a scan for `..`, so zip slip fails by not being on the
   list. Determinism is 1980 timestamps in the zip headers plus sorted keys;
   a test proves two writes 1.1 s apart are byte-identical.
2. ✅ **shipped 2026-09-21.** `GET /spaces/{key}/export/pack` and "Export as a
   pack" in space settings, walking pages through the exporter's own
   permissions.
   As built: the zip is spooled to a temp file opened `DeleteOnClose` rather
   than written to the response. `ZipArchive` writes synchronously and
   finishes its central directory on Dispose, and Kestrel refuses synchronous
   writes to a response, so streaming straight out throws on every export.
   The attachment storage keys the writer needs are returned alongside the
   model and closed over, never carried in the format: they are this
   instance's business, and an importer mints its own.
3. ✅ **shipped 2026-09-21.** `PackRewriter`, pure, with 19 fixture tests.
   As built: it walks every string in the document rather than an allow-list
   of link attributes, so a node type added later is rewritten too. An image's
   `src` was the case an allow-list would have missed. A comment mark with
   nothing behind it is dropped rather than left dangling, and the `marks` key
   goes with it when it was the only one, so a document that gained and lost a
   comment is byte-identical to one that never had it.
4. ✅ **shipped 2026-09-21.** `POST /spaces/import` with the transaction,
   storage sweep, rewriting, audit, 409 and rate limit; "Import a pack" on the
   spaces list.
   As built: pages and versions need **two** saves inside the transaction,
   because a page points at its current version and a version points at its
   page, which EF refuses to insert as one batch. The rollback and the byte
   sweep are one `finally` guarded by a `committed` flag, since a validation
   failure returns from inside the transaction after rows have been added and
   has to undo exactly what an exception would.
5. ✅ **shipped 2026-09-21.** 12 HTTP round-trip tests, importing as a
   different user.
   As built: exporting ordered attachments and comments by `CreatedAt` in SQL,
   which SQLite cannot sort, so the ordering moved into memory. The suite runs
   on SQLite, so this would have shipped as a Postgres-only feature otherwise.
6. ✅ **shipped 2026-09-21.** `architecture.md` and the CHANGELOG. The manual
   chapter is 10.5's.

**Two things the live walk found that no test had.**

*A re-zipped pack was refused.* `zip -r` writes a directory entry for every
folder and this writer never does, so `pages/` was "an unexpected file" and
the reader turned down the one workflow the format exists for: 10.5 commits
the manual's pack **unzipped** and zips it back to import it. Directory
entries are now skipped. Every unit test had built its zip with the writer,
which is why they all agreed with each other and with nothing else.

*Two exports of an unchanged space differ by one line*, `exportedAt` in the
manifest, and nothing else: the content is byte for byte identical. The
determinism test passed because it builds the model once and writes it twice,
so the timestamp was fixed. This is left as it is on purpose: a pack that
travels outside a repository should say when it was made, and one obvious
line is a long way from the "forty-seven rewritten files" the canonical rule
was written against. **But it means a re-export always shows as changed**,
so if 10.5 wants a clean `git status` when nothing has moved, dropping the
field is a one-line change to make then rather than now.

**Verify** with the FIXTURE space, which has every element: export it,
import it as FIXTURE2, open each page and compare, including a page link
between two pages in the pack, an attachment download, an inline comment
thread, labels and the template. Then: the same pack a second time under
the same key is refused; a manifest edited to `format: 2` is refused; a zip
with a `../` entry is refused; import as a member who cannot see one of the
pages and confirm the manifest's `omitted` count. Then re-export FIXTURE2
and diff the unzipped trees: only ids and dates should differ.

**Not decided here, deliberately.** Matching authors to target accounts by
email (an identity decision; version 1 attributes to the importer).
Carrying restrictions by principal *name* for an importer to confirm one by
one (useful, and the same risk as matching by name, so it needs its own
design). Importing into an existing space as an update (a merge; a different
problem). Exporting an entire instance (Phase 9). Signing or encrypting packs.

**What a space is made of today** (taken from the schema on 2026-09-21, as
input for the design rather than as any part of it: the sketch above says
"every earlier phase adds fields the pack must carry", so here is the list
as it now stands). Whether each of these travels, and how, is Fable's call.

| Rows | Fields worth a decision |
|---|---|
| `Spaces` | `Key` (unique, so a collision is an import question), `HomepageId`, `Archived`, `IsPublic`, `PublicComments`, `PublicSince`, `IconKind`/`IconValue`/`IconColor` (6; an uploaded icon is a file, like an attachment) |
| `Pages` | `ParentPageId` and `Position` (tree order), `CurrentVersionId`, `Status`, `DeletedAt`/`DeletedById` (is the trash in the pack?), `FullWidth` (12.2), `SearchText`/`SearchVector` (derived, so rebuilt rather than carried) |
| `PageVersions` | the whole history, or only the current one. `ContentHtml` is dead since 12.1 and should not travel |
| `Attachments` | `StorageKey` is a local storage path, so it needs re-keying on import; the bytes are files in the zip |
| `Labels`, `PageLabels` | labels are instance-wide, not per-space, so an import merges by name rather than by id |
| `PageTemplates` | per space, and carries `ContentJson` like a page |
| `Comments` | threads (`ParentCommentId`), `AnchorJson` into the document, and `DeletedAt` tombstones |
| `SpacePermissions` | `PrincipalType`/`PrincipalId` pointing at users, groups or roles that may not exist on the target. The hard one |
| `Watches`, `Webhooks` | per-user and per-space respectively; both point outward (`Webhooks.Secret` is a credential and must not travel) |
| `CollabDocuments` | the live Yjs draft (8.6). Almost certainly not in a pack: it is a session, not content, and it carries `meta.version` bound to this instance's version numbers |

Every one of these has a `CreatedById`/`AuthorId`/`UploadedById` pointing at
a `User` row that the target instance will not have, which is the same
question as permissions in a different coat.

### 8.6 External edits: API and MCP writes as tracked changes in a live draft · `L` · Model: Fable → Opus · ✅ **shipped 2026-09-21** (steps 1–5; step 6 folded into 10.5)

**The problem (found 2026-09-13).** A page has two stores: the page
version in Postgres, and the Yjs document in `CollabDocuments` that the
editor actually edits. The API, `PageWriter` and the MCP `update_page`
tool write the first and never touch the second; the editor seeds the
second only when it is empty. So once a page has been opened in the
editor, a write from anywhere else is invisible to the next editing
session, and pressing Update there writes the stale draft over it. Every
assistant-written change is one browser edit away from being lost.

**The decision.** Treat an API or MCP write the way a second person's
typing is treated: it lands in the live document, visibly, and the human
decides what to do with it. Concretely:

1. **The write lands in the draft as tracked changes.** Text the write
   added is marked `externalInsert`; text it removed stays in place,
   marked `externalDelete`, struck through. Both marks carry
   `{ source: 'api' | 'mcp' | 'page', actor, at }`, so the highlight can
   say *Added by MCP · Sam's laptop token, 2 minutes ago* on hover and
   color by source.
2. **Publishing accepts.** Before the editor sends its content, it runs
   `acceptExternalEdits`: `externalDelete` ranges are removed,
   `externalInsert` marks are unwrapped, and the result is what gets
   published. The human can edit inside a highlighted run first: it is
   ordinary text with a mark on it. A banner above the editor says how
   many external changes are pending, with **Accept all** and **Reject
   all** (reject = delete the inserts, un-strike the deletes) for people
   who want to decide before they finish.
3. **No session open means nothing is lost either.** When the sidecar
   loads a stored document whose recorded page version is behind the
   page's current version, it applies the same tracked-change
   reconciliation before handing it to the first client. Unpublished
   edits from a session everyone closed are kept, marked against what was
   published meanwhile, never silently discarded, never silently kept as
   if newer.
4. **Publish is optimistic-concurrency checked.** The editor sends the
   version it last reconciled to; if the page has moved on (a notification
   was missed), the API answers 409 and the editor reconciles from the
   response with source `page` and shows the banner, instead of
   overwriting. API and MCP callers may omit the version and keep
   last-write-wins, as today.

**One reconcile function, three callers.** `reconcile(ydoc, pageJson,
{ source, actor, version })` lives in the web tree
(`src/web/src/editor/externalEdits.ts`) next to the schema it depends
on, and is used by: the sidecar on a live-write notification; the
sidecar on load of a stale document; the client on a 409. The diff is
**block-level**: top-level blocks compared by canonical JSON (with any
pending external marks stripped from the old side first, i.e. "old as if
accepted"), longest-common-subsequence over the block list, removed
blocks re-inserted with `externalDelete` on their inline content, added
blocks inserted with `externalInsert`, applied as Y.XmlFragment
insert/delete inside one `ydoc.transact`. A replaced paragraph therefore
reads as the old one struck through followed by the new one highlighted.
Blocks with no inline content (images, live blocks, rules) are inserted
or removed plainly, because a mark cannot sit on a block; say so in the
manual. Character-level merging inside one paragraph is the refinement
for later, not the first version.

**The schema is the one in `extensions.ts`, in both places.** The sidecar
needs the ProseMirror schema to convert JSON to Yjs. It must not get a
second copy: a second Vite entry (`vite.schema.config.ts`) bundles
`getSharedExtensions` plus `externalEdits.ts` into one headless ESM file
that the collab image copies in at build time. Any schema change then
reaches the sidecar by rebuilding, and nothing can drift. (`y-prosemirror`
becomes an explicit dependency of the web package; it is already there
transitively through the collaboration extension.)

**The version lives in the document.** A `Y.Map('meta')` with `version`,
set by the client when it seeds, by every reconcile, and by the client
after a successful publish. The sidecar's `fetch` compares it to
`PageVersions.VersionNumber` for the page (the sidecar already signs in
as the app's database role) and reconciles on mismatch before returning
the state.

**The write path notifies the sidecar.** `PageWriter.UpdateAsync` and
`CreateAsync`, after commit, POST `{ contentJson, source, actor,
version }` to the sidecar's `POST /pages/{id}/reconcile` (a plain HTTP
route on the Hocuspocus server via its `onRequest` hook, served on the
same port, guarded by the existing `COLLAB_SHARED_SECRET` in a header:
the same shape as the PDF sidecar's `X-Pdf-Secret`). Source is decided by
how the caller authenticated: cookie session → `editor`, bearer token →
`api`, the MCP endpoint → `mcp`. For `editor` the sidecar only records
the version: the content is already the document's. The call is
best-effort with a short timeout: a failure is logged, the page write
stands, and item 3 or 4 catches up later. The sidecar applies a live
notification with `server.openDirectConnection`, so a document nobody has
open is not loaded just to be edited: it is reconciled on next load
instead.

**Defensive strip on the server.** `PageWriter` removes both marks from
anything it stores, so a client that forgot to accept, or a script that
copied a draft, cannot publish tracked changes into a page version. One
JSON walk, one test.

**Opus implements, in this order, each step shippable alone:**
1. ✅ **shipped 2026-09-20.** The two marks in `extensions.ts` with CSS (tint
   by source, strike for deletes, `title` for hover);
   `acceptExternalEdits` / `rejectExternalEdits` commands; the server-side
   strip with a test. Nothing produces the marks yet, so this is inert.
   As built: the marks live in `editor/externalEditMarks.ts` and are
   registered in the *shared* schema, not the editor's alone, because the Yjs
   document carries them and every peer has to be able to read one. Color is
   by insert/delete rather than by source (green added, struck red removed),
   with the source in the hover label: two questions, two channels. The
   strip is in `PageContent.TryNormalize`, the single door every page write
   goes through, and keeps the text including the deletions, because a safety
   net cannot know what the human meant. Found on the way: the print rules
   hiding this kind of highlighting named `.comment-mark`, which nothing
   renders, so commented text had been printing with its ground.
2. ✅ **shipped 2026-09-20.** `externalEdits.ts` (block diff + Yjs apply)
   with unit tests against fixture documents, including "old has pending
   marks" and "block with no inline content".
   As built: the diff is pure and Yjs-free (`diffBlocks`,
   `reconcileDocument`) with the CRDT write a thin wrapper over
   y-prosemirror's own `updateYFragment`, which is the minimal-change
   applier the collaboration extension already uses for every keystroke.
   That is what keeps an untouched paragraph untouched in the CRDT, and so
   keeps other people's cursors where they were; a wholesale replacement
   would look identical in a screenshot and clobber anyone typing.
   `blockKey` canonicalises before comparing: sorted keys, and null or empty
   attributes dropped, because TipTap writes `attrs: { textAlign: null }`
   where the API writes no attrs at all and the two are the same block.
   **One deliberate departure from the spec above:** "old as if accepted" is
   read as *marks stripped*, not *changes accepted*. The text under a
   pending deletion stays. Truly accepting would let a second write silently
   accept the first one's deletion on the human's behalf, which is the thing
   this phase exists to stop; the cost is a redundant (idempotent) reconcile
   when a draft holds an unresolved deletion.
   **Vitest was added to the web package** for this, at the owner's approval,
   with the boundary written into CLAUDE.md: logic yes, rendering never.
3. ✅ **shipped 2026-09-20.** The schema bundle build and the sidecar's
   `fetch`-time reconcile with `meta.version`; the client seeds
   `meta.version`. This alone fixes the no-session case.
   As built: `vite.schema.config.ts` bundles `getSharedExtensions` plus
   `externalEdits.ts` into one headless ESM file, built inside the collab
   image (a first stage runs `npm run build:schema`), so a schema change
   reaches the sidecar by rebuilding and cannot drift. `yjs` and
   `y-prosemirror` stay external, because two copies of Yjs in one process do
   not share types and the failure is confusing rather than loud.
   **A document with no `meta.version` is adopted, not reconciled.** Every
   draft that existed before this shipped is in that state, and their
   unpublished edits are not an assistant's changes; marking them all up on
   the first load after deploying would be noise in the one feature whose
   job is to be believed. The cost is that a genuinely stale pre-existing
   draft is not caught, which is the behavior those drafts already had.
   **Two bugs only running it could find.** First: ProseMirror materialises
   every attribute a node declares, so a paragraph out of the CRDT carries
   `textIndent: 0` where the same paragraph as the API stored it carries no
   attrs at all. Compared directly, *every block of every document* read as
   changed on *every* reconcile. Both sides now go through
   `schema.nodeFromJSON().toJSON()` first, with a regression test. Second,
   and worse: a reconcile that is not *persisted* is re-run from the same
   stale state on the next load, and because Yjs merges rather than
   replaces, the second run's insertions land beside the first's, so the
   page's new paragraph appears twice. Hocuspocus only stores a document
   that changed while somebody was connected, so a reconcile nobody then
   edited was forgotten. The sidecar writes it immediately now, through one
   `persist()` the store hook shares. Found by restarting the sidecar with a
   page open, which is also how it is now verified.
4. ✅ **shipped 2026-09-21.** The notifier in `PageWriter`, the sidecar's
   `onRequest` route, source detection by auth scheme,
   `openDirectConnection` apply. This is the live case.
   As built: `ICollabNotifier` posts to `POST /pages/{id}/reconcile` after
   the commit, beside the webhook dispatch and for the same reason, guarded
   by the `Collab:Secret` the two already share and with a three-second
   timeout. Best effort on purpose: the page is already saved, and a sidecar
   that is down means the reconciliation waits for the document's next load
   (step 3). Failing the write instead would be a healthy API refusing to
   save because an optional service is unwell.
   `openDirectConnection` is used **only if the document is already open**;
   a page nobody is editing is left for its next load, so the sidecar's
   memory tracks how many people are editing rather than how busy the API is.
   **Why an `editor` write records the version and draws nothing**, beyond
   "the content is already the document's": publishing and then carrying on
   typing is ordinary, so by the time the notification lands the draft is
   legitimately ahead of the page, and diffing would strike through the
   words the human is still writing and blame somebody else. The gap that
   leaves is a cookie-session write that did not come from the open editor,
   which the application has no flow for.
   Hocuspocus's `onRequest` contract is worth knowing: a hook that rejects
   with an *empty* value means "handled", while rejecting with a real error
   is rethrown, so the route rejects with nothing after writing its own
   response.
5. ✅ **shipped 2026-09-21.** The editor banner, `baseVersion` on publish,
   409 handling and client reconcile.
   As built: the banner sits above the editor beside the connection status,
   not inside the page, because it is about the document rather than any one
   place in it; it counts pending runs and offers Accept all and Reject all.
   Pressing Update accepts anyway, as the last thing before the content
   leaves, and reads the body back from the editor rather than from React
   state, which lags a transaction behind.
   `baseVersion` is `meta.version`, the version this draft was last
   reconciled to, and it is **optional on the wire**: an API or MCP caller
   holds no draft that could be stale, so last-write-wins stays right for
   them and no existing script breaks. The 409 carries the page as it stands,
   not just a refusal, because the editor reconciles against that body to
   show the difference and cannot do it from a status code.
   The non-collaborative editor deliberately gets none of this: with no
   shared document there is nowhere for an outside write to land, so it is
   always looking at exactly what it loaded.
6. ⏸ **moved to 10.5** (2026-09-21). Manual pages: *Saving, drafts and
   editing together* gains a section on changes from assistants; *The MCP
   server* says what an assistant's write looks like to someone mid-edit.
   Those pages no longer exist: the manual was written as content inside
   the instance, and this database has no such space, nor does the oldest
   retained backup. Rather than write two sections into a manual that is
   gone and out of date besides, the whole manual is rebuilt as **10.5**
   and this is part of its "working together" chapter. How the mechanism
   works is in `architecture.md` and `CHANGELOG.md` already, so nothing
   technical is waiting on that.

**Verify** with two browser contexts on one page plus an MCP write between
them: the highlighted change appears in both; editing inside it works;
Reject all restores the page; Accept all then Update publishes the merged
text; the audit log shows the MCP version and the human version in order.
Then the no-session case: close every editor, write via MCP, reopen: the
change is highlighted with source `page`. Then the 409 case with the
sidecar stopped.

**Not decided here, deliberately:** whether a page *view* should show
pending tracked changes to a reader (recommend no: the page is what was
published), and whether an assistant should be able to mark its own write
as "needs review" so it stays highlighted after publish (a different
feature; do not conflate).

---

## Phase 9 · Backups: an admin section now, offsite targets later

Written 2026-09-17 by Fable 5.1, at the owner's request, from the code and
from the live stack (not from the docs; the docs described a stub that was
never wired). Two items: **9.1** is specified below in full and is ready
for Opus. **9.2** is a future item; it holds the research and the
decisions the owner still has to make.

**What is true today, and what this phase changes.** There are two
independent backup systems and the app can see neither:

- The `backup` sidecar (`deploy/backup/run.sh`) takes a `pg_dump` and a
  `tar.gz` of uploads on start and then every `BACKUP_INTERVAL_HOURS`,
  and prunes with `find -mtime +RETENTION_DAYS`. A failed run is retried
  a full interval later (24 h by default; on 2026-09-17 a restart cost a
  day of backups exactly this way). Interrupted runs leave 0-byte
  `*.tmp` files that nothing removes (six on this instance).
- The `pgbackrest` sidecar (`deploy/pgbackrest/run.sh`) holds its
  full/incremental counter in memory, so **every restart takes a full
  backup**, and `repo1-retention-full=2` then expires the oldest. Two
  restarts in a day shrink the point-in-time window to hours. `set -e`
  turns one failed backup into a container exit and another full.
- Neither records what it did. "Keep forever" is not expressible. The
  dashboard's Health tiles (2.5) were never built because there was
  nothing to read.

**The owner's decisions (2026-09-17), fixed for both items:**

1. A backup is **kept** if it is one of the newest *N* **or** taken within
   the last *D* days; it is **removed only when it is outside both**. An
   outage can therefore never erode the newest *N*.
2. **One policy governs both systems.** For pgBackRest "a backup" is a
   full plus the incrementals that depend on it.
3. Retention **off means keep everything forever**.
4. Extras, all four: alert admins on failure or lateness; a **Back up
   now** button; a **Test restore** button that records when a backup was
   last proven restorable; automatic clean-up of orphaned temp files.
5. The retention policy affects backups only. Nothing else in the app
   reads it.

### 9.1 Backups admin section · `L` · Model: Fable → Opus · ✅ **shipped 2026-09-17** (spec as Fable, implementation as Opus)

**The decision: the database is the contract between the app and the
sidecars.** The sidecars already connect as the database owner (they need
it to dump and to archive), so they can read a policy row and write run
records with no new credential, port or volume. The app reads those rows
through its least-privilege role and never touches a backup file: the
plaintext dumps and the encrypted pgBackRest repository stay out of the
web process, which is the same reason the runtime role split exists (3.1).

Rejected, and why: a *status file on a shared volume* means mounting the
`backups` volume (plaintext dumps) into the app, gives no history, and
needs ad-hoc locking for requests; an *HTTP endpoint in each sidecar* is
a new network surface plus a new shared secret inside images that have no
web server; *mounting the volumes read-only* still cannot read pgBackRest
(the repository is encrypted and `info` needs the cipher key) and cannot
carry requests back. The database already has transactions, a queue
primitive (`FOR UPDATE SKIP LOCKED`), the audit log and the alerting.

**Schema: one migration, `Backups`.** Column names are PascalCase and
quoted, like every other table; the sidecars write them by name.

- On `SiteSettings` (typed columns, cached 30 s, like every other setting):
  `BackupRetentionEnabled` (bool, default true), `BackupKeepCount` (int,
  default 3, valid 1..1000), `BackupKeepDays` (int, default 14, valid
  1..3650), `BackupPolicyChangedAt` (nullable; null means "never set by
  anyone", see the seed below), `BackupPolicyChangedById`.
- `BackupAgents`, one row per sidecar, primary key `Name` (`logical` |
  `physical`): `StartedAt`, `LastSeenAt` (heartbeat), `NextRunAt`,
  `IntervalHours`, `FullEveryDays` (physical only), `ToolVersion`
  (`pg_dump`/pgBackRest version string), `VolumeFreeBytes`,
  `VolumeTotalBytes`, `WalArchivedAt` (physical: mtime of the newest
  archived WAL file, which is how far forward PITR reaches),
  `AppliedRetentionEnabled`, `AppliedKeepCount`, `AppliedKeepDays`,
  `PolicyObservedAt` (see the grace period), `Message` (the last log line,
  for the status card). **App role: read-only.**
- `Backups`, the inventory (the disk is the truth; this is its mirror,
  refreshed every poll): `Id`, `Agent`, `Label` (the cycle stamp
  `20260917T050527Z` for logical; the pgBackRest label `20260917-034639F`
  for physical), `Type` (`dump` | `full` | `diff` | `incr`), `Prior`
  (physical: the label this one depends on), `StartedAt`, `CompletedAt`,
  `SizeBytes` (logical: dump plus archive on disk; physical: the
  repository size of that backup, `info.repository.size`), `DetailJson`
  (file names; WAL start/stop and LSNs), `HasUploads`, `Error`,
  `FirstSeenAt`, `LastSeenAt`, `RemovedAt` (null while present),
  `RemovedReason` (`retention` | `missing`), `LastVerifiedAt`,
  `LastVerifyOk`. Rows are never deleted: a removed backup stays as
  history. **App role: read-only.**
- `BackupJobs`, the queue and the run log: `Id`, `Agent`, `Kind` (`backup`
  | `restore-test`), `Trigger` (`scheduled` | `manual` | `startup`),
  `Status` (`requested` | `running` | `succeeded` | `failed`), `Target`
  (a backup label, restore tests only), `RequestedAt`, `RequestedById`,
  `StartedAt`, `FinishedAt`, `Error`, `ResultJson` (what was produced:
  labels and sizes; what retention removed and why; orphans cleaned;
  restored table count), `LogTail` (last 40 lines). The app inserts
  `requested` rows; the sidecars insert `scheduled`/`startup` rows and own
  every update. **App role: append-only** (add to
  `DatabaseRoles.AppendOnlyTables`).
- `DatabaseRoles` gains `ReadOnlyTables = ["BackupAgents", "Backups"]`
  (revoke INSERT, UPDATE, DELETE, TRUNCATE from the app role), applied in
  `EnsureAppRoleAsync` next to the append-only loop, with a test that both
  lists name these tables (SQLite cannot enforce them, so the list is what
  the test protects; see `ThreatDetectionTests` for the existing one).

**The retention rule, exactly.** Take one agent's *successful* backups,
newest first, index `i` from 0. Backup `i` is kept if `i < KeepCount` or
`StartedAt >= now - KeepDays × 24 h`; otherwise it is removed. Retention
disabled: nothing is removed, ever. Consequences that follow and are not
separately configurable: the newest backup is always kept (`KeepCount >=
1`); a stretch of failures cannot cause deletions (failures are not in the
list and each success only pushes old ones down by one).

- *Logical:* the unit is a **cycle**, one stamp shared by the dump and
  the uploads archive. `run.sh` computes the stamp once and passes it to
  both scripts (`BACKUP_STAMP`); a cycle exists if either file does.
  Removing a cycle removes both files.
- *Physical:* the list is of **full** backups; removing a full removes its
  incrementals and, through pgBackRest's own archive retention, the WAL
  before the oldest surviving full. Implement it as pgBackRest's native
  count: `K = max(KeepCount, number of fulls with StartedAt >= now -
  KeepDays)`, clamped to at least 1, and run `pgbackrest expire
  --repo1-retention-full=K` after each successful backup. Never use
  `expire --set` (it has its own WAL rules and is easy to get wrong).
  `pgbackrest.conf` changes `repo1-retention-full` to `9999999` with a
  comment saying the sidecar supplies the real value per run; this also
  means an operator running `pgbackrest backup` by hand no longer expires
  anything (document that in the runbook).
- Retention disabled on the physical side: skip `expire` entirely (with
  the conf at 9999999, the automatic expire after a backup keeps all).

The same rule lives twice, in C# (`BackupRetention.Plan(policy, backups,
now)` in `Infrastructure/Backups/`, used by the preview endpoint and the
tests) and in bash (each sidecar, with `RETENTION_DRY_RUN=1` printing
the plan without deleting). Opus verifies the bash against the C# cases
by hand with the dry run. The cases, with ages in days:

| KeepCount | KeepDays | Backups (age) | Removed |
|---|---|---|---|
| 3 | 14 | 1, 2, 3, 20, 30 | 20, 30 |
| 3 | 14 | 20, 30, 40, 50 | 50 |
| 1 | 1 | 0.5, 2 | 2 |
| 5 | 14 | 1, 2 | none |
| disabled | | anything | none |
| physical, 2 | 14 | fulls 1, 10, 20, 30 | fulls 20, 30 and their incrementals (K = 2) |

**The grace period on reductions, enforced by the sidecar.** Each agent
records the policy it last applied (`Applied*`). When it observes a policy
stricter than that (retention newly enabled, or a smaller count, or fewer
days), it sets `PolicyObservedAt = now` and keeps applying the *old* policy
until 24 hours have passed, then applies the new one. Loosening applies at
once. "Never applied one" counts as stricter, so an upgrade or a fresh
sidecar removes nothing during its first day. Why: the app role can write
`SiteSettings`, so a compromised admin session could otherwise set 1 day /
1 backup and have the history gone before anyone reads the alert. The app
merely displays the resulting "takes effect at" time, computed from the
agent row; it cannot shorten it.

**Seeding the policy on upgrade.** Compose passes
`Backup__SeedRetentionDays: ${BACKUP_RETENTION_DAYS:-14}` to the app. At
startup, after migrations, if `BackupPolicyChangedAt` is null the app sets
`KeepDays` from that value, `KeepCount = 3`, `Enabled = true`,
`ChangedAt = now`, `ChangedById = null`. An operator who had raised
`BACKUP_RETENTION_DAYS` keeps their days; the count of 3 keeps at least as
many fulls as today's 2. After the seed the variable is not read again;
`.env.example` says so. `RETENTION_DAYS` leaves the `backup` service's
environment. The `BACKUP_S3_ENABLED` stub (a log line and nothing else)
leaves `run.sh`, compose and `.env.example`; the `S3_*` lines stay
commented as "reserved for 9.2".

**The sidecars, rewritten.** Bash, no new binaries: `psql` writes the
rows and **Postgres parses the JSON** (`pgbackrest info --output=json` is
passed as a `psql` variable and unpacked with `jsonb_array_elements`;
`find -printf` builds the logical listing the same way). Both sidecars
share the same loop shape, in a sourced `common.sh`:

1. Wait for the database (`pg_isready` loop; this is the bug behind the
   2026-09-17 gap). If `to_regclass('"BackupJobs"')` is null the tables do
   not exist yet (first boot before the app migrated): run in **legacy
   mode**, backing up on the interval and removing nothing, and re-check
   each poll.
2. On start, mark this agent's own `running` jobs `failed` ("agent
   restarted"); upsert the agent row with `StartedAt`, versions and the
   interval. `NextRunAt` is read from the row: if it is null or in the
   past, a backup is due now; otherwise the schedule survived the restart.
   This is what removes the restart-takes-a-full behavior.
3. Every `POLL_SECONDS` (60): heartbeat (`LastSeenAt`, disk from
   `df -B1`, `WalArchivedAt` on the physical side); sync the inventory
   from disk or `info` (new rows, `LastSeenAt` on present ones,
   `RemovedAt`/`missing` on rows whose files are gone without a retention
   record); claim one `requested` job for this agent (`UPDATE ... WHERE
   Id = (SELECT ... FOR UPDATE SKIP LOCKED) RETURNING`); run the due
   scheduled backup; run claimed jobs.
4. A backup job: insert or claim the row, run, then on success apply
   retention (subject to the grace period) and, on the logical side,
   delete `*.tmp` files older than one hour; write `ResultJson`,
   `LogTail`, `Status`, and set `NextRunAt = now + interval`. On failure:
   `failed` with `Error`, and `NextRunAt = now + backoff` (15 min,
   doubling, capped at 6 h). Nothing in the loop runs under `set -e`; a
   failure is a row, not an exit.
5. Physical backup type: `full` if there is no full or the newest full's
   start is older than `BACKUP_FULL_EVERY_DAYS` (7, env), else `incr`.
   Then `expire --repo1-retention-full=K` as above, then `info` to
   refresh the inventory.
6. Restore-test job: logical runs `verify-backup.sh <dump>` (it already
   restores into a throwaway database and counts tables); physical runs
   `verify.sh`, extended to accept `--set=<label>` (it restores to
   `/tmp` inside the container and checks `pg_controldata`). Either way
   the job records the outcome and the backup row's `LastVerifiedAt` /
   `LastVerifyOk`. `pitr-selftest.sh` stays manual: it writes to the live
   database.
7. Compose healthchecks for both services: the loop touches
   `/tmp/heartbeat` each poll; `test: find /tmp/heartbeat -mmin -5`.

`backup.sh`, `backup-files.sh` and `verify-backup.sh` stay runnable by
hand exactly as the runbook shows; a file made by hand is picked up by
the next inventory sync as an ordinary backup.

**Endpoints** (`Features/Admin/BackupEndpoints.cs`, group
`/api/admin/backups`, `RequireAdmin`; sudo where marked, audited where
marked, both via the existing helpers):

- `GET /` overview: the policy with each agent's `effectiveAt`; both
  agents (online = `LastSeenAt` within 5 min; `nextRunAt`; disk; tool
  version); per agent: last successful backup (label, when, size), last
  failure, success rate over 30 days, present backups count and total
  bytes, oldest restore point, and for physical the PITR window (oldest
  full's start to `WalArchivedAt`); last restore test (when, label,
  ok); the present backups newest first, each with its verify status;
  the last 50 jobs. `?includeRemoved=true` adds backups removed in the
  last 30 days with `RemovedReason`.
- `PUT /policy` (**sudo**; audit `backup.policy_changed` with old and
  new values): validates the ranges; when the new policy is stricter,
  raises `backup.retention_reduced` (Critical, alert) through the
  detector, so every admin gets the email.
- `POST /policy/preview`: runs `BackupRetention.Plan` against the current
  inventory and returns, per agent, what would be removed (labels,
  count, bytes) and the new oldest restore point. Advisory: the sidecar
  decides, and its `ResultJson` says what it actually did.
- `POST /run` `{ agents?: [...] }` (audit `backup.requested`): inserts
  one `requested` `backup` job per agent; 409 if that agent already has a
  `requested` or `running` job.
- `POST /{label}/restore-test` (audit `backup.restore_test_requested`):
  same shape, `Kind = restore-test`, `Target = label`.
- `GET /jobs/{id}`: one job with its log tail, for polling a running one.
- Deliberately **no download endpoint**: backup files never pass through
  the web tier. The runbook's `docker compose exec`/`docker run` lines
  stay the way to fetch them.

**Alerts.** `BackupMonitor : BackgroundService` (1 min initial delay,
every 5 min; same shape as `AuditChainMonitor`), using a new
`ISecurityDetector.BackupProblemAsync(kind, severity, metadata)` that
wraps `RaiseAsync(alert: true, key: agent)`; the existing cooldown by
`(kind, key)` keeps a flapping agent to one alert per window:

- `backup.failed` (Warning): a job finished `failed` since the last check.
- `backup.overdue` (Critical): no successful backup within
  2 × `IntervalHours`, except during the first interval after
  `StartedAt`.
- `backup.agent_offline` (Warning): `LastSeenAt` older than 15 min, or
  no agent row 10 min after the app started.
- `backup.restore_test_failed` (Critical).
- `backup.disk_low` (Warning): free space under 10 % of the volume or
  under twice the last backup's size.
- `backup.retention_reduced` (Critical): raised inline by `PUT /policy`.

Add the six kinds wherever the existing kinds are listed for operators
(the detection section of `docs/security.md` or `architecture.md`,
whichever holds the table today).

**UI: `/admin/backups`, tab "Backups" after Security.** Routes in
`main.tsx`, the tab in `AdminLayout.tsx`, so **the live-walk rule
applies in full** (member, admin, signed out).

- *Status strip:* one card per agent ("Database dumps and uploads",
  "Physical backups and point-in-time recovery"): a status dot (online /
  offline / last run failed), last backup with age and size, next run,
  oldest restore point (physical: the PITR window as a range), disk free,
  last restore test. **Back up now** (both agents) sits above the cards;
  each card has **Test restore** for its newest backup. A running job
  shows inline and polls `GET /jobs/{id}` every 5 s until it finishes.
- *Policy form:* a choice between "Keep every backup forever" and "Prune
  old backups", the latter with "Keep the newest [N] backups and
  everything from the last [D] days" and the sentence *A backup is
  deleted only when it is outside both.* Save calls the preview first and
  shows what will be removed and the new oldest restore point in a
  confirmation panel (not `window.confirm`: it has a list in it); the
  client's `reauth_required` handling supplies the sudo prompt. A
  reduction shows "Takes effect at <time>; nothing is removed before
  then."
- *Backups table:* label, kind (physical rows say full/incr and name their
  full), taken, size, verified (time or "never"), a **Test restore**
  action. A collapsed "Removed" section lists the last 30 days of removed
  ones with the reason.
- *Runs table:* the last 50 jobs (when, trigger, kind, status, duration,
  a one-line summary from `ResultJson`), each expandable to its log tail.
- *Dashboard (2.5's Health, finally):* two stat tiles, "Last database
  dump" and "Last physical backup", showing age and a tick or cross,
  linking to the tab. Both read from the same overview data (a slim
  `GET /api/admin/backups/summary`, or fold two fields into the dashboard
  response; Opus's call, but not a second heavy query).
- Themed like the rest of the admin area; the status dot uses
  `--danger` only for failure, never for "no backup yet".

**Tests** (`tests/Api.Tests/BackupTests.cs`): the retention rule (the
table above, plus the boundary at exactly `KeepDays`); policy validation,
sudo, audit row, and the `backup.retention_reduced` alert only when
stricter; the preview's numbers against a seeded inventory; `POST /run`
inserts one job per agent and 409s on a pending one; the overview's
overdue/offline computations and the PITR window; `BackupMonitor` raising
each kind from seeded rows and staying quiet when healthy; the
`DatabaseRoles` lists; the startup seed (null `ChangedAt` → seeded from
config; non-null → untouched); the dashboard tiles.

**Verification, live.** Rebuild `app`; restart `backup` and `pgbackrest`
with their new scripts. Expect the inventory to show this instance's
present files (the 6 dumps, 4 uploads archives and 2 pgBackRest fulls as
of 2026-09-17) and the six `.tmp` orphans to be gone after the first
logical cycle. Press **Back up now** and watch both jobs run to
`succeeded`; run **Test restore** on each newest backup; change the
policy to a stricter one and confirm the preview lists the right
backups, the grace time is shown, and nothing is removed; loosen it and
confirm it applies at once. Stop the `backup` container for 16 minutes
(or lower the threshold in a test build) and confirm
`backup.agent_offline` arrives as an alert and an email. Then the full
live walk.

**Docs.** `backup-recovery.md` (the layers table gets a retention row;
the admin tab becomes the first thing the runbook points at; the
`pgbackrest.conf` retention note; the env changes and the upgrade seed;
the offsite section now says "see dev-plan 9.2"), `README.md`'s
backup paragraph, `architecture.md`'s backups section (the contract and
the three tables), `.env.example`, `docs/security.md` (the role lists and
the alert kinds), and a dated CHANGELOG entry.

**Decided out of scope (say so in the CHANGELOG):** the schedule
(`BACKUP_INTERVAL_HOURS`, `BACKUP_FULL_EVERY_DAYS`) stays deploy-time and
is shown read-only; automatic scheduled restore tests (a later toggle);
encrypting the logical dumps (a prerequisite of 9.2, done there or as its
own small item); downloading backups through the app; `pitr-selftest.sh`
in the UI.

### 9.2 Offsite backups: cloud, network drives and removable media · `L` · Model: Fable → Opus · ✅ **shipped 2026-09-22** (Fable designed, Opus implemented)

**Designed 2026-09-21 (Fable); ready for Opus.** Research done 2026-09-17
(Opus, from official docs; items marked *unverified* were not confirmed).
The owner answered every open decision on 2026-09-21; the answers are
recorded with the questions below, and the design that follows them is
what Opus implements. **Prerequisites:** 9.1 shipped (it did), and the
logical dumps **encrypted before anything copies them off the box** (they
are plaintext today; step 2 of the design is what satisfies this, because
nothing leaves the box except through restic).

**Recommendation.** Local first, then replicate (3-2-1); never
remote-only, because a remote outage would then fill `pg_wal`.

- *Database:* pgBackRest **dual repository**: keep `repo1` local, add
  `repo2` on S3-compatible storage (Backblaze B2 as the documented
  default) or a NAS. WAL streams to both continuously, so PITR exists
  offsite; each repo has its own retention, cipher passphrase and backup
  command (`backup --repo=2`, weekly is enough). Set
  `repo2-bundle=y`, and set `archive-push-queue-max` so a dead remote
  cannot fill the disk (WAL past the limit is *dropped*, which breaks
  PITR from that repo until its next backup, so alert when it trips).
  Upgrade pgBackRest to 2.59.1 (2.59.0 fixed dotted bucket names with
  path-style URIs).
- *Uploads and logical dumps:* **restic**, not `rclone` or a plain copy.
  It encrypts client-side always, deduplicates (back up the uploads
  volume directly instead of a fresh tarball), and its `forget
  --keep-last N --keep-within Dd --prune` **is the owner's retention
  rule**. Backends: local, SFTP, its own `rest-server`, S3 and
  compatibles, B2, Azure, GCS. `check --read-data-subset=5%` after each
  run. Not Glacier or Deep Archive: they break restic, and 12 to 48 hour
  restores with 90/180-day minimums are a trap for disaster recovery
  anyway.
- *NAS:* a Docker named volume with `driver: local` and `type=nfs` or
  `cifs`. If the NAS is offline the container fails to start, which is
  the right failure (a host *bind* mount to a missing share silently
  writes to local disk). NFS volumes can fail to come back after a reboot
  (moby#47153). SMB has no symlinks or POSIX uids: mount with
  `uid=999,gid=999` and use `repo-type=cifs` or `repo-symlink=n`
  (pgBackRest 2.57+). Keep spool and lock paths local; never put PGDATA
  on a NAS. Best used as a copy target (repo2, or a restic
  `rest-server --append-only` on the NAS), not as the only repository.
- *Removable media (a drive plugged into the host):* the classic offline
  copy, and **a different target class from a NAS**, because it is absent
  most of the time. That one fact rules out everything continuous. It can
  never be a pgBackRest repository or a WAL destination: an unplugged
  `repo2` makes `archive_command` fail, which is the postmaster crash-loop
  this machine has already been through. So it is an **on-demand copy** of
  the latest verified backup set, as a restic repository on the drive,
  written by the sidecar when asked, with `check` after the write and
  `sync` before the screen says "safe to remove". restic is the format for
  the offsite reasons plus one more: a drive leaves the building, so
  client-side encryption is the whole point, and a restic repository is
  self-contained (restic and the passphrase restore it on any machine, with
  no Tesria). The mount is a host **bind** mount at a fixed path,
  deliberately the opposite of the NAS choice: the container must start
  whether or not the drive is there. That reopens the silent-write trap (an
  absent drive leaves an ordinary directory on the boot disk, and a copy
  into it fills the boot disk), and the defense is a **sentinel file on the
  drive itself**: `.tesria-backup-target`, written once when the target is
  set up and carrying the target's id. No sentinel, no write, and the
  screen says "drive not present" rather than "done". A mount-point check
  cannot do this job from inside a container, where a bind mount is always
  a mount point. Filesystems: exFAT and FAT32 have no ownership, no
  symlinks and no hardlinks; restic needs none of those, and the setup step
  formats nothing and warns on FAT32 (its 4 GB file limit; restic's packs
  stay under it by default, but say so). Retention is per target and
  conservative, `--keep-last N` with no time window, because a drive
  plugged in twice a year must not prune itself to nothing. "Copy whenever
  it appears" is possible without udev or launchd, which a container cannot
  see: the sidecar polls for the sentinel on a schedule. On this Mac a
  drive lives under `/Volumes/<name>`, which Docker Desktop shares by
  default.
- *Immutability:* S3 Object Lock (needs versioning; *governance* can be
  bypassed with a permission, *compliance* cannot be shortened), B2
  Object Lock with an application key lacking `deleteFiles`, restic's
  `rest-server --append-only`. R2 has bucket locks but no Object Lock API,
  no versioning, and slow pgBackRest restores (pgbackrest#2782).
  **Immutability conflicts with retention:** a credential that cannot
  delete makes `expire` and `prune` fail, so retention moves to provider
  lifecycle rules (with the lock no longer than retention), or a separate
  maintenance key kept off the host. "Keep forever" must never shorten a
  lock.
- *Credentials:* deploy-time (`.env` or Compose secrets), not the UI.
  Anything the app can write, an attacker who owns the app can use; keys
  that can delete or bypass a lock stay out of its reach. The admin
  screen for this (a "Storage targets" section beside 9.1's) edits type,
  endpoint, bucket, prefix, region, URI style, enabled, per-target
  retention and schedule; shows read-only whether credentials are set
  (key-id fingerprint only), encryption, versioning or lock detected,
  last offsite backup and WAL push, bytes stored, last verify and any
  archive-gap or staleness warning; and has **Test connection**, which
  the sidecar runs (`check --repo=2`, `restic snapshots`) and reports
  through the 9.1 tables. `EgressGuard` covers only the app; sidecar
  egress is unguarded and a LAN NAS is a private address the guard would
  refuse anyway.
- *Costs at 1 to 50 GB (list prices, 2026-09):* B2 $6.95/TB with 10 GB
  free (about $0.30/month at 50 GB, free egress up to 3× stored, Object
  Lock); AWS S3 Standard about $1.15/month at 50 GB plus $0.09/GB
  egress; R2 $0.015/GB after 10 GB, free egress, no lock; Wasabi has a
  1 TB minimum bill and a 90-day minimum storage duration, so not at
  this scale.
- *Proving it restores:* `pgbackrest --repo=2 verify` (manifests, WAL
  continuity, checksums) daily and `--repo=2 check` for WAL arrival; a
  real drill restores `--repo=2 --type=time` into a throwaway
  `postgres:18` container and records how long it took. restic:
  `check --read-data-subset`, and periodically `restore latest` followed
  by `pg_restore --list` and an uploads file count. Wire both into 9.1's
  restore-test jobs.

**Phasing when scheduled:** (1) database offsite: repo2 on B2, own
passphrase, `repo2-retention-full` about 4, `archive-push-queue-max`,
weekly `backup --repo=2`, daily `verify --repo=2`, alerts, credentials
in `.env`, pgBackRest 2.59.1; (2) uploads and dumps offsite: restic
replaces the tarball, the 9.1 policy drives `forget`; (3) the Storage
targets screen, versioning plus a governance lock no longer than
retention, a write-only daily key and an offline maintenance key, and a
monthly automated scratch restore; (4) removable media as an on-demand
target with the sentinel, once the screen exists to put its button on.
*LAN-only alternative:* the NAS as
repo2 (SFTP, or NFS with `repo-symlink=n`, or `cifs`) plus a restic
`rest-server --append-only` on it; a cloud copy of the NAS can come
later.

**Open decisions for the owner, all answered 2026-09-21.** In order: (1)
B2 is the documented default; (2) credentials stay in `.env`, never the
UI, the owner's words being "keep it all in .env to avoid issues and keep
top security", and the analysis that led there is in the design below;
(3) governance lock, no longer than retention; (4) separate passphrases,
escrowed off the host; (5) each remote gets its own schedule and
retention; (6) restic replaces the uploads tarball; (7) NAS first-class,
SMB tested; (8) removable media is on-demand only and never counts as the
only copy off the box. The questions stay as the record of what was asked.


1. The documented default provider: B2, generic S3, or R2.
2. Offsite credentials: deploy-time only (recommended), or editable in
   the UI.
3. Lock mode and period, and how "keep forever" or a short local
   retention interacts with it.
4. Local and remote passphrases: the same or separate (recommended
   separate), and where they are escrowed. A lost passphrase makes the
   remote copy unrecoverable.
5. Whether repo2 gets its own schedule and retention (recommended yes).
6. restic replaces the uploads tarball, or both are kept.
7. ~~NAS as a first-class target type, or a documented recipe.~~
   **Answered 2026-09-21: first-class.** The owner wants people to use the
   storage they already have, and made a NAS available for building and
   testing, an SMB share on the LAN. SMB/CIFS is therefore the documented
   and tested LAN protocol; NFS stays a recipe.
8. Removable media (added 2026-09-21 at the owner's request): on-demand
   only, which is the recommendation for version 1, or also "copy whenever
   the drive appears" by polling for the sentinel. And whether a removable
   target may stand as an instance's *only* copy off the box, or whether the
   screen says plainly that an offline copy is not a schedule.

**Risks to carry into the design:** a repo2 archive gap silently breaks
PITR from it; `archive-push-queue-max` trades a full disk for dropped
WAL; NFS after reboot; SMB and symlinks; no-delete credentials break
`expire`/`prune`; Wasabi minimums; Glacier breaks restic; R2 restores;
`rclone sync` deletes and its config passwords are only obscured; a lost
passphrase; sidecar egress is unguarded; today's dumps are plaintext; an
absent removable drive is an ordinary directory and the sentinel is the only
guard; FAT32's 4 GB file limit; a drive pulled mid-write.

**The design (2026-09-21, Fable).** Three target types, one rule each
was chosen by, and a mechanism that is the same for the two that are paths.

**Why the credentials are not in the admin page.** The retention policy is
enforced by Tesria's code, and a stolen credential never passes through
Tesria's code, so a policy in the UI protects against Tesria's own
mistakes and against a stolen key not at all. Worse, and this is from the
code rather than from theory: Data Protection keys are persisted **in the
database** (`Program.cs`, `PersistKeysToDbContext`), so a logical dump
would contain both a UI-stored key and the means to decrypt it, and every
backup would carry the key to delete itself. A key that can delete is the
fatal case, since `forget --prune` and `expire` need one. The write-only
design (a key without `deleteFiles`, verified by Test connection refusing
a key that can delete, with pruning moved to provider lifecycle rules) was
put to the owner and he chose `.env` instead, for simplicity: one place,
no misconfiguration that quietly weakens it. So: **every offsite secret
lives in `.env`, is read only by the backup sidecar, and never reaches the
app, the database, a log, or a screen.** The screen shows fingerprints.

**Three targets, fixed slots.** Rather than a numbered list of arbitrary
targets, `.env` declares at most one of each kind: `OFFSITE_CLOUD_*`,
`OFFSITE_NAS_*`, `OFFSITE_REMOVABLE_*`. Fixed slots are readable in a
`.env`, map one-to-one onto three cards on the screen, and cover "use the
storage you have"; a second cloud is rare enough to be a later extension.
Each slot has its own passphrase (`_PASSPHRASE`), separate from the local
repository's and from each other's, so a leaked cloud passphrase does not
read the NAS copy and a lost drive does not expose either. Four
passphrases in all (local, cloud, NAS, removable); the runbook says to
escrow every one in a password manager off the host, and the sidecar
**refuses to run a target whose passphrase is missing** rather than
falling back to another's. A lost passphrase is a lost copy, by design.

**The database goes offsite through pgBackRest; the files through
restic.** These are the two tools, one each, and the split is by what
each is for:

- *pgBackRest* keeps `repo1` local and adds **`repo2` on the cloud slot**
  (S3 API; B2 is the documented default). WAL streams to both, so
  point-in-time recovery exists off the box; `repo2` has its own cipher
  passphrase, `repo2-bundle=y`, `repo2-retention-full` from the slot's
  retention, a weekly `backup --repo=2` and a daily `verify --repo=2`.
  `archive-push-queue-max` bounds what a dead remote can do to the disk,
  and tripping it raises an alert, because WAL past the limit is dropped
  and PITR from that repo is broken until its next backup. Upgrade to
  pgBackRest 2.59.1 first. **A NAS is not a pgBackRest repository by
  default.** A mounted path cannot be one safely: `archive_command` runs
  in the *database* container, where no sentinel check can run, and an
  unmounted share is an ordinary directory that pgBackRest would happily
  make a fresh repository in, on the boot disk. The one safe way is the
  **SFTP repository type** (pgBackRest 2.46+, no mount at all; the NAS
  being down is a network error, not a silent local write), and that is a
  documented recipe for a LAN-only instance that wants PITR on its NAS,
  not a slot. Step 1 must confirm the pinned version's multi-repository
  `archive-push` semantics (whether one failing repository fails the
  command) before relying on them; the research did not verify this.
- *restic* replaces the uploads tarball and carries **both the uploads
  volume and the logical dumps** to every configured target: the cloud
  slot (B2 backend), the NAS slot (a path), the removable slot (a path).
  It encrypts client-side always, which is what satisfies the plaintext
  prerequisite; it deduplicates, so the uploads volume is backed up
  directly; and `forget --keep-last N --keep-within Dd --prune` is the
  slot's retention, applied per target. `check --read-data-subset=5%`
  after every run. One repository per target, never shared.

**One mechanism for the two path targets.** The NAS and a removable drive
are both "a path on the host that may or may not be a real mount right
now", and they get the same guard: the share or drive is mounted **on the
host** (macOS: Finder or `mount_smbfs`, under `/Volumes`; Linux: `fstab`
with `_netdev,nofail`) and **bind-mounted** into the backup sidecar, so
the sidecar always starts, and a **sentinel file** on the target itself
(`.tesria-backup-target`, written once at setup, carrying the slot's id)
is checked before every write. No sentinel means no write and a plain
status ("NAS not reachable", "drive not present"), never a copy into the
empty directory the absent mount leaves on the boot disk. A Docker
`cifs`/`nfs` named volume stays as a documented alternative where host
mounting is awkward, with its known trade (the container does not start
while the share is down). They differ only in **policy**: the NAS is
scheduled, every 9.1 run pushes to it, and its absence is an alert; the
removable drive is **on demand only**, a "Copy now" button and nothing
scheduled, its absence is normal, and retention is `--keep-last N` with no
time window so a drive plugged in twice a year cannot prune itself away.
After a removable copy the sidecar runs `check`, then `sync`, and only
then reports "safe to remove"; it formats nothing and warns on FAT32. NAS
immutability is the NAS's own snapshot schedule (every mainstream NAS has
one), documented as the recipe; `rest-server --append-only` is the
stronger alternative for a NAS that can run it.

**Where it appears.** The 9.1 backups section gains **Storage targets**:
three cards, Cloud, NAS, Removable. The app never reads `.env` for this;
the **sidecar publishes its own configuration, secrets reduced to
fingerprints, into the 9.1 status tables**, and the cards show that: type,
endpoint or path, bucket and prefix, whether a key and a passphrase are
set (fingerprints), encryption on, and for the cloud whether versioning
and a lock were detected; then last backup, last WAL push, bytes stored,
last verify, and any warning (archive gap, queue tripped, stale, not
reachable, drive absent). Two actions, both sidecar jobs reported through
the 9.1 tables: **Test connection** (`check --repo=2`, `restic snapshots`,
the sentinel check) and, on the removable card only, **Copy now**. One
more thing the dashboard says, per decision 8: an instance whose only
configured target is removable media has **no offsite backup**, in those
words, because an offline copy is not a schedule.

**Proving it restores.** `pgbackrest --repo=2 verify` daily; a monthly
automated drill that restores `--repo=2 --type=time` into a throwaway
`postgres:18` container and records how long it took; for restic, `check
--read-data-subset` after each run and a monthly `restore latest` followed
by `pg_restore --list` and an uploads file count. All wired into 9.1's
restore-test jobs, so they show where the local ones do. And the runbook
gains the chapter that is the point of all of this: **the machine is
gone**. Fresh host, `.env` and the passphrases from escrow, and the steps
from a B2 bucket, a NAS share or a drive back to a running Tesria, using
`deploy/scratch-instance.yml`, which already exists for exactly this.

**Security notes carried in.** Sidecar egress is unguarded (`EgressGuard`
covers the app only; a LAN NAS is a private address the guard would refuse
anyway). Secrets are redacted in every sidecar log line. `.env` stays mode
600. The sidecar reports fingerprints, never values, and the app has no
code path that can return a value because it never holds one.

**What multi-repository `archive-push` actually does (confirmed 2026-09-21
on the pinned 2.59.1, by experiment, not by reading).** Step 1 was asked to
confirm this before relying on it, and the answer changes how the feature
must be operated.

- **A WAL segment is acknowledged to Postgres only once every configured
  repository has it.** The documentation's "when a repository cannot be
  reached, WAL will still be pushed to other repositories" is about the
  *data*, not the acknowledgement. With `repo2` unreachable, `archive_count`
  froze, `.ready` files piled up in `pg_wal/archive_status`, and `pg_wal`
  grew. `archive-async=y` is required for even that much, and this stack
  already has it.
- **`archive-push-queue-max` works, and is the only thing standing between a
  dead remote and a full disk.** With the limit at 48MB and `repo2` broken,
  pgBackRest logged `WARN: dropped WAL file '...' because archive queue
  exceeded 48MB` and Postgres carried on. This is why **2.59 is the floor**:
  pgbackrest#2629 reports the limit not taking effect while archive-push was
  erroring, fixed in 2.59. On 2.58, which this repository shipped until now,
  the limit could be set and silently not protect anything.
- **The worst finding: dropped WAL is lost from the local repository too.**
  Of the four segments dropped in that test, one had reached `repo1` and
  three had not, and they are gone. So an unreachable *offsite* repository,
  left long enough to trip the queue, does not merely break point-in-time
  recovery offsite; **it breaks it locally as well.** That is more severe
  than this item's earlier risk note ("a repo2 archive gap silently breaks
  PITR from it") and it drives three rules: the queue limit is set
  generously (the default here is 16GiB, and `.env` says to keep it well
  under the free space on the pgdata volume), the archive-gap alert must
  fire on a *growing backlog* rather than only when the limit trips, and a
  full backup is taken as soon as an outage ends, because PITR before it is
  broken either way.
- **A misconfigured `repo2` stops archiving immediately**, with no error from
  Postgres: `archive_command` returns success because archiving is
  asynchronous, and the failure is only in the sidecar's own log until the
  backlog or the queue warning shows up. Enabling an offsite target on a
  running instance therefore needs `stanza-create` on the new repository
  before, or at the same time as, the configuration reaching the `db`
  container. Step 1 does this from the sidecar and step 5's Test connection
  is what proves it by hand.
- **pgBackRest speaks TLS to S3 and has no plain-HTTP option.**
  `repo-storage-verify-tls=n` turns off certificate *checking*, not TLS
  itself: against a plain-HTTP MinIO it fails with `TLS error [1:167772427]
  wrong version number`. A local S3 test server therefore has to be given a
  certificate, which is what the test recipe in
  `deploy/pgbackrest/README.md` does.

**Opus implements, in this order, each step shippable alone:**
1. ✅ **shipped 2026-09-22.** The slot model and the cloud repository:
   `.env` schema for the three slots, sidecar config publishing
   (fingerprints), pgBackRest 2.59.1, `repo2` on the cloud slot with its own
   passphrase, retention, bundle, queue limit, weekly backup, daily verify,
   and the archive-gap alert. The findings are written up above.
   As built: **the configuration cannot travel as environment variables.**
   pgBackRest rejects one that is defined but empty (`environment variable
   'repo2-type' must have a value`) and Compose cannot leave one out, so
   passing `PGBACKREST_REPO2_*` through would have stopped WAL archiving on
   every instance with no offsite target. It is generated instead as a
   drop-in under `config-include-path`, by `deploy/pgbackrest/offsite.sh`,
   which both the `db` container and the sidecar run at start. A drop-in may
   add options but must not repeat one from `pgbackrest.conf`, which is why
   everything generated is `repo2-*`.
   The `db` container needed an entrypoint wrapper for this, since
   `archive_command` runs there and must know about `repo2` from the first
   segment; it is written so a bad backup target can never stop the database
   from starting.
   `LastWalAt` is **not** `pg_stat_archiver`: with async archiving Postgres
   is told "archived" as soon as a segment is queued, so that clock keeps
   advancing while the offsite repository is unreachable. It is derived
   instead from whether repo2's newest segment has kept up with repo1's.
   The queue-tripped alert folded into the archive-gap one: a backlog of
   three segments fires first and is the signal worth acting on, since by
   the time the queue trips the WAL is already gone.
   Verified against MinIO (which needs TLS: pgBackRest has no plain-HTTP
   mode for S3), including a real outage, the alert firing, and recovery.
2. ✅ **shipped 2026-09-22.** restic carries the uploads volume and the
   logical dumps to every configured target, per-target `forget` from the
   slot's retention, `check --read-data-subset=5%`, and with it the
   plaintext prerequisite is met. Tests for the retention translation (14).
   As built: restic reads **the uploads volume and the dump directory
   directly**, not the nightly tarball. Shipping the tarball would have
   defeated deduplication completely, since a freshly compressed archive is
   new bytes end to end every cycle. The local tarball is untouched: 9.1's
   restore path is a shipped feature and this step does not disturb it.
   `BackupTargets` gained a `Kind` (`database` or `files`) and is now keyed
   on the pair. A slot holds two repositories written by two sidecars: the
   database goes to pgBackRest (cloud only), the files to restic (every
   slot). Two rows rather than two sets of columns is also what lets 9.3's
   cloud card chart one against the other, and it means the NAS and
   removable slots, which have no database repository, simply have no such
   row.
   The retention translation lives in C# (`ResticRetention`) with the tests,
   and in bash as `restic_forget_args`, the same "change one, change both"
   arrangement `BackupRetention` already uses. The case worth pinning: with
   retention off there are **no** arguments and `forget` must not run at
   all, because `forget` with no rules deletes every snapshot.
   Verified against MinIO: a snapshot of both paths, then a restore of a
   dump that came back **byte-identical** to the local original and listed
   36 tables, and uploads matching the live volume. The encryption claim was
   checked by scanning MinIO's own data files: 20MB across 11 objects, none
   containing the `PGDMP` header or the string `Tesria`, with a control file
   proving the scan could detect them.
   **Corrected 2026-09-22: it ran every minute.** Found during the Test
   connection walk, when a new MinIO repository gained a snapshot a minute.
   `offsite_tick` is called on every pass of the loop (`POLL_SECONDS`, 60),
   and it copied to the cloud and the NAS unconditionally: a snapshot a
   minute kept for the whole retention window, and a 5% read check a
   minute, which against a real provider downloads about 72 times the
   repository a day, far past B2's free egress. The comment on
   `offsite_tick` already said the right thing ("local first, then
   replicate"); the code did not do it. None of the step 2 to 6 walks could
   see it, because each looked at one pass.
   A scheduled slot is now copied **once per local cycle**
   (`offsite_copy_due`): when a local backup has completed since the slot's
   `LastBackupAt`, or when it has never been copied, or when `.env` now
   points it somewhere else (another bucket, path or passphrase), so a new
   target does not wait a day for its first copy. Persisted, so a restart
   does not copy again.
   A second fault under the first: **an unreachable target held the whole
   sidecar**. restic retries a refused connection for up to about fifteen
   minutes, and while `restic list locks` waited, so did every queued job
   (Back up now, a restore test, a restore) and the other targets. Every
   scheduled copy now opens the repository first with the same 30-second
   probe Test connection uses, fails fast with the reason on the card, and
   backs off for `OFFSITE_RETRY_MINUTES` (15) before trying again.
   Verified against MinIO over 25 minutes: one copy per slot on start (new
   targets), none for five minutes, exactly one each after a local backup,
   none after a restart; with MinIO stopped, the cloud failed in 31 seconds
   ("Could not reach minio:9000"), backed off, and the NAS copied straight
   after; back up, one catch-up copy and the card's message cleared. Three
   snapshots in 25 minutes, where the old loop would have taken 25.
3. ✅ **shipped 2026-09-22.** The path mechanism: host mount plus bind
   mount plus sentinel, shared by the NAS and removable slots; the NAS slot
   scheduled with its absence an alert; the SFTP `repo3` recipe and the
   NAS-snapshot recipe in the runbook.
   As built, and worth knowing before step 4 uses the same mechanism: **on
   macOS, Docker Desktop must be granted access to the mounted share**, and
   until it is, the bind mount *hangs* rather than failing. It looks exactly
   like a stuck backup. Allowing it once fixed it and the mechanism then
   worked unchanged; this is in the runbook.
   `BackupTargets` gained `Present`, kept separate from `Enabled`, because
   for a path target they are different questions: a removable drive is
   configured and absent most of the time, which is normal, while a network
   drive that is absent is a problem. Only the NAS raises
   `backup.offsite_absent`; alerting on a drive in a drawer would train
   people to ignore the whole class.
   The default for an unconfigured path slot is a committed empty directory
   with no sentinel, since Compose cannot leave a mount out, and a path with
   no sentinel is exactly what "not there" already means.
   Verified against a real NAS over SMB, in a folder set aside for it: the
   unclaimed share was reported absent with nothing written, claiming it
   started backups, the copy on the NAS restored **byte-identical** with 36
   tables, and a scan of the NAS files (with a control) found no `PGDMP`
   header and no plaintext.
4. ✅ **shipped 2026-09-22.** The removable slot: `Copy now` as a queued
   job (`copy-offsite`), `--keep-last` with no window, `check` then `sync`
   then "safe to remove", the FAT32 warning, and the
   `backup.offsite_manual_only` warning when a drive is the only target.
   As built, two things the design did not anticipate, both now in the
   runbook. **"Safe to remove" is about the data, not the eject:** `sync`
   flushes every byte, so pulling the drive cannot lose the backup, but the
   sidecar holds a bind mount that keeps the drive busy, so the operating
   system refuses to eject until `docker compose stop backup`. And **the
   FAT32 warning is silent on macOS**, because Docker Desktop passes a bind
   mount through its own file sharing and the container sees `fuse`
   whatever the drive is; it reports properly on Linux. A warning that is
   sometimes silent beats one that guesses.
   Verified with a mounted disk image behaving as a removable volume:
   unclaimed reported absent with nothing written, claiming plus a queued
   job copied and verified and reported safe to remove, the copy restored
   **byte-identical** with 36 tables, a malformed job was refused, and the
   manual-only warning fired while the drive was the only target. APFS
   rather than exFAT, because `diskutil` will not make a blank exFAT image;
   the filesystem is not what the mechanism depends on.
5. ✅ **shipped 2026-09-22.** The Storage targets screen: a card per slot
   built from the published configuration, Copy now, warnings.
   As built: **Test connection was dropped**, because by the time the screen
   existed it had nothing to add. The sidecars already test every target on
   every pass and publish the result, so the card is never more than a
   minute stale, and a button that re-ran what just ran would be a second
   way to say the same thing. If a target can be configured but not yet
   exercised, this is worth revisiting.
   **Added back 2026-09-22, at the owner's request.** The case for it turned
   out to be the one above: somebody has just changed `.env` (a key
   rotated, a share remounted, a drive plugged in) and wants the answer now
   rather than at the next pass. It queues a `test-target` job for each
   agent that writes to the slot (both, for the cloud), and each opens its
   repository without changing it: `restic --no-lock cat config` then
   `snapshots`, and `pgbackrest --repo=2 info`, not `check`, because `check`
   forces a WAL switch. The answer is the sidecar's own sentence, mapped
   from what the tools actually print for each failure (checked against
   MinIO: wrong key, wrong secret, missing bucket, unknown host, wrong
   passphrase, empty path). restic retries a refused key for minutes, so the
   test has a 30-second limit and reads the reason out of the retry lines.
   A failed test raises no alert: nothing was being backed up.
   Three bugs the walk found, none of which the shell testing could have.
   A **stale restic lock** left by a sidecar killed mid-run wedges a
   repository permanently, and the error reads like a broken target; runs
   now clear a stale lock first, which also means a container restart during
   a backup is survivable. `cat config` failing does **not** mean a
   repository is missing (a lock or a slow share look the same), so an
   `init` that is refused for already existing is treated as "there but
   unreadable this time" rather than a fault. And an absent target was
   storing its absence as a *message*, which then sat on the card after the
   drive came back saying "not plugged in" next to a green Healthy; absence
   is a state now, the screen says it in its own words, and coming back from
   absent clears what was said while away.
   Walked as an administrator with all three targets live, and as a plain
   member, who is refused the overview and the copy endpoint alike.
6. ✅ **shipped 2026-09-22.** The restore drill, the "the machine is gone"
   chapter in `backup-recovery.md`, `architecture.md`, CHANGELOG.
   As built: the drill is its own thing rather than an extension of 9.1's
   restore-test job, because it answers a different question. 9.1 restores
   what is on this disk; the drill pulls the newest dump back **out of an
   offsite target**, loads it into a throwaway database and counts the
   tables. Monthly by default (`OFFSITE_DRILL_DAYS`), one target per pass,
   last in the pass so that proving a copy never delays taking one.
   A failed drill is a **critical** alert and outranks everything else on
   the card, which is the point: a backup that fails is noticed, while a
   copy that quietly will not restore looks healthy in every other respect
   until the morning somebody needs it. Demonstrated rather than asserted:
   against a repository `restic check` calls clean ("no errors were found"),
   the drill failed.
   `verify.sh` gained `--repo=N`, so the physical restore test can be run
   against the offsite repository instead of the local one.
   One bug worth recording, because it reads correctly and does nothing: a
   `trap ... RETURN` used for cleanup fires *after* the function returns,
   when its locals are gone, so under `set -u` the trap body dies on the
   first variable it touches and silently skips the cleanup it exists for.
   The drill creates a database and restores a whole dump, so that would
   have filled the disk a drill at a time. Cleanup is explicit now.

**Verify** end to end with the scratch instance: configure all three
slots, run a backup, unplug the drive and unmount the NAS and confirm the
sidecar reports both plainly and writes nothing to the boot disk, then
restore from the cloud copy alone into the scratch instance and open a
page. Then the drill everyone skips: delete the `.env` passphrase for one
slot and confirm that slot refuses to run rather than falling back.

**Sources:** pgbackrest.org (configuration, user-guide, command,
release notes); pgbackrest issues 2782, 2854, 2148, 592;
restic.readthedocs.io (preparing a new repo, forget, working with repos,
faq); restic 0.19.1 release notes; github.com/restic/rest-server;
rclone.org (crypt, sync); docs.docker.com (volumes, secrets);
moby/moby#47153; AWS S3 Object Lock and pricing pages; Backblaze B2
pricing, Object Lock and application-key docs; Wasabi pricing and FAQ;
Cloudflare R2 pricing and bucket locks.

---

### 9.3 Space charts on the backups page · `M` · Model: Opus · ✅ **shipped 2026-09-22**

**What the owner asked for (2026-09-21).** A pie chart on the backups page
showing backups against overall disk usage against free space, and one such
chart per backup target, so that a person with a NAS or a drive configured
sees a chart for each. "Every backup target should be represented here."

**What each chart says.** Three slices: **backups** (this target's backup
data), **other** (everything else on that disk or share), **free**. The
numbers sit beside the chart, in bytes a person can read and with the time
they were measured, because the numbers are the point and a pie without
them is decoration. The caption names the mount, for the reason below.

**Where the numbers come from, and the one that is missing.** The sidecar
already measures free and total bytes for its volume on every run
(`deploy/backup/common.sh`, `df`, into `BackupAgent.VolumeFreeBytes` and
`VolumeTotalBytes`). It does not measure what its backups occupy, so that
is the whole data-model change for the local chart: `VolumeBackupBytes`, a
`du` of the agent's backup path, and the filesystem's identity
(`df --output=source`) so that two agents on one filesystem draw **one**
chart rather than two of the same disk. "Other" is then total minus free
minus backups. Both agents normally share a filesystem (on this Mac, always:
see below), so the local card usually has one chart; two only when the
logical dumps and the pgBackRest repository genuinely live on different
disks. The app never runs `df` or `du` itself; it renders what the sidecar
published, the same rule as everything on this page.

**On a Mac the disk is Docker's.** Under Docker Desktop the volumes live on
the Docker VM's virtual disk, which has its own size cap in Docker Desktop's
settings, so the chart will not match Finder and should not. The caption
says which mount was measured, and the runbook has one sentence on why the
number differs from the Mac's own.

**The other targets (after 9.2).** 9.2's sidecar publishes per-slot status
into the 9.1 tables; each slot gains the same three numbers and a measured
time, taken once per run and on Test connection. **NAS:** `df` on the mount
gives the share's total and free (SMB reports them), and backups are the
restic repository's size on it. **Removable:** the same, but only while the
drive is present; when it is absent the card shows the **last-known** chart
labeled "as of <date>, drive not present", because the question the chart
answers ("is my drive filling up") is still worth answering from the last
copy. **Cloud:** there is no disk and no free space, and a pie that invents
one would be a lie. The cloud card instead charts **composition**, the
database repository against the files repository, with the total stored
and an **estimated monthly cost** from the provider's list price (one
constant per provider, with the date it was checked; the 2026-09 figures
are in 9.2's research). An optional `OFFSITE_CLOUD_BUDGET_GB` in `.env`
gives the pie a denominator for people who want a "free space" feel, stored
against remaining budget; without it, composition only. That is how every
target is represented without a number being made up for one of them.

**The alert the chart exists to prevent.** "Backups stopped because the
disk was full" is the classic failure, and a chart nobody looks at does not
prevent it. The same numbers drive a **low-space warning** on the card and
in 9.1's alerts, with a threshold that scales itself: free space below the
size of the two largest backup sets on that target, rather than a
percentage, which means the wrong thing on a 100 GB disk and a 10 TB NAS.

**Rendering.** The editor's pie in `editor/ChartView.tsx` is a small
themed SVG with `role="img"` and a label; extract that drawing into
`components/PieChart.tsx` and use it in both places, so there is one pie
in the product and no new dependency. Theme tokens for the slices, the
legend carrying the bytes, and the aria label reading the three numbers
out.

**Dependencies.** The local chart depends on nothing and can ship now. The
NAS, removable and cloud charts depend on 9.2 steps 1 to 5, since they draw
what 9.2's slot status publishes.

**Opus implements, in this order, each step shippable alone:**
1. ✅ **shipped 2026-09-22.** The sidecar measures `VolumeBackupBytes` and
   the filesystem identity; the migration; tests that two agents on one
   filesystem collapse to one chart and on two do not (13).
   As built: `du` runs on its own clock (`BACKUP_MEASURE_SECONDS`, ten
   minutes) rather than every heartbeat, because it walks the whole backup
   directory and the number moves slowly.
   A test caught a real bug: when `df` and `du` disagree, which they will,
   being two commands moments apart, clamping each number separately made
   the slices sum to **more than the disk**. `df`'s free space is now taken
   as authoritative and the backups are fitted into what is actually used,
   so the three always add up.
2. ✅ **shipped 2026-09-22.** `PieChart.tsx` extracted from the editor, the
   local chart with its legend and caption, and the low-space warning.
   The extraction is exact: the editor's pie still renders with the same
   `viewBox`, the same `M60 60 ... A55 55` geometry and the same colors,
   checked in the editor rather than assumed. It also gained a fix on the
   way: a pie of a single slice used to draw a degenerate arc, which is
   invisible, and is now drawn as a circle.
   The first colors chosen were surface and border tokens, which are
   nearly the card behind them and drew a solid disc with no readable
   slices. They are now blue for ours, gray for everything else, green for
   free, which stay distinct in both themes.
3. ✅ **shipped 2026-09-22.** The per-slot numbers, and the cloud card's
   composition chart with the total stored and an estimated monthly cost
   carrying the date its prices were checked.
   **Not done: the optional `OFFSITE_CLOUD_BUDGET_GB` denominator.** It
   would give the cloud pie a free-space feel, and it is the one part of
   this item that invents a number rather than reporting one. Worth adding
   if anyone asks for it; left out rather than guessed at.
   **Added 2026-09-22, at the owner's request.** `OFFSITE_CLOUD_BUDGET_GB`
   is published by the pgbackrest sidecar as `BudgetBytes` on both cloud
   rows, in a statement of its own so a sidecar ahead of the migration
   loses only the budget. The pie gains a "Left in budget" slice, never
   called free space, and passing it shows "Over budget by" in the danger
   color. It refuses and removes nothing, and raises no alert: a budget
   somebody picked is a number to watch, not a limit.
   The NAS and removable cards show composition only when a slot holds two
   repositories, which in practice is the cloud alone, since the others
   carry files and never the pgBackRest repository.
4. ✅ **shipped 2026-09-22.** `architecture.md`, the runbook paragraph on
   the disk, CHANGELOG.

**Corrected the same day, after the owner checked the number against macOS.**
The chart said 1.6 TB free; his Mac said 761 GB. Both were "right": `df` on
the container's own volume reports **Docker's virtual disk**, which under
Docker Desktop is sparse and reports the size it may grow to rather than the
space the host can still give it. For a warning whose whole purpose is to
fire before backups fill the disk, that is the worst possible error, because
it fires late or never.

The fix is that a **host bind mount** is passed through the host's own
filesystem, so `df` on one reports the host's real figures. Both sidecars
already mount their scripts that way, so the measurement now uses that path
(`BACKUP_HOST_REF`) and reads 706 GB free of 1.8 TB, which is what the Mac
reports. On a Linux host the two are usually the same filesystem and nothing
changes. This is the difference between a chart that decorates and one that
can be trusted, and it was only found because somebody compared it to the
operating system.

Three smaller changes at the owner's request, the same day:

- A **live wiki** slice, so the chart answers "how much is the wiki and how
  much is its backups" rather than lumping the wiki in with everything else.
  Each sidecar measures the part it can see, the attachments for one and the
  database directory for the other, and those add up.
- A **drop shadow** on the cards, which sit on a section of the same color
  and read as one flat block without it. Deepened in dark mode, where the
  light-background alpha disappears.
- The **administration area uses the width it is given**. It was capped at
  the 900px measure meant for prose, so on a large display the tables
  scrolled sideways inside a narrow column with empty space either side. The
  cap is gone and prose inside is held to 80ch so it stays readable.
  A first attempt also capped the card tracks, on the theory that a lone card
  should not stretch across a 4K display. The owner was right that this was
  worse: it stopped the two agent cards short of the edge and squeezed the
  chart into wrapping text. The tracks are `1fr` again and the cards split
  whatever width they are given.
  Administration → Settings needed the same treatment separately: its forms
  carry the 480px form width, so as one column they left most of a large
  display empty. They are a grid now, four across at 1990px.

**Colors, after the owner looked at it.** Gray for "everything else" read as
*disabled* rather than as a slice, and the wiki and its backups were shades
close enough to be taken for each other. The four slices are now four
distinct hues with their own tokens (blue, magenta, orange, green), lifted
for dark mode where the light-theme values go muddy. The pie itself also
gained the drop shadow the cards have, so it lifts off the card the way the
card lifts off the section.

**Verify** by filling a scratch volume with a large file and watching the
"other" slice grow and "free" shrink on the next run; delete it and watch
them return; take a backup and watch "backups" grow. Then the warning:
shrink the volume until free space is under two backup sets and confirm the
card and the alert both say so. After 9.2: unmount the NAS and confirm its
chart stays with a "not reachable" note; unplug the drive and confirm "as
of <date>, drive not present"; point the cloud slot at MinIO and confirm
composition and cost.

---

### 9.4 Restore from the admin page · `L` · Model: Fable → Opus · ✅ **shipped 2026-09-22** (Fable designed, Opus implemented)

**What the owner asked for (2026-09-22).** The backups page has Test
restore on every row but nothing that restores one: "It appears that you
can only restore to the most recent backup which may not be desired. What
if the user wants to select an older backup and restore that?" Offered a
choice between a runbook pointer and a real restore from the page, the
owner chose the restore, gated hard, and both kinds of backup: "Let's do B
and lets cover both."

**Designed 2026-09-22 (Fable). The owner answered every decision the same
day; the answers are recorded with the questions at the end, and the design
reflects them. Ready for Opus.** Prerequisites, all shipped: 9.1 (the job
contract), 9.2 (offsite copies exist, so the local disk is never the only
copy being touched), 10.1 (the owner), 11.1 (rights), 11.3 (the
password-in-the-request pattern).

**What does not change.** The app still never touches a backup file, and
9.1's contract stands: the app appends a `requested` job, a sidecar does
the work and owns every update, and the sidecars alone hold the owner
credential. What changes is the worst thing a web request can cause. Until
now that was a backup; from here it is the wiki being replaced by an older
one. So the gate is the center of this design, and it is stronger than
11.3's, which guards one space.

**The problem that shapes everything: the status lives in the thing being
replaced.** `BackupJobs`, `BackupAgents`, `BackupTargets`, `SiteSettings`,
the sessions, the audit chain and the data-protection keys are all rows in
the database a restore replaces. Today's `deploy/backup/restore.sh`
terminates every connection, drops the database and rebuilds it from the
dump: a job row recording that would be destroyed by its own job, and the
page watching it would read the restored past as if it were the present.
Point-in-time recovery is harder still: the whole cluster is rewritten with
Postgres stopped, so for minutes nothing can be read at all. Three rules
answer this:

1. **Restore beside, then swap.** A logical restore goes into a *new*
   database (`<db>_restore`) and is verified there while the wiki stays up,
   read-only. The swap is two renames and takes milliseconds. The old
   database is renamed, not dropped: `<db>_pre_restore`, which is the undo,
   instant and complete.
2. **The sidecar remembers what the database cannot.** Every restore has a
   directory on the sidecar's own volume (`/backups/restores/<jobId>/`: the
   request, the progress, the log, the tables carried across). After the
   swap the sidecar writes the job's final row into the *new* database from
   that directory. The backup history (`Backups`, `BackupJobs`,
   `BackupTargets`) is exported before the swap and re-imported after,
   because those tables describe the disk, not the wiki, and a restore must
   not make the page forget which backups exist.
3. **The app restarts itself when a restore completes.** Startup is the one
   path that already does everything a restored database needs: migrations
   (the dump may predate a schema change), the runtime role's grants (a
   dump carries none; `--no-privileges`), the seeds, the settings cache, and
   dropping every pooled connection and cached object. A runtime re-migrate
   would be a new path with the same steps; the restart is the trodden one,
   and compose's `restart: unless-stopped` brings the container back
   whatever the exit code.

**The gate.** Every one of these, in this order, none skippable:
- **A right of its own, `backups.restore`** (Administration scope,
  `DefaultFrom: Owner`, like `users.promote_admins`: off for administrators
  until the owner grants it to a role). Admin-tier rights already require
  two-factor.
- **The backup's label typed back**, exactly as displayed, for the reason
  11.3 gives: it proves the right row is on screen.
- **The password or one-time code in the same request**, verified as
  `VerifyPasswordOrCodeAsync` does for deleting a space, counting toward
  lockout on failure. The sudo window is not enough here either.
- **A safety backup first, taken by the sidecar, never optional.** A restore
  is the one moment a person most wants a backup they did not have to
  remember to take. Logical: a fresh cycle (dump and uploads archive)
  through the ordinary `do_backup`, so it appears in the inventory and goes
  offsite on the next tick. Physical: an incremental, then
  `pg_switch_wal()`, so WAL up to the moment before the restore is in the
  repository. If the safety backup fails, the restore does not start.
- **One at a time.** A second request while one is pending is 409, as
  `PendingAsync` already does for the other kinds.
- **Audited twice and alerted once.** `backup.restore_requested` is recorded
  before (the swap removes it from the live chain; the safety dump and the
  pre-restore database keep it); `backup.restored` is recorded after, in
  the new database, naming the backup, the target time, who asked and when.
  `backup.restored` is also a Critical alert with no cooldown to the owner
  and every administrator, by email where 4.3 is configured, whoever did
  it. The threat this answers is an owner-level account erasing evidence by
  restoring to before it: the safety backup keeps the evidence, the kept
  copy keeps it again, and everyone hears.

**Maintenance mode.** From the moment the request is accepted until the app
restarts after the restore, the wiki is read-only:
- `SiteSettings` gains `RestoreJobId`, `RestoreStartedAt` and
  `RestoreCancelRequestedAt`. The app also holds the job id in memory: the
  database flag is for surviving an app restart mid-restore, the memory
  flag is for the minutes when the database cannot be read.
- A middleware, early in the pipeline: while a restore is pending, every
  request that is not `GET` or `HEAD` gets 503 with `Retry-After` and a
  JSON body `{ maintenance: { reason: "restore", startedAt, jobId } }`,
  except the restore's own status, cancel and health endpoints. Reads
  continue while the live database is up, and fail for the seconds of the
  swap or the minutes of a PITR, which the SPA handles below. API tokens
  and the MCP server see the same 503.
- The SPA: any 503 carrying that body switches the app into a full overlay,
  "A restore is in progress. The wiki is read-only until it finishes,"
  polling `GET /api/health` every 5 seconds and reloading when it clears.
  For the person who asked, the backups page shows the job's phases
  instead: safety backup, restoring, verifying, switching over, restarting,
  done, with the elapsed time and the log tail. During a PITR the phases
  come from the app's memory and the clock, because the database is down;
  the detail arrives when it is back.
- **The collab sidecar has to forget what it holds.** An open editor keeps
  the page's document in memory and writes it back on the next keystroke,
  which after a restore would resurrect content from after the backup. Two
  layers: the app, on entering maintenance, posts `{ maintenance: true }`
  to the sidecar on the internal endpoint 8.6 added, and the sidecar closes
  every connection, drops every document and refuses new connections with
  503 until told otherwise or thirty minutes pass (a stuck flag must not
  brick editing); the app posts `false` at every startup. The backstop, for
  a restore the app did not announce: the sidecar's fifteen-second sweep
  reads `SiteSettings.LastRestoredAt`, and when it is newer than the
  sidecar's own start the process exits and compose starts it clean. A
  restart is the reliable way to forget.
- **Sessions and keys.** `UserSessions` and the data-protection keys are
  rows too. Sessions created after the backup are gone after the restore,
  so those people sign in again, possibly including the person who asked.
  Keys rotate every 90 days, so the key that signed today's cookies almost
  always predates the backup; when it does not, everyone signs in again.
  The dialog says both, and the completion screen says "if you are asked to
  sign in, that is expected".

**The logical restore, exactly** (`deploy/backup/restore.sh`, rewritten as
the swap; the runbook uses the same script by hand):
1. Preconditions, each a clear refusal: the dump present and readable
   (`pg_restore --list`); the cycle's uploads archive present if the
   inventory says it has one; free space on the host disk at least four
   times the dump plus twice the archive (a restored database is larger
   than its dump; the figure is conservative and the message says what was
   needed and what is free); no cancel requested; the sidecar not already
   restoring.
2. The safety cycle, verified, its label recorded in the job result as
   `safetyBackup`.
3. The carry-across export: the three backup tables to CSV in the restore
   directory, with a header.
4. `CREATE DATABASE "<db>_restore"` (a leftover from an earlier failed
   attempt is dropped first), `pg_restore --no-owner --no-privileges` into
   it. A failure drops it and fails the job; the wiki was never touched.
5. Verification in the new database: `Pages`, `Users` and
   `__EFMigrationsHistory` exist and are non-empty, and the history holds
   no migration the live database lacks. That last check refuses a dump
   taken by a newer Tesria ("this backup was taken by a newer version;
   upgrade first"), because a downgrade is not something a restart can
   repair.
6. The last cancel check.
7. **The point of no return, one psql session:** `ALTER DATABASE live WITH
   ALLOW_CONNECTIONS false`, terminate its backends (the app reconnects
   within milliseconds, which is why connections are refused first, not
   only terminated), drop an older `<db>_pre_restore` if one exists,
   `ALTER DATABASE live RENAME TO <db>_pre_restore`, `ALTER DATABASE
   <db>_restore RENAME TO live`. The kept copy stays with
   `ALLOW_CONNECTIONS false`: nothing connects to it except the undo.
8. The uploads swap: the sidecar's uploads mount becomes read-write
   (`uploads:/data/uploads`, and the compose comment says this is why); the
   current entries move into `/data/uploads/.pre-restore/` (an older one
   removed first), and the archive is extracted into place. Entry moves
   within one volume are renames, so the swap is fast and a crash leaves
   both copies, never half of each.
9. Reconciliation, in the new live database: clear `RestoreJobId` and
   `RestoreCancelRequestedAt` there (a safety dump taken during maintenance
   carries the flag, and restoring *that* dump one day must not leave the
   wiki in maintenance forever); wait for the app to restart and migrate
   (until `__EFMigrationsHistory` has at least as many rows as the old
   database had, ten minutes at most, then proceed with a note); import the
   carried tables with `ON CONFLICT DO NOTHING`; upsert the job row with
   status `succeeded`, the phases with their times, the safety label, what
   was restored, the counts, and the kept copy's sizes; set
   `SiteSettings.LastRestoredAt`, `LastRestoreJobId` and `LastRestoreFrom`.

The app's side while this runs: a hosted service polls the job row every
three seconds and tolerates connection errors (the swap).
`LastRestoreJobId` equal to the pending id, or the job row reaching
`succeeded`, means restart (`IHostApplicationLifetime.StopApplication`);
`failed` means leave maintenance and show the error. At startup, if
`RestoreJobId` is set (the app died mid-restore) maintenance resumes and
polling continues; if `LastRestoreJobId` names a job with no
`backup.restored` audit event yet, the event and the alert are written
then. That startup check is how the audit entry lands in the right chain,
robust to any number of restarts, and idempotent.

**The kept copy's lifetime (the owner's decision 2).** The retention policy
governs it, by the same rule as a backup. On every retention pass the
logical sidecar enters the kept copy into the plan as if it were a backup
taken at the moment of the restore, and removes it when the plan would
remove that backup: drop `<db>_pre_restore`, delete `.pre-restore`, and
record a `restore-discard` job with trigger `retention` so the page shows
when and why it went. Retention off keeps it until someone removes it by
hand, as retention off keeps everything. The 24-hour grace on a stricter
policy applies to it too, since it is the same pass. The Previous copy card
shows the date the policy will remove it, computed by `BackupRetention.Plan`
in C# with the kept copy included in the list, which is the code the policy
preview already uses. After a PITR there is no kept copy: its undo is the
safety incremental and the WAL, which the physical retention already
governs the same way.

**Point-in-time recovery, exactly.** pgBackRest restores into the data
directory with Postgres stopped, and nothing outside the `db` container can
stop Postgres without a Docker socket, which no sidecar gets. So the `db`
container stops and starts its own database:
- `deploy/db/entrypoint.sh` (which 9.2 already owns) becomes a small
  supervisor: it starts the image's own entrypoint as a child, forwards
  `SIGTERM` and `SIGINT` to it so `docker compose stop` behaves exactly as
  today, and every two seconds looks for a request at
  `/var/run/postgresql/tesria-restore/request`. That volume is shared by
  `db` and the `pgbackrest` sidecar and nothing else, which is the
  authorization model: only the sidecar that validated the job can ask,
  and the web tier has no path to that file.
- The request names the job id, the type (`time` with a target, or
  `immediate` with a `--set` label, meaning "the end of that backup") and
  the repository (`1`; see the decisions). On seeing it the supervisor
  moves it to `running`, writes a `status` file it updates at each phase,
  stops Postgres (`pg_ctl stop -m fast`), runs today's
  `deploy/pgbackrest/restore.sh` logic (`--delta`,
  `--target-action=promote`), starts the entrypoint again (it skips
  `initdb` when `PG_VERSION` exists), waits for `pg_isready`, and writes
  `done` or `failed` with the reason. A request whose job id it has already
  handled, or whose file is older than ten minutes, is ignored and logged:
  a stale file after a container restart must never restore twice.
- The `pgbackrest` sidecar's job: claim, preconditions (the target inside
  the bounds, no cancel, the repository verified within the last day or
  verified now), the safety incremental and the WAL switch, the
  carry-across export, the request file, then wait on the status file
  (hours at most), then `wait_for_db`, then the same reconciliation as the
  logical path. The next physical backup after a PITR is a **full**, taken
  at once: pgBackRest can continue incrementally across a timeline switch,
  but a full is cheap here and makes the new timeline's chain stand on its
  own.
- Bounds on the page: from the end of the oldest present physical backup
  to `WalArchivedAt`, in the person's local time with the offset shown.
  pgBackRest chooses the backup set for a time target itself (2.32 and
  later; 2.59.1 is pinned; Opus confirms it live). The default for a
  physical row is "the end of this backup", which is `--set` plus
  `--type=immediate`.
- Undo for a PITR is another PITR, to the moment the restore began
  (`RestoreStartedAt`, which the safety incremental and the WAL switch made
  reachable). The Previous copy card offers it with that time filled in.
- The database's healthcheck fails while it is stopped, and that is
  correct: compose does not restart on a failed healthcheck, and the
  supervisor is PID 1 and never exits. If the container *is* stopped
  mid-restore, the supervisor marks the request failed and the runbook's
  by-hand procedure applies.

**The preview.** `GET /api/admin/backups/{label}/restore-preview?at=`
returns what the dialog shows before anyone types anything: when the
backup was taken (or the target time), and what has happened since, by
the same method as 11.3's deletion preview: page versions saved, pages
created, comments, attachments and their bytes, accounts created, and how
many sessions will end. Plus the sizes involved, the free space against
what is needed, and a `blockedBy` list (sidecar offline, a restore pending,
no uploads archive, low disk) so the button is disabled with a reason
rather than failing later.

**Endpoints** (all `backups.restore`; the password gate on the four that
change anything):
- `GET /admin/backups/{label}/restore-preview?at=`.
- `POST /admin/backups/{label}/restore`, body `{ confirmLabel, password?,
  code?, at? }`. `at` only for a physical backup, refused outside the
  bounds. Appends a `restore` job with `Target = label` and a new nullable
  `OptionsJson` column (`{ "at": "...", "mode": "logical" | "pitr" }`),
  sets the maintenance flags, tells collab, records the audit entry. 202
  with the job id.
- `POST /admin/backups/restore/cancel`: while `requested`, the job is
  failed with "canceled" and maintenance ends; while `running`, it sets
  `RestoreCancelRequestedAt`, which the sidecar honors at its last check
  before the point of no return, and the response says so. After that
  point there is no cancel, only undo.
- `POST /admin/backups/restore/undo` and
  `POST /admin/backups/restore/discard-kept`, both jobs. `restore-undo`
  swaps the kept database and uploads back, the same swap in reverse and
  with the same safety cycle first; after a PITR it queues a PITR to
  `RestoreStartedAt`. `restore-discard` drops `<db>_pre_restore` and
  removes `.pre-restore`, gated because it is the destruction of the undo.
- `GET /api/health` reports `maintenance` when set, anonymously, for the
  overlay.

**UI.** Every backup row gains **Restore** beside Test restore, shown only
with the right. The dialog (a modal with a form, as 11.3): what is in this
backup; what has happened since it (the preview counts, in words); what
will happen (read-only now, a safety backup first, the switch, a restart,
sign-ins possibly ending); the undo ("the previous copy is kept until you
remove it or the retention policy does"); for a physical row a time field, bounded, defaulting to the
backup's end; "Type **20260922T031200Z** to confirm"; the password or code;
and **Restore now**, disabled until every field is filled and `blockedBy`
is empty. Afterwards the backups page carries a **Previous copy** card
while a kept copy exists: when it was kept, its sizes, the date the
retention policy will remove it, **Undo the restore** and **Remove**. The
kept database counts in the space chart's wiki slice, and the card says so.

**Two sidecar findings to fix in step 1.** (a) The dispatch in `common.sh`
treats any kind it does not know as a backup and runs one; a `restore` job
reaching an older sidecar would take a backup and report success. Unknown
kinds must fail with "this sidecar does not know how to run X". (b)
`agent_start` fails every `running` job of its agent on restart, which is
right for a backup and wrong for a restore whose sidecar restarted after
the point of no return; the restore directory is the truth, and the
sidecar reconciles from it on start before anything else.

**Steps for Opus, in order; each a commit that leaves the product
working.**
1. *Foundations.* The right; the job kinds and `OptionsJson`; the
   `SiteSettings` columns; the maintenance middleware, the health field,
   the SPA overlay, the collab maintenance endpoint and the app's calls to
   it; the preview, request and cancel endpoints with the full gate; the
   audit events, the alert, the startup audit from `LastRestoreJobId`; the
   `SecurityMonitor` change (a chain shorter than last time is explained,
   not warned about, when `LastRestoredAt` is newer than its last run); the
   two sidecar fixes. A `restore` job ends in a clean failure ("not
   supported by this sidecar yet"), so the whole path can be walked end to
   end before anything can be destroyed.
2. *The logical restore.* `restore.sh` as the swap, the sidecar job, the
   uploads swap, the carry-across, the reconciliation, the app's poller and
   restart, the restore panel, the Previous copy card, undo and discard,
   the runbook. Live tests below.
3. *Point-in-time recovery.* The supervisor, the request protocol, the
   `pgbackrest` job, the time field and its bounds, the forced full, undo
   by PITR, the runbook.
4. *Docs.* `docs/security.md` (a layers row: what an owner-level session
   can now do and what answers it; the password-in-request table gains the
   restore), `architecture.md`, the changelog, and a note for 10.5.

**Tests.** Unit (SQLite): 403 without the right and 200 with it granted to
a custom role; 400 on the wrong label; 401 on the wrong password, nothing
queued, the failure counted toward lockout; a one-time code for an SSO
account; 409 while one is pending; 400 for a time outside the bounds; the
preview counts; the middleware (GET passes, POST is 503 with the body, the
allow-list passes, health reports it); cancel in each state; the startup
audit is written once and only once; the monitor explains rather than
warns; the kept copy is removed by the retention plan exactly when a backup
of its age would be, never before, and never with retention off. Bash: `RESTORE_DRY_RUN=1` prints every step and changes nothing,
and the schema comparison is tested with a fixture history. **Live, on
this instance** (it holds test spaces and has offsite copies, and the
safety backup runs first regardless): create a marker page, restore an
older logical cycle, confirm the marker is gone, the kept copy has it, and
undo brings it back; PITR to a minute before the marker, then undo; cancel
before the point of no return leaves the wiki untouched and no `_restore`
database behind; kill the app container during `pg_restore` and see it
resume; the low-disk refusal with an artificially low threshold; an editor
open in another tab is disconnected at the start and reconnects to the
restored page; `docker compose stop`, `start`, `restart db` and
`down`/`up` behave exactly as before under the supervisor, with `stop`
completing in under ten seconds; a stale request file is ignored on
start. The `.env` is restored byte for byte after, as in 9.2.

**Open decisions for the owner, all answered 2026-09-22.** The questions
stay as the record of what was asked.
1. Who holds `backups.restore` by default: the owner only, grantable to a
   role (recommended), or the owner and administrators. **Answered: the
   recommendation.**
2. The kept copy: until removed by hand, or removed automatically. **Answered:
   the retention policy removes it "when it reaches end of life by the
   policy definition"**, which is the paragraph on the kept copy's lifetime
   above: it ages like a backup taken at the moment of the restore and goes
   when the plan would remove that backup.
3. A logical restore always restores the cycle's uploads with the dump
   (recommended: 9.1 made the cycle the unit), or offers a choice.
   **Answered: the recommendation.**
4. Restoring from an offsite copy from the page: not now (recommended).
   The page restores what is on the local disk; when the local disk is gone
   the app is gone with it, and that is the runbook's "The machine is
   gone". The neat later form is "fetch this offsite backup to local",
   after which the page restores it like any other. **Answered: the
   recommendation.**
5. The `db` container gains a supervisor in front of Postgres. It is the
   riskiest change here, because it sits in the database's start path.
   Accept it (recommended, shipped as step 3 so the logical restore lands
   and is used first), or keep PITR by hand and ship steps 1, 2 and 4.
   **Answered: the recommendation.**

**Not decided here, deliberately:** restoring a single space or page out of
a backup rather than the whole wiki (the swap design makes it reachable:
the `_restore` database can be queried before it is dropped); a downgrade
(a dump from a newer schema is refused, not handled); more than one `app`
container (the in-memory flag assumes one, as the compose file does
throughout).

**As built (2026-09-22), where it differs from the spec above.** All four
steps shipped together. Three bugs were found by running it against this
instance, and none of them would have been found any other way, which is the
argument for the live walk in one paragraph.

- **The restart signal is the database's OID, not the migration count.** The
  spec said the application restarts when the restore finishes, and left how
  it learns that open. The obvious answer, reading `LastRestoreJobId`, cannot
  work: a restored database is an older one and may not have that column, so
  the application waits for a signal that cannot arrive while the sidecar
  waits for the restart that would migrate it. A real deadlock, hit on the
  first live run. The second attempt, comparing the row count of
  `__EFMigrationsHistory`, failed for an independent reason: a dump restores
  `--no-privileges`, so the runtime role has no grants on the restored
  database and cannot read that table either. What works is
  `SELECT oid FROM pg_database WHERE datname = current_database()`:
  `pg_database` needs no grant and no schema, and the OID behind the name
  changes when the swap renames one database out and another in. It says the
  true thing directly, "I am connected to a different database than the one I
  started with".
- **`restore.sh` sources `common.sh`.** It is run as a child process, not
  sourced, so the `restore_*` helpers simply did not exist inside it and the
  carry-across, the cancel check and the completion record were all skipped
  in silence. The wiki restored perfectly and its own bookkeeping vanished,
  which is exactly the failure the restore directory was designed to survive,
  and did: the phases and the log were on disk when the database had no idea
  any of it had happened.
- **Two smaller ones from the same run.** `common.sh` set `RESTORE_DIR=""` at
  the top level, which clobbered the value `restore.sh` inherits from the
  environment, so everything written "to the restore directory" went nowhere.
  And the carry import used `CREATE TEMP TABLE ... ON COMMIT DROP` without a
  transaction: psql commits each statement on its own, so the staging table
  was dropped the instant it was created and every `\copy` into it failed.
  Both are now wrapped and commented with what they cost.
- **The maintenance middleware lets the restore's own endpoints through.**
  The spec listed cancel and health as the exceptions. In practice a second
  "restore this backup" while one runs should get the endpoint's own 409
  naming the reason, not a blanket "the wiki is read-only", so the whole
  restore subtree passes and each endpoint keeps its own gate.
- **The preview counts in memory.** SQLite cannot translate a
  `DateTimeOffset` comparison, the same limitation 8.5 met with ordering, so
  the counts come from a projection of timestamps rather than `CountAsync`.
  It keeps the endpoint identical on both providers.
- **The completion service also checks once a minute when idle.** A restore
  can finish after the application has already restarted and given the wiki
  back, so the lasting audit entry would never be written by the startup path
  alone.
- **`docker compose stop db` is now fast.** Interposing a supervisor made the
  shutdown signal a choice, and Postgres reads SIGTERM as a *smart* shutdown
  that waits for every client: the container used to wait out its timeout and
  be killed. The supervisor forwards SIGINT instead. Measured at under a
  second against the ten it used to take.
- **The restore dialog is wider than the other confirmations** (34rem rather
  than 440px). It carries more to read, and at the shared width that was a
  column of two-word lines.

**Verified live on this instance (2026-09-22).** Four full restore cycles and
four undos, the last two driven entirely through the admin page: the marker
page written after the backup disappeared and came back each time; the
application restarted itself on both the restore and the undo, logging the
OID change; writes were refused with 503 while reads kept working; the health
endpoint reported maintenance anonymously; the backup history carried across,
including the undo job carrying its own row; exactly one `backup.restored`
audit entry and one Critical alert; and an administrator without the right
was refused at the endpoint and shown no Restore button. The instance ended
with 34 pages, 8 spaces and 6 attachments, which is what it started with,
and the `.env` byte-identical.

**Corrected 2026-09-22: every restore that was undone stayed "running".** The
owner noticed five restores on Recent runs still saying Running…, and the
page polling them every five seconds for ever. The walk above checked that
history carried across; it did not check that it carried across *correctly*.
The kept copy an undo puts back is a snapshot taken mid-restore, so it holds
that restore as "running", and the carry-over added missing rows but never
updated existing ones, so the snapshot's stale row beat the finished one.
The same applies to any restore: an older dump holds the job that took it
as "running", and one that was "requested" would have been run again.
Two fixes. The carry-over now lets a job row the carried copy shows further
along (requested, running, finished) take the carried outcome. And each
sidecar closes, at the top of every pass, any job of its own still marked
running, since it runs one job at a time and so cannot really be running
one then; a restore or undo takes its outcome from its restore directory,
anything else is recorded as interrupted. The one exception is the restore
the database records as in progress, because a point-in-time restore is
carried out by the db container and the sidecar restarting while it waits
must not end maintenance under a cluster still being rewritten. The five
closed as succeeded, with the finish times their own logs recorded.

---

## Phase 10: Owner and onboarding

Written 2026-09-20 by Fable 5.1 at the owner's request, from the code as
it stands after 9.1. Four items, in order: **10.1** the Owner role,
**10.4** the media harness (the clips the tours use), **10.2** first-run
setup for the owner, **10.3** the tour and tips for everyone else. Each is
specified below in full and is ready for Opus.

**What the owner asked for.** A built-in onboarding: on a fresh server, the
first sign-in walks the administrator through creating their account and
configuring the instance (retention policy, email server, and so on). A new
role, **Owner**, above admin; the first account is the owner. Onboarding for
every new user explaining spaces, the editor and the rest, with recorded
clips or at least screenshots, and power-user features taught as tips (the
`/` menu, for one). Tips can be turned off and the non-essential parts of
onboarding skipped; the critical first-time owner setup cannot be.

**The owner's decisions (2026-09-20), fixed:**

1. **Owner powers are ownership only.** Only the Owner promotes or demotes
   administrators and transfers ownership. Administrators keep every other
   power they have today.
2. **Four owner setup steps cannot be skipped:** saving recovery codes,
   instance name and address, the registration mode, and the backup
   retention policy. Everything else (email, two-factor, the first space)
   can be skipped and finished later from Administration.

**Decisions made here (Fable), which the items below depend on:**

- **Exactly one owner, always.** Ownership moves by transfer, never by
  editing a role. The owner cannot be demoted, suspended, deleted or
  have the role set through the role endpoint.
- **On upgrade, the earliest-created active administrator becomes the
  owner** and the setup wizard is marked complete, so an existing instance
  changes nothing visible except a badge. A fresh instance runs the wizard.
- **The wizard is enforced by the SPA and by data, not by blocking the
  API.** The owner's own API calls are what complete the steps; blocking
  them would fight the wizard. `/auth/me` says `setupRequired`; the SPA
  routes the owner to `/setup` until it is false. The server decides
  "complete" from evidence it holds, not from a flag the client sets.
- **Clips are silent WebM loops with a PNG poster, in both themes,**
  recorded by the existing screenshot harness (Playwright's own video
  recording; the PDF image has no ffmpeg, so no MP4 or GIF). A poster is
  what shows where WebM will not play or when the viewer prefers reduced
  motion. GIF was the request; a video loop is smaller, sharper and pauses.
- **Tips live in code, state lives on the user.** The catalog of tips is
  a TypeScript file; which ones a person has dismissed, and whether they
  want tips at all, is on their account so it follows them across devices.
- **Existing accounts get tips, not the tour.** Accounts created before
  10.3 ships have been using the product; the tour is a link on their
  profile, not a modal on their next sign-in.

### 10.1 The Owner role · `M` · Model: Fable → Opus · ✅ **shipped 2026-09-20** (spec as Fable, implementation as Opus)

**Model.** `UserRole.Owner = 2`. Every admin check becomes `>= Admin`:
`CurrentUser.IsAdminAsync` (and a new `IsOwnerAsync`), the places that
pick administrators as recipients (`NotificationService.NotifyAdminsAsync`,
`NotificationEmailService`), `RequireTotpForAdmins` (applies to the owner
too), `DashboardEndpoints` counts, `SecurityMonitor`'s admin new-address
check, and the five frontend sites (`AdminLayout`, `Layout`,
`AdminUsersPage`; `UserRole` in `client.ts` gains `Owner: 2`). A new policy
`AuthPolicies.RequireOwner` with its own requirement handler, same shape as
`AdminRequirementHandler` including the two-factor rule.

**Invariant: exactly one owner.** Enforced in three places:
- `Register` and `OidcUserProvisioner`: the first account is `Owner`, not
  `Admin` (the existing serializable transaction already makes "first"
  safe).
- **`OwnerSeed`, a startup step beside `BackupPolicySeed`:** if users exist
  and none is an owner, promote the earliest-created **active**
  administrator; if there is none, the earliest-created administrator of
  any status; if there is none, the earliest-created user. Audit
  `owner.assigned` with `{ Source = "upgrade", Email }` (a startup step
  rather than migration SQL so the row is chained). Also set
  `SiteSettings.SetupCompletedAt = now` when it is null and any user
  exists, so an upgraded instance never sees the wizard (10.2).
- Guards on every mutation of a user: the owner's role cannot be changed by
  `SetRole` (400 "Ownership is transferred, not assigned"), the owner
  cannot be suspended (400) or deleted, and a transfer never leaves the
  seat empty.

**Endpoints.**
- `PUT /api/admin/users/{id}/role` now requires **Owner** (every role
  change is about administrators). The "only administrator" guard goes:
  the owner is always there. Promotion still raises `admin.promoted`.
- `POST /api/admin/users/{id}/transfer-ownership` (**Owner**, **sudo**,
  audited `owner.transferred` with both emails): target must be active and
  not the caller; in one transaction the target becomes `Owner` and the
  caller becomes `Admin`. Raises `owner.transferred` (Critical, alert, no
  cooldown) so every administrator, including the one who just lost it,
  hears about it. Rotate nothing: sessions stay valid; the role is read
  from the database on every request.
- `GET /api/admin/users` rows carry `role` as today; the list sorts the
  owner first.

**UI.** Users page: an "Owner" badge; on the owner's own row nothing
destructive; on every other active row, for the owner only, a **Transfer
ownership** action with a confirmation naming the person and saying the
caller becomes an administrator. Administrators see the role controls
disabled with the hint "Only the owner changes roles." Profile page: the
owner's role reads "Owner". The admin refusal text in `AdminLayout` stays.

**Docs and tests.** `docs/security.md`: a row in the layers table (what an
admin session can no longer do), and the checklist item "the owner account
has two-factor and recovery codes". `architecture.md`'s roles paragraph.
Tests (`OwnerTests.cs`): first account is owner (local and OIDC); the seed
picks the earliest active admin on an instance with several, and marks
setup complete; an admin cannot change roles (403) and the owner can; the
owner cannot be demoted, suspended, or transferred to a suspended user or
to self; a transfer swaps both roles atomically, audits, alerts, and the
old owner is now an admin; the owner passes every `RequireAdmin` route;
`RequireTotpForAdmins` binds the owner; alert emails reach the owner.

### 10.4 Onboarding media harness · `M` · Model: Opus · ✅ **shipped 2026-09-20**

Extends `scripts/screenshots/shot.mjs` so one spec produces the stills and
clips 10.2 and 10.3 embed. Runs the way the harness already runs (the PDF
image, Caddy's network namespace, a real signed-in account) against a
demo space the spec itself creates and removes.

- **`record`:** a shot with `"record": { "seconds": 8 }` opens a fresh
  context with `recordVideo` at the viewport size, runs its steps with
  their `wait`s as the pacing, closes the context, and moves Playwright's
  randomly named `.webm` to `<name>.<theme>.webm`. A final screenshot of
  the same state is the poster, `<name>.<theme>.png`. Steps gain
  `typeSlowly` (per-character delay, so typing reads as typing) and
  `moveTo` (a visible cursor path is not available in a recording, so the
  clip relies on hover states and focus rings instead; annotate nothing).
- **Themes:** `SHOT_THEME=light` and `dark` produce the two variants; the
  runner script `scripts/screenshots/onboarding.sh` runs both and fails if
  any clip exceeds **600 KB** or the set exceeds **8 MB**. Viewport
  1280×800; clips 6 to 10 seconds; the first and last frames should match
  so a loop does not jump.
- **The demo space:** a `setup` shot creates space `DEMO` ("Getting
  started") with three pages through the UI, and `teardown` deletes it;
  both run every time, so the clips never depend on this instance's real
  content and never leak it.
- **Output is committed:** `src/web/public/onboarding/` (Vite copies
  `public/` into the build). The spec is `scripts/screenshots/onboarding.json`.
  The media list, with the shot each comes from, is the first section of
  `docs/onboarding.md`, so a UI change that dates a clip has a recipe to
  re-record it.

The clips (each in both themes):

| Name | Shows | Used by |
|---|---|---|
| `spaces` | the spaces list, opening a space, the page tree | 10.3 tour |
| `new-page` | New page, a title, typing a paragraph, Publish | 10.3 tour |
| `editor-slash` | typing `/`, the menu filtering, inserting a table | 10.3 tour, tip |
| `editor-toolbar` | selecting text, the bubble menu, a heading from the toolbar | 10.3 tour |
| `mention` | typing `@`, picking a person | tip |
| `inline-comment` | selecting text, Comment, a reply | 10.3 tour, tip |
| `page-tree-drag` | dragging a page under another | tip |
| `search` | the search box, results, a label chip | 10.3 tour |
| `templates` | Save as template, then New page from it | tip |
| `link-shortcut` | Cmd/Ctrl+K on a selection | tip |
| `watch` | the Watch toggle and the bell | tip |
| `profile` | avatar, two-factor section, notifications | 10.3 tour |

Stills only (light and dark): `admin-overview` (the Administration
dashboard), `admin-backups` (the Backups tab), for 10.2's Done screen.

**As built (2026-09-20), where it differs from the above.**
- **The video is scaled, not the viewport.** The page still renders at
  1280×800; Playwright writes the video at 864×540. At 1280×800 the set
  came to 13.6 MB against an 8 MB budget, and every clip was already
  inside the 6-to-10-second window, so resolution was the only lever with
  give in it. Posters stay at the full 1280×800. The set is 7.6 MB.
- **`page-tree-drag` reorders rather than nests.** Dropping on a sibling's
  center is a reorder to dnd-kit; nesting needs a horizontal offset that
  would take several fourteen-minute runs to tune blind. The clip shows a
  page being moved in the tree and saved, which is the tip either way.
- **Two leaks had to be closed** once the first recordings were looked at,
  and both are the kind that only show up in the output: the spaces list
  filmed every real space on the instance, and `@` resolved to a real
  person. The clip now hides non-demo space cards through a shot-level
  `css` applied before the first held frame, and the mention types `@Demo`
  so it lands on the recording account. `setup` creates two further demo
  spaces so the list still looks like a list.
- **Recording raises two Critical `space.deleted` alerts**, one per theme,
  because the demo space really is destroyed each time. That is 11.3
  working, not noise to suppress.
- **Steps beyond the item's list:** `dragTo`, `deleteSpace` (teardown needs
  11.3's password-in-request, which cannot live in a JSON spec),
  `skipCapture`, a shot-level `css`, and a spec-level `deviceScaleFactor`
  so a set that ships inside the app is not shot at the documentation
  harness's 2x.

### 10.2 First-run setup for the owner · `L` · Model: Fable → Opus · ✅ **shipped 2026-09-20**

**When it runs.** `GET /api/instance` (from 5.5; anonymous):
`{ instanceName, needsOwner, publicReading, allowPublicRegistration }`.
`needsOwner` is "no users exist".
When true, the SPA sends `/`, `/login` and `/register` to `/setup`; the
Login page itself shows "This instance has no owner yet. Set it up." with
the link, for anyone who lands on it directly. Once the owner exists, the
wizard continues only for the owner: `/auth/me` carries
`setupRequired = role == Owner && SiteSettings.SetupCompletedAt == null`,
and a global `SetupGate` (in `Root`, beside `RecoveryCodesPrompt`) routes
every path except `/setup` and `/logout` there while it is true. This is
convenience: the API is not blocked, and the docs say so.

> **Update 2026-09-20:** Phase 11 adds a required `permissions` step
> (the rights matrix) between registration and backups, and one more piece
> of completion evidence. The table and the evidence list below include it.

**Steps.** A single page, `/setup`, with a left rail of steps and one
step's form on the right, so progress is visible and the owner can go back
to a completed step. Required steps show a lock glyph and no Skip button;
optional ones have **Skip for now**. Each step saves through the endpoint
that already exists for it, then records itself with
`POST /api/setup/steps/{key}` (**Owner**; body `{ skipped }`), which
appends to `SiteSettings.SetupProgressJson` (`{ key: { at, skipped } }`).
The server refuses to record a required step as skipped.

| # | Key | Required | What it does | Saves through |
|---|---|---|---|---|
| 0 | `welcome` | | What this wizard covers, and that the required steps take two minutes | nothing |
| 1 | `account` | yes | Create the owner account (email, name, password); then the recovery codes, with **I have saved these** as a checkbox the Continue button needs | `POST /auth/register` (first account); the checkbox records `User.RecoveryCodesAcknowledgedAt` via `POST /auth/me/recovery-codes/acknowledge` |
| 2 | `instance` | yes | Instance name; public address (prefilled from the deploy-time value; explained: "links in email use this") | `PUT /admin/settings` |
| 3 | `registration` | yes | Two cards, neither preselected: **Invite only** ("you create invite links; nobody can sign up on their own") and **Open** ("anyone who can reach this address can create an account"). Continue needs a choice. Below them, *(added 2026-09-20, see 5.5)* the switch **Allow anonymous reading**, off by default, with its two-sentence explanation | `PUT /admin/settings` |
| 4 | `permissions` | yes | *(Added 2026-09-20 for Phase 11.)* The rights matrix from 11.1 as it stands, the Owner column included, with one sentence on what a tier is. **Keep these defaults** or edit and **Save**; either records the step and sets `PermissionsReviewedAt`. Only the owner can be here, so every column is editable | `POST /admin/roles/review` or `PUT /admin/roles/{id}/permissions` |
| 5 | `backups` | yes | The retention policy as 9.1's form, prefilled with the seeded values, plus one sentence on what the two backup systems are and that `BACKUP_ENCRYPTION_KEY` in `.env` must be kept off this machine. **Keep these settings** or **Save changes**; either records the step. Saving needs sudo: the owner signed in a minute ago, so the client will not prompt; if the wizard sat idle past the window, `ReauthDialog` asks, which is correct | `PUT /admin/backups/policy` |
| 6 | `email` | | SMTP host, port, username, password, from, TLS; **Send test email** to the owner's address; Continue is enabled after a successful test or a Skip | `PUT /admin/settings`, `POST /admin/settings/email/test` |
| 7 | `two-factor` | | The profile's TOTP enrollment inline, with "recommended for the account that owns this instance" | the existing `/auth/me/totp/*` |
| 8 | `first-space` | | Name and key for a first space, or Skip | `POST /spaces` |
| 9 | `done` | | What was set, what was skipped with a link to finish each in Administration, the `admin-overview` still, and two buttons: **Invite people** (Administration → Invites) and **Take the tour** (10.3) | `POST /api/setup/complete` |

`POST /api/setup/complete` (**Owner**) verifies the evidence server-side
and sets `SetupCompletedAt`: the caller has `RecoveryCodesAcknowledgedAt`;
`SiteSettings.UpdatedById` is not null; `registration` and `instance` are
recorded and not skipped; `PermissionsReviewedAt` is not null (Phase 11);
`BackupPolicyChangedById` is not null (9.1's
seed leaves it null; an owner saving or keeping the policy sets it). A
missing piece returns 409 with the step key, and the SPA jumps there.
Audit `setup.completed` with the list of skipped keys.

**Also.** Registration is not disabled while `needsOwner`: `/register` is
the same endpoint and still makes the first account the owner; the wizard
is the friendlier door to the same room. A second person arriving at
`/setup` after the owner exists sees "This instance already has an owner"
with a sign-in link (the `needsOwner` check, re-read on load). OIDC on an
empty instance: the first provisioned user is the owner and lands in the
wizard at step 1's recovery-codes half (SSO accounts have no codes; that
half is skipped for them and the requirement waived, since codes reset a
password they do not have).

**As built (2026-09-20), where it differs from the above.**
- **`SetupGate` wraps the outlet** rather than sitting beside it in `Root`.
  As a sibling its `<Navigate>` raced `SessionGate`'s, which renders
  deeper and won, so a fresh instance landed on `/login` instead of the
  wizard. Found by running it, not by reading it.
- **A signed-in account answers for itself.** `needsOwner` comes from
  `/api/instance`, which is fetched once at load and never refetched, so
  after the owner is created it still says "true". The gate therefore uses
  `user.setupRequired` whenever there is a session and only falls back to
  `needsOwner` for anonymous visitors. Without that, finishing the wizard
  bounced the new owner straight back into it.
- **Registering now reads the session back.** `POST /auth/register` returns
  `RegisteredResponse`, which is a *different* shape from `UserResponse`:
  no permissions, no role name, no `setupRequired`. The SPA was setting
  that partial object as the session, so a newly registered owner had an
  empty rights list and the wizard's matrix step rendered read-only.
  `AuthContext.register` now calls `/auth/me` after registering. This was
  a latent bug in every registration, not only the first.
- **`ApiError` carries the parsed body** as `details`, which is how the
  409's `step` reaches the client so the wizard can jump there. There was
  no way to read it before, and a cast made the mistake typecheck.
- **The email step is a short form**, not the full SMTP form with a test
  send: the password field and **Send test email** stay in Administration
  → Settings, which already has them. The step saves host, port, username
  and from-address, and says where to finish.
- **A throwaway stack for testing it**: `deploy/scratch-instance.yml` and
  `scripts/scratch-instance.sh`, two containers under their own project
  name. See `docs/onboarding.md` for the two traps (cookies ignore the
  port; a Secure cookie is dropped over HTTP).
- **Not done:** the instance name in the wizard's own heading does not
  update when the instance step changes it, because `InstanceProvider` has
  no refresh. It is cosmetic and lasts for the rest of one wizard run.

**Docs and tests.** `README.md`'s Quick start points at `/setup` instead
of `/register`. `docs/onboarding.md` describes the wizard and how to re-run
a skipped step. Tests (`SetupTests.cs`): `needsOwner` flips on the first
account; a member cannot record steps or complete; a required step cannot
be recorded as skipped; complete refuses with 409 naming each missing
piece, then succeeds and audits; an upgraded instance (seed) is already
complete; `setupRequired` on `/auth/me`. Live: run the wizard end to end on
a **fresh** compose stack (`docker compose down -v` on a scratch project
name, never on this instance), in both themes and at 375 px; then the full
live walk, since `main.tsx` and `Root` change.

### 10.3 Welcome tour and tips · `L` · Model: Fable → Opus · ✅ **shipped 2026-09-20**

**State.** On `User`: `TipsEnabled` (bool, default true) and
`OnboardingJson` (jsonb: `{ tourCompletedAt?, tourSkippedAt?, tourVersion?,
tips: { key: dismissedAt } }`). `GET /auth/me` includes `onboarding:
{ tourDue, tipsEnabled, dismissedTips[] }` so the SPA has it on load;
`PUT /auth/me/onboarding` takes partial updates (`tourCompleted`,
`tourSkipped`, `tipsEnabled`, `dismissTip`, `resetTips`, `resetTour`).
`tourDue` is true for accounts created after the migration that have
neither completed nor skipped the tour, and after **Show the tour again**.

**The tour: `/welcome`.** Full-screen, five screens, each a clip (poster
under `prefers-reduced-motion` or where the video does not play) beside
three sentences; Next, Back, **Skip the tour** on every screen, and on the
last one **Done** with the checkbox "Show me tips as I go" (checked). The
SPA sends a signed-in user there when `tourDue`, once per session; leaving
mid-way marks it skipped, and the profile can reopen it.

1. *Spaces and pages* (`spaces`): a space is a home for related pages;
   pages nest; the tree on the left is the map.
2. *Writing* (`new-page`, `editor-toolbar`): New page, type, Publish; the
   toolbar and the bubble menu; drafts are private until published.
3. *Working together* (`inline-comment`): live co-editing, comments on a
   selection, `@` to bring someone in, Watch to be told.
4. *Finding things* (`search`): search everything you can see; labels;
   the recent list.
5. *You* (`profile`): avatar, two-factor, email notifications; where tips
   can be turned off.

**Tips.** A catalog in `src/web/src/onboarding/tips.ts`: `key`,
`context` (where it may appear), `trigger` (a predicate over the page's
state), `title`, `body`, optional `clip`, `priority`. The `TipHost`
component (in `Root`) shows **at most one tip at a time, at most three per
day per person** (a counter in `localStorage` per user id), only when
`tipsEnabled`, never inside a modal or the wizard, never while the editor
has a selection or a menu open. A tip is a small card anchored to its
control (bottom-right of the viewport when the control is off-screen or on
a phone) with **Got it** (dismisses this tip for good) and **Turn off tips**
(sets `TipsEnabled = false`, with an undo link for ten seconds). Dismissals
save through `dismissTip`; a network failure keeps the tip dismissed for
the session.

| Key | Where | Fires when | Teaches |
|---|---|---|---|
| `slash-menu` | editor | the editor gains focus for the first time | type `/` for blocks: tables, panels, diagrams, charts |
| `bubble-menu` | editor | a selection of more than three words | select to format, link, or comment |
| `link-shortcut` | editor | the second editing session | Cmd/Ctrl+K on a selection makes a link |
| `mention` | editor | a page has two or more collaborators, or any comment exists | `@` to bring someone in |
| `emoji` | editor | the tenth editor session | `:` for emoji |
| `indent` | editor | a list with three or more items | Cmd/Ctrl+] and Cmd/Ctrl+[ |
| `clear-formatting` | editor | pasted text carrying marks | Cmd/Ctrl+\ clears formatting |
| `templates` | editor | the third page created by this person | Save as template, then New page from it |
| `page-tree-drag` | space | a space with three or more pages | drag pages to reorder or nest |
| `inline-comment` | page view | a published page with no comments, second visit | select text to comment on it |
| `watch` | page view | a page by someone else | Watch to be notified of changes |
| `labels` | page view | a page with no labels, owned by this person | labels group pages across spaces |
| `search-scope` | search | the second search | search finds titles, text, and labels; a space filter narrows it |
| `full-width` | page view | a page containing a table | Full width for wide tables |
| `two-factor` | profile | two-factor off, third visit | protect the account |

Order is by `priority`; a tip whose control is not on the page is skipped,
not queued. The tour's "Done" schedules `slash-menu` as the first tip.

**Profile → "Tour and tips" section:** the tips toggle, **Show the tour
again**, **Reset dismissed tips**. The owner sees the same section.

**As built (2026-09-20), where it differs from the above.**
- **"Created after the migration" is recorded, not computed.** The
  migration stamps every existing account as having skipped the tour
  rather than the server comparing `CreatedAt` to a deploy time it would
  have to know. Same outcome, no clock to get wrong.
- **The tour gate lives in `SetupGate`**, which already runs above every
  route and already answers "where should this person be". A second gate
  beside it would have raced the first, which is the bug 10.2 hit.
- **`Clip` asks the video to play** rather than trusting the `autoplay`
  attribute, and falls back to the poster when that is refused. Found at
  375px, where the clip loaded, stayed paused and showed nothing: the
  spec's "where the video does not play" turns out to include "where it
  simply never started".
- **The in-handler suspended check was removed as unreachable.**
  `OnValidatePrincipal` already rejects the cookie of any account that is
  not Active, so the endpoint cannot be reached by one; the test asserts
  the 401 that actually happens and says where it comes from.
- **Triggers read from signals, not the DOM, where the fact is not
  visual.** `onboarding/signals.ts` records editing sessions, pages
  created, searches, profile and page visits, and who wrote the open page,
  in `localStorage` per user id. The server is never told: losing a
  counter costs one repeated tip, and the alternative is reporting how
  often somebody opens the editor.
- **Dismissals do go to the server**, so retiring a tip holds across
  devices, which a `localStorage` counter would not.

**Docs and tests.** `docs/onboarding.md` gains the catalog with each
tip's trigger. Tests (`OnboardingTests.cs`): `tourDue` is true for a new
account and false for one created before the migration (seed the
`CreatedAt`); each `PUT /auth/me/onboarding` field; a member can only
change their own; `/auth/me` carries the summary; a suspended account
cannot update. Frontend has no tests, so the live walk covers: the tour
on a fresh member account in both themes and at 375 px, reduced motion
(the poster), three tips firing and the daily cap, Turn off tips and its
undo, the profile section, and every route touched by `Root`.

### 10.5 The Support site and the Demo space · `XL` · Model: Opus 5.5

**Rescoped by the owner, 2026-09-22.** What began as "rebuild the user
manual" is now the product's support site. The owner owns tesria.com and
will host it there: the **Support** space is exported as a static site
(12.2) and served from Cloudflare. A second space, **Tesria Demo**, holds
the pages the screenshots and clips are taken from. The tree below was
approved by the owner on 2026-09-22.

**Why it was here in the first place still stands.** A manual written on
2026-09-11 (47 pages, 86 screenshots) lived only inside the instance and is
gone; nothing in the repository held a copy. So the Support space is
exported as a wiki pack and **committed to the repository** (the owner's
decision), which also means anyone can pull it and run the support site
locally. The wiki stays the source of truth (decided 2026-09-21): the site
is written in Tesria and exported, not written as Markdown and imported.

**The two spaces.**

- **Support** (`SUPPORT`): public, published, exported to tesria.com. It
  links to nothing in Demo.
- **Tesria Demo** (`DEMO`): private, never exported. One clean page per
  editor element so every screenshot shows one element alone; a believable
  fictional team wiki (a product launch: projects, meeting notes,
  decisions, labels, tasks with assignees, page properties) so the live
  content blocks have something real to report; pages with history,
  comments and inline comments; a small published space and a sample
  exported site for the publishing pages. Fictional names only.

**The approved tree** (about 150 pages).

- Welcome to Tesria (space home)
- Getting started: What is Tesria · Prerequisites · System requirements
  (including the phones, tablets and browsers supported) ·
  Quick start · First-run setup wizard · Your first space and page
- Installation and operations: Installing with Docker Compose ·
  Configuration reference (every `.env` setting) · HTTPS and domains ·
  Trusting the local certificate · Single sign-on (OIDC) · Email (SMTP) ·
  Upgrading · Health checks and monitoring · Backups and recovery (how
  backups work, retention, restoring and undo, offsite copies, testing a
  target and the cloud budget, restore drills, when the machine is gone) ·
  Security hardening · Uninstalling and moving
- User manual
  - Basics: signing in, accounts and invites · two-factor and recovery
    codes · resetting a password · finding your way around · the tour and
    tips · theme and accent
  - Spaces: what a space is · creating one · the space home and watching ·
    who can see a space · archiving and deleting
  - Pages: creating (and templates) · drafts, Publish and Update · the page
    tree and reordering · page actions · labels · attachments · history and
    restoring · restrictions · trash
  - The editor: tour · the slash menu · text formatting · colors ·
    alignment and indentation · Markdown shortcuts · keyboard shortcuts ·
    floating menus
    - **Elements, one page each**, with its slash command, every other way
      to insert it, every option, how to use it, and its limits: Normal
      text, Headings, Blockquote, Divider, Bullet list, Ordered list, Task
      list, Link, Panels (all five), Expand, Decision, Layout, Table, Code
      block, Diagram (Mermaid), Math, Chart, Image, Gallery, File or video
      (and animation), Embed, Smart link, Status, Date, Mention, Emoji,
      Table of contents, Excerpt, Page properties
    - Live content: how it works, then one page per block: Children
      display, Recently updated, Content by label, Attachments, Change
      history, Contributors, Include page, Excerpt include, Page properties
      report, Labels list, Task report, Page tree
  - Working together: editing at once · comments · mentions and
    notifications · watching · changes from assistants and the API
  - Finding things: search · labels
  - Exporting and publishing: a page · a space as a website · hosting an
    exported site · wiki packs · public reading · turning exports off
  - Your profile: avatar, name, email · sessions · email notifications ·
    API tokens · password
  - **Tesria on phones and tablets** (added by the owner, 2026-09-22): the
    phone top bar and menu · moving between spaces and pages · the
    editor's phone toolbar and Insert button · tables by touch · the
    keyboard and the sticky bars · what a phone does not offer (reordering
    the tree, for one) · tablets
- Administration: owners, administrators and users · one page per admin
  tab (Dashboard, Users, Spaces, Invites, Security, Backups, Roles, Groups,
  Audit log, Branding, Settings) · Space settings (Details, Exports, site
  and pack export, Archive and danger zone, Permissions, Webhooks, Trash)
- REST API: overview and the interactive reference · authentication ·
  errors and why missing looks like 404 · rate limits · webhooks · a
  reference page per endpoint group, **generated from the OpenAPI spec** so
  it cannot drift · curl examples
- MCP: what it is · connecting clients · permissions and read-only tokens ·
  one page per tool (the ten in `TesriaTools.cs`) · how assistant edits
  appear to people editing
- Troubleshooting · FAQ · Glossary · Release notes (from the changelog) ·
  Security · License and credits

**Media rules** (the owner's, and 10.5's original ones).
**Every element page shows the element on a desktop and on a phone**, side
by side (the owner, 2026-09-22: mobile support took a great deal of work
and the first tree left it out). The harness already shoots at phone width
with touch on (`SHOT_MOBILE=1`), so each shot is taken twice. Light theme,
blue accent, unbranded (branding reset to Tesria; only the
Branding page shows it set). Seeded fictional content only. Every
screenshot and clip comes from a committed spec in `scripts/screenshots/`,
so each can be regenerated after a redesign. Cloudflare Workers static
assets allow **25 MiB per file** and 20,000 files on the free plan
(checked 2026-09-22; the owner had understood 50 MB); the harness's clips
are a few hundred kilobytes, so neither limit binds.

**Decisions (the owner, 2026-09-22).**

1. **Animations are a mode of the existing "File or video" element**, not a
   new element: "Play as animation" shows a video attachment silent,
   looping, starting by itself, with no player controls, and shows its
   still frame instead to anyone whose system asks for reduced motion. The
   site export already copies attachments beside each page, so exported
   pages carry the clips with no new mechanism. To check: that Safari on
   iPhone plays the harness's WebM; if not, the harness also writes MP4.
2. **Every product gap the inventory found is fixed before anything is
   shot** (step 1), so nothing is documented broken and nothing is
   recorded twice.
3. **System requirements are measured**, not guessed: the running stack's
   memory, CPU at rest and under a PDF export, and disk, published with
   stated headroom.
4. **The owner creates the Demo accounts** (fictional people for mentions,
   co-editing, the Users tab and contributors) from a list this item
   provides. **The existing spaces are all build leftovers and are deleted
   by the owner after step 1**, before Demo is seeded.
5. **A pack of the Support space is committed to the repository.**
6. **One password rule.** The setup wizard asked for 12 characters in the
   browser while the server, and every other form, require 8; nothing
   recorded 12 as a decision. The wizard now follows the server's rule. The
   owner can ask for a stronger owner rule instead, enforced by the server.

**Steps, each shippable alone.**

1. **Fix the gaps first.** From the 2026-09-22 inventory:
   - Smart link: an address field when empty, a way to change it, and the
     card/inline switch its `display` attribute already supports.
   - Panels, Expand, Decision and Excerpt can be removed, keeping their
     content; a panel's type can be changed from the panel itself.
   - Math can be inline as well as on its own line, as its description
     already claims.
   - Include page and Excerpt include pick a page by title, not a raw id.
   - Headings 4 to 6 in the Style menu and the slash menu; Justify in the
     Alignment menu.
   - A mention notification reads as a sentence, not `user.mentioned`.
   - The mention tip stops claiming mentions work in comments.
   - The "Require two-factor for administrators" switch stops saying
     two-factor has not shipped.
   - The setup wizard's password rule matches the server's (decision 6).
   - Templates can be listed, renamed and deleted from a screen.
   - Groups can be renamed from the Groups tab.
   - An administrator can regain access to a space from the Spaces tab (the
     endpoint exists; the button does not).
   - The "Create invite links" right gets a screen, or its holders a way to
     use it.
   - The "Create spaces" right's description stops saying the creator
     administers the space, which the permission model does not do.
   - Added by the owner while this step was under way (2026-09-22): a
     phone held sideways fills the screen instead of sitting between two
     empty bands; the space sidebar can be hidden, on any screen wide
     enough to have one; the date popup's field fits its box on iOS; and on
     a phone held upright a table keeps readable columns and scrolls
     sideways instead of squeezing.
   - Found on the way: the bell and emails showed a mention as
     `user.mentioned` and an alert by its internal name; warning notices
     had no style; and "Get access" on an open space would have granted the
     space's first permission, which closes it to everyone else. It now
     grants nothing there.
   - ✅ **Fixed 2026-09-23.** **Reported by the owner: a used invite still
     reads "Unused".** The cause: registration looks an invite up only
     when public registration is closed, so with it open (as on this
     instance) an invite link creates the account and the invite is never
     spent, never linked to the account, and stays usable. The fix: spend
     a valid invite whenever one is presented, and have the Invites tab
     say who it created (`Invite.UsedByUserId` is already stored; the list
     does not return it). Worth a test for each registration setting.
     As fixed: a presented invite is looked up whenever one comes with a
     registration; with registration open a token that does not match is
     ignored, since anyone may register anyway. The Invites tab shows "used
     by <name>, <date>". Tested with registration open and closed. Invites
     used before the fix were never linked to their account and cannot be
     after it; they stay usable until they expire or are revoked.
2. ✅ **shipped 2026-09-22.** **"Play as animation"** on File or video, in
   the editor, the reading view and the exported site.
   As built: a `playback` attribute on the element (`player` or
   `animation`), a "Show as" choice beside the file picker, and an
   **Animation** item in the slash and Insert menus that inserts the
   element already set, because "GIF" is what people look for. Silent,
   looping, starting by itself, no player chrome, and a small pause button
   (WCAG 2.2.2: anything moving longer than five seconds must be
   stoppable) that shows on hover and focus, and always on a touch screen.
   Reduced motion starts it paused on its first frame. An export carries it
   through the one script it keeps (`SiteChrome.ThemeScript`), driven by the
   same data attributes, with `muted` written as an attribute by hand
   because React sets only the property and a capture would have lost it.
   **Safari on iOS plays the harness's WebM** (checked on an iPhone 18 Pro
   simulator, iOS 27), so no MP4 is needed. A paused video's first frame
   is shown with `#t=0.1`, which also fixed the ordinary player showing a
   black box on iOS until played. Checked in the editor, the reading view,
   on the iPhone, and in an exported site served as static files, where it
   played muted by itself and the pause button worked. Not checked: the
   reduced-motion start, since nothing here can turn that setting on; it is
   one short branch in each of the two places.
3. ✅ **measured 2026-09-22.** **The system requirements**, for the
   Getting started page. Measured on this instance (Docker 29.8,
   Compose 5.5.1, linux/arm64, the Docker VM given 12 CPUs and 7.7 GiB):
   - **Memory at rest: about 550 MiB for the whole stack.** App 254,
     PDF renderer 126, collab 54, database 53, Caddy 52, the two backup
     agents 5 to 6 each.
   - **At peak: 665 MiB**, sampled every two seconds through two PDF
     exports of the every-element page and a site export. The PDF renderer
     reached 235 MiB and 86% of a core, the app 258 MiB and 41%. Samples
     that far apart can miss a spike, which is what the headroom below is
     for.
   - **Disk for the images: about 5.2 GB**, counting shared layers once.
     The PDF renderer's Chromium is 3.5 GB of it; the four Postgres-based
     images share one 668 MB base. The first install also builds the app
     and pulls the .NET and Node build images, so it needs room beyond that
     while it runs. Data here is under 1 GB, most of it backups, which
     grow with the retention policy.
   - **Browsers**: the floor is set by CSS the app relies on (`:has()`,
     container queries, `color-mix()`): Chrome and Edge 111, Firefox 121,
     Safari 16.2, so iOS 16.2. The application's own JavaScript uses
     nothing newer than its build target. Only current versions were
     actually run: Chromium (the harness) and Safari on iOS 27.
   **What the page will say**, with the headroom stated as headroom:
   **2 CPU cores and 2 GB of memory** to run it (three times what was
   measured at peak), **4 GB if the machine also builds the images**, since
   that compiles .NET and the web app (not measured, and said so), and
   **20 GB of disk** to start, more as backups accumulate. Any host that
   runs Docker with Compose v2: Linux on x86-64 or ARM64, macOS or Windows
   with Docker Desktop.
4. **The owner creates the Demo accounts and deletes the old spaces.**
   The accounts, all fictional, on `example.com` (reserved for examples, so
   no mail can reach a real person): **Alex Rivera** (administrator, for
   the admin screens), **Priya Natarajan**, **Sam Okafor**, **Mei Chen**
   (users, for mentions, co-editing, contributors, task assignees and the
   Users tab), and optionally **Jordan Brooks**, a fifth ordinary user.
   (First written as "a user without two-factor, for the Users tab's
   warning badge": wrong twice over. Two-factor is off for every account
   until its owner turns it on, and the badge means *no recovery codes
   left*, which registration makes impossible for a new account, since it
   issues them. Not faked; the badge is shown on its own page if at all.)
   Created by the owner 2026-09-22: Alex Rivera (administrator), Priya
   Natarajan, Sam Okafor and Mei Chen. Two of
   them co-editing need the harness to sign in as a second person: their
   sign-ins go in the gitignored `.debug-credentials` beside the existing
   one, never in the repository.
   Before the spaces go: **App Design is real content** (CLAUDE.md), so it
   is exported as a pack first. FIXTURE is re-seeded by the harness from
   `tests/` whenever it is needed, so it can go.
5. ✅ **seeded 2026-09-23.** **Tesria Demo**, from
   `scripts/demo/seed-demo.sh`, which signs in as Alex (SHOT) and Sam
   (SHOT2) from the gitignored `.debug-credentials` and trusts the local
   certificate from the running stack. 58 pages: the Kestrel Sync 2 launch
   wiki (plan, three meetings, checklist, architecture, FAQ, with page
   properties, tasks with assignees, decisions, labels, Sam's edits and
   comments), a Reports page of live content, and an Element gallery with
   one page per element. Images and a PDF are drawn by the script; the clip
   is the onboarding recording. Re-running changes nothing: pages are found
   by title and compared by content. That comparison first compared the
   JSON text and rewrote every page on every run, because the server stores
   documents as jsonb, which reorders keys; it now compares with keys
   sorted. Every page was opened in a browser and checked for error
   messages, empty live content and console errors: none.
   Not seeded, done while shooting: inline comments (they live in the
   document as marks tied to a comment id). The Embed page shows *Big Buck
   Bunny*, the Blender Foundation's open film (CC BY 3.0, credited on the
   page), from Blender's own channel: fine to show on a public site, and
   YouTube is already on the default allowlist.
6. ✅ **written 2026-09-23.** **Write Support** in the order of the tree,
   shooting as each section is written. `scripts/support/publish-support.sh`
   writes 165 pages from `scripts/support/sections/`, each picture taken on
   a desktop and a phone from the Tesria Demo space; the wizard's pictures
   come from a scratch instance (`scripts/support/shoot-setup.sh`). Reading
   every screen's code to document it turned up the fixes in the CHANGELOG
   and the whole of Phase 15, which were built first so nothing was shot
   twice.
7. ✅ **exported 2026-09-23.** **Export**: the pack committed to the
   repository, and the static site checked against Cloudflare's limits and
   walked as a reader. `scripts/support/export-support.sh` writes
   `support/support-pack.zip` (23.5 MiB, 395 files) and the site (396
   files, 50 MiB unpacked, the largest file 1.5 MiB: well inside 25 MiB and
   20,000 files). Every internal link, image and anchor resolves. The walk
   found that exported Expand blocks could not be opened, which hid every
   FAQ answer, and that live blocks kept their editing header; both are
   fixed. The site is exported as the author (`audience=me`) because this
   instance has anonymous reading off; nothing in Support is restricted, so
   the pages are the same.

---

## Phase 11: Roles with assignable rights

Written 2026-09-20 by Fable 5.1 at the owner's request, from the code as it
stands after 10.1. Three items: **11.1** the permission model, the matrix
and its enforcement; **11.2** custom roles; **11.3** deleting a space, the
first destructive action gated by a right from 11.1. All three are
specified in full below.
Phase 10's remaining items move behind them (see the order of execution):
10.2's wizard gains a required step where the owner reviews the matrix, and
that step should be built once, against the real thing.

**What the owner asked for.** Rights assignable to roles: Owner, Admin and
User for now, with good defaults. The owner reviews and approves the
defaults, or changes them, during onboarding. The example given: an
instance may not want administrators to be able to change the backup
retention policy.

**The owner's decisions (2026-09-20), fixed:**

1. **The owner edits everything.** Administrators cannot change the rights
   of administrators, but can change the rights of users, and of any other
   roles that get created.
2. **The owner is subject to the matrix, except for roles and ownership.**
   The owner can switch their own access to an area off and back on; what
   they cannot give up is changing roles, transferring ownership, and (so
   that "back on" is always possible) editing the matrix itself.
3. **User-level rights in the matrix:** create spaces; create API tokens
   and use the API and MCP with them; export pages; create invite links
   (off by default); and two delete rights: **delete pages you created**
   (on by default for users) and **delete pages created by others** (off by
   default for users; administrators and the owner have it).

**Decisions made here (Fable):**

- **Tier and role are two different things, and the code already has the
  first.** The existing `UserRole` enum (`Member`, `Admin`, `Owner`) stays
  and becomes the *tier*: the ordering that decides who may edit whom, who
  receives alerts, whom the two-factor requirement binds, and what the
  owner alone can do. A *role* is a named set of rights that belongs to a
  tier. Three built-in roles exist, one per tier, and custom roles (11.2)
  are extra roles within the User or Admin tier. Nothing in 10.1 changes.
- **Defaults preserve today's behavior, with exactly two exceptions**:
  users lose "delete pages created by others" (the owner's decision), and
  gain nothing they did not have. An upgraded instance therefore changes
  in one visible way, stated in the CHANGELOG and shown on the new Roles
  tab. Administrators keep everything they can do today.
- **Rights are additive over space permissions, never a bypass.** An
  instance right says what a person may do at all; the space's own View,
  Edit and Admin grants still decide where. "Delete pages created by
  others" does not let anyone delete in a space they cannot edit.
- **Three powers are reserved to the owner and never appear as checkboxes:**
  changing a user's tier, transferring ownership, and editing admin-tier
  or owner rows of the matrix. That is what makes "the owner can never
  lock themselves out" true without a special case.
- **The catalog is code; the grants are data.** Which rights exist, their
  wording, grouping and defaults live in one C# file, so a new right is a
  code change with a test. Which roles hold which rights is rows in the
  database, cached like site settings.
- **Editing the matrix is sudo, audited as a diff, and alerts.** A stolen
  session that widens an administrator's rights is the new way to take an
  instance short of ownership; it is treated like reducing backup
  retention was in 9.1.

### 11.1 Permission model, matrix and enforcement · `L` · Model: Fable → Opus · ✅ **shipped 2026-09-20** (spec as Fable, implementation as Opus)

**The catalog** (`Infrastructure/Permissions/InstancePermissions.cs`):
a static list of `(Key, Area, Label, Description, Scope, Defaults)`, where
`Scope` is `Content` (meaningful for users) or `Administration`, and
`Defaults` names which built-in roles hold it. Keys are stable strings and
are what the database stores.

| Key | Right | User | Admin | Owner |
|---|---|---|---|---|
| **Content** | | | | |
| `spaces.create` | Create spaces | yes | yes | yes |
| `pages.delete_own` | Delete pages you created | yes | yes | yes |
| `pages.delete_any` | Delete pages created by others | no | yes | yes |
| `pages.export` | Export pages as PDF, HTML or Markdown | yes | yes | yes |
| `tokens.use` | Create personal API tokens; use the API and MCP with them | yes | yes | yes |
| `invites.create` | Create invite links | no | yes | yes |
| **People** | | | | |
| `users.view` | See the user list | no | yes | yes |
| `users.manage` | Suspend, unlock, sign out, revoke tokens, reset passwords (never on the owner) | no | yes | yes |
| `users.assign_roles` | Assign a user-tier role to a user (11.2) | no | yes | yes |
| `users.promote_admins` | Promote a user to administrator. Demoting one stays the owner's. *(added 2026-09-20, off for administrators by default)* | no | no | yes |
| `invites.manage` | See and revoke invite links | no | yes | yes |
| `groups.manage` | Create and shape groups | no | yes | yes |
| **Spaces** | | | | |
| `spaces.manage` | See every space; recover access; archive | no | yes | yes |
| `spaces.publish` | Make a space public or private | no | yes | yes |
| `spaces.delete` | Delete a space and everything in it (11.3) | no | yes | yes |
| **Security** | | | | |
| `audit.view` | Read the audit log and verify the chain | no | yes | yes |
| `security.view` | Security overview, events, alerts, limits | no | yes | yes |
| `security.respond` | Acknowledge and resolve alerts; block and unblock addresses | no | yes | yes |
| `security.settings` | Rate limits, lockout, the two-factor requirement, the embed allowlist | no | yes | yes |
| **Backups** | | | | |
| `backups.view` | See the Backups tab | no | yes | yes |
| `backups.run` | Back up now; test restore | no | yes | yes |
| `backups.policy` | Change the retention policy | no | yes | yes |
| **Instance** | | | | |
| `dashboard.view` | The administration dashboard | no | yes | yes |
| `settings.instance` | Instance name and public address | no | yes | yes |
| `settings.registration` | Open or close registration | no | yes | yes |
| `settings.email` | The email server | no | yes | yes |
| `settings.public_spaces` | The instance-wide public reading switch | no | yes | yes |
| `permissions.view` | See the Roles tab | no | yes | yes |
| `permissions.edit_user_tier` | Edit user-tier roles; create user-tier custom roles | no | yes | yes |
| **Always the owner** (listed on the tab, no checkboxes) | | | | |
| `roles.assign_tier` | Promote to or demote from administrator | | | always |
| `ownership.transfer` | Hand the instance to someone else | | | always |
| `permissions.edit_admin_tier` | Edit admin-tier and owner rows; create admin-tier roles | | | always |

**Schema: one migration, `Roles`.**
- `Roles`: `Id`, `Key` (`user` | `admin` | `owner` for the built-ins, null
  for custom), `Name` (unique, 60), `Description`, `Tier` (`UserRole`),
  `BuiltIn`, `CreatedAt`, `CreatedById`. Built-ins cannot be renamed
  (their names are fixed strings: User, Administrator, Owner), deleted or
  re-tiered.
- `RolePermissions`: `RoleId`, `Key` (100), primary key on both. A row is
  a grant; absence is not. Unknown keys (a right removed from the
  catalog) are ignored on read and dropped on the next write.
- `Users.RoleId` (nullable FK, restrict delete). Null means "the built-in
  role of my tier", which is also what `RoleSeed` fills in and what every
  write from now on sets explicitly. The tier column stays authoritative
  for ordering; `RoleId` is validated to belong to a role of that tier.
- `SiteSettings.PermissionsReviewedAt` (nullable), for 10.2's step.
- **App role:** `Roles` and `RolePermissions` are ordinary tables (the app
  writes them through audited endpoints). Not append-only: the audit diff
  is the record.

**`RoleSeed`, a startup step beside `OwnerSeed`:** creates any missing
built-in role with the catalog defaults; gives every user with a null
`RoleId` the built-in of their tier; on an instance that already has
users and no `PermissionsReviewedAt`, leaves it null (10.2's wizard is
skipped on upgraded instances anyway, because `SetupCompletedAt` is set;
the Roles tab is where an upgraded owner reviews). Idempotent; never
resets a built-in's rights once it exists.

**Effective rights** (`IInstancePermissions`, scoped, with a singleton
`PermissionCache` of 30 seconds like `SiteSettingsCache`, invalidated on
every write): the user's role's grants, plus the three reserved keys when
the tier is Owner. Anonymous callers have no rights, with one exception
below. A suspended account has none.

**Enforcement.** A `RequirePermission("key")` route extension backed by an
`IAuthorizationPolicyProvider` that materialises `perm:<key>` policies on
demand, and one handler: authenticated, holds the key, and, when the
holder's tier is Admin or above and `RequireTotpForAdmins` is on,
enrolled (the same rule `RequireAdmin` applies today). `RequireAdmin`
stays for exactly one thing: the `/admin` route group's *listing* of the
administration area is replaced by per-route keys, so `RequireAdmin` is
no longer used by any route and is removed, along with the blanket policy
on the admin, security, backup, dashboard, group and audit groups. Every
route names its key:

- `/admin/users` → `users.view`; status, unlock, sessions, tokens, reset →
  `users.manage` (the 10.1 owner-account guard stays on top); role →
  `roles.assign_tier` (owner) for tier changes and `users.assign_roles`
  for a role within the user tier (11.2); transfer → `ownership.transfer`.
- `/admin/spaces` → `spaces.manage`; `/public` → `spaces.publish`;
  `recover-access` → `spaces.manage`.
- `/admin/invites` GET and DELETE → `invites.manage`; POST →
  `invites.create`. A user with only `invites.create` sees an Invites page
  that creates and lists their own.
- `/admin/settings` GET → any `settings.*` or `security.settings`; PUT is
  checked **per field**: the handler maps each request field to its key
  (`InstanceName`, `BaseUrl` → `settings.instance`;
  `AllowPublicRegistration` → `settings.registration`; the SMTP fields →
  `settings.email`; `AllowPublicSpaces` → `settings.public_spaces`;
  `RequireTotpForAdmins`, `EmbedAllowlist`, the limits →
  `security.settings`) and refuses the whole request with 403 naming the
  first missing key. The response's `permissions` tells the SPA which
  sections to render editable.
- `/admin/security/*` → `security.view` for reads, `security.respond` for
  alerts and blocks; `/admin/security/limits` → `security.view`.
- `/admin/backups` GET and `jobs/{id}` → `backups.view`; `run` and
  `restore-test` → `backups.run`; `policy` and `policy/preview` →
  `backups.policy`.
- `/admin/dashboard` → `dashboard.view`.
- `/admin/audit/verify` and `/audit` → `audit.view`.
- Group create, update, delete, membership → `groups.manage`; listing
  stays open to any signed-in user.
- `POST /spaces` → `spaces.create`.
- Page delete (trash) and purge: the existing space check first (edit for
  trash, space admin for purge), then `pages.delete_any`, or
  `pages.delete_own` when `Page.CreatedById` is the caller. Restore and
  discarding one's own draft are unchanged: neither destroys anything
  someone else made.
- Export: a signed-in caller needs `pages.export`; an anonymous reader of
  a public page is allowed exactly when the built-in **User** role holds
  it (anonymous is never more privileged than a user).
- API tokens: `POST /api-tokens` needs `tokens.use`, and
  `ApiTokenAuthenticationHandler` fails a token whose owner no longer
  holds it ("This account may not use API tokens."). Existing tokens go
  inert rather than being deleted, and work again if the right returns.
  MCP goes through tokens, so this covers it.

**Endpoints** (`Features/Admin/RoleEndpoints.cs`, group `/admin/roles`):
- `GET /` (`permissions.view`): the catalog (keys, areas, labels,
  descriptions, scope), every role with its tier and grants, the reserved
  keys, and `editable: string[]` (which role ids this caller may edit).
- `PUT /{roleId}/permissions` (**sudo**, audited `permissions.changed`
  with `{ Role, Added, Removed }`): the full set of keys for one role.
  Caller must hold `permissions.edit_user_tier` for a user-tier role or be
  the owner for admin-tier and owner rows (`permissions.edit_admin_tier`,
  reserved). Reserved keys in the body are ignored. Raises
  `permissions.expanded` (Critical, no cooldown) when an admin-tier or
  owner row gains a key, and (Warning) when a user-tier row gains an
  Administration-scope key.
- `POST /{roleId}/reset` (same guards, sudo, audited): back to the
  catalog defaults for that role.
- `POST /review` (**Owner**, audited `permissions.reviewed`): sets
  `PermissionsReviewedAt`. 10.2's wizard calls it for **Keep these
  defaults**; a save through `PUT` also sets it.

`/auth/me` gains `permissions: string[]` (effective) and `roleName`.

**UI.**
- **Administration → Roles** (`/admin/roles`, after Users): the matrix,
  rows grouped by area with the label and a description on hover, one
  column per role in tier order. Columns the viewer may not edit are
  read-only with a lock and "Only the owner edits this role". Checkboxes
  edit a draft; **Review changes** lists what each role gains and loses;
  **Save** (sudo through the client's reauth) commits. **Reset to
  defaults** per column. The reserved powers are a final group titled
  "Always the owner" with no checkboxes. A banner on an upgraded instance
  until the owner has saved or reviewed: "Users can no longer delete pages
  created by others. Review these defaults."
- **Everywhere else, rights decide what renders.** `useAuth` exposes
  `can(key)`. The Admin nav entry shows when any Administration-scope key
  is held; `AdminLayout` renders only the tabs the user can open; Settings
  renders sections editable or read-only per field group; the Backups
  policy form is read-only without `backups.policy`; the page view hides
  Delete when neither delete right applies to this page; the profile hides
  API tokens without `tokens.use` and says why; New space is hidden without
  `spaces.create`. Every hidden control is also refused server-side; the
  hiding spares people a page of 403s.
- The Users page's role column shows the role name (User, Administrator,
  Owner, or a custom name) with the tier badge as today.

**Docs and tests.** `docs/security.md`: a layers row ("Instance rights
(11.1)") and an update to the owner row; `docs/architecture.md`: a section
"Instance rights" after "Roles and administrators" (tier versus role, the
catalog, additive over space permissions, the reserved powers, caching,
the per-field settings check); README's admin paragraph; CHANGELOG with the
upgrade note. Tests (`InstancePermissionTests.cs`): the catalog's defaults
match the table above and every route's key is in the catalog (a test
that walks the endpoint metadata); the seed creates built-ins, fills
`RoleId`, and is idempotent; each enforcement bullet above (one test per
route family, including the per-field settings refusal naming the key);
delete own versus any against space permissions; anonymous export follows
the User role; a token goes inert and returns; an admin cannot edit an
admin-tier row (403) and can edit the user row; the owner can remove
`backups.policy` from their own row and is then refused, and can put it
back; reserved keys cannot be removed from the owner's effective set;
`permissions.changed` audits a diff; `permissions.expanded` alerts;
`/auth/me` carries the set. Live: the Roles tab as owner and as admin
(locked columns), a saved change taking effect within 30 seconds, the
retention example end to end (remove `backups.policy` from Administrator,
sign in as the admin fixture, see the policy read-only and get 403 on
`PUT`), a member deleting their own page and being refused on someone
else's, and the full live walk, since `Layout.tsx` and `main.tsx` change.

### 11.2 Custom roles · `M` · Model: Fable → Opus · ✅ **shipped 2026-09-20**

A custom role is a named set of rights within the User or Admin tier.
People are assigned to it instead of to the tier's built-in role; their
tier does not change, so everything tier-based (alerts, two-factor, the
owner's reserved powers) is unaffected.

- `POST /admin/roles` `{ name, description, tier, copyFrom? }`: creating
  a user-tier role needs `permissions.edit_user_tier`; an admin-tier role
  needs the owner. The new role starts as a copy of `copyFrom` (default:
  the tier's built-in). `PUT /{id}` renames or describes (same guards;
  built-ins refuse). `DELETE /{id}` (sudo, audited) only when no user holds
  it; the UI offers "Move everyone to <built-in> first".
- **Assigning:** `PUT /admin/users/{id}/role` takes `{ roleId }`. If the
  role's tier equals the user's current tier, the caller needs
  `users.assign_roles` (and, for an admin-tier role, must be the owner).
  If the tier differs, this is the 10.1 promotion or demotion and stays
  owner-only, audited as `user.role_changed` with both role names. The
  owner's role cannot be changed here (10.1's rule); a transfer gives the
  new owner the built-in Owner role and the old one the built-in
  Administrator role.
- **Registration and SSO** assign the built-in User role. Invites do not
  carry a role (see the open questions).
- **UI:** the Roles tab gains **New role** (name, description, tier
  limited to what the caller may create, copy from) and a rename or delete
  per custom column; the Users page role picker lists the roles of the
  user's tier the caller may assign, and the owner additionally sees the
  tier change as a separate, confirmed action.
- **Tests** (`CustomRoleTests.cs`): create in each tier with the right
  guard; copy-from; rename refuses built-ins; delete refuses while held;
  assign within tier by an admin; cross-tier assignment refused for an
  admin and works for the owner; a custom role's grants apply and its
  deletion is blocked until reassignment; `/auth/me` names the role.

### 11.3 Delete a space · `M` · Model: Fable → Opus · ✅ **shipped 2026-09-20**

**What the owner asked for (2026-09-20).** Administrators and the owner
can delete a space, from the space's settings page, with a warning that
it is irreversible and destroys every page under it, a confirmation
prompt, and the password required to confirm.

**Decisions.**
- **It is an instance right, `spaces.delete`, not a space permission.**
  A space's own admin (the person who created it, say) cannot delete it
  unless their role grants the right; by default only administrators and
  the owner hold it. Archiving remains the reversible alternative for
  space admins, and the dialog says so.
- **Confirmation is the key plus the password, in one dialog.** Typing the
  space key proves the person is looking at the right space; the password
  proves it is them. The password is verified by the server in the same
  request, with the same rules as `/auth/reauth`: an account with a
  password gives its password; an SSO account with no password gives a
  one-time code. A wrong answer counts as a failed sign-in for lockout
  purposes, as reauth does. The ordinary five-minute sudo window is not
  enough here: this is the one action where "you signed in a few minutes
  ago" must not stand in for "you mean it".
- **"Irreversible" is told truthfully.** The dialog says the pages,
  versions, comments, attachments and history are destroyed and cannot be
  restored from the trash, and that only a backup taken before now still
  holds them. It does not promise there is no way back at all, because
  9.1's backups exist and an operator should know that.
- **Files are deleted after the database commits, best effort.** The row
  deletion is one transaction; attachment files and the icon are removed
  afterwards, and any that fail are logged by path so the runbook can
  sweep them. A crash between the two leaves orphan files, never a
  half-deleted space.
- **The audit entry keeps what the pages cannot:** the space key and name,
  the page count, attachment count and bytes, and who did it. The rows
  are gone; the record of the deletion is not.

**Endpoint.** `DELETE /api/spaces/{key}` (`spaces.delete`), body
`{ confirmKey, password?, code? }`:
1. 404 if no such space (including archived: an archived space can be
   deleted, and archiving first is not required).
2. 400 `confirmKey` if it does not match the key exactly (case-sensitive,
   as displayed).
3. 401 if the password or code does not verify, recorded as a failed
   sign-in attempt and counted toward lockout, exactly like reauth.
4. In one transaction: clear `CurrentVersionId` on every page in the
   space (the restrict FK, as purge does), delete every page including
   drafts and trashed ones (versions, attachments, comments, labels,
   views, restrictions cascade), delete the space's `CollabDocuments` by
   their document names (`page:<id>` for each page; confirm the naming in
   `CollabEndpoints` and the sidecar), delete `Watches` whose target is
   the space or any of its pages, delete the space row (webhooks,
   templates scoped to it, space permissions cascade). Notifications are
   left: they are a person's history, and a link to a deleted page
   already 404s gracefully.
5. Audit `space.deleted` with `{ Key, Name, Pages, Attachments, Bytes,
   WasPublic }`. Raise `space.deleted` (Critical, alert, no cooldown)
   through the detector: a whole space gone is the loudest thing after
   ownership changing, whoever did it.
6. After commit: delete each attachment file by storage key and the icon
   image if any, logging failures. Then 204.
7. The collab sidecar: any live editing session on those pages must end.
   The sidecar loads a document from `CollabDocuments` on first open, so
   a new session finds nothing; an *open* session holds the document in
   memory and would write it back. Add the small thing this needs: the
   sidecar's store hook checks the page still exists (or the app's token
   endpoint refuses a token for a page that is gone, and the sidecar
   closes connections whose token check fails), whichever the sidecar's
   code makes simplest. The test is a page open in the editor when its
   space is deleted: the editor is disconnected and nothing is
   resurrected.

**UI.** Space settings → a final **Danger zone** section, rendered only
when `can('spaces.delete')`, with **Delete this space**. The dialog (a
modal, not `window.confirm`: it has a form in it): the warning in full,
the counts ("42 pages, 17 attachments, 3.4 MB"), the archive alternative
as a link, a field "Type **APP** to confirm", the password field (or
one-time code for an SSO account), and **Delete space** disabled until
both are filled. On success, navigate to `/spaces` with "The space APP
was deleted." Public spaces: the sitemap and any public URLs simply stop.

**Sequencing.** After 11.1, so the right and `can()` exist. If it is ever
pulled forward, gate it on tier `>= Admin` and swap in the right later.

**Docs and tests.** Runbook: a line under recovery scenarios ("a deleted
space is restored from a backup taken before the deletion, as scenario B
or C"), and the orphan-file sweep. `docs/security.md`: the sudo row gains
"deleting a space needs the password again in the same request".
Tests (`SpaceDeleteTests.cs`): a member is refused even as the space's
creator; an admin succeeds and everything under the space is gone,
including a trashed page and a draft, and the audit entry carries the
counts; the wrong key is 400 and nothing changes; the wrong password is
401, nothing changes, and the failed attempt counts toward lockout; an
SSO account confirms with a code; the alert is raised; attachment files
are removed from the uploads directory; watches on the space and its
pages are gone; a template scoped to the space is gone and an
instance-wide one is not; an archived space can be deleted. Live: delete
a throwaway space with a page open in another tab and confirm the editor
disconnects; then the full walk, since the settings route changes.

**As built (2026-09-20), where it differs from the spec above.**
- **Collab document names are the bare page id**, not `page:<id>`: the token
  endpoint issues `id.ToString()` and the sidecar binds the token to it. The
  spec asked for this to be confirmed; it is, and the deletion matches on the
  plain id.
- **The sidecar polls rather than being told.** Its store hook writes only
  when the page still exists (and closes that document's connections when it
  does not), and a 15-second sweep closes connections for any open document
  whose page is gone. The sweep is what reaches an editor that is open but
  idle, since the store hook only runs when someone is typing. Polling keeps
  the sidecar's only inbound surface the websocket, and covers a page purged
  on its own as well as a whole space deleted.
- **A space the caller cannot view is 404, not deletable.** The spec's step
  list was silent on this; 11.1 says rights are additive over space
  permissions and never a bypass, so `spaces.delete` means "may destroy
  spaces at all", and reaching a space you hold no grant for still means
  recover-access first, which is audited.
- **A deletion preview endpoint** (`GET /spaces/{key}/deletion-preview`,
  same right) supplies the counts the dialog shows, rather than the dialog
  borrowing the admin spaces list, which needs `spaces.manage`.

**Not decided here, deliberately:** whether invites can name a role
(recommended later, and only user-tier roles for invites created by
non-owners); whether groups should carry instance rights (no: groups are a
space-permission mechanism and mixing the two is how "who can do what"
becomes unanswerable); whether anonymous readers get any right beyond
export.

---

## Phase 12: Exports that look like the page

*Added 2026-09-20 at the owner's request, designed by Fable. The complaint:
"the export output is terrible; it flattens elements and makes them look
nothing like the rendered page; tables look completely different." Plus
two new formats: a page as a static HTML file, and a whole space as a
static site that can be hosted on Cloudflare Pages and looks the same.
The motivating use is the official Tesria documentation: written here,
published cheaply.*

**Why it is terrible, precisely.** Three causes, and none of them is a
table bug.

1. The exported document's whole stylesheet is fifteen lines: `pre`,
   `code`, `blockquote`. The page renders content through about two
   thousand lines of `index.css`. There is no table rule in the export at
   all, so every table is a browser-default table.
2. `ProseMirrorRenderer.cs` is a hand-written *second* renderer of
   thirty-five node types that copies colors out of `index.css` by hand
   (its own comments say "matching index.css"). The plan already pays the
   tax: every editor change needs "a matching case in
   `ProseMirrorRenderer`" (7.D). Two renderers drift; that is what
   renderers do.
3. Eight node types are React node views: charts (inline SVG), Mermaid,
   math, embeds, dynamic blocks with live data, expand, table of
   contents, media. Anything that is not the browser reproduces those a
   third way, and a headless `generateHTML` pass would leave them hollow.

**The decision: exports are captured from the real page, not
regenerated.** The PDF sidecar already runs Chromium. Instead of being
handed hand-made HTML, it loads a chrome-free export route of the SPA,
waits for the page's own ready signal, and then prints (PDF) or
serializes the DOM (HTML, and the site). One renderer. What you see is
what you export, by construction, and the fidelity problem cannot come
back without also breaking the page view. `ProseMirrorRenderer` keeps
Markdown, which is a genuinely different target and which it does well;
its HTML path is retired.

**The posture change, stated plainly.** The sidecar today runs with the
network switched off and is handed a self-contained document. Capture
means it reaches the app. The rule becomes: the sidecar may reach **the
app service and nothing else**, enforced in the sidecar by a `page.route`
allowlist (`http://app:8080/**` and `data:`; everything else aborted)
and by the compose network, exactly as today. It still never reaches the
internet, and the files it produces are still self-contained. The
exported document is the same document a reader sees, fetched with the
same permissions, so the 5.1 masking holds without a second
implementation of it.

### 12.1 Capture-based export, and every element checked · `L` · Model: Fable → Opus · ✅ **shipped 2026-09-20**

**The render route.** `/export/pages/:id` in the SPA, outside `Layout`
(like `/welcome`): the title and `<Editor editable={false}>` inside the
same `.paper`, the same `index.css`, and nothing else. No topbar, sidebar,
labels, comments or action bar. It forces the light theme
(`data-theme=light`) whatever the account prefers, because a PDF is
paper. It sets `data-export-ready` on `<html>` when its own work is done:
the editor has mounted, every image has loaded or failed, Mermaid and
KaTeX have rendered, every dynamic block has answered. The sidecar waits
for that attribute with a timeout and then one short quiet period, which
is a signal rather than a guess. `?chrome=site` (12.2) adds the space
tree, a breadcrumb and a footer; the default adds nothing.

**Print rules live in `index.css`, next to the screen rules,** under
`@media print` and a `.paper--export` class: `break-inside: avoid` on
table rows, panels, images, code blocks and decisions; headings keep
their next block (`break-after: avoid`); a full-width table falls back to
the page width; an embed renders as its card (the `card` kind already
exists) because an iframe has nothing to show on paper; the comment mark
and external-edit marks (8.6) render as plain text. Every one of these
is a rule beside the screen rule it modifies, not a copy of it.

**The render token.** The sidecar's browser has no session. The export
endpoint mints a token for it: HMAC over `{ userId | anonymous, scope:
page:<id> | space:<id>, exp }`, five minutes, signed with `Pdf:SharedSecret`
which both sides already hold, prefix `trx_`. `ApiTokenAuthenticationHandler`
accepts it as a second token form: no database row, no counting against
the twenty-an-hour mint limit (which a fifty-page site export would blow
through), read scope only, and the principal it yields is the exporting
user or the anonymous principal. The sidecar sets it as
`Authorization: Bearer` through `extraHTTPHeaders`, so every fetch the
page makes is authenticated the same way the reader's would be.

**The endpoint.** `GET /pages/{id}/export?format=pdf|html|markdown` is
unchanged in shape. `pdf` and `html` now go through the sidecar:
`POST /render` takes `{ url, token, format: 'pdf' | 'html' }`.
- `pdf`: A4, `printBackground`, 16/18 mm margins as today, and a footer
  template with the page title and "n of N", because a document with
  page numbers is one somebody can cite.
- `html`: the serialized DOM of the render route, with the compiled
  stylesheet inlined, images and file links inlined as data URIs through
  the existing `InlineAssets`, every `contenteditable` stripped, and
  every `<script>` stripped except the theme script and toggle described
  in 12.2, so a single exported file keeps the same light, dark and
  system choice the site does. One file that opens anywhere. The Mermaid
  bundle that the HTML export used to carry is gone: the SVG is already
  rendered.
- `markdown`: `ProseMirrorRenderer`, as today.

**The fixture: one page with everything on it.** `tests/fixtures/
every-element.json` is a page containing each insertable element once
(the slash catalog: headings, lists, task list, link, blockquote, code
block, table, image, divider, table of contents, expand, layout,
decision, status, date, excerpt, page properties, Mermaid, math, chart,
embed, smart link, file, gallery) plus every mark (bold, italic,
underline, strike, inline code, highlight, text color, sub, sup, link),
alignment, indent, a mention, an emoji, a table with column widths, cell
backgrounds and a header row, a full-width table, a full-width page, and
one of each dynamic block kind. It is loaded by the export tests, seeded
into a space by the screenshot harness for the visual check, and is the
page a future "does export still look right" question is answered
against.

**The audit, which is the other half of what was asked.** For each row of
the fixture, five columns, each checked live and recorded in
`docs/export-fidelity.md`: **Editor** (inserts, edits, saves, survives a
reload), **View** (renders on the page), **PDF**, **HTML**, **Site**
(12.2). A cell is either a checkmark or the number of the bug it found.
Bugs in the editor found this way are fixed in this item, because
"renders properly in the export" is not a claim worth making about an
element that does not work in the editor. Each fix gets its own CHANGELOG
line.

**What is removed.** The HTML half of `ProseMirrorRenderer` and its
inlined stylesheet, `IPdfRenderer.RenderAsync(html)` in favor of
`RenderAsync(url, token, format)`, and the Mermaid bundle in exports.
The Markdown half stays, and so do its tests.

**Tests** (`ExportTests`, rewritten for the html path; `PdfExportTests`;
new `RenderTokenTests`): a render token authenticates read-only, as the
right user, only inside its scope, only before expiry, and never for a
write; a page the user cannot see is masked through the token exactly as
through a cookie; the html export contains the elements of the fixture
by class and carries no `<script>`; the sidecar refuses a `url` that is
not the app's own render route; the anonymous principal through the
token sees exactly what 5.1's matrix says. The fidelity itself is not a
unit test; it is the harness shooting the fixture page and the PDF of
it, and the matrix above.

**Live.** The fixture page in the editor (every element inserted through
the UI, not the API, at least once); its view; its PDF opened and read;
its HTML file opened from disk with the network off and its theme toggle
tried; both themes for the view and the HTML file, light only for PDF,
which is deliberate; 375 px for the view.

**As built (2026-09-20), where it differs from the above.**
- **Chromium would not load the app over plain http.** It upgrades an
  http navigation to https whenever the host is a *name*, whatever
  `--disable-features=HttpsUpgrades` says, and there is no TLS on the
  app's port inside the compose network. The sidecar resolves the app's
  hostname to an address per capture and navigates to that; bare addresses
  are exempt from the upgrade. The allowlist still checks the configured
  origin, so the substitution cannot widen what may be loaded.
- **Assets are inlined in the browser, not by `InlineAssets`.** The
  captured DOM's image sources still point at the instance, so the page
  fetches its own images and file links and rewrites them to data URIs
  before serializing. The site export turns that off and ships real files
  under `assets/` instead, which keeps pages small and lets a browser
  cache an image once rather than once per page.
- **`GET`, not `POST`, for the site export.** Building a site reads pages
  and changes nothing, it is the verb the single-page export already uses,
  and a read-only API token should be able to do it; `POST` locked those
  out through `TokenScopeMiddleware`. The audience is a query parameter.
- **Validation before infrastructure.** "This space is not public" is
  something the caller can act on and a missing renderer is not, so the
  audience check answers first.
- **`IPermissionService.AsAnonymous()`** is new, beside `AsUser`. Deciding
  which pages go into a public site by the exporter's own access is how a
  private page reaches the internet; the anonymous audience is evaluated
  as nobody, whoever is calling.
- **The HTML half of `ProseMirrorRenderer` is deleted** (done as its own
  change after 12.2 shipped, for the regression risk). `ToHtml`,
  `RenderHtml*` and thirteen helpers left orphaned by them went, found by
  walking reachability from `ToMarkdown`; `InlineAssets.cs` went with them,
  since a captured export inlines its assets in the browser. 1145 lines to
  726. `TocOptionsTests` was rewritten against Markdown: what it pins is
  the option *semantics*, and bullet *shapes* are now the stylesheet's,
  covered by the fidelity capture instead. Worth knowing: the hostile-value
  filtering for `indent` and `cssClass` moved with them, into
  `tocOptions.ts`, which is correct now that the browser renders but leaves
  it with no automated test, this repo having no frontend tests.
- **The removal script over-reached and was caught by counting.** Deleting
  "the HTML tests" by pattern took fifteen Markdown tests with them, for
  code that is still live: pipe escaping, GFM checkboxes, panel
  blockquotes, mention labels, the anchors-only-when-linked rule, math
  delimiters, the chart reference, the neutral dynamic-block shapes and
  the `javascript:` guard on embeds. The suite total dropping by 44 when
  about 13 were meant to go is what surfaced it, and the arithmetic had to
  be made to reconcile before it was believed. All fifteen are restored as
  Markdown-only tests. The lesson is the obvious one: a pattern that
  matches a *file*'s tests is not a pattern that matches a *renderer*'s.
- **Four bugs found by the fixture**, all fixed with tests: a mention of
  an id with no account 500'd the save; a half-committed create left a
  page that was invisible and permanently unupdatable; version numbers
  came from the current-version pointer rather than the versions; and the
  Markdown export did not sanitize link hrefs, so a stored
  `javascript:` URL came out of it as a working link.

### 12.2 Publish a space as a static site · `L` · Model: Fable → Opus · ✅ **shipped 2026-09-20**

**What it is for.** Write the documentation in Tesria, export the space,
host the result on Cloudflare Pages, GitHub Pages or any static host.
Readers get the same rendering with no server, no editor and no way to
change anything. This is not 8.5's wiki pack, which is a portable
archive for importing into another Tesria; it is a website. The two share
the walk over a space's pages and attachments and nothing else, and 8.5
should reuse that walk when it comes.

**Endpoint.** `POST /api/spaces/{key}/export/site` with
`{ audience: 'anonymous' | 'me', includeTree: true }`; returns a zip.
Needs `pages.export` (the right to export pages one by one, batched) and
view on the space; no new right, because it grants nothing the caller
could not already do page by page.
- **`audience: 'anonymous'`** renders every page as the anonymous
  principal: the space has to be public and anonymous reading on, and
  restricted pages, drafts and the trash are absent by the same rule that
  keeps them off the public web. This is the default for the stated use
  and the leak-proof one: a docs site built this way cannot contain a
  private page by accident, whatever the exporter's own access. On a
  space that is not public it exports nothing and says so.
- **`audience: 'me'`** renders as the caller, for a handbook to be hosted
  behind the host's own access control. The dialog says which of the two
  it is doing and why that matters.

**The site.** Cloudflare Pages conventions, which are everyone's:

```
index.html                    the space: name, description, the tree
getting-started/index.html    one directory per page, clean URLs
getting-started/install/      nested to mirror the tree
assets/site.css               the app's compiled stylesheet, verbatim
assets/fonts/…                KaTeX's fonts, when a page uses math
assets/<attachment id>-<name> images and files, real files not data URIs
404.html
```

Slugs are the kebab-case title, deduplicated with `-2`, `-3` among
siblings. Each page is the render route with `?chrome=site`: the tree on
the left with the current page marked, a breadcrumb, the content, and a
footer reading "Exported from <instance> on <date>", which is also the
honest caption for dynamic blocks, frozen at the moment of export. Every
internal link (`/spaces/KEY/pages/ID`, with or without a heading anchor)
is rewritten to a relative site path; a link to a page not in the export
(restricted, another space) becomes plain text with a `title` saying so.
Attachment URLs are rewritten to `assets/`. Embeds keep their iframe, the
site is online.

**Themes survive the export.** *(Owner's request, 2026-09-20.)* The app's
light, dark and system themes and its accent colors are a `data-theme`
and `data-accent` attribute on `<html>`, set from `localStorage` by a
small inline script before first paint (`theme.ts`, and the same logic
in `index.html`). The site ships that script verbatim in every page and
a theme toggle in the header, the same control the app has. That is the
only JavaScript in the output: a few lines, no dependencies, and the
reader's choice lives in their own browser. A reader who never touches
it gets the system theme through the stylesheet's `prefers-color-scheme`
rule exactly as before. `index.html` and `theme.ts` already have to be
kept in step (architecture.md, "Theming"); the exporter reads the script
from one place rather than adding a third copy to keep in step.

**Size and time.** One page renders in roughly a second. The export runs
synchronously with a cap of 300 pages and streams the zip as it goes;
above the cap it refuses and names the number. A job model with progress
is the follow-up if a real space ever needs it, and it is not built
before one does.

**Not in v1, deliberately:** search (Pagefind is the obvious fit and
needs no server; a follow-up once a site exists to try it on); a custom
domain, base path or theme; comments; versions; anything that needs a
server. **Not decided here:** whether a site export should be schedulable
or hookable (a webhook on publish that re-exports), which is the natural
next step for documentation and belongs with 8.5's "export it, host it".

**UI.** Space settings → **Export** section, above the Danger zone:
**Export as a site**, with the audience choice and a sentence on where to
host the result. The page menu's Export entries are unchanged.

**Tests** (`SiteExportTests`): every page the audience may see is in the
zip and no other; slugs are unique and mirror the tree; every internal
link resolves to a file in the zip; every attachment referenced is in
`assets/` and nothing else is; `audience: 'anonymous'` on a private space
returns an empty site with a message; the cap. **Live:** export the
fixture space, serve the zip with a plain static file server, walk it in
the browser with the network to the instance blocked, check a table, a
diagram, a chart, an image and an internal link; then the same site on
a phone width.


**Follow-up, same day: the chrome (owner's request).** The export was the
page and nothing else, which is faithful and does not look like Tesria. Both
HTML exports now carry the app's top bar (the mark, the instance name, and
the full appearance menu with three modes and six accents), and a site also
carries the space sidebar: icon, key, public badge, name, and the page tree
under a PAGES heading with the current page marked. The index and the 404
carry it too.

- **The chrome is built, not captured, and that is the one exception to 12.1's
  rule.** It is in `Features/Export/SiteChrome.cs`. The rule exists because a
  second renderer of page *content* drifts; the chrome is not content, it is
  the exporter's own answers (which pages are in the site, what they are
  called, where they live, which one you are on), and the capture route has
  no chrome to photograph in the first place. Building it once also covers
  the index and the 404, which are not captures; rendering it in the SPA
  would have left those two needing a second copy, which is the drift the
  rule is about. It emits the app's class names against the app's compiled
  stylesheet, so restyling the app restyles every export.
- **A bug only the phone showed.** The app hides `.sidebar` under
  `--bp-mobile` because `.space-actionbar` covers its jobs; an export has no
  action bar, so a site would have shipped with no navigation at all on a
  phone. `.space-layout--export` brings it back, stacked above the page and
  capped at 45vh.
- **PDFs are untouched**, and the render route now says so explicitly:
  `chrome=page` and `chrome=site` keep the reader's theme, no chrome at all
  means paper and stays forced light.
- **An export now works from the filesystem** (owner, same day). Unzipped
  and opened, clicking a sidebar link showed Chrome's folder listing instead
  of the page: every link ended at a directory, which a server resolves to
  its index file and `file://` cannot. Links name `index.html` now (sidebar,
  body and brand alike), which works in both places and costs a hosted site
  nothing, and the 404 stopped linking to `/`, the root of the disk. The one
  case still not right is a host serving `404.html` for a missing path
  *below* the root, where the browser resolves its relative links against
  that path; making them root-absolute would fix it and break `file://`, and
  the export is more often read locally than 404'd at depth.
- **The width toggle came back** (owner, same day). The capture route has no
  action bar, so an export had lost both the reading view's full-width
  control and the page's own `fullWidth` setting, which meant every exported
  page was full width regardless of how it was written. An HTML export now
  opens at the page's width and carries the toggle in the top bar; the
  reader's choice persists across a site, and the phone hides it for the same
  reason the app does. A PDF gets neither: the sheet is the width, and
  `.page-wrap`'s 900px cap on A4 would be margins nobody asked for.
- **Two tweaks after the first look** (owner, same day). The PDF footer's
  page numbering was four flex items under `space-between`, so it read
  "Every element    1    of    4"; it is one item now, right-justified. And
  the space tile in an export is the theme accent rather than one of the
  app's twelve per-space colors: those tell spaces apart in a list and an
  export is one space, so the color says nothing there. Export only, the
  app keeps its own colors, and `SiteChrome.SpaceHead` deliberately does
  not carry `IconColor` so nothing can quietly start using it.
- **Branding is half done by accident.** The wordmark is the instance name,
  which an administrator already sets, so it already travels. The replaceable
  mark is the other half and is written up in `roadmap.md`;
  `SiteChrome.Brand.LogoPath` is the seam, and a space's uploaded icon (which
  is copied into `assets/` here) is the pattern to copy.

---

### 12.3 Turn a space's exports off, format by format · `S` · Model: Opus 5.5 · ✅ **shipped 2026-09-22**

**What the owner asked for (2026-09-22).** "Add in the space settings the
ability to disable exports on the entire space. It should be granular so we
can enable/disable markdown, pdf, and website." A permission administrators
and the owner hold by default, every format on by default, "but this will
allow admins to disable exports on a specific space if it is more sensitive
than other spaces in their instance."

**Decisions made in building it, stated so they can be reversed cheaply.**
1. **Five switches, not three.** The product has two more exports than the
   request names: a page as a single HTML file, and the whole space as a wiki
   pack. Leaving them out would let a sensitive space be downloaded whole, as a
   pack with its history, after an administrator believed exports were off.
   So each of the five is a switch: Markdown, HTML, PDF, Website and Wiki pack.
   Dropping two is a one-line change each if the owner wants three.
2. **A right of its own, `spaces.exports`,** Administration scope,
   `DefaultFrom: Admin`, so administrators and the owner hold it and any role
   can be granted it. It is not a space permission: judging a space more
   sensitive than the rest belongs to the instance's administrators, not to
   whoever created the space, the same reasoning as deleting a space (11.3).
3. **Off means off for everyone,** administrators and the owner included. The
   people who can change the setting can turn a format back on, and that is
   audited. Letting administrators bypass it would make the setting a
   suggestion.
4. **Refused with 403 and `code: export_disabled`**, naming the space and the
   format, before any rendering starts. Not 404: the reader can see the
   space, so pretending it does not exist would be a stranger answer than the
   truth.
5. **It stops downloads, not reading.** Anyone who can read a page can copy
   it, and the API and the MCP server return page content to anyone allowed
   to read it. The space settings text says so plainly rather than
   overstating what the switch does.
6. **Not carried by packs.** An imported space starts with every export on,
   like any new space. A pack from a space whose pack export is off cannot be
   made in the first place.

**As built.**
- Five columns on `Space`, all defaulting to true, including for existing
  rows through the migration.
- `SpaceExports` checks and words the refusal once, for the page, website and
  pack endpoints.
- `PUT /api/spaces/{key}/exports` sets all five at once, returns 404 for a
  space the caller cannot see, and audits `space.exports_changed` with before
  and after (nothing is recorded when nothing changed).
- Every space response carries the five flags.
- Space settings gain an **Exports** section for holders of the right, and
  hide the website and pack sections when those are off.
- The page view leaves out the downloads the space does not allow, for
  signed-in and anonymous readers alike.
- Tests: 11 (`SpaceExportTests`).


## Phase 13: Branding

### 13.1 Instance branding · `L` · Model: Opus 5.5 · ✅ **shipped 2026-09-22** (designed and implemented by Opus 5.5)

**What the owner asked for (2026-09-22).** "Add branding support to the app.
It will allow owners and admins (off by default) to edit the page branding.
They can set a new logo for Tesria and replace the word Tesria on the header
bar with their own brand name. They can also set the favicon." Logos in
several formats (SVG, PNG and so on). Theme options can be restricted for
users: only light, only dark, and a fixed accent color. The accent can be a
custom color, not only one of the six built in, and "if they allow both dark
and light themes they can set both a light and dark accent color". And:
"The branding should also be reflected in the web export. If someone exports
a page or site the branding they set should be visible in their exported
html."

**Model note.** The first item designed under the 2026-09-22 model gate, in
which Opus 5.5 does design and implementation. The decisions most worth a
second opinion, if the owner wants one, are the SVG handling (decision 6) and
getting branding into the page before first paint (decision 3), because a
mistake in either is a security hole or a visible flash on every load.

**What already exists, and shapes this.**
- **The name already exists.** `SiteSettings.InstanceName` (default "Tesria")
  is used in email subjects, the two-factor issuer, the public page titles
  `PublicMetaMiddleware` writes, the export's wordmark and the static site's
  footer. Only three places still hard-code "Tesria": the header in
  `Layout.tsx`, the `<title>` in `index.html`, and the setup wizard's fallback.
- **The export already has a seam.** `SiteChrome.Brand(Name, LogoPath)`
  was left for this, with a note saying so. `LogoPath` is always null today,
  and the `<img>` it would emit is fixed at 20×20, which would squash a wide
  logo.
- **Themes and accents are per browser**, in `localStorage`, applied before
  first paint by an inline script in `index.html`. That script is allowed by
  the CSP **by hash**, computed at startup from the file on disk
  (`SecurityHeadersMiddleware.InlineScriptHashes`). Anything that changes the
  script's text per instance breaks the hash and the script stops running.
- **An accent is six tokens per mode**, hand-tuned and contrast-checked
  (`index.css`, the accent block): `--primary`, `--primary-dark`,
  `--primary-soft`, `--primary-softer`, `--primary-soft-border`,
  `--on-primary`, plus an `--accent-dot-*` swatch.
- **The favicon is generated at runtime** from the accent (`theme.ts`,
  `applyFavicon`), following the OS theme rather than the app's.
- **Uploaded images are re-encoded by SkiaSharp**, which is the security
  control (EXIF stripped, polyglots defeated, decompression bombs bounded),
  and **SVG is rejected outright** (`ProfileMediaService`). Output is a 256px
  WebP square: right for an avatar, wrong for a logo.
- **The license is Apache 2.0.** It asks for a NOTICE file in
  redistributions and nothing in the running product's UI, so the product can
  be fully white-labeled without a "Powered by" line.

**Decisions made in this design, with the reasons.**

1. **The brand name is its own field, and nothing changes until someone sets
   it** (the owner's decisions A and B). `BrandName` is empty by default, and
   empty means "Tesria". Renaming the instance does not touch the header: an
   instance renamed "Acme Docs" with no branding still says Tesria in the top
   left. The brand name, when set, is the only name the interface shows:
   - **It is used in** the header, the sign-in, registration, reset and
     setup pages, the maintenance overlay, and the exported wordmark.
   - **The instance name keeps** the places that name *this installation*
     rather than decorate it: the browser tab title (decision 15), link
     previews' `og:site_name`, email subjects, the authenticator app's entry,
     and the static site's "Exported from" footer.
   - **One behavior changes for existing instances.** The name beside the
     logo in the top bar of an exported HTML page or site (the export's copy
     of the app header) shows the instance name today, while the app itself
     says "Tesria". After 13.1 both say the same thing: the brand name, or
     Tesria.

2. **A right of its own, `settings.branding`,** Administration scope,
   `DefaultFrom: Owner`, grantable to a role on the Roles tab. This is what
   "owners and admins (off by default)" means in the rights model 11.1
   built, and it is the same shape as `users.promote_admins` and
   `backups.restore`. It covers the brand name, logos, favicon, colors and
   locks. The instance name stays under `settings.instance`, which
   administrators keep, because the two are separate things.

3. **Branding reaches the page before first paint, and the inline script
   never changes.** The server already rewrites the HTML shell for public
   pages (`PublicMetaMiddleware`). That becomes the rule for every SPA HTML
   response, so the shell arrives with the branding in it:
   - `<title>` following the pattern in decision 15;
   - the favicon `<link>`s pointing at the brand's favicon when there is one;
   - a `<style id="brand-accent">` block holding the custom accent's tokens,
     which the CSP already allows (`style-src 'unsafe-inline'`);
   - `data-theme-lock` and `data-accent-lock` attributes on `<html>`.
   The inline bootstrap script reads those attributes and **its text stays
   byte-identical**, so its CSP hash still matches. Putting the policy inside
   the script would have been the obvious move, and it would have silently
   disabled the script on every branded instance. The shell stays
   `no-cache` (it already is), so a change reaches everyone on their next
   load. The SPA also receives the same branding from `/api/instance`, for
   the header and to react to a change without a reload.

4. **A custom accent is one color per mode, and everything else is
   derived.** The owner picks a light-mode color and, when both themes are
   allowed, a dark-mode one. The server derives the other five tokens with a
   fixed algorithm in OKLCH (a darker hover, two soft tints, a border tint,
   and `--on-primary` chosen by measured contrast, white or dark ink). The
   algorithm lives once, in C#, and the preview endpoint returns its output,
   so the admin page shows exactly what the server will emit. **Stored as
   normalized `#rrggbb` and validated as such, never as free CSS text**,
   because a color field rendered into a `<style>` block is a CSS injection
   point otherwise.

5. **The custom accent is checked for readability, and the owner has the
   last word** (decision C). `--primary` is link text, so the check is
   **4.5:1 against the page background** in its mode (`#ffffff` light,
   `--bg` dark), and `--on-primary` at 4.5:1 against `--primary`: the same
   checks the built-in six pass. A color that fails is shown with its
   ratio, and the nearest passing shade (same hue, lightness adjusted, computed
   by the server) is offered beside it as a one-click swap. The owner can
   still save the original. It is then saved with a plain warning that links
   and buttons will be hard for some people to read, and the audit entry
   records that the check was overridden.

6. **SVG logos are accepted, with two independent defenses.** SVG is a
   document format that can carry script, which is why avatars refuse it.
   A logo is where people actually have SVG, so here it is accepted, but:
   - **Sanitized on upload by an allowlist, not a blocklist.** Elements:
     `svg g path rect circle ellipse line polyline polygon text tspan defs
     linearGradient radialGradient stop clipPath mask use symbol title desc`.
     Attributes: geometry, presentation and transform attributes, `id`,
     `class`, `viewBox`, `xmlns`, and `href`/`xlink:href` only as an internal
     `#fragment`. Removed: `<script>`, `<foreignObject>`, `<style>` elements,
     every `on*` attribute, any `url(` that is not `url(#…)`, external
     references, entities and DOCTYPEs (parsed with DTD processing off, which
     also closes XML bombs). Anything unparseable is refused, never
     "cleaned as far as possible". Capped at 256 KB.
   - **Only ever displayed through `<img>`, never inlined as markup**, in
     the SPA and in every export. An SVG inside an `<img>` cannot run script
     or load anything, whatever it contains, so a sanitizer bug is not an
     XSS. It is served with `Content-Security-Policy: default-src 'none';
     style-src 'unsafe-inline'; sandbox` and `X-Content-Type-Options:
     nosniff`, so opening the file's URL directly is inert too.
   Raster logos (PNG, JPEG, WebP) are re-encoded by SkiaSharp like every
   other upload, with **the aspect ratio kept**: fitted within 1024×256,
   stored as lossless WebP (logos are flat color and need their
   transparency). GIF is refused: an animated logo in the header is not a
   feature. 2 MB upload cap before decoding, the existing decoded-pixel cap
   after.

7. **A dark-mode logo, optional.** A black logo vanishes on the dark theme.
   When both themes are allowed, the owner may upload a second logo for dark
   mode; without one, the light logo is used in both. It is switched by CSS
   on the same attributes the theme uses, so it never flashes. This is decision
   D only because the owner did not ask for it.

8. **The logo and the name are displayed in one of three ways**: logo and
   name (the default, as Tesria is today), logo only (for a logo that already
   contains the name), or name only. The logo renders at the header's
   height with its width following its aspect ratio, capped so a very wide
   logo cannot push the search box off the bar.

9. **The favicon is separate from the logo.** A wide logo makes a useless
   16px icon, so reusing it would be a trap rather than a convenience.
   Accepted: SVG, PNG, ICO, JPEG, WebP. Stored: the sanitized SVG when one
   was given, plus PNG at 32px (tabs), 180px (`apple-touch-icon`) and 512px.
   Rasterising an SVG favicon needs an SVG renderer: `Svg.Skia`, a NuGet
   library on the SkiaSharp already in the app (decision E, answered). **It
   is not a new image or container.** It runs inside the existing `app`
   process and does work only while an SVG favicon is being uploaded, so its
   steady-state memory cost is nothing. **Unverified until Opus checks:** its
   license, that it fetches nothing and runs nothing, and how much it adds to
   the app image. Opus records the size difference in the changelog, and if
   it proves heavy, falls back to requiring a PNG alongside an SVG favicon. When a favicon is set, the
   runtime accent favicon stops. With no custom favicon, a custom accent
   paints the built-in mark in the brand color, as the six do now.

10. **Theme and accent policy.**
    - Theme: *users choose* (today), *light only*, or *dark only*.
    - Accent: *users choose*, with the brand accent as the default for anyone
      who has not picked and shown first in the picker under the brand name;
      or *locked* to the brand accent, which removes the picker.
    - Only the modes in use need an accent color: light only needs the light
      value, dark only the dark one, both themes need both.
    - A lock overrides a person's stored preference without deleting it, so
      lifting the lock gives everyone back what they had chosen.
    - The appearance menu hides what is locked, and disappears when both are.
    - Locking the accent to one of the six built-in colors is allowed too.
      A custom color is not required to lock.

11. **Where the branding appears:** the header, the tab title and favicon,
    the sign-in, registration, reset and setup pages (which today show no
    brand at all), the maintenance overlay, public read-only pages, and both
    web exports. **Not in:**
    - The PDF. It is paper: chromeless and forced light by design (12.1).
    - Email. It already carries the name; logos in HTML email are out of
      scope.
    - Wiki packs. A pack is content and moves between instances; branding
      belongs to whichever instance renders it.

12. **Exports carry the branding as it was at export time.** An export is
    a photograph, and it does not change when the branding does.
    - `SiteChrome.Brand` gains the dark logo, the display mode and the accent
      block.
    - A site export copies the logo, dark logo and favicon into `assets/`.
    - A single-page HTML export inlines them as `data:` URIs.
    - Both reference them only through `<img>` and `<link rel=icon>`, never as
      inline SVG markup. An exported file is opened from disk with no CSP at
      all, so `<img>` is its only defense.
    - The custom accent's `<style>` block goes into the export's `<head>`.
    - The export's own theme script honors the same two lock attributes.
    - Its appearance menu hides locked parts, and shows the brand accent as a
      swatch when the accent is free.
    - The fixed 20×20 logo size in `SiteChrome.Topbar` is replaced by a
      height with automatic width.

13. **Audited, not alerted.** `branding.changed` records who changed what,
    with before and after values and file hashes. A changed logo is visible
    to everyone at once, which makes it its own alarm. An alert would add
    noise and no new information.

14. **A "Reset to Tesria" button** clears the brand name, removes every
    uploaded file, and restores Tesria's mark, the six accents with no locks,
    and the generated favicon. It asks for confirmation first, because the
    uploaded files cannot come back. The instance name is untouched: it was
    never branding.

15. **Tab titles are `Instance Name - Space Name / Page Name`** (the owner's
    instruction, 2026-09-22). Today the application never sets the title, so
    every tab says "Tesria" whatever is open in it. From here:
    - A page: `Acme Docs - Engineering / Architecture`.
    - A space with no page open (its overview, settings, trash):
      `Acme Docs - Engineering`.
    - Anywhere else, the section: `Acme Docs - Search`,
      `Acme Docs - Administration`, `Acme Docs - Sign in`.
    - Before a page's title has loaded, the space alone. It never flashes
      "Tesria" in between.
    - The server-rendered shell starts with the same title for public pages,
      replacing today's `Page · Space · Instance`, so a link preview and the
      first paint agree with what the app shows afterwards. `og:title` stays
      the bare page title.
    - Exports use the same pattern, including a site's index
      (`Acme Docs - Engineering`) and each exported page.
    - One small pure function builds the title, in the SPA and mirrored in
      C# for the shell and the exports, so the two cannot disagree.

16. **The sign-in page carries the brand** (the owner's request,
    2026-09-22: "Above that put the Logo and Branding"). Today the card says
    only "Sign in". From here, above the card's heading, the brand block in
    one of four arrangements (the owner's refinement, the same day):
    - **Logo and name side by side, the default.** Unbranded, that is
      `[Tesria mark] Tesria`, the header's own arrangement at a larger size:
      the logo up to 48px tall, the name at heading size, centered on each
      other.
    - **Logo above name, stacked.** The logo up to 72px tall, the name
      beneath it.
    - **Logo only.** For a logo that already contains the name, up to 72px
      tall.
    - **Name only.**

    It is **one setting that covers the header and the sign-in page, plus one
    sign-in-only choice.** *What to show* (logo and name, logo only, name
    only; decision 8) applies to both places, so a logo that contains the
    name never gets the name printed twice beside it on one screen but not
    the other. *How to arrange them* (side by side, or stacked) matters only
    when both are shown, and only on the sign-in page, because the header is
    always side by side. Together they give exactly the owner's four options.
    - **Sizing.** Logo widths follow the logo's aspect ratio, capped at the
      card's width. The dark logo is used in dark mode, as in the header.
    - **Unbranded, it is Tesria's own mark and name,** side by side. The page
      is no longer anonymous, and it doesn't look like it is waiting to be
      configured.
    - **The same block is on every page built on the sign-in card**: sign-in,
      its two-factor step, registration, account recovery, and the setup
      wizard's first screen.
    - **No separate sign-in logo is needed.** An SVG scales to any size
      cleanly. A raster logo is stored at up to 256px tall (decision 6), which
      is enough for 72px on a high-density screen. The one thing that can look
      soft is a small raster original, so the Branding tab warns when an
      uploaded raster logo is under 150px tall and suggests SVG or a larger
      file. An optional separate sign-in logo, for a compact header mark with
      a full lockup at sign-in, is easy to add later if anyone asks.
    - "Powered by Tesria" sits under the card, as decision F says.

**Schema.** One migration, on `SiteSettings`:
- `BrandLogoHash`, `BrandLogoFormat` (`svg` | `webp`), `BrandLogoDarkHash`,
  `BrandLogoDarkFormat`;
- `BrandFaviconHash`, `BrandFaviconHasSvg`;
- `BrandName` (nullable; empty means "Tesria"), `BrandDisplay`
  (`logo-and-name` | `logo` | `name`), `SignInArrangement` (`side-by-side` |
  `stacked`, default `side-by-side`);
- `ThemePolicy` (`any` | `light` | `dark`), `AccentPolicy` (`any` | `locked`),
  `AccentName` (one of the six, or `brand`), `BrandAccentLight`,
  `BrandAccentDark` (both nullable `#rrggbb`);
- `BrandChangedAt`, `BrandChangedById`.

Files go through `IAttachmentStorage` under `branding/` with deterministic
keys, like avatars, so a replacement overwrites and the S3 slot that
interface reserves covers them. The content hash is the cache buster.

**Endpoints.**
- `GET /api/branding/logo`, `/logo-dark`, `/favicon.svg`, `/favicon-{32|180|512}.png`:
  anonymous, because the sign-in page needs them. Immutable caching on
  `?v=<hash>`, and the SVG headers from decision 6.
- `GET /api/instance` gains a `branding` object: the brand name (or
  Tesria), display mode, logo URLs, policies, accent name and the derived
  tokens.
- `GET /api/admin/branding`, and `PUT /api/admin/branding` for the brand
  name, display mode, policies and colors (`settings.branding`, sudo,
  audited).
- `POST /api/admin/branding/logo`, `/logo-dark` and `/favicon`, as multipart
  uploads, each with a `DELETE`.
- `POST /api/admin/branding/accent-preview` takes `{ light, dark }` and
  returns the derived tokens, the four contrast ratios, pass or fail, and
  the nearest passing shade.
- `POST /api/admin/branding/reset`, with sudo and audit.

**UI.** Administration gains a **Branding** tab, shown with the right:
- A live preview of the header bar in both themes that follows every change
  before it is saved.
- The brand name, blank by default with "Tesria" as its placeholder.
- The logo, the dark logo and the favicon, each with an upload, a preview and
  a remove button. The logo previews show both the header size and the
  sign-in size, and a raster logo under 150px tall gets the softness warning
  from decision 16.
- What to show (logo and name, logo only, name only), and, when both are
  shown, how the sign-in page arranges them (side by side, or stacked). The
  live preview includes the sign-in block.
- The theme policy.
- The accent: the six built-in swatches plus "Custom", which opens two color
  fields with their contrast readouts, the nearest passing shade offered as a
  one-click swap, and the warning when a failing color is kept.
- The accent policy.
- Save, and **Reset to Tesria**, which confirms first.
- At the foot of the tab, as on every Administration tab, the version line
  from decision F.

**Steps for Opus, each a commit that leaves the product working.**
1. **The name, titles and the shell.** `BrandName` and the right exist. The
   header, sign-in, reset, registration and setup pages and the export's
   wordmark show the brand name, or Tesria. Tab titles follow decision 15
   everywhere, including the shell and exports. The shell is rewritten for
   every SPA HTML response. "Powered by Tesria" and the version line from
   decision F. No uploads yet.
2. **Theme and accent.** Policies, custom accents with the derivation and
   contrast checks, the lock attributes, the bootstrap script reading them
   (unchanged text, hash test), the appearance menu honoring them, the
   preview endpoint, and the tab's accent and policy sections.
3. **Logos and favicon.** The sanitizer, raster re-encoding, storage, the
   serving endpoints with their headers, the dark logo, display modes, the
   brand block on the sign-in card (decision 16), the favicon set, and the
   tab's upload sections.
4. **Exports.** `SiteChrome`, the site export's assets, the single-page
   export's data URIs, and the export theme script honoring the locks.
5. **Docs.** `architecture.md`, `security.md` (a layers row for SVG uploads),
   the changelog, and a note for 10.5 so the manual's screenshots are taken
   unbranded.

**Tests.**
- **Unit:**
  - The right is the owner's by default and grantable.
  - A wrong color format is refused.
  - The derivation is stable, and each built-in accent put through it passes
    its own contrast checks.
  - Renaming the instance leaves the header saying Tesria. Setting a brand
    name changes it, and clearing the brand name goes back to Tesria.
  - The title function, for a page, a space, a section, a page still loading,
    and names containing a slash or a hyphen (vitest on the SPA's copy, xunit
    on the C# copy, the same cases in both).
  - The public shell's title follows the pattern, and `og:title` is the bare
    page title.
  - "Powered by Tesria" is absent from an unbranded sign-in page and present
    on a branded one.
  - A color below 4.5:1 gets the warning and a suggested shade that passes.
    Saving it anyway works, and the audit entry records the override.
  - The shell carries the title, favicon, style block and lock attributes.
  - **The inline script's hash is the same on a branded and an unbranded
    shell.**
  - Locks: light only needs no dark color.
  - Reset to Tesria clears the brand name and every file, and leaves the
    instance name alone.
  - Audit entries are written.
- **The sanitizer gets its own suite of hostile SVGs:** `<script>`, `onload`,
  `<foreignObject>` with HTML, `javascript:` and external `href`,
  `url(https:…)` in a style attribute, `@import` in a `<style>` element, an
  XXE entity, a billion-laughs DOCTYPE, `<use>` pointing at another file, and
  an SVG that is also valid HTML. Each is either refused or comes out inert,
  and a benign logo comes out visually unchanged.
- **Raster:** aspect ratio is kept, EXIF is gone, a decompression bomb is
  refused and a GIF is refused.
- **Live:**
  - Brand this instance with an SVG logo, a dark logo, a custom favicon and a
    custom accent.
  - Walk the header in both themes, a public page, and the tab icon.
  - Walk the sign-in page signed out: unbranded, then branded with an SVG
    logo and a small PNG, in all four arrangements, in both themes and at
    phone width. Also the two-factor step, registration and recovery.
  - Check the tab title on a page, a space, search and Administration, and
    in an exported page and site.
  - Lock to dark and confirm there is no light flash on load, then unlock and
    confirm each person's own choice comes back.
  - Export a page and a site, open both from disk offline, and check the logo,
    accent and locks are there.
  - Open the logo's URL directly and confirm nothing runs.
  - Reset, and leave the instance unbranded as it was found.

**Decisions, answered by the owner 2026-09-22.** The owner's words are
quoted where they settled something.
- **A. Branding and the instance name are separate.** "The instance name and
  branding are two separate entities." Branding has its own right, and the
  instance name stays with administrators under `settings.instance`.
- **B. Only the brand name shows in the interface.** "The instance name is
  pretty inconsequential." Nothing changes until branding is set on purpose:
  "This should only replace the branding if someone intentionally configures
  the branding." See decision 1.
- **C. Suggest a better shade, but let the owner keep theirs.** "Suggest a
  better shade, but allow users to override it if they like." See decision 5.
- **D. The dark-mode logo is in.** "This is a good idea."
- **E. `Svg.Skia` is in, as long as it adds no image.** "As long as this isn't
  another docker image. I am worried about the number of images and the
  amount of ram required to run Tesria." It is a library inside the existing
  app process. See decision 9 for the size check and the fallback.
- **F. Attribution: subtle, in two places.** "I want it to be light if
  someone wants to use their branding. I don't need to be in users face
  screaming that this is Tesria. I like a subtle approach."
  - **Under the sign-in form**, a single muted line, "Powered by Tesria",
    in the small secondary text size, linking to the project. Shown only
    once branding is configured: before then the page already carries
    Tesria's mark and name, and saying it twice is the opposite of subtle.
  - **At the foot of Administration**, a version line, "Tesria" and the
    version `/api/health` already reports, on every tab, always. Only administrators see it, and it
    is useful anyway when asking for help.
  - Not in exports, not in the app's header or any page readers use, and no
    footer is added. No switch to hide it either: it is already as quiet as
    it can be while still being there, and a switch can be added later.

**As built (2026-09-22), where it differs from the spec above.** All five
steps shipped together, in one sitting, rather than as five commits.

- **The page shell now goes through one endpoint.** The design said the
  server would write into the shell "for every SPA HTML response". In the
  code that meant replacing the static-files fallback with an endpoint
  (`Features/Public/SpaShell.cs`), dropping `UseDefaultFiles`, and mapping
  `/` and `/index.html` explicitly. The fallback's catch-all does not match
  the root, and routing runs before static files, so a path rewrite alone
  could not reach it. `PublicMetaMiddleware` is folded into the same
  endpoint. An unknown `/api/...` route now answers 404 instead of a page of
  HTML, which the old fallback served.
- **The inline script changed once, in the file, and never per instance.**
  It now reads the lock attributes, and it reads storage through a guard so
  that a browser blocking site data still gets the locks. A test renders a
  fully branded shell from the real `src/web/index.html` and compares the
  script's bytes.
- **The app image shrank from 1,070 MB to 455 MB**, with the SVG renderer
  added. Svg.Skia brings HarfBuzz, and the framework-dependent publish had
  been copying every package's native libraries for about fifteen platforms,
  Windows debug symbols included, into a Linux image: 155 MB more with the
  new package. `deploy/Dockerfile` now publishes for the image's own
  architecture (`-r linux-arm64` or `linux-x64` from BuildKit's
  `TARGETARCH`), which drops all of it, SkiaSharp's own included. Nothing
  about the owner's condition changed: no new image, no new container, and
  the renderer is only loaded while an SVG favicon is being uploaded.
- **Exports stopped pointing at the instance for their favicon.** The
  capture kept the page's `<link rel="icon" href="/favicon.svg">`, which
  showed nothing once the file left the server. This was true before
  branding and nobody had noticed. Exports now carry the brand's favicon,
  or Tesria's, as a file or an inline URI. `SiteChrome.ApplyToDocument`
  strips whatever branding the capture happened to carry (attributes, the
  accent block, icon links) and writes the export's own, so there is exactly
  one of each.
- **One implementation slip, caught by an existing test.** The upload and
  delete routes, written in their own file, were first mapped without the
  right, so any signed-in account could have replaced the logo. The
  route-rights test (`Every_administrative_route_names_a_right_the_catalog_defines`)
  failed on them before anything was committed. The right is now required on
  the whole `/admin/branding` group, and a test tries each file route as an
  administrator without it.
- **A readability check the owner can override, and the audit says so.** A
  failing color is shown with its ratios and the nearest passing shade as a
  one-click swap. Saving it anyway records `ContrastOverridden` on the
  `branding.changed` entry.
- **Colors and locks apply by reloading the page after Save**, because they
  live in the shell the server sends. The name, display mode and logos
  update the header in place.

**Verified live on this instance (2026-09-22).**
- **Unbranded:** the sign-in page shows `[Tesria mark] Tesria` side by side,
  with no "Powered by". The stored theme still applied before paint, and
  there were no CSP errors.
- **Branded:** tested with an SVG logo, a dark-mode logo, an SVG favicon and
  a custom accent (`#7a1fa2` light, `#d49cf0` dark).
  - The sign-in page in logo-only mode, and stacked while locked to light,
    with the operating system in dark mode. The lock held, and the light logo
    and accent were used.
  - The header on every Administration tab, and the Branding tab.
  - The logo's URL returns the sandbox policy.
- **Tab titles** on a page, a space, search, Administration and the editor.
- **Exports:** a single-page HTML export and a whole-site export, each
  checked for its title, logos, favicon, accent block and root attributes,
  with no reference back to the instance. The site was then served from a
  bare local web server: it followed the system's dark mode, showed the
  dark-mode logo and the brand accent, and offered the brand swatch first.
- **Reset to Tesria** put everything back and deleted the files. The
  temporary grant of the right to the Administrator role was revoked, and
  `.env` was byte-identical.

**Trial record (the model gate, 2026-09-22).** The first item designed and
built by Opus 5.5. Nothing that surfaced during implementation was a design
miss of the kind the gate exists for:
- The two traps the design named, the CSP hash and a hostile SVG, were the
  ones that mattered.
- The routing and image-size findings were implementation detail.
- The unguarded upload routes were an implementation slip, and an existing
  test caught them.

**Not decided here, deliberately:**
- Per-space branding.
- Custom fonts.
- Arbitrary custom CSS. Never recommended: it is script-adjacent and
  unreviewable.
- A logo in HTML email.
- Branding the PDF.

---

## Phase 14: The public release

**What the owner asked for (2026-09-23).** "It will go public as a repo
under the Apache 2 license. I also want to post to dockerhub if it's free so
people can pull there. I have an account ready to do that." Publishing by
GitHub Actions (the owner's choice, 2026-09-23). The Docker Hub account is
`brianrodz`, on the free Personal plan: unlimited public repositories, and
people pulling are limited to 100 pulls an hour each.

`LICENSE` (Apache 2.0) and `NOTICE` exist already (8.2). What stands between
the repo and the public is the audit `CLAUDE.md` has been waiting on since
2026-09-08. 3.7 was a security review of the code as of 2026-09-09; phases 5
to 13 came after it.

### 14.1 Pre-release audit · `M` · Model: Opus 5.5

Before the repo or any image is public:

1. **Secrets and private details in the whole history**, not only the
   tree: credentials, the NAS address and share names, personal email
   addresses, anything from `.env`, `.nas-credentials` or
   `.debug-credentials`. A finding in history means rewriting history
   before the first public push, which is the one hard-to-reverse decision
   here, so it goes to the owner first. Known already (2026-09-24): the
   owner's personal address is in commits `586b045` and `41b4a2a` (the
   CHANGELOG and `SECURITY.md` as they were then). The tree no longer has
   it: `SECURITY.md` now gives the project's security address.
2. **What the repo says about its owner.** `CLAUDE.md`, the CHANGELOG and
   the plan are written for this project's sessions and name the owner's
   machines and habits. Decide what a public reader should see: keep,
   trim, or move to a private place.
3. **A security pass over everything since 3.7**, the same method as 3.7
   (every route for authorization, every upload and export path), with its
   findings added to `security.md`'s "Known gaps" or fixed.
4. **Dependencies**: `npm audit` and `dotnet list package --vulnerable`
   clean, or each finding explained.
5. `SECURITY.md` gets a real contact (3.7 left the disclosure path to this
   point).

**Fixed 2026-09-24** (all of the list below, with four worse findings the
review of it turned up; see the CHANGELOG's "14.1 The security findings,
fixed").

**Steps 1 to 5 done 2026-09-24, not released** (the owner's instruction:
audit fully, then hand a fresh clone to an outside reviewer first):
1. History rewritten: every commit and the `v0.5.0` tag carry the owner's
   GitHub no-reply identity; a personal address and a NAS path were
   scrubbed from old document versions; old versions of the Support pack
   (screenshots that could not be checked) were removed. Verified on a
   fresh clone: no personal address, machine name, tailnet, NAS path or
   credential value in any file version or commit, and no secret-shaped
   string outside tests. The pre-rewrite repository is kept as a bundle
   outside the repository.
2. `CLAUDE.md` keeps the conventions; the owner-specific notes moved to a
   gitignored `CLAUDE.local.md`. The dev plan and CHANGELOG were trimmed.
   Old versions of some files still name the owner's public site, which is
   not a secret.
3. A second security review in three parts: seventeen findings, the two
   high ones and most others fixed (CHANGELOG, "14.1 The second security
   review, fixed"), the rest recorded in `security.md`'s known gaps.
4. `scripts/audit.sh` clean.
5. `SECURITY.md` has the project's address.

What is left before release: the outside review, then 14.2.

**Found by the 10.5 fact-finding, 2026-09-23 (all fixed 2026-09-24):**

- A full-access token can mint more tokens and revoke the owner's sessions,
  and tokens never expire.
- Render tokens (`trx_`) reach far more of `/api` than their comment says,
  and space-scoped ones are not held to their space.
- Some refusals are 403 where the rest of the API says 404, which reveals
  that a hidden space exists (webhooks, space permissions, page restore,
  restriction removal, template delete).
- Drafts and trashed pages are readable by id through versions,
  attachments, labels and comments.
- `GET /api/users` and group member lists give every signed-in user every
  account's email address.
- Holders of `users.manage` can suspend, sign out and issue reset links for
  other administrators; a reset link is an account takeover.
- Admin routes that check rights inside the handler skip the "administrators
  must have two-factor" rule.
- Comment edit and delete do not re-check that the author can still see the
  page.
- Any user can create an instance-wide template.
- The audit list is cut to its limit before permission filtering.
- The favicon's source comment named the owner's personal site, and every
  HTML export and exported site shipped that comment. Removed.

### 14.2 Images on Docker Hub · `M` · Model: Opus 5.5 · after 14.1

- **Five images**, each for both `linux/amd64` and `linux/arm64`:
  `brianrodz/tesria-app`, `-db` (used by the `db` and `pgbackrest`
  services), `-backup`, `-collab` and `-pdf`. Caddy and MinIO stay their
  upstream images. The repositories are created by the first push; nothing
  is made by hand.
- **Tags**: `1.2.3`, `1.2` and `latest` from a pushed git tag `v1.2.3`,
  nothing from ordinary commits.
- **GitHub Actions**, one workflow on `v*` tags and on demand. Each
  architecture builds on its own native runner (`ubuntu-24.04` and
  `ubuntu-24.04-arm`, free for public repos) and a last job joins them into
  one multi-architecture tag; emulating ARM would take the PDF image, which
  carries a browser, from minutes to most of an hour.
- **Credentials**: a Docker Hub access token with read and write access,
  held as the repository secret `DOCKERHUB_TOKEN`, with the account name in
  the variable `DOCKERHUB_USERNAME`. The owner makes both; no session sees
  the token.
- **`docker-compose.yml`** names each image (`image:`) next to its
  `build:`, with the version from `TESRIA_VERSION` (default `latest`). So
  `docker compose pull` then `up -d` runs the published images, and
  `up -d --build` still builds from source.
- **Support site**: the Quick start leads with pulling the images, and
  building from source becomes the alternative. Rewritten as part of this
  item, once the commands can be tried against real images; until then it
  says to clone and build, which is true today. (The owner confirmed on
  2026-09-24: the `git clone` stays until the public release and the
  Docker Hub rollout.)

**Not decided here, deliberately:** the Docker-Sponsored Open Source
program (it lifts the pull limit and allows an organization namespace, but
needs an established public project to apply), signing images, and
publishing to GitHub's own registry as well.

### 14.3 Enterprise hardening: the known gaps worth closing · `M` · Model: Opus 5.5

Asked for by the owner, 2026-09-24: "I want this to be enterprise grade
software." Each known gap in `docs/security.md` now carries a verdict; this
item is the ones marked Must fix and Should fix, in the order worth doing.

**Must fix**
1. **A `migrate` service** holds the database owner's credentials, runs the
   migrations and creates the least-privilege role, then exits; the app and
   the collaboration service start after it with the app role only (gap 10).
2. **Live-editing connections end when access does** (gap 11): the
   collaboration service closes a connection when its token expires and when
   the app tells it (over its existing authenticated channel) that a user was
   suspended, signed out everywhere, or restricted from the page.
3. **Registration is audited** (`user.registered`, with how: invite, open
   registration, single sign-on) (gap 3).

**Should fix**
4. The version is answered to signed-in callers only (gap 1).
5. The password-reset email is sent in the background (gap 12).
6. The TOTP challenge is single-use (gap 6).
7. The Compose network gets a fixed subnet, and only it is trusted (gap 7).
8. NAT64 in the egress guard's private list (gap 14).
9. An optional image-host allowlist for pages (gap 2).

**Acceptable, documented:** gaps 4, 5, 8, 9 and 13 (single server).

---

## Phase 15: What the Support site found missing

**What the owner asked for (2026-09-23).** Writing the Support site (10.5)
meant documenting every screen, and the fact-finding turned up access
questions and features people would expect. The owner's answers:

1. Someone with two-factor on who has lost the phone and every recovery code
   needs a way back: "Separate admin action".
2. An imported pack: "Make it private to the importer, but make them assign
   users or groups immediately after import. There should also be 3 default
   groups that cannot be deleted Owner, Admin, and Users." Membership nested
   (the owner's choice between nested and by-tier).
3. Rights of the administration area: "Do not give these rights to user tier
   roles. Make them get promoted to an admin if you want to give them admin
   privileges."
4. The missing features: "All of them. Lets add them to the dev plan now and
   build them."

### 15.1 Access · `M` · Model: Opus 5.5 · ✅ **shipped 2026-09-23**

- **Turn off two-factor, by an administrator.** A button on Users, beside
  Reset password, for accounts with two-factor on. Needs `users.manage`,
  asks for the password again (sudo), is never offered on the owner, and is
  refused on another administrator unless the caller is the owner (the same
  rule as promoting). Clears the secret and signs the account out
  everywhere. Audited as `user.totp_disabled`; raises a Warning alert,
  "Two-factor turned off for an account", with no cooldown, because this is
  exactly what an attacker holding an admin session would do. Resets stay
  as they are: a reset link never touches two-factor.
- **Built-in groups: Owner, Admins, Users.** Seeded on startup, cannot be
  renamed, deleted or have members added or removed. Membership is not
  stored: it follows the tier at the moment of the check.
  - Owner: the owner.
  - Admins: administrators and the owner.
  - Users: every active account.
  The permission service resolves them wherever a group principal is
  checked. The Groups tab lists them first, marked built in, with their
  members shown read-only.
- **Imported packs start private.** The import grants the importer Admin on
  the new space, so it starts as private as a space whose first grant was
  theirs. The result screen then goes straight to **Who should have
  access?**: the space's permissions, with the built-in groups offered first
  (Users is one click from "everyone signed in"). Skipping it leaves the
  space to the importer alone, and the screen says so. The pack export
  screen's promise is then true.
- **Administration rights stay in the administrator tier.** Rights whose
  scope is Administration are not offered for user-tier roles: the matrix
  shows them as not applicable there, and the server refuses to grant them.
  A one-time migration removes any the user tier already holds, and the
  audit log records what was removed. Content rights (including creating
  invite links) stay available to both tiers.

### 15.2 Editor · `L` · Model: Opus 5.5 · ✅ **shipped 2026-09-23**

- **Tables:** turn the header row and the header column on and off; merge
  and split cells; a **Delete table** button. All in the cell menu (the
  arrow in a cell), beside the background colors.
- **Images:** resize by dragging a corner (widths stored as a percentage of
  the column, so they survive other screens), alignment (left, center,
  right, and full width), a caption under the picture, and editable alt
  text. All in the image bubble; exports keep them.
- **File or video:** an **Upload** button in the block itself, on new pages
  too, instead of a trip to the Attachments tab.
- **Smart link, inline:** Inline becomes a real inline link that sits in a
  sentence (a separate inline node), not a block that happens to be short.

### 15.3 Pages and collaboration · `L` · Model: Opus 5.5 · ✅ **shipped 2026-09-23**

- **Resolve comments.** A thread can be resolved and reopened by its author,
  the page's editors, and space administrators. Resolved threads fold away
  under **Show resolved**; an inline comment's highlight goes when it is
  resolved and comes back if reopened.
- **Compare versions.** In History, pick two versions and see what changed
  between them, words added and removed highlighted, reusing the tracked-
  change marks of 8.6 in a read-only view.
- **Move and copy from the page menu.** **Move** picks a new parent, in this
  space or another the person can edit, with the same checks as a drag.
  **Copy** makes a new page (and optionally its sub-pages) with the content
  and labels, not the history or comments, titled "Copy of …".
- **A label index.** `/labels` lists every label in use with its page count,
  linked from each label page and from Finding things.
- **Mentions in comments.** Typing @ in a comment box offers people the same
  way the editor does; the person is notified if they can see the page.
- **Reopening a restricted space.** Space settings → Permissions gets **Make
  this space open again**: removes every grant after a confirmation that
  says the space becomes readable, editable and administrable by everyone
  signed in, and asks for the password. Audited; alerts administrators.

### 15.4 Confirmations · `S` · Model: Opus 5.5 · ✅ **shipped 2026-09-23**

Every destructive or account-affecting admin action gets a confirmation
naming what it does and to whom: Suspend, Sign out, Revoke tokens, invite
Revoke, removing a block, removing a group member, and the Security tab's
one-click mitigations.

### 15.5 Trust this device · `M` · Model: Opus 5.5 · ✅ **shipped 2026-09-23**

**Verified by the owner on Windows and iOS (2026-09-23)**, after two fixes
his first tries found: Windows' execution policy (now a pasted line) and a
numeric address that can never match the certificate (now explained).

**Asked for by the owner, 2026-09-23**, reviewing the first Support site:
trusting the local certificate was written for people who already know
what a certificate is. "If it's hard to setup or maintain people will drop
it in favor of something easier to use." The owner suggested an endpoint
that serves the certificate and the scripts, and a wizard that asks a few
questions and fills the script in.

**What it is.** A page at `http://<server>/trust`, served over **plain
HTTP** on purpose: it is the one page a device that trusts nothing yet can
open without a warning, the same reason `/ca.crt` is served on port 80. It
asks two questions, with answers guessed already:

1. **Which device is this?** Windows, Mac, Linux, iPhone or iPad, Android,
   from the browser's user agent, changeable.
2. **What address do you open Tesria at?** Filled in from the address the
   page was opened at. A bare IP address gets a warning, with the reason
   and how to find the computer's name instead.

Then numbered steps for that device, in the words a beginner needs: on a
Mac or Linux, download a script **with the address already written in** and
run it (the exact command, where to type it, and that the password prompt
shows nothing as you type); on Windows, **one line to paste into
PowerShell**, because PowerShell's execution policy refuses downloaded
scripts by default and employers lock it (the owner's first try on
Windows, 2026-09-23), while a typed command is not affected and trusts the
server for that Windows account with no administrator; by hand as the
second option; for a phone, the
certificate itself and the Settings path to trust it. Last, **Check it
worked**: a link to `https://<address>/` that should open with no warning.
Firefox, which keeps its own list, gets its own note.

**The scripts** stay `deploy/scripts/trust-ca.sh` and `trust-ca.ps1`, one
copy. Each gains a clearly marked line near the top, `TESRIA_ADDRESS`,
which is the one thing a person edits by hand and the one line the wizard
fills in. The app embeds both at build time and serves them at
`/trust/trust-tesria.sh` and `/trust/trust-tesria.ps1?address=...`.

**Where you find it.** One wizard, three ways in. **Profile → Trust this
device**, the owner's suggestion (2026-09-23): most people click past the
warning once, sign in, and can then set the device up properly from inside
the app. The sign-in page, for someone past the warning but not signed in.
And the address itself, which is the only way for a browser that will not
let anyone click past (managed browsers, or any that has seen HSTS) and the
easiest on a phone. The wizard stays a page of its own rather than a screen
of the app, because it has to work over plain HTTP for those last cases;
two copies would drift.

**Where it shows.** Only where the server uses its own certificate
(the default `deploy/Caddyfile`). With `CADDYFILE=deploy/Caddyfile.public`
there is nothing to trust, and `/trust` says so. The app learns which file
is in use from the same `CADDYFILE` variable, so there is nothing new to
set. The sign-in page links to it ("Seeing a security warning?") on
servers with their own certificate.

**Decisions worth a second look** (security, named so the owner can choose
to have them reviewed):

- **An anonymous page over plain HTTP.** It holds nothing secret: the
  certificate's public half, which `/ca.crt` already serves, and two
  scripts. Someone who can tamper with a LAN's plain HTTP could change the
  page or the script. That is already true of `/ca.crt`, which this does
  not make worse: a first contact over an untrusted network has to trust
  something, and on a home or office LAN that is the accepted risk. Both
  scripts print the certificate's fingerprint for anyone who wants to
  compare it at the server.
- **The address is written into a script someone will run as
  administrator.** It is checked against a strict pattern (letters, digits,
  dots and hyphens, or an IPv4 address) before it goes anywhere near the
  script, so no quote, space, `$` or backtick can reach a shell. Tested
  with hostile input.
- **The page's script is a separate same-origin file**, so the Content
  Security Policy is unchanged: no inline script, no nonce.

### 15.10 Email an invite · `S` · Model: Opus 5.5 · ✅ **shipped 2026-09-24**

Asked for by the owner: with email set up, the invite form emails the link
with a message the inviter can edit, so nobody copies a code into a chat.
Tesria appends the link, the address it works for and its expiry, so they
cannot be edited out. Sent when the invite is made, the only moment the
token exists in plain text; a failed send keeps the invite and says why.
See the CHANGELOG.

### 15.9 Filter the page tree · `S` · Model: Opus 5.5 · ✅ **shipped 2026-09-23**

The owner: "add a search to the top of the pages tree that filters the list
based on the search", in exports too, resetting once a page is chosen.
Matches and their parents, highlighted, in the app's tree and an exported
site's sidebar, by the same rule. A toggle adds the pages under each match.
The owner then chose to keep the filter while its results are opened,
rather than clearing it.

### 15.8 Numbered and bulleted page trees · `S` · Model: Opus 5.5 · ✅ **shipped 2026-09-23**

Asked for by the owner after trying 15.7 on the Support site: "an option
to automatically # or bullet the space pages kind of like the table of
contents element", and the markers "only a visual element and not part of
the actual page key or title", reflowing when pages move. A space setting
(`Space.TreeStyle`, migration `SpaceTreeStyle`): Plain, Numbered (outline,
1.1) or Bulleted. Computed from the tree's order wherever it is drawn: the
app's sidebar and phone menu, and an exported site. Support is numbered.

### 15.7 Page emoji · `S` · Model: Opus 5.5 · ✅ **shipped 2026-09-23**

The owner asked for an emoji on the Support site's section pages, so they
stand out in the tree. Built as a page setting rather than a character in
the title, so search, breadcrumbs and exported addresses stay clean, and so
anyone can use it: `Page.Emoji` (migration `PageEmoji`), set from the page
by whoever may edit it, shown above the title and in the tree, and carried
by copies, packs and exported sites. Not versioned, like the page width.

### 15.6 The Support site, rewritten for beginners · `L` · Model: Opus 5.5 · ✅ **shipped 2026-09-24**

**Rolled out 2026-09-24** after the owner approved the pilot pages: every
section rewritten to the rules below, a Features page, and Gmail and
Outlook pages under Email (SMTP). Exported to `support/support-pack.zip`
and a static site within Cloudflare's limits, with no broken links. One
thing is left: the setup-wizard pictures are whole 1024px windows and want
retaking narrow on a scratch instance, which needs the owner to create its
first account.

**The owner's review of the first version (2026-09-23)**: "very basic and
dry and feels like AI wrote it with no care for the users." The rules for
the rewrite, which also apply to anything written for users from now on:

- **Write for someone new.** Context first: what a thing is, why you would
  want it, with an example. Then how.
- **Procedures are numbered steps**, Step 1, Step 2, Step 3, each with a
  picture that has a box around the thing to click and an arrow to it.
- **Offer a script wherever a step is technical**, for Windows, macOS and
  Linux, and say exactly what to edit in it. Downloads are attachments on
  the page, and gain a GitHub link once the repository is public (14.2).
- **One picture per row**, never two side by side, and every picture has a
  shadow. Desktop and phone go in separate "On a computer" and "On a phone"
  sections, and only where the phone is different; otherwise the page shows
  the desktop.
- **A picture only where it shows something words cannot**, such as where
  a control is or what a layout looks like. Never a picture of text the
  page already says: the owner's example was a screenshot of the Storage
  targets card, a paragraph shrunk to unreadable size above the same words.
- **Pictures the right size on every device.** Close-ups are taken in a
  narrow window (480px) and shown at their own size on a desktop; on a
  phone a resized picture fills the column. A crop of a 1280px window was
  shown at about a quarter of its size on a phone.
- **Lists, not tables, for anything longer than a few words.** A two-column
  table of explanations runs off the side of a phone.
- **Every variant, live, with an example of when to use it** (the owner,
  2026-09-23: the chart page showed none of bar, column, line and pie).
  An element with kinds, styles or options shows each one on the page:
  every chart type, every panel type, each list style, table options,
  status colors, and so on.
- **Element pages show the real element**, live on the page, and explain
  when you would use it with examples. A picture only where it shows
  something moving. Every element page has the same **Insert it** section in
  the same place, giving the slash command.
- **Pilot first.** Three pages go to the owner before anything else
  changes: Trusting the local certificate (built on 15.5), Panel (an
  element page) and Creating a space (a manual page). The owner's review is
  in progress (2026-09-23): the certificate and Creating a space pages
  looked good, and the Panels animation was fixed; Opening Tesria by name
  and Templates were added at his request. The rollout waits for his
  approval.

## Phase 16: Versions, for the app, the docs and wiki packs

Asked for by the owner, 2026-09-24, to plan now and build later: "we need
to think about a version system for the app and the docs", starting at
**0.5** as the product approaches beta, living "nicely with the build
pipelines we create in GitHub as GitHub Actions", with "backwards
compatibility and migrations for imported space packs" so that "if
someone spends a lot of time making a space and then they distribute it
as a pack it doesn't break if we update Tesria".

**Where things stand (2026-09-24).** `Api.csproj` says 0.2.0 and the web
app's `package.json` says 0.0.0; nothing shows either. Wiki packs carry a
format number (`WikiPack.Format`, 1): adding an optional field leaves it
alone (the page emoji and tree style of 15.7 and 15.8 did), changing what
a field means raises it, and a reader refuses a format newer than it knows.
There is no upgrade path for an older one yet, because there has only
ever been one. The database has EF Core migrations, which run at startup.

### 16.1 One version number, set by the release · `M` · Model: Opus 5.5 · ✅ **shipped 2026-09-24**

- **Semantic versioning, starting at 0.5.0** (the owner's suggestion).
  While the major number is 0, a minor release (0.6) may change behavior
  and a patch (0.5.1) only fixes; 1.0 is the promise of stability.
- **One source of truth**: a pushed git tag `v0.5.0`. GitHub Actions reads
  it and stamps it into the server build (`-p:Version`), the web build and
  the Docker images (14.2's tags come from the same tag). A build without a
  tag, such as a local one, is `0.5.0-dev+<commit>`, so it can never be
  mistaken for a release.
- **Shown where people look**: Admin → Dashboard, the footer of Admin, the
  API (`/api/instance` and the OpenAPI document), exported sites' footers,
  and `docker compose` health output. Upgrading tells the owner what
  version they came from and went to (audit entry).
- **Release notes**: the Support site's Release notes page gains an entry
  per release, from the CHANGELOG's dated entries grouped by version.

### 16.2 Docs that say which version they describe · `M` · Model: Opus 5.5 · ✅ **shipped 2026-09-24**

- **Every Support page ends with a small version table**: *Applies to*
  (such as "Tesria 0.8 and later"), *Last updated* (a date), and a short
  *What changed* line. Written by the publisher, not by hand: each section
  records the version a page was last checked against, and the date comes
  from the page's own history.
- **"Applies to" moves only when the page's content changes for a new
  version.** A page written for 0.5 that is still true in 0.9 keeps saying
  0.5 and later. A page describing a feature added in 0.7 says 0.7 and
  later, which also tells readers of an older install that they do not
  have it yet.
- **The published site is the latest release's.** Older versions are the
  wiki pack committed at each release tag (10.5), which anyone can import
  to read the docs for the version they run. A version picker on the site
  is possible later, and not planned.

### 16.3 Packs that keep working across versions · `M` · Model: Opus 5.5 · ✅ **shipped 2026-09-24**

- **The compatibility promise**: a pack exported by any release from 0.5
  on imports into every later release. (Packs from before 0.5 are format 1
  too, and are covered by the same path.)
- **Pack migrations**, like database migrations: when a release has to
  raise `WikiPack.Format`, it ships an upgrade step from the previous
  format (`PackUpgrades`, 1 → 2, 2 → 3, …). The importer applies the steps
  in order to the pack's JSON before reading it, so the import code only
  ever reads the current format. Document content has the same problem one
  level down: a change to an editor node's shape (renamed attribute,
  split node) ships a content upgrade that the pack upgrade applies to
  every version of every page, and that the app applies to stored pages in
  a database migration.
- **The version that made a pack is recorded** (the manifest's
  `generator` already names Tesria; it gains the release number), so an
  import can say "made with Tesria 0.6" and an error can say which step
  failed.
- **Tested by fixtures, not by hope**: every release that raises the format
  commits a small pack made by the release before it
  (`tests/Api.Tests/Packs/format-N.zip`), and a test imports every fixture
  into the current build. A fixture is never regenerated, which is what
  makes it a test of the old format.
- **Newer than known stays refused**, with a message saying which Tesria
  version is needed, rather than a partial import.

**Decisions for the owner before building**: 0.5.0 as the first number
(suggested by the owner); whether the Docker `latest` tag follows every
release or only non-prerelease ones; and whether the site keeps a copy of
older versions' docs online or only in the repository.

**As built (2026-09-24, Opus 5.5, overnight at the owner's request).** The
owner chose 0.5.0 and "add the workflow, push v0.5.0". The other two
decisions are still open and neither blocks anything: the Docker tag
belongs to 14.2, and older docs live in the repository for now.
Differences from the plan above:
- **The version lives in `src/Api/Api.csproj`** (`0.5.0`, informational
  `0.5.0-dev`), and the tag stamps it: `release.yml` passes
  `TESRIA_VERSION` to the Docker build, which sets `InformationalVersion`.
  A local Docker build says `0.5.0-dev` without `+<commit>`, because the
  build context has no `.git`. The web app's `package.json` says 0.5.0 but
  nothing reads it except the Support publisher (for "Applies to").
- **Shown on** the admin dashboard (a "Tesria version" card with the
  version upgraded from), `/api/health`, `/api/instance`, the OpenAPI
  document, exported sites' footers and packs' `generator`, and the foot
  of every Administration tab, which already showed `/api/health`'s
  version and now shows the real one.
- **The release workflow builds the image but publishes none**: that is
  14.2. Its release notes are the CHANGELOG's section, cut to the
  highlights when longer than GitHub allows (0.5.0 is the whole history).
- **16.2's registry** is `scripts/support/page-versions.json`, keyed by
  "Parent / Title". A section passes `since` to move "Applies to" and
  `changed` for the "Changes" line; otherwise a change to the words
  says "Revised." The table's labels are *Applies to*, *Updated* and
  *Changes*, shorter than planned so they fit a phone. The first time a
  page is seen, "Updated" is the day
  its current words were published, from the page itself.
- **16.3's fixture** is `tests/Api.Tests/Packs/tesria-0.5.0-format-1.zip`,
  made by a 0.5.0 build from synthetic content. `PackUpgrades.All` is
  empty; the tests prove the path with a pretend format 2.

## Phase 17: Developer docs on the Support site · ✅ **shipped 2026-09-24**

Asked for by the owner, 2026-09-24: once versioning (Phase 16) is in,
the Support site gains developer documentation **below the user
sections**, fit for the official support site of a production open-source
project, exported and hosted at tesria.com like the rest (10.5). Written
to 15.6's rules: beginner-first, every step shown, pictures only where
they show something words cannot, and every page carrying 16.2's version
table.

- **REST API**: what it is for, getting a token and its scopes, making a
  first request in curl, JavaScript and Python, errors and rate limits,
  pagination, webhooks and their signatures, and a reference generated from
  the OpenAPI document so it can never drift from the code.
- **MCP**: what an assistant can do through it, connecting Claude Desktop
  and other clients step by step, the ten tools with examples, token scopes
  for assistants, and how an assistant's edits arrive as tracked changes
  for a person to accept.
- **Architecture**, with diagrams drawn in the wiki's own Mermaid element
  so they stay editable: the services and how a request flows through
  Caddy, the app, the collaboration sidecar, the PDF sidecar and the
  database; the editor's schema and real-time model; permissions; the
  backup pipeline; exports and wiki packs.
- **Contributing**: building from source, running the tests, the project's
  conventions, how a change is proposed and reviewed, and the release
  process (16.1).
- **The documents an open-source project is expected to have**, published
  here and kept in the repository: the license and third-party notices, the
  security policy and how to report a vulnerability, a code of conduct,
  governance and support, and the changelog by version.

---

**As built (2026-09-24, Opus 5.5).** A **Developers** section at the end of
the Support site (`scripts/support/sections/developers.mjs`), with REST API
and MCP moved under it (same page ids). Added: **Code examples** (one
program in JavaScript and Python, kept in `docs/examples` and run against
the instance before publishing), an **API reference** generated from
`/api/openapi.json` at publish time (every endpoint by area, with its query
and body fields; the operations carry no summaries, so each area has a
written line instead), **How Tesria is built** (the parts, a request start
to finish, editing together, permissions, exports and packs, two Mermaid
diagrams), **Contributing** (building, tests, layout and conventions,
proposing a change, making a release) and **Project documents** (code of
conduct, governance, getting help), with `CONTRIBUTING.md`,
`CODE_OF_CONDUCT.md`, `GOVERNANCE.md` and `SUPPORT.md` in the repository.
The code of conduct is written for Tesria, "inspired by the Contributor
Covenant", with reports to the security address (the owner's choice).
Found on the way: the README's "Local development (without Docker)" did
not work (the database publishes no port, and the fallback connection
string still said "confluence"); it now describes the Docker loop the
project actually uses. Running the API outside Docker is not documented.

## Phase 18: Email through the providers people already have

Asked for by the owner, 2026-09-24, after the Support site's Gmail and
Outlook pages showed the gap: Tesria signs in to a mail server with a
username and password only, so a personal Outlook.com account cannot send
its email at all (Microsoft has required an OAuth sign-in there since
September 2024), Microsoft 365 turns password sign-in for sending off by
default at the end of December 2026, and Gmail works only through an app
password. Most small teams have no mail server of their own; the email they
have is one of these.

**No new container.** MailKit, already in the app, speaks the SASL
`XOAUTH2` mechanism both providers use, so this is code in the app and a
settings screen.

**What each popular provider allows, checked 2026-09-24** (recheck each when
its item is built):

- **Microsoft** (Outlook.com, Hotmail, Live, Microsoft 365): OAuth, scope
  `https://outlook.office.com/SMTP.Send` plus `offline_access`, through
  `smtp.office365.com:587`. Microsoft 365 still needs Authenticated SMTP
  allowed for the mailbox by its administrator, OAuth or not.
- **Google** (Gmail, Google Workspace): OAuth, scope `https://mail.google.com/`
  (the only one that allows SMTP), through `smtp.gmail.com:587`. App
  passwords keep working and stay the simple option.
- **Apple iCloud Mail, Fastmail, Zoho Mail**: app passwords
  (app-specific passwords at Apple). Work today; documented 2026-09-24.
  Fastmail needs a plan above Basic, and Zoho a paid plan for new accounts.
- **Proton Mail**: an SMTP token, on paid plans and only for an address on
  your own domain (`smtp.protonmail.ch:587`). Works today; documented
  2026-09-24.
- **Sending services** (Amazon SES, Postmark, Mailgun, SendGrid, Brevo,
  Resend, SMTP2GO): SMTP credentials they generate. Work today; the right
  answer for a larger team, and free or cheap at a small team's volume.

### 18.1 Provider presets in the email settings · `S` · Model: Opus 5.5 · ✅ **shipped 2026-09-24**

A **Provider** choice at the top of Settings → Email and the setup wizard's
Email step: Gmail, Outlook or Microsoft 365, iCloud Mail, Zoho Mail (with its region), Fastmail, Proton Mail, Amazon SES (with its region),
Postmark, Mailgun, SendGrid, Brevo, or Other. Choosing one fills the host,
port and encryption, says in one line what to use as the password ("an app
password, not your Gmail password"), and links to that provider's Support
page. The fields stay editable, and Other is today's form. The table of
presets lives in one place (server side, so the wizard and settings agree)
and is tested against the Support pages' values.

### 18.2 Sign in with Microsoft · `M` · Model: Opus 5.5 · ✅ **shipped 2026-09-24**

For Outlook.com and Microsoft 365: a **Sign in with Microsoft** button in
the email settings, in place of the username and password when the
Microsoft preset is chosen.

- **The administrator registers Tesria with Microsoft once** (Microsoft
  Entra, free, about ten minutes; the Support page walks through it with
  pictures): an app registration for "personal and work accounts", a
  redirect address of `https://your-server/api/admin/mail/oauth/callback`,
  the SMTP.Send permission, and a client secret. There cannot be one shared
  Tesria registration, because every instance has its own address. The
  client ID and secret go in the settings, the secret encrypted like the
  SMTP password is.
- **The sign-in**: authorization code flow with PKCE and a `state` bound to
  the session; the refresh token is stored encrypted (Data Protection, as
  the SMTP password is) and never shown. Each send uses a fresh access
  token, refreshed as needed. The signed-in mailbox's address becomes the
  From address, since Microsoft refuses any other.
- **When it stops working** (the user revoked it, the password changed, the
  secret expired, which Entra secrets do within two years): the send fails
  with Microsoft's reason, administrators get a security-style alert, and
  the settings say "Reconnect". An expiring client secret is warned about
  ahead of its date when Microsoft reports it.
- **Security decisions to name for the owner** (a stored refresh token can
  send mail as that mailbox until revoked): connecting needs a recent
  password (sudo), like other credential changes; connect, disconnect and
  every refresh failure are audited; the token never leaves the server,
  and a pack or site export never contains it. Fable review offered, since
  this is a credential store.
- **Tests**: the token exchange and refresh against a fake token endpoint;
  the XOAUTH2 send against a fake SMTP server; revocation raising the alert.

### 18.3 Sign in with Google · `M` · Model: Opus 5.5 · ✅ **shipped 2026-09-24**

The same flow and storage as 18.2, for Gmail and Google Workspace.

- **The administrator makes a Google Cloud project once**, with an OAuth
  client and a consent screen. Two things the Support page must get right,
  from Google's own pages: an **External** app left in **Testing** has its
  refresh tokens expire after seven days, so it is published **In
  production**. It needs no Google review: Google exempts OAuth plugins
  "such as SMTP for WordPress", and the only user is the administrator,
  who sees "Google hasn't verified this app" once and continues. A Workspace
  organization can choose **Internal** instead, which shows no warning.
- **Redirect addresses**: Google accepts only addresses on public domains,
  not raw IP addresses (to confirm when building: `.local` names). For an
  instance on a LAN name, the fallback is a paste-back: a desktop-type
  client redirects to `http://127.0.0.1`, the page fails to load, and the
  administrator pastes its address into Tesria, which reads the code from
  it. Design it once in 18.2 so either provider can use it.
- Google also keeps at most 100 refresh tokens per account per client,
  silently dropping the oldest: reconnecting replaces the stored token
  rather than adding one.

### 18.4 Support pages for every provider · `S` · Model: Opus 5.5 · ✅ **shipped 2026-09-24**

Under Email (SMTP). **Written 2026-09-24**, each checked that day against
the provider's own help pages: Sending with Gmail, Sending with Outlook or
Microsoft 365, **Apple iCloud Mail**, **Zoho Mail**, **Fastmail** and
**Proton Mail**, then **Sending services** (Amazon SES, Postmark, Mailgun,
SendGrid, Brevo, Resend, SMTP2GO: what each is, its free tier as
documented, where its SMTP credentials are, what it verifies first). The
Gmail and Outlook pages walk through signing in (the Google Cloud project,
the Microsoft Entra registration), with the app password kept as Gmail's
quick way. Every settings table on these pages is built from 18.1's
presets, fetched from the app when the pages are written.

**As built, where it differs from the design above:**
- A personal Microsoft account sends through `smtp-mail.outlook.com`, not
  Microsoft 365's `smtp.office365.com`; Tesria picks by the tenant in the ID
  token (the research for 18.4 found it).
- The paste-back is Google's only, as designed: Microsoft accepts any https
  address, so it always returns to the callback. Google's loopback address
  is `http://127.0.0.1`; its current docs make the client secret optional
  for desktop clients, and Tesria sends it anyway.
- An expiring Entra secret is not warned about ahead of time (Microsoft
  does not report it); the refusal when it has expired is explained in
  words, and a new secret for the same client keeps the sign-in.
- The setup wizard's Email step gains the presets, the encryption and the
  password; signing in with a provider stays in Administration, since the
  wizard has no sudo prompt.

**Not planned**: Yahoo and AOL Mail, at all (the owner, 2026-09-24; their
OAuth is closed to new applications anyway), and a Tesria-run relay that would let instances skip registering their own app
(it would put every instance's mail through a service the project runs,
which is exactly what a self-hosted wiki is for avoiding).

## Phase 19: Reaching Tesria from anywhere with Tailscale

Asked for by the owner, 2026-09-24: people who already run Tailscale should
be able to reach their Tesria from anywhere, without opening it to the
internet.

**Which Tailscale feature, checked against Tailscale's docs that day.** The
owner asked for an "app connector". In Tailscale's terms an app connector
routes tailnet traffic to applications by domain name through a dedicated
Linux device ("App connectors let you route your self-hosted applications and
software as a service (SaaS) applications through dedicated devices in your
Tailscale network", tailscale.com/kb/1281/app-connectors). That works for
Tesria today, with no change to Tesria, for anyone who already runs one: they
add Tesria's LAN name to it. What Tesria can ship itself is the other
documented pattern, a **Tailscale container beside the app with Tailscale
Serve** (tailscale.com/kb/1282/docker, docs/features/tailscale-serve): Tesria
joins the tailnet as its own device, reachable at
`https://tesria.<tailnet>.ts.net` with a certificate Tailscale provisions, so
tailnet devices need no "trust this device" step at all. Both get a Support
page; the sidecar is what gets built.

### 19.1 A Tailscale sidecar, off unless asked for · `M` · Model: Opus 5.5 · ✅ **shipped 2026-09-24**

**As built:** Serve forwards to Caddy (`https+insecure://caddy:443`), so
`/collab` and every route work as on the LAN; verified live on the owner's
tailnet (valid certificate, sign-in, the API and the collab WebSocket). The
status is the sidecar's health check writing `tailscale status --json
--peers=false` to a shared volume every 30 seconds; the app reads the file
only (no control socket) for a Tailscale card in Settings that shows the
address and warns about the device's key expiry until it is turned off.
The card and the Support page carry Tailscale's own logo, unmodified, from
its media kit, with a trademark line.


- A `tailscale` service in `docker-compose.yml` under a Compose **profile**,
  so nobody runs it without choosing to: the official Tailscale image,
  `TS_AUTHKEY` from `.env` (an auth key, or an OAuth client secret with its
  tag), `TS_HOSTNAME` (default `tesria`), `TS_STATE_DIR` on its own volume so
  the device keeps its identity across restarts, and `TS_SERVE_CONFIG`
  pointing at a committed JSON that serves HTTPS on 443 to the stack.
- **Where Serve forwards to**, to decide when building: to Caddy, so `/collab`
  and everything else is routed exactly as on the LAN, or to the app with
  the collab sidecar reached separately. Caddy is the likely answer; check
  that its redirects and headers behave with a second hostname in front.
- **Serve only, never Funnel.** Funnel publishes to the whole internet; the
  point here is the opposite. The committed config has no Funnel entry, and
  the Support page says why.
- **Tesria with two addresses.** Links in email use one base address, so the
  settings need a clear answer for "reachable on the LAN and on the
  tailnet": probably keep the LAN or public address as the base and accept
  the ts.net name as another origin. Check cookies (per host), the CSRF and
  origin checks, the collab WebSocket and the OAuth callbacks of Phase 18
  against the ts.net name.
- **Health and status**: the Health checks page and `/api/health` mention
  whether the sidecar is connected, if that can be read without giving the
  app access to Tailscale's socket (to decide; leaving it out is acceptable).
- **Security decisions to name for the owner**: the auth key in `.env` can
  add a device to the tailnet until it expires or is revoked (recommend a
  tagged, non-reusable key or an OAuth client, and say how to revoke); who
  on the tailnet may reach Tesria is Tailscale's access policy, not Tesria's,
  and Tesria's own sign-in still applies to everyone.
- **Tests**: the compose file validates with and without the profile; a
  live check on the owner's tailnet, since it cannot be faked honestly.

### 19.2 Support pages · `S` · Model: Opus 5.5 · ✅ **shipped 2026-09-24**

Under Installation and operations, next to Opening Tesria by name:
**Reaching Tesria from anywhere with Tailscale**, written for someone new
(what a tailnet is, why it beats opening a port, what it costs: free for
personal use at the time of writing, to recheck), with numbered steps: make
an auth key, add it to `.env`, start the profile, enable HTTPS in the
tailnet, open the ts.net address. A second section for people who already
run an **app connector or subnet router**: add Tesria's address to it, and
what changes (nothing in Tesria). Pictures only of Tesria; Tailscale's own
screens are described in words, since they change.

## Phase 20: Progress you can see

### 20.1 A progress bar for exporting a site or a pack · `S` · Model: Opus 5.5 · ✅ **shipped 2026-09-24**

Asked for by the owner, 2026-09-24. **Export as site** captures every page
through the PDF sidecar's browser and **Export as pack** gathers every page,
version and attachment; on a large space either takes minutes, and today the
button just waits. Show how far along it is: pages done out of the total,
the page being worked on, and time so far, with the download starting when
it is ready. Likely shape: the export runs as a job with an id, the page
polls its progress (or reads a server-sent stream), and a finished job keeps
its file for a short while so a closed tab can come back for it. Decide
when building whether both exports share one job mechanism (the backups'
job table may already fit), and what canceling does.

**As built (2026-09-24).** No job table: the export still runs in its own
request, and the page polls a progress side channel (`?progress=<id>`,
`GET /api/export-progress/{id}`, in memory per exporter). Canceling aborts
the request, which stops the work. The one thing given up is coming back
to a closed tab for the file; running exports outside a request to allow it
would move them away from the permissions and render token they run with.

## Order of execution, flattened

1. **0.1** Roles (Fable→Opus) → **0.2** Settings → **0.3** Telemetry → **0.4** Media storage
2. **1.1** Profile → **1.2** Avatars → **1.3** Recovery codes + admin reset → **1.4** Registration control → **1.5** Author identity
3. **2.1** Admin shell → **2.2** Users → **2.3** Settings UI → **2.4** Spaces → **2.5** Dashboard
4. **3.0** Proxy trust & headers → **3.1** DB role + audit chain (Fable) → **3.2** Rate limiting → **3.3** Detection & alerts (Fable→Opus) → **3.4** Egress/input → **3.5** Sessions & 2FA → **3.6** Dependencies → **3.7** Review & gate (Fable)
5. **4.1** SMTP → **4.2** Email recovery → **4.3** Alert & notification email
6. **5.1** Anonymous permission model + leak matrix (Fable) → **5.2** Server → **5.3** SPA → **5.4** Operator controls
7. **6** Space icons
8. **7.A** → **7.B** → **7.C** → **7.D** (Fable→Opus) → **7.E** → **7.F**
9. **8.1** PDF (after 7.A) → **8.2** License (any time) → **8.3** OpenAPI → **8.4** MCP (Fable→Opus) → **8.6** External edits as tracked changes (Fable→Opus) → **8.5** Wiki packs (Fable→Opus)
10. **9.1** Backups admin section (Fable→Opus; shipped 2026-09-17) → **9.2** Offsite backups (Fable→Opus; shipped 2026-09-22) → **9.3** Space charts (shipped 2026-09-22) → **9.4** Restore from the admin page (Fable designed and the owner answered its five decisions 2026-09-22; Opus shipped it the same day)
11. **10.1** Owner role (shipped 2026-09-20) → **11.1** Instance rights and the Roles tab (shipped 2026-09-20) → **11.2** Custom roles (shipped 2026-09-20) → **11.3** Delete a space (shipped 2026-09-20) → **5.5** Anonymous access is opt-in twice (shipped 2026-09-20) → **10.4** Media harness (shipped 2026-09-20) → **10.2** Owner setup wizard (shipped 2026-09-20) → **10.3** Tour and tips (shipped 2026-09-20) (all specified 2026-09-20 as Fable; Opus implements). Phase 11 goes before the wizard because the wizard has a required step that reviews the matrix, and before 10.3 because the tour's screens should show the real Roles tab. 10.4 before 10.2 because the wizard's Done screen and the tour embed its output.
12. **12.1** Capture-based export and the element audit (shipped 2026-09-20) → **12.2** Publish a space as a static site (shipped 2026-09-20). 12 before 8.5 because the site export builds the walk over a space that the wiki pack will reuse, and because the owner's documentation is waiting on it.
13. **8.6** External edits as tracked changes (steps 1–5 shipped 2026-09-21; step 6 folded into 10.5).
13a. **8.5** Wiki packs (designed 2026-09-21 as Fable; Opus implements next). Before 10.5, because the pack is what a rebuilt manual is committed as.
13c. **12.3** Turn a space's exports off, format by format (Opus 5.5, shipped 2026-09-22).
13d. Three small follow-ups (Opus 5.5, shipped 2026-09-22): the dashboard's two top-ten tables as cards, **Test connection** back on Storage targets (9.2 step 5), and `OFFSITE_CLOUD_BUDGET_GB` (9.3 step 3).
13b. **13.1** Instance branding (designed and shipped 2026-09-22 by Opus 5.5, the first item under the new model gate). Before 10.5 at the owner's request, and the manual's screenshots are then taken on an unbranded instance.
14. **10.5** The Support site and the Demo space (rescoped from "rebuild the user manual" by the owner, 2026-09-22), **after 8.5**, now that the owner has settled the wiki as its source of truth (2026-09-21). A manual whose only copy is inside the instance is how the last one was lost, so the pack that can export it is a prerequisite, not a preference.

15. **15.1** Access → **15.2** Editor → **15.3** Pages and collaboration → **15.4** Confirmations (asked for 2026-09-23). Before 10.5's step 7, so the Support site documents them and its pictures show them.
15a. **15.5** Trust this device → **15.6** the Support rewrite, pilot pages first (asked for 2026-09-23). Before 14, because the Support site is what goes public with the images.
16. **14.1** Pre-release audit → **14.2** Images on Docker Hub (asked for 2026-09-23). After 10.5, so the Support site and the images go public together.
17. **16.1** One version number → **16.2** Versioned docs → **16.3** Pack migrations (asked for 2026-09-24; all three shipped 2026-09-24 with the 0.5.0 release). 16.1 alongside 14.2, since both are the same GitHub Actions release pipeline; 16.2 before the Support site is published at tesria.com; 16.3 before the first release that changes the pack format.
18. **17** Developer docs on the Support site (asked for 2026-09-24; shipped 2026-09-24), after Phase 16 so every page carries its version table from the start.
19. **18.1** Provider presets and **18.4** the app-password and sending-service pages (asked for 2026-09-24): small and independent, so any time; best before 14, since they are what a new owner meets in the setup wizard. Then **18.2** Sign in with Microsoft → **18.3** Sign in with Google, before 14 if the owner wants the public release to work with a personal Outlook.com account.
20. **19.1** Tailscale sidecar → **19.2** its Support pages (asked for 2026-09-24). Independent of everything else; best after 14.2, so the compose file it extends is the published one.
21. **20.1** Export progress bars (asked for 2026-09-24; shipped 2026-09-24).

Phases 6 and 8.2 are floaters (small, no dependents) and can fill gaps.
3.6 (dependency fixes) can also be pulled forward at any time; the npm
findings don't get better by waiting.

## Things this plan deliberately does not decide

- Whether admins bypass restrictions (0.1): recommended yes; a Fable call.
- Whether to log search queries (2.5): a privacy trade-off.
- Whether public pages expose version history (5.1): recommended no.
- Blog posts as a content type (Phase 7 table): a product decision.
- Public repo visibility (8.2).
- Immediate vs. digest for email notifications (4.3).
- Everything under 9.2's "Open decisions": provider, where credentials live, lock mode, passphrases, repo2 schedule, restic vs. tarball, NAS as a target type.
- Whether the backup schedule joins the retention policy in the admin UI (9.1 keeps it in `.env`).
- Whether the welcome tour should also run for accounts that existed before 10.3 (decided no for now: tips only; the tour is on their profile).
- Whether invites should be able to carry a role, so an invited person arrives as an administrator (10.1 leaves promotion to the owner afterwards).
- MP4 alongside WebM for the clips (needs ffmpeg in an image; the poster is the fallback until someone asks).
- Phase 11's three: invites naming a role, groups carrying instance rights (recommended no), and anonymous rights beyond export.
- 8.5's four: matching pack authors to accounts by email, carrying restrictions by principal name for confirmation, importing into an existing space as a merge, and signed or encrypted packs.
- 13.1's five: per-space branding, custom fonts, arbitrary custom CSS (never recommended), a logo in HTML email, and branding the PDF.
- 9.4's three: restoring one space or page out of a backup, restoring a dump from a newer schema, and more than one `app` container.
