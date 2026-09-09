# Architecture

This document records how Tesria is put together and why. It is kept
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

### Proxy trust and transport security (`Infrastructure/Security`, dev-plan 3.0)

The app never sees the client: Caddy terminates TLS and proxies to Kestrel
over plain HTTP on the compose network. Two consequences the rest of Phase 3
depends on being fixed:

* **Client address.** `UseForwardedHeaders` runs first in the pipeline and
  believes `X-Forwarded-For` / `X-Forwarded-Proto` only from
  `Proxy:TrustedNetworks` (`ProxyTrust.cs`; default loopback + RFC 1918 +
  ULA). Trust is by *network*, not by Caddy's container IP, because that IP
  changes on every `compose up`. `ForwardLimit = 1`: only the nearest hop's
  entry — the one Caddy appended — is used, so a client cannot choose its
  own address by sending the header itself. Everything downstream
  (`RemoteIpAddress`, the audit log's `Ip`, 3.2's rate limiter) sees the
  real client. The tests set the connection address with a startup filter
  (`TestRemoteIpStartupFilter`) so they can act as the proxy or as a stranger.
* **Cookie `Secure`.** In Production the session cookie is `Secure` always,
  not "same as request" (which, as the app saw it, was never HTTPS).
  `Security:AllowInsecureCookies=true` is the documented escape hatch for a
  deliberately HTTP-only install.

**Security headers** are set by `SecurityHeadersMiddleware` on every
response — in the app, not Caddy, so they hold whichever proxy is in front
and the tests can assert them: `nosniff`, `X-Frame-Options: DENY`,
`Referrer-Policy`, `Permissions-Policy`, COOP/CORP `same-origin`, and a CSP
with `frame-ancestors 'none'`, `object-src 'none'`, `base-uri 'self'`.

The CSP's `script-src` is `'self'` plus a **hash** of the inline theme
script in `index.html`, computed at startup from the `wwwroot/index.html`
this process serves — so a rebuild that changes the script changes the hash
with it, and `'unsafe-inline'` is never needed for scripts. `style-src`
does allow `'unsafe-inline'`: the editor writes inline `style` attributes
(cell colours, alignment) and React sets them directly; blocking inline
styles would break content, and style injection is a far smaller hazard
than script injection. `connect-src` names the collaboration websocket
origin explicitly per request (`wss://<host>`) rather than relying on every
browser reading `'self'` as covering `wss:`. `Security:CspReportOnly=true`
switches the header to report-only for troubleshooting.

**HSTS** is the one header deliberately left to Caddy, and only in
`deploy/Caddyfile.public`: it is a one-way door that would lock a LAN user
out of clicking past the internal CA's certificate warning. See
`tls-and-lan-access.md`, Path 3.

### Database roles and the audit hash chain (`Infrastructure/Security/DatabaseRoles.cs`, `Infrastructure/Audit/AuditChain.cs`, dev-plan 3.1)

Three layers, meant to be read together: the role split makes tampering
with the audit log **hard**, the hash chain makes it **detectable**, and
point-in-time recovery (already shipped) makes it **recoverable**.

**Two connections.** `ConnectionStrings:Default` is the owner (the
`POSTGRES_USER` superuser). It is used once, at startup, unpooled: apply
migrations, backfill the chain, provision the runtime role. Then it is
gone. `ConnectionStrings:App` is `tesria_app` (`APP_DB_PASSWORD`), which
the running app and the collab sidecar use for everything else. It has
`SELECT/INSERT/UPDATE/DELETE` on every table and sequence **except**
`UPDATE/DELETE/TRUNCATE` on the append-only tables
(`DatabaseRoles.AppendOnlyTables`: `AuditLogs`, `PageViews`; 3.3 adds
`SecurityEvents`). Verified live: `UPDATE "AuditLogs"` as `tesria_app` →
`permission denied`.

The app provisions the role itself, on every start, rather than a database
init script: init scripts run only on a fresh volume, which would have left
every existing install on the superuser, and re-running the grants after
`Migrate()` means tables added by later migrations are covered without
anyone remembering to. Rotation is "change `APP_DB_PASSWORD`, restart app
and collab". An empty `APP_DB_PASSWORD` falls back to the owner connection
with a startup warning — a half-configured split must not brick an install
that worked yesterday. Postgres referential actions (cascades, `SET NULL`)
run as the table owner, so the role's lack of `DELETE` on `PageViews` does
not stop a page purge.

**The chain.** Every `AuditLog` row carries `Sequence` (contiguous from 1),
`PrevHash` and `Hash = SHA-256(PrevHash ‖ canonical row)`. Linking happens
in `AppDbContext.SaveChanges[Async]` — the one place every write passes
through, so no code path can add an unchained row — under a
transaction-scoped Postgres advisory lock so concurrent appenders serialise
on the tail. A unique index on `Sequence` makes any race that got past the
lock fail rather than fork.

Two round-trip hazards shaped the canonical form. `MetadataJson` is `jsonb`,
and Postgres re-orders keys, strips whitespace and normalises numbers on
the way in — so the hash is over a canonical form (keys sorted, compact,
numbers via `decimal`) computed identically at write and at verify.
`CreatedAt` is truncated to milliseconds before hashing because Postgres
keeps microseconds and .NET keeps 100 ns ticks. Both were confirmed by
recomputing every stored hash independently in Python from a `psql` dump.

Rows written before the chain existed are linked at startup by
`AuditChain.BackfillAsync`, in `CreatedAt, Id` order, on the owner
connection (the only one allowed the `UPDATE`).

**Verification** (`AuditChainVerifier`) walks the chain in sequence order
and reports the first link that fails: a gap (row deleted), a `PrevHash`
mismatch, or a hash mismatch (row altered). It runs on demand from
`POST /api/admin/audit/verify` (audited), from
`scripts/verify-audit-chain.sh` for cron, and daily in-process
(`AuditChainMonitor`), which also remembers the last verified length so a
chain that got *shorter* — the one thing a chain cannot detect on its own —
is reported too. What verification cannot do is outlive a compromise of the
app binary: an attacker who controls the app can make the endpoint lie.

That is why every chained row is **also written to stdout** as it commits,
as one JSON line under the `Tesria.Audit` log category. `docker compose
logs app`, and anything forwarding it, holds a copy that never touched the
database and that the database password cannot reach. "An attacker can't
scrub logs" is really "logs exist in more than one place".

### Brute-force protection (`Infrastructure/Security/RateLimits.cs`, dev-plan 3.2)

Two mechanisms with different targets. The **address limiters** bound one
attacker; the **account lockout** bounds one target, so a guess spread
across many addresses against a single account still runs out of road.

Limiters use the framework's `RateLimiter` middleware, placed after
authentication (so it can tell a session from a stranger) and before
authorization. Three policies, each keyed on the client address 3.0 made
real: `auth` (sliding window per address; sign-in, registration and both
recovery endpoints share it), `token-mint` (per account, hourly), and a
global limiter for **anonymous** callers only — signed-in users are not
globally limited because their identity is the accountability, and this
global limiter is what Phase 5's public-read mode relies on. Rejections
are 429 with `Retry-After` and a small JSON body.

Every limit is a `SiteSettings` field, editable from Admin → Security. The
partitioner cannot await, so it reads the last-loaded settings through
`SiteSettingsCache.Peek()`; `UpdateAsync` now *sets* the cache instead of
invalidating it, and startup warms it, so a change applies to the next
request. The limit value is part of the partition key, so a change starts
fresh windows immediately rather than waiting for old ones to idle out.

The lockout lives on the user row (`FailedLoginCount`, `LockedUntil`) —
persisted so a restart does not hand an attacker a fresh budget, and so
administrators can see it. After `LockoutThreshold` consecutive failures
the account is locked for `LockoutBaseSeconds`, doubling per further
failure up to `LockoutMaxSeconds`. **Never permanent**: a permanent lock
would let anyone lock anyone out by trying their address. A locked account
is refused even with the right password, with the same empty 401 as a
wrong one, so the lock cannot be used to confirm a guess; and the right
password does not reset the counter while locked, or an attacker who found
it would clear their own lock. A successful sign-in, a completed recovery,
or an admin unlock resets it.

### Threat detection and admin alerting (spec — dev-plan 3.3, designed 2026-09-09)

**What it is for.** The limiters in 3.2 stop an attack from succeeding
cheaply; this tells an administrator that one is happening and gives them
something to do about it in one click. The requirement, verbatim: "if an
attack is suspected an email / notification goes to the admin group and it
gives them mitigation options." Email arrives in 4.3; notifications are
in-app now, through the existing bell.

**Three tables, one of them append-only.**

* `SecurityEvents` — what a detector saw. *Append-only*: the runtime
  role cannot update or delete it (added to `DatabaseRoles.AppendOnlyTables`),
  so the record of an attack cannot be tidied away by the app. Columns:
  `Kind` (dotted, e.g. `login.credential_stuffing`), `Severity`
  (Info/Warning/Critical), `Key` (what the detector counted on — an address,
  an actor id), `Ip`, `ActorId`, `TargetType`/`TargetId`, `MetadataJson`,
  `CreatedAt`.
* `SecurityAlerts` — the workflow object an administrator acts on, one per
  event that crossed a threshold. Mutable: `Status` (Open → Acknowledged →
  Resolved), `Note`, who and when. Split from the event precisely so that
  acknowledging does not require an UPDATE on the append-only table.
* `BlockedNetworks` — the address blocklist: `Cidr`, `Reason`, `ExpiresAt?`,
  who added it.

**Detectors.** Two shapes. *Discrete* signals write an event (and usually
an alert) every time: an admin promoted, `AllowPublicSpaces` toggled, a
webhook pointed at a private address, the audit chain found broken, an
admin signing in from an address never seen for that account, an account
locked. *Burst* signals count in a sliding window and write **one** event
when the threshold is crossed, with the count in the metadata — the audit
log already has every individual failure, and a thousand rows saying
"still happening" would bury the one that matters. Windows and thresholds
live in `SecurityThresholds` and are deliberately constants, not settings:
tuning them is a decision for someone reading the code, not a field to
mis-set under pressure.

| Kind | Counted on | Fires at | Severity |
|---|---|---|---|
| `login.failed_burst_ip` | address | 20 failures / 10 min | Warning |
| `login.credential_stuffing` | address | failures against 5 distinct accounts / 10 min | Critical |
| `account.locked` | account | every lockout (event only) | Info |
| `account.repeated_lockouts` | account | 3 lockouts / 1 h | Warning |
| `login.admin_new_address` | account | admin sign-in from an address absent from that account's login history | Warning |
| `http.denied_spike` | address | 100 × 401/403 / 5 min | Warning |
| `content.mass_removal` | actor | 10 pages trashed or purged / 10 min | Warning |
| `token.minting_burst` | actor | 5 tokens / 10 min | Warning |
| `registration.burst` | instance | 10 registrations / 10 min | Warning |
| `admin.promoted` | — | always | Warning |
| `settings.public_spaces_toggled` | — | always | Critical |
| `webhook.private_target` | actor | always (3.4 also blocks it) | Warning |
| `audit.chain_broken` | — | always | Critical |

Counters are in-process (`SecurityCounters`, a singleton of timestamp
queues per `(kind, key)`, pruned on use and capped in size). A restart
forgets them, which is acceptable: the events already written are not
lost, and an attack that survives a restart will cross the threshold
again. The same class holds the **cooldown**: after an alert for a
`(kind, key)`, further crossings within one hour are suppressed, so an
ongoing attack produces one alert an hour, not one a second.

**The alert lifecycle.** A crossing writes the event, the alert (Open),
and one `Notification` per administrator (`targetType = "security"`,
pointing at the alert; the bell links it to Admin → Security). An
administrator *acknowledges* ("seen, looking") or *resolves* ("done"),
optionally with a note; both are audited. Nothing auto-resolves — a
detector cannot know the attacker gave up.

**Mitigations** — each one click on the Security page, each already
audited by the endpoint it calls: block the address or a CIDR (with an
optional expiry), suspend the user, sign the user out everywhere (rotate
stamp), revoke the user's tokens, **disable all public spaces** (the 0.2
kill switch), close registration, require TOTP for administrators (enforced
in 3.5). An alert row offers the ones relevant to its key.

**The blocklist middleware** runs immediately after forwarded headers and
before authentication: a blocked address gets 403 and no further work,
cookie or not. The list is cached in-process (`BlocklistCache`), reloaded
when it changes and every minute regardless; expired entries are ignored
on match and purged on reload. Blocked hits are counted, not written as
events — a blocked scanner retrying is exactly the flood events exist to
avoid.

**What this does not do.** It does not detect a slow attacker who stays
under every threshold; the limiters make that attacker slow enough that
the audit log is the right tool. It does not correlate across kinds. It
does not phone home. And it cannot notify anyone if the app itself is
down — that is a monitoring concern, outside the app.

### Egress and input hardening (`Infrastructure/Security/EgressGuard.cs`, `CsrfHeaderMiddleware.cs`, `Infrastructure/Storage/ContentTypes.cs`, dev-plan 3.4)

**Outbound requests (SSRF).** Every HTTP request the server makes on a
user's behalf — webhooks now, link previews later — goes through
`EgressGuard`. The attack is an editor pointing a webhook at
`http://169.254.169.254/` or `http://db:5432/`, which the server can reach
and the editor cannot. The defence holds at two moments, because a
hostname can resolve publicly when saved and privately when delivered
(DNS rebinding): `ValidateAsync` checks the URL (http/https only, no
credentials, no `localhost`/`.local`/`.internal`) and *every* address it
currently resolves to; `CreateHandler` builds a `SocketsHttpHandler` whose
`ConnectCallback` resolves again and checks the exact address about to be
dialled. Automatic redirects are off; `SendAsync` follows at most three by
hand, validating each hop. Five-second timeout. `Egress:AllowedNetworks`
lets an operator open a private range deliberately (a LAN automation
server); the 3.3 detector still records the attempt. A refused delivery is
logged and not retried — it is not transient.

**Attachments.** The declared content type is a suggestion. `ContentTypes.
Resolve` lets the bytes win where a signature is recognised (PNG, JPEG,
GIF, WebP, PDF), otherwise keeps the declared type unless it is something
a browser might *execute* — HTML, XHTML, SVG, XML, scripts — or the bytes
look like markup, in which case the file is stored and served as
`application/octet-stream`. Downloads already carried
`Content-Disposition: attachment` and, since 3.0, `nosniff`; this closes
the remaining gap, where a same-origin HTML or SVG attachment opened
directly would run with the site's cookies. Avatars were already safe:
they are re-encoded through SkiaSharp.

**CSRF.** Three layers, each sufficient on its own for the JSON endpoints:
the API only accepts JSON bodies (a cross-site form cannot send one); the
session cookie is `SameSite=Lax` (a cross-site POST does not carry it);
and `CsrfHeaderMiddleware` requires `X-Requested-With: Tesria` on every
state-changing `/api` request authenticated by the cookie. The header is
the layer that also covers the two multipart upload endpoints, which a form
could otherwise target if SameSite were ever weakened. A browser will not
add a custom header cross-origin without a CORS preflight, and no
cross-origin caller passes ours, so the header can only have come from our
own page. Bearer-token callers have no cookie and are exempt; sign-in has
no session yet and is exempt. The SPA's `request()` and its two raw
`fetch` calls send it. Chosen over a double-submit token because it needs
no token plumbing. The framework's own anti-forgery metadata stays
disabled on the `IFormFile` endpoints — that scheme (form tokens) is not
the one in use.

**Body size.** Kestrel's `MaxRequestBodySize` is set to 100 MB to match
Caddy, so the limit does not silently depend on which proxy is in front.
Attachments are capped at 25 MB by the endpoint.

### Sessions, two-factor sign-in and sudo mode (`Infrastructure/Auth/TotpService.cs`, `PasswordHasher.cs`, dev-plan 3.5)

**Sessions.** Each sign-in creates a `UserSession` row and the cookie
carries its id (`tesria:session`). `OnValidatePrincipal` now checks three
things on every request: the security stamp (revokes *all* of an account's
cookies), the session row (revokes *one*), and the `auth_time` claim
against `Auth:SessionAbsoluteDays` (90) — however active, a session ends
then; sliding expiry alone (`ExpireTimeSpan`, now 14 days idle) would let a
cookie live forever. Sign-out revokes the row, so a copy of the cookie
taken earlier dies with it. Profile → Sessions lists every browser with
address and last activity, with per-session and "all others" revoke; an
admin's revoke-sessions marks the rows too. Cookies that predate sessions
carry no claim and are rejected, which signed everyone in once — the safe
direction, as with the stamp.

**Two-factor (TOTP).** RFC 6238 with the parameters every authenticator
supports: 20-byte secret, SHA-1, 30-second steps, six digits. Secrets rest
under Data Protection (keys in the database, so a backup restores them
and a dump alone does not read them). Enrolment is scan → type a code →
on; the pending secret is not live until a code proves the device has it.
Enabling rotates the security stamp so every *other* session must pass
the new factor; the enrolling one is re-issued in place. Disabling needs
the password or a code — never just a live session.

Sign-in becomes two requests: `/login` verifies the password and, for an
enrolled account, returns `{ requiresTotp, challenge }` instead of a
cookie — the challenge is a Data-Protection-signed token (5 minutes,
bound to the client address). `/login/totp` takes it with a code, or a
**recovery code** in the code's place (1.3's set, spent on use: one set
of backup codes, not two). Wrong codes count toward the 3.2 lockout,
because six digits is a small space. A code's time step is stored on
acceptance and anything at or before it is refused, so a code seen over a
shoulder is worthless once typed.

`RequireTotpForAdmins` is enforced in `AdminRequirementHandler`: an
un-enrolled administrator gets 403 on every admin route at once, `me`
reports `totpRequired`, the admin shell says why and links to the
profile; the enrolment endpoints live under `/auth/me`, outside the
policy, so the way out is always open. Such an admin cannot turn TOTP
back off while the rule stands.

**Sudo mode.** Destructive administration — changing who is an admin,
flipping the public-spaces switch, purging a page, removing a block —
calls `AuthEndpoints.RequireSudo`, which passes only if the session
authenticated within `Auth:SudoMinutes` (5; shorter than the fresh-login
window on purpose). Otherwise the endpoint returns 403 with
`code: reauth_required`. The SPA's `request()` recognises that code, opens
the re-authentication dialog (password, or a code for enrolled accounts),
calls `/auth/reauth` — which re-issues the cookie with a fresh `auth_time`
on the *same* session — and retries the original request once. Several
requests failing together share one prompt. A wrong answer counts as a
failed sign-in.

**Argon2id, pinned.** 64 MiB, 3 passes, 4 lanes, 32-byte output, written
down in `Argon2PasswordHasher` rather than left to library defaults that
have changed between versions. `NeedsRehash` reads the parameters out of
the encoded hash; a successful sign-in — the one moment the plaintext is
in hand — upgrades a weaker hash in place, so raising the parameters
later upgrades every account over time without a forced reset.

### Email (`Infrastructure/Email`, dev-plan 4.1–4.3)

One sender, `SmtpEmailSender` over MailKit, configured from `SiteSettings`
at send time (host, port, TLS mode, credentials — the password decrypted
through Data Protection). With `EmailEnabled` off or settings incomplete
it declines with a reason rather than throwing; callers decide what that
means (a test send reports it; a password-reset request stays silent, as
it must). Messages are plain text; `EmailTemplates.Html` makes the HTML
twin (escaped, line breaks, bare URLs linked) — no template engine, every
email is a few sentences and a link. Failures are logged and audited as
`email.failed` without the body. Links use `SiteUrl.Resolve`: the
`BaseUrl` setting if set, else `Site:BaseUrl` from the deploy
(`https://$DOMAIN`). Tests swap in `RecordingEmailSender`.

**Notifications by email (4.3)** are an outbox. `Notification.EmailedAt`
is null while a row waits; `NotificationEmailService` polls every minute
(`Notifications:EmailPollSeconds`), groups the pending rows by recipient,
and applies three rules in order: security alerts to an administrator go
immediately whatever their preference; `Immediate` recipients get the
pass's rows in one message; `DailyDigest` recipients get one message per
24 h (`User.LastDigestAt`). `Off` retires the email copy and keeps the
in-app one. `EmailedAt` is set on the *attempt*, so a dead server yields
one audited failure per row rather than one a minute; rows older than 24 h
are retired unsent so turning email on never replays history. With
`EmailEnabled` off the pass does nothing and marks nothing. Links are
built from `SiteUrl.Resolve` and the page's space key.

### Public read mode — the anonymous principal (spec — dev-plan 5.1, designed 2026-09-09)

**What it is for.** A space can be published so that anyone — no account,
no sign-in — can *read* it: the game-wiki case. It is gated twice: the
instance-wide `AllowPublicSpaces` switch (0.2, the 3.3 kill switch) and
the space's own `IsPublic`. Both must be on; the per-space flag is kept
when the switch is off, so re-enabling restores the previous state.

**Who may publish.** A site administrator, not a space owner: exposing
content to the internet is an instance-level risk. `PUT
/api/admin/spaces/{key}/public` needs the admin role, sudo mode (3.5),
and the instance switch on to publish (unpublishing is always allowed).
It is audited (`space.published` / `space.unpublished`) and always raises
a 3.3 security event and alert, in both directions — publishing exposes
content; unpublishing might be an attacker undoing a mitigation.

**The anonymous principal.** A request with no session and no token has
`CurrentUser.Id == null`. `PermissionService` treats that as a principal
with exactly one capability:

* `CanViewSpace(space)` ⇔ `AllowPublicSpaces` ∧ `space.IsPublic` ∧
  ¬`space.Archived`.
* `CanViewPage(page)` ⇔ `CanViewSpace(page.Space)` ∧ `page.Status == Current`
  ∧ ¬deleted ∧ **no restriction of any kind on the page or any
  ancestor**. A signed-in user is only hidden from by *View* restrictions;
  an anonymous reader is hidden from by *any* restriction, because a
  restricted page in a public space is the author saying "not for
  everyone", and "everyone" now includes the internet.
* `ViewableSpaceIds` = the public spaces, or nothing when the switch is off.
* Every edit/admin capability is false.

**Masking.** Anything anonymous may not see is **404, never 403** — the
rule that already protects restricted pages from signed-in users. That
includes private spaces by key. The SPA therefore says "sign in to view
this, or it may not exist", not "this exists but is private".

**What opens (5.2) and what stays closed.** Opened to anonymous readers,
each still permission-checked through the service: space by key, the
public spaces list, the page tree (filtered), a page, its labels, its
attachments (list and download, checked through the page), search
(scoped to public spaces), export (Markdown/HTML — "take your docs with
you" holds for readers), and comments *only* when the space's
`PublicComments` is on (read-only; writing stays authenticated). Closed:
**version history** (the edit history of a public page can carry
withdrawn content), drafts, trash, the user directory, groups, labels
across spaces, watches, collab tokens, avatars, notifications, everything
that writes. Anonymous callers hitting a closed route get 401.

**The leak matrix** is `PublicReadTests`: every opened endpoint × {public
space, private space, restricted page in a public space, draft, trashed
page, attachment of a restricted page, search hit, label listing, export,
version history, tree, comments with `PublicComments` off/on, kill switch
off, archived public space, every write}. The matrix was written before
the routes were opened; the routes are opened only as far as it is green.

**Caching and telemetry.** Anonymous page GETs carry
`Cache-Control: public, max-age=60` and an ETag (version id + layout);
`If-None-Match` gets 304. Unpublishing therefore takes effect within a
minute for cached readers — acceptable, and documented. Signed-in
responses stay uncached. The 3.2 anonymous limiter applies. Page views
are recorded with `UserId = null`, so the dashboard counts them.

**Discovery.** `robots.txt` allows `/spaces/{key}` for each public key
and disallows `/api`; `sitemap.xml` lists every publicly viewable page
with its `lastmod`. For a public page URL, a small middleware injects the
page's `<title>`, a description and Open Graph tags into the SPA shell it
already serves — decided per request through the same anonymous check,
so a private page's title never leaks into a shared link preview. Full
server-side rendering is out of scope; if search indexing beyond titles
matters later, that is the option.

### Roles and administrators (spec — dev-plan 0.1, designed 2026-09-08)

> **Update 2026-09-09:** group management (create/edit/delete/membership)
> and reading the audit log are administrator operations; group listing
> stays open to any signed-in user for the permission picker. In the SPA,
> Groups and Audit are Admin tabs and API tokens live on the profile.

Two roles, one enum: `User.Role` is `Member = 0 | Admin = 1`. An enum, not
a bool, so a future `Viewer` or `Moderator` is a new value rather than a
migration of a bool.

**Who becomes admin.** The first account on an empty instance — whether it
arrives via `/register` or via OIDC provisioning — is created as `Admin`.
Registration runs the "is the table empty" check and the insert inside one
serializable transaction so two racing first registrations cannot both win
(SQLite, used by tests, serialises writes anyway). The migration that adds
the column also **promotes the earliest-created user** on existing installs,
so no instance is left with content and nobody able to administer it. On
this dev instance that is the owner's account, not the docs bot.

**How the role is checked.** `CurrentUser.IsAdminAsync()` reads the row
(one indexed primary-key lookup, cached for the request) rather than
trusting a claim. A role claim would be stale until the next sign-in;
reading the row means a demotion takes effect on the demoted user's very
next request. When `SecurityStamp` lands (dev-plan 1.1) the check can move
to a claim validated against the stamp — until then, the lookup is the
correct and cheap answer. `RequireAdmin` is an authorization policy over
that check; `/api/auth/me` returns `role` so the SPA can show admin
navigation.

**Admins do not silently bypass permissions.** This is the decision the
plan left open, and the answer is Confluence's own: a site admin sees
exactly what their grants allow, like anyone else. What they have that
others don't is a **recover-access** action —
`POST /api/admin/spaces/{key}/recover-access` — which writes them an
explicit `SpaceOperation.Admin` grant on that space. From then on the
existing rules apply unchanged: an explicit space admin can view and edit
the space and is not blocked by page restrictions (that rule already
exists in `PermissionService`). Recovery is audited as
`space.access_recovered` and, once dev-plan 3.3 exists, raises a security
event visible to every other admin. The reasons:

- A silent bypass lets any admin read any team's private space and leaves
  no trace. Explicit recovery gives the same safety valve with a record.
- It reuses the permission logic that already exists instead of adding a
  second "unless admin" branch to every check — fewer places to get wrong.
- Revoking the grant afterwards returns the admin to normal, which a
  silent bypass could never offer.

**What admins can see without recovering access:** metadata, not content.
The admin Spaces page (dev-plan 2.4) lists every space — key, name, owner,
counts, archived, public — through admin-only endpoints that never return
page content. Search, the page tree and page bodies stay permission-checked
for admins exactly as for members.

**Instance-level operations** (`/api/admin/*` — settings, users, spaces
listing, recover-access, later the security page) are gated by
`RequireAdmin` alone; they are about the instance, not about any space's
content.

**Tests the implementation must include:** first registered user is Admin
and the second is Member; first OIDC-provisioned user on an empty instance
is Admin; a Member gets 403 on an admin route; an Admin gets 404 (not 403,
per the masking rule) on a private space they hold no grant for; after
recover-access they can read it, a `space.access_recovered` audit row
exists, and revoking the grant restores the 404; the existing suite still
passes — several tests register two users in sequence, so assert nothing
about them changed except the first one's role.

### Avatars (`components/Avatar.tsx`, dev-plan 1.2)

Every user has an avatar from the moment they register, with nothing stored:
`Avatar` renders an inline SVG of their initials on one of twelve backgrounds,
chosen by an FNV-1a hash of their id. Not a sum of char codes — user ids are
hex GUIDs, which share an alphabet and a length, exactly the case where a weak
hash clusters. The twelve colours all carry white text at 4.5:1 or better
(measured), and all are dark enough to read on both the light and dark page
grounds, so a generated avatar needs no per-theme treatment.

`User.AvatarVariant` records an explicit pick from the twelve; null means
"derive it from the id". It is stored as an index rather than a colour so the
set can be restyled later without rewriting rows, and it survives an upload —
so removing a picture returns to the colour the user chose rather than to the
derived one.

An uploaded picture always wins over a variant. Uploads are cropped to a
square in the browser before being sent, using `createImageBitmap`, which
decodes off the main thread and honours EXIF orientation — without it a
portrait phone photo arrives sideways. The crop is not cosmetic: the server
centre-crops too (0.4), so cropping here is what makes the stored result match
what the user was shown. It is also downscaled to 512px first, so a 12MP phone
photo is not uploaded whole to produce a 256px thumbnail.

`avatarIdentity.ts` holds the colours and helpers, separate from `Avatar.tsx`,
which exports only the component — React Fast Refresh needs component-only
modules, and the linter enforces it.

**Where avatars appear today:** the topbar and the profile page. Comments and
version history return only an `AuthorId` and do not render author identity at
all yet, so avatars there wait until they show names. The user directory
(`GET /api/users`) does carry `avatarHash`/`avatarVariant` already, for the
admin users list in dev-plan 2.2 — the existing people picker is a `<select>`,
whose options cannot contain markup, so it cannot show them.

### Session revocation — the security stamp (dev-plan 1.1)

A cookie scheme is stateless by design: the cookie *is* the proof, so nothing
on the server can normally take it back before it expires. `User.SecurityStamp`
is what makes revocation possible. It is issued into the cookie as a claim at
sign-in and compared against the stored column on every request, in the cookie
handler's `OnValidatePrincipal`. Rotating the column therefore invalidates
every outstanding cookie for that account on its **next request**, not at the
cookie's next expiry.

Changing a password rotates it — which is the point of changing a password you
believe someone else has. The session that made the change is re-issued with
the new stamp, so the person doing it is not signed out along with everyone
else. Suspension (dev-plan 2.2), admin force-logout (3.3) and 2FA enrolment
(3.5) all reuse this one mechanism rather than adding their own.

The same validation also rejects a cookie whose account has become
`Suspended` or been deleted, so those take effect immediately too.

Two consequences worth knowing:

* **Cookies issued before the stamp existed carry no claim and are rejected**,
  which signs everyone in once on deploy. That is the safe direction: treating
  a missing claim as valid would mean a pre-existing cookie outliving the
  password change meant to kill it.
* **API tokens are unaffected** — they authenticate through a different scheme
  and carry no cookie, so a password change does not revoke them. Revoking a
  token is its own action (`DELETE /api/api-tokens/{id}`), and dev-plan 2.2
  adds a bulk revoke. This is a deliberate separation: a script's credential
  should not die because its owner rotated a password, but it must be
  independently revocable.

It costs one primary-key lookup per authenticated request. That is the same
row `CurrentUser.IsAdminAsync` reads, so the two can be collapsed into one
read if this ever shows up in a profile.

### Instance settings (`Infrastructure/Settings`, dev-plan 0.2)

Runtime configuration an administrator changes in the app, as distinct from
deploy-time configuration (connection strings, OIDC, the collab secret) which
stays in environment variables — those are secrets and topology, fixed before
the process starts.

One row, fixed primary key (`SiteSettings.SingletonId`), **typed columns
rather than key/value**: EF validates them, every shape change is a migration,
and the admin UI binds to them without parsing strings. The row is created
lazily on first read; a race to create it is resolved by the fixed key, and
the loser re-reads.

`ISiteSettingsService` is scoped (it needs the request's `DbContext`) but the
cache is a singleton (`SiteSettingsCache`), because a scoped cache would be
useless across requests. Reads go through a 30-second TTL and every save
invalidates. **Single-instance assumption:** invalidation is in-process, so a
second replica would keep its copy until the TTL expired — the short TTL is
the bound on that staleness.

The SMTP password is encrypted with ASP.NET Data Protection, whose keys
already live in this database (`DataProtectionKeys`), so a database restore
stays self-consistent. It is **write-only over the API**: responses carry
`smtpPasswordSet: bool` and never the value. `PUT /api/admin/settings` treats
every field as optional — an omitted field keeps its stored value, so a caller
can change one setting without clobbering the rest — with one addition for the
password, where an empty string means "clear it", something `null` cannot
express. Audit entries name which fields changed, never the secret.

**Registration exemption.** `AllowPublicRegistration` is enforced in
`AuthEndpoints.Register`, but the very first account on an empty instance
ignores it. Otherwise an operator who closes registration before anyone has
signed up could never set the instance up at all. Every later account needs
the toggle on (dev-plan 1.4 adds invite links as the other way in).

### Usage telemetry (`Infrastructure/Telemetry`, dev-plan 0.3)

Three signals, recorded ahead of the admin dashboard (dev-plan 2.5) that
consumes them, so that dashboard ships with real history rather than an empty
chart.

**`User.LastSeenAt`** — stamped by `LastSeenMiddleware` after authorization,
throttled by the singleton `LastSeenTracker` to at most one write per user per
five minutes. "Active in the last 7 days" needs coarse resolution only, so a
write per request would be a lot of work to learn almost nothing. The update
is by primary key with no prior read, and failures are swallowed: knowing when
someone was last active is never worth failing their request over. The tracker
is in-process, so a restart costs one extra write per user, and it prunes
itself past 10,000 entries.

**Login events** — `user.login` attributed to the account, and
`user.login_failed` attributed to nobody. The failure case deliberately
records neither the user id nor the attempted address: an audit log every
admin can read should not become a list of addresses somebody guessed, and a
failure must not confirm which addresses exist. The per-account counter that
brute-force protection needs is dev-plan 3.2's job, not this log's. Recording
these required `IAuditLogger.RecordAs(actorId, …)`, because sign-in happens
before the request has a principal.

**`PageView`** — one row per read, written after the permission check so a
refused read is never counted. Browser sessions only: an API token is a
script, and a nightly export would otherwise dwarf every human in "most viewed
pages". The test for that is the same one the Smart policy scheme uses to pick
its handler, so the two cannot disagree about what a token request is. `UserId`
is nullable from the day the table was created, because public read mode
(Phase 5) writes anonymous views into this same table and widening the column
later would be a migration on a table that is large by then. Raw rows, not a
rollup, with indexes on `(PageId, ViewedAt)` and `ViewedAt`; a rollup can
follow if volume demands it, but starting with one would discard the detail
before knowing which detail matters.

**Recorded IPs are the proxy's, not the client's, until dev-plan 3.0.** The
app sits behind Caddy and has no forwarded-header handling, so
`RemoteIpAddress` is the container address of the proxy — verified on the
running stack, where three requests from two different clients all logged
`172.18.0.7`. The value is recorded anyway so the history exists and becomes
correct the moment 3.0 ships. **Do not build per-IP logic on it before then**:
a rate limiter reading this would see the whole world as one address.

### Profile media (`Infrastructure/Storage/ProfileMedia.cs`, dev-plan 0.4)

Avatars and space icons go through the existing `IAttachmentStorage` under
their own key namespaces (`avatars/…`, `space-icons/…`) rather than a second
storage abstraction — so the S3 implementation that interface reserves a slot
for will cover them too, for free. Keys are deterministic per owner
(`avatars/{userId}.webp`), so replacing an image overwrites rather than
accumulating orphans.

**Every upload is re-encoded, and that is the security control, not a
convenience.** The bytes written are always ones this process produced, which:
strips EXIF (routinely carrying GPS coordinates on a photo someone uses as an
avatar); defeats polyglot files, where one file is simultaneously a valid PNG
and a valid HTML or ZIP document; and bounds decoded dimensions, so a
decompression bomb cannot be stored and then re-served to every viewer.
Dimensions are read from the codec header *before* any pixel buffer is
allocated, so an oversized image is refused rather than decoded and then
rejected. Output is a fixed 256px square WebP, so exactly one content type is
ever served.

**SVG is rejected outright**, by sniffing the leading bytes rather than
trusting the declared content type — which is attacker-controlled, so an SVG
labelled `image/png` must not get through. It is a script-bearing document
format and there is no reason to accept one for a 256px square. The prebuilt
avatars in dev-plan 1.2 are SVG, but this application generates those; it
never accepts one.

**Library choice: SkiaSharp (MIT).** ImageSharp 3.x and later moved to the Six
Labors Split Licence, which would complicate the Apache 2.0 release dev-plan
8.2 intends; SkiaSharp and its Linux native assets are both MIT. The runtime
container is glibc (Ubuntu 24.04, glibc 2.39) and the package ships a matching
`linux-arm64` build — verified in the container, not just on the build host,
because the native-asset variant is the thing most likely to differ between
them.

**Cache busting** is a content hash on the URL (`?v=<hash>`), stored on the
row as `User.AvatarHash` so serving costs no hashing and the URL can be built
from data already loaded with the user. Each version is therefore its own URL,
which is why the response can be cached indefinitely without ever going stale.

An avatar is readable by any signed-in user: it renders next to every comment
and page version, so gating it per viewer would gate nothing while costing a
permission check on each render. Page attachments stay permission-checked,
which is the case that matters. A user with no avatar and a user id that does
not exist both return 404, so the endpoint cannot be used to probe for ids.

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
collapses behind a hamburger, the desktop sidebar (permanently visible) is
replaced by an inline page tree on the space landing page (`SpaceHome`,
`.space-home-tree`), and the page's full-width toggle hides (a distinction
without a difference once the reading column already fills the viewport).
`useDismissable.ts` (outside-click/Escape dismissal) is shared by the
hamburger nav drawer and `OverflowMenu`. `--page-pad` is the one custom property
worth being careful with: it's read both by `.paper`'s own padding and by
the full-width table breakout math (see the Editor section below) — change
it in one place, not both, or they drift apart and a full-width table
overflows the viewport by the difference.

### Theming (`theme.ts`, `components/ThemeToggle.tsx`)

Light/dark/system, expressed to CSS as a `data-theme` attribute on `<html>`:
absent means "system" (the `prefers-color-scheme` media query decides),
`light`/`dark` are explicit overrides. index.css defines the light palette on
`:root`, the dark palette twice — once inside `@media (prefers-color-scheme:
dark)` guarded by `:root:not([data-theme="light"])`, once under
`:root[data-theme="dark"]` — which is what lets an explicit choice win in
both directions.

The accent colour is a second, independent axis on the same mechanism — a
`data-accent` attribute driving every `--primary*` token. Each accent is
defined twice (light and dark), never derived: the contrast requirement pulls
the two in opposite directions. Note that `:root[data-accent="x"]` ties on
specificity with the dark base `:root:not([data-theme="light"])`, which is why
per-accent blocks exist for *every* accent including the default — a
higher-specificity dark block has to exist for each, or an explicit accent
choice would pull the light palette into dark mode.

`index.html` carries a small inline, synchronous script that re-applies the
stored preference before first paint; a deferred or module script runs too
late and the page visibly flips. **The storage key and attribute logic are
duplicated between that script and `theme.ts` — change them together.**

Every colour resolves through a custom property. `--surface` (raised: cards,
`.paper`, popovers, the topbar, inputs) is separate from `--bg` specifically
because they are identical in light mode and must differ in dark. Two
deliberate exceptions: the code block keeps its own dark palette in both
themes, and content colours the *author* chose — a `tableCell`'s
`backgroundColor` attr, a `highlight` mark's `color` — are stored in the
document and cannot be re-themed without discarding that choice, so dark mode
pins dark ink on those elements rather than restyling them. Panel icons are
`mask-image`, not `background-image`, so one `--panel-icon` token per type
re-tints them instead of needing a second set of data URIs.

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
- **Table cell backgrounds** (`TableCellMenu.tsx`) — Confluence's per-cell
  chevron, in the top-right of whichever cell holds the cursor, opening a
  "Background colour" palette. Cursor-driven rather than hover-driven, so
  deliberately *not* sharing `useHoveredTable` with the two controls above:
  the menu belongs to the cell being edited, not whichever one the mouse
  passed over. The colour is a `backgroundColor` attr on both `tableCell` and
  `tableHeader` (`extensions.ts`, via a shared mixin, same
  `.extend()`-and-disable-the-stock-one pattern as `Table`), and unlike the
  `table` node's `width` a plain inline `style` is safe here — `TableView`
  rewrites only the table's own width and colgroup, never cell styles, so
  there's nothing to clobber it. The Cell/Row/Column scope buttons widen the
  written rect via `TableMap.cellsInRect()` and apply every cell in one
  transaction, rather than moving the user's selection to a `CellSelection`
  and calling `setCellAttribute` — the cursor stays where it was.
- **Panels** (`panelExtension.ts`) — Confluence-style callouts. `panelType`
  is exactly ADF's own set (`info`/`note`/`warning`/`success`/`error`);
  Confluence's legacy Info/Tip/Note/Warning macros map onto it, with the old
  Tip macro being today's `success`, so no sixth type is needed. The
  type-specific colour and icon live in `index.css` (`.panel--*`) keyed off
  the rendered `data-panel-type`, which keeps the icon a `::before`
  pseudo-element — ProseMirror owns this node's children and would fight an
  injected element — and means read-only rendering gets the icon with no node
  view to mount. `PANEL_TYPES`/`PANEL_LABELS` are exported so the toolbar
  popover and the slash menu can't drift apart.
- **Colour palettes** (`palette.ts`, `ColorPalette.tsx`) — the swatch grid is
  shared by the highlight dropdown and the cell-background menu; only the
  tiers differ (highlight drops the bold tier, which doesn't hold `--text`
  legibly). Values are Atlassian's own light/medium/bold palette, matching
  the fixed palette Confluence offers instead of a hex input. They're stored
  *in the document* (a cell attr, or the `highlight` mark's `color` — hence
  `Highlight.configure({ multicolor: true })`), not as CSS classes, so they
  survive export and read-only rendering with no stylesheet. The export
  renderer whitelists them to plain hex before they reach a `style`
  attribute (`ProseMirrorRenderer.IsSafeCssColor`), since document JSON is
  stored as given and an unvalidated colour would be CSS injection into
  exported HTML.
- **`ToolbarPopover.tsx`** is the always-visible popover trigger (highlight
  palette, panel picker). Not to be confused with `ToolbarDropdown.tsx`,
  which looks similar but exists *only* as the mobile collapsed form of a run
  of buttons and is `display: none` above `--bp-mobile`.
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

### Page tree drag-and-drop (`components/PageTree.tsx`)

The page tree — rendered identically in the desktop sidebar and the mobile
`SpaceHome` inline copy (same component, same data, see Responsive layout
above) — is the only way to reorder or reparent pages once created; there is
no separate "move" dialog. Dragging only happens in **Reorder mode**,
toggled per-tree-instance by a compact icon button next to the "📑 Pages"
heading (`PageTree.tsx`'s `editMode` state) — a flat pencil (`PencilIcon`,
same stroke-icon language as the editor toolbar and the topbar bell) when
off. Outside it, rows are plain `StaticRow` links with no dnd-kit hooks and
no `touch-action` override at all — not just visually inert, structurally
incapable of starting a drag. This exists because the first version made
every row a drag source all the time: on mobile, `touch-action: none`
(needed so a touch-drag isn't raced by the browser's own scroll gesture)
meant any swipe that happened to start on a page title reordered it instead
of scrolling the list, which was exactly backwards.

Reorder mode is a **batch edit**, not one-drag-one-save: drags apply to a
local draft tree (`draftTree` state, seeded from the `tree` prop and frozen
against further prop updates until the session ends — see the `useEffect`
guarded on `!editMode`) and nothing reaches the server until an explicit
**Save**; **Cancel** discards the draft and never sends a request at all.
This replaced an earlier single-toggle "Done" button that committed each
drag immediately — reparenting a page is very often the first of several
related moves, and re-entering Reorder mode before each one made that
workflow tedious. Each completed drag both updates `draftTree` (via
`applyMove`, a pure function that removes the dragged node — with its
subtree intact — and reinserts it under the new parent at the new index,
letting the existing `flatten()` recompute correct depths for the whole
moved subtree for free) and appends `{pageId, parentPageId, index}` to a
`pendingMoves` queue. **Save** replays that queue as sequential
`PUT /api/pages/{id}/move` calls, in the order the moves were made, each
against whatever the server now holds. That ordering guarantee is what
makes replay safe without needing to diff the draft against the original
tree: every intermediate state Save produces is one the draft itself
already passed through — and validated a parent choice against — while the
user was dragging, so replaying in the same order converges to the same
tree. A failure mid-replay aborts the remaining queued moves, surfaces an
error, and refetches the tree so the UI reflects however far Save actually
got — never a state the user hasn't seen. Rows aren't links while
editing (`DraggableRow` renders a `<div>`, not a `NavLink`): mid-batch, a
stray click on a row would otherwise navigate away and abandon whatever
hasn't been saved yet.

Within a single Reorder session, a page row is itself the drag source (no
separate handle icon — dnd-kit's `distance: 4` activation constraint tells
a click from a drag). Dragging it up or down reorders it among siblings;
dragging it horizontally while over another row changes its nesting depth,
reparenting it. Rather than live-shuffling the rest of the list, a line
shows where the row would land — a 2px line with an 8px circular terminal
bleeding 4px past its own left edge, matching Atlassian's own drop-indicator
spec
([atlassian.design/components/pragmatic-drag-and-drop/design-guidelines](https://atlassian.design/components/pragmatic-drag-and-drop/design-guidelines))
— with the line's left offset (`marginLeft`) doubling as the nesting-depth
indicator. An earlier version used an always-visible grip-icon handle with
live-reordering; both the extra element and the nested flex row it required
turned out to be the source of a mobile layout-overflow regression, so the
row-is-the-handle + static-list-plus-line design fixed both the UX
complaint and the bug at once.

Built on `@dnd-kit/core` + `@dnd-kit/sortable` (chosen over
`react-beautiful-dnd`/`react-dnd` for native touch support via Pointer
Events, so the same code drives both the mouse-driven desktop tree and the
touch-driven mobile one — no separate touch handling). The tree is
flattened to `{id, parentId, depth}` for the drag session; `project()`
derives the dragged row's new depth from horizontal drag distance, clamped
between the row above's depth+1 (can't skip a nesting level) and the row
below's depth (can't leave a gap) — the standard "sortable tree" projection
technique. The dragged row's own descendants are excluded from that working
list for the duration of the drag (computed against `draftTree`, so this
still holds correctly across several drags in one session, not just the
first), so a subtree can never be dropped inside itself; the backend's
cycle check (`WouldCreateCycleAsync`) is the backstop, not the only guard.

`PUT /api/pages/{id}/move` takes `{ parentPageId, index }`, where `index` is
a slot among the destination's *current* siblings (0 = first) — not a raw
`Position` value the frontend has to compute or guess at. The endpoint
resolves that group, inserts the moving page at `index`, and renumbers the
whole group's `Position` densely (0..n-1) in one pass, so a drag can never
collide with or leave a gap relative to its new neighbors.

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
