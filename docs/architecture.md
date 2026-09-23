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
                        backup sidecar ──► pg_dump ──► backups volume (status in BackupAgents/Backups/BackupJobs)
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

- **Local accounts**: cookie-based sessions; passwords hashed with Argon2id.
- **API tokens** (`Authorization: Bearer <token>`): for scripts/integrations
  (Features/ApiTokens). Only a SHA-256 hash is stored; the raw token is shown
  once, at creation.
- **OIDC/SSO**: optional, pluggable for any standards-compliant provider
  (Keycloak, Authentik, Google, ...) via `Oidc:Authority`/`ClientId`/
  `ClientSecret`. A first login provisions a passwordless local account; a
  verified-email match links to an existing local account; an unverified-email
  match is refused (would otherwise allow account takeover). With no Authority
  configured the app behaves exactly as local-accounts-only.

A `Smart` policy scheme picks Cookie vs. API-token per request based on the
`Authorization` header. Unauthenticated API calls receive `401` (no login
redirect), since the client is a SPA, except the OIDC login endpoint, which is
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
  entry, the one Caddy appended, is used, so a client cannot choose its
  own address by sending the header itself. Everything downstream
  (`RemoteIpAddress`, the audit log's `Ip`, 3.2's rate limiter) sees the
  real client. The tests set the connection address with a startup filter
  (`TestRemoteIpStartupFilter`) so they can act as the proxy or as a stranger.
* **Cookie `Secure`.** In Production the session cookie is `Secure` always,
  not "same as request" (which, as the app saw it, was never HTTPS).
  `Security:AllowInsecureCookies=true` is the documented escape hatch for a
  deliberately HTTP-only install.

**Security headers** are set by `SecurityHeadersMiddleware` on every
response: in the app, not Caddy, so they hold whichever proxy is in front
and the tests can assert them: `nosniff`, `X-Frame-Options: DENY`,
`Referrer-Policy`, `Permissions-Policy`, COOP/CORP `same-origin`, and a CSP
with `frame-ancestors 'none'`, `object-src 'none'`, `base-uri 'self'`.

The CSP's `script-src` is `'self'` plus a **hash** of the inline theme
script in `index.html`, computed at startup from the `wwwroot/index.html`
this process serves, so a rebuild that changes the script changes the hash
with it, and `'unsafe-inline'` is never needed for scripts. `style-src`
does allow `'unsafe-inline'`: the editor writes inline `style` attributes
(cell colors, alignment) and React sets them directly; blocking inline
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
with a startup warning: a half-configured split must not brick an install
that worked yesterday. Postgres referential actions (cascades, `SET NULL`)
run as the table owner, so the role's lack of `DELETE` on `PageViews` does
not stop a page purge.

**The chain.** Every `AuditLog` row carries `Sequence` (contiguous from 1),
`PrevHash` and `Hash = SHA-256(PrevHash ‖ canonical row)`. Linking happens
in `AppDbContext.SaveChanges[Async]` (the one place every write passes
through, so no code path can add an unchained row) under a
transaction-scoped Postgres advisory lock so concurrent appenders serialize
on the tail. A unique index on `Sequence` makes any race that got past the
lock fail rather than fork.

Two round-trip hazards shaped the canonical form. `MetadataJson` is `jsonb`,
and Postgres re-orders keys, strips whitespace and normalizes numbers on
the way in, so the hash is over a canonical form (keys sorted, compact,
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
chain that got *shorter*, the one thing a chain cannot detect on its own,
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
global limiter for **anonymous** callers only: signed-in users are not
globally limited because their identity is the accountability, and this
global limiter is what Phase 5's public-read mode relies on. Rejections
are 429 with `Retry-After` and a small JSON body.

Every limit is a `SiteSettings` field, editable from Admin → Security. The
partitioner cannot await, so it reads the last-loaded settings through
`SiteSettingsCache.Peek()`; `UpdateAsync` now *sets* the cache instead of
invalidating it, and startup warms it, so a change applies to the next
request. The limit value is part of the partition key, so a change starts
fresh windows immediately rather than waiting for old ones to idle out.

The lockout lives on the user row (`FailedLoginCount`, `LockedUntil`):
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

### Threat detection and admin alerting (spec, dev-plan 3.3, designed 2026-09-09)

**What it is for.** The limiters in 3.2 stop an attack from succeeding
cheaply; this tells an administrator that one is happening and gives them
something to do about it in one click. The requirement, verbatim: "if an
attack is suspected an email / notification goes to the admin group and it
gives them mitigation options." Email arrives in 4.3; notifications are
in-app now, through the existing bell.

**Three tables, one of them append-only.**

* `SecurityEvents`: what a detector saw. *Append-only*: the runtime
  role cannot update or delete it (added to `DatabaseRoles.AppendOnlyTables`),
  so the record of an attack cannot be tidied away by the app. Columns:
  `Kind` (dotted, e.g. `login.credential_stuffing`), `Severity`
  (Info/Warning/Critical), `Key` (what the detector counted on: an address,
  an actor id), `Ip`, `ActorId`, `TargetType`/`TargetId`, `MetadataJson`,
  `CreatedAt`.
* `SecurityAlerts`: the workflow object an administrator acts on, one per
  event that crossed a threshold. Mutable: `Status` (Open → Acknowledged →
  Resolved), `Note`, who and when. Split from the event precisely so that
  acknowledging does not require an UPDATE on the append-only table.
* `BlockedNetworks`: the address blocklist: `Cidr`, `Reason`, `ExpiresAt?`,
  who added it.

**Detectors.** Two shapes. *Discrete* signals write an event (and usually
an alert) every time: an admin promoted, `AllowPublicSpaces` toggled, a
webhook pointed at a private address, the audit chain found broken, an
admin signing in from an address never seen for that account, an account
locked. *Burst* signals count in a sliding window and write **one** event
when the threshold is crossed, with the count in the metadata: the audit
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
| `admin.promoted` |, | always | Warning |
| `settings.public_spaces_toggled` |, | always | Critical |
| `webhook.private_target` | actor | always (3.4 also blocks it) | Warning |
| `audit.chain_broken` |, | always | Critical |
| `backup.failed` | agent | a backup job finished failed (BackupMonitor, every 5 min) | Warning |
| `backup.overdue` | agent | no successful backup within 2 × the interval | Critical |
| `backup.agent_offline` | agent | no heartbeat for 15 min, or no agent row 10 min after start | Warning |
| `backup.restore_test_failed` | agent | a restore test finished failed | Critical |
| `backup.disk_low` | agent | free space under 10 % or under twice the newest backup | Warning |
| `backup.retention_reduced` | instance | an admin saved a policy that can remove more | Critical |

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
optionally with a note; both are audited. Nothing auto-resolves: a
detector cannot know the attacker gave up.

**Mitigations**: each one click on the Security page, each already
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
events: a blocked scanner retrying is exactly the flood events exist to
avoid.

**What this does not do.** It does not detect a slow attacker who stays
under every threshold; the limiters make that attacker slow enough that
the audit log is the right tool. It does not correlate across kinds. It
does not phone home. And it cannot notify anyone if the app itself is
down: that is a monitoring concern, outside the app.

### Egress and input hardening (`Infrastructure/Security/EgressGuard.cs`, `CsrfHeaderMiddleware.cs`, `Infrastructure/Storage/ContentTypes.cs`, dev-plan 3.4)

**Outbound requests (SSRF).** Every HTTP request the server makes on a
user's behalf (webhooks now, link previews later) goes through
`EgressGuard`. The attack is an editor pointing a webhook at
`http://169.254.169.254/` or `http://db:5432/`, which the server can reach
and the editor cannot. The defense holds at two moments, because a
hostname can resolve publicly when saved and privately when delivered
(DNS rebinding): `ValidateAsync` checks the URL (http/https only, no
credentials, no `localhost`/`.local`/`.internal`) and *every* address it
currently resolves to; `CreateHandler` builds a `SocketsHttpHandler` whose
`ConnectCallback` resolves again and checks the exact address about to be
dialled. Automatic redirects are off; `SendAsync` follows at most three by
hand, validating each hop. Five-second timeout. `Egress:AllowedNetworks`
lets an operator open a private range deliberately (a LAN automation
server); the 3.3 detector still records the attempt. A refused delivery is
logged and not retried: it is not transient.

**Attachments.** The declared content type is a suggestion. `ContentTypes.
Resolve` lets the bytes win where a signature is recognized (PNG, JPEG,
GIF, WebP, PDF), otherwise keeps the declared type unless it is something
a browser might *execute* (HTML, XHTML, SVG, XML, scripts) or the bytes
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
disabled on the `IFormFile` endpoints: that scheme (form tokens) is not
the one in use.

**Body size.** Kestrel's `MaxRequestBodySize` is set to 100 MB to match
Caddy, so the limit does not silently depend on which proxy is in front.
Attachments are capped at 25 MB by the endpoint.

### Sessions, two-factor sign-in and sudo mode (`Infrastructure/Auth/TotpService.cs`, `PasswordHasher.cs`, dev-plan 3.5)

**Sessions.** Each sign-in creates a `UserSession` row and the cookie
carries its id (`tesria:session`). `OnValidatePrincipal` now checks three
things on every request: the security stamp (revokes *all* of an account's
cookies), the session row (revokes *one*), and the `auth_time` claim
against `Auth:SessionAbsoluteDays` (90), however active, a session ends
then; sliding expiry alone (`ExpireTimeSpan`, now 14 days idle) would let a
cookie live forever. Sign-out revokes the row, so a copy of the cookie
taken earlier dies with it. Profile → Sessions lists every browser with
address and last activity, with per-session and "all others" revoke; an
admin's revoke-sessions marks the rows too. Cookies that predate sessions
carry no claim and are rejected, which signed everyone in once: the safe
direction, as with the stamp.

**Two-factor (TOTP).** RFC 6238 with the parameters every authenticator
supports: 20-byte secret, SHA-1, 30-second steps, six digits. Secrets rest
under Data Protection (keys in the database, so a backup restores them
and a dump alone does not read them). Enrollment is scan → type a code →
on; the pending secret is not live until a code proves the device has it.
Enabling rotates the security stamp so every *other* session must pass
the new factor; the enrolling one is re-issued in place. Disabling needs
the password or a code, never just a live session.

Sign-in becomes two requests: `/login` verifies the password and, for an
enrolled account, returns `{ requiresTotp, challenge }` instead of a
cookie: the challenge is a Data-Protection-signed token (5 minutes,
bound to the client address). `/login/totp` takes it with a code, or a
**recovery code** in the code's place (1.3's set, spent on use: one set
of backup codes, not two). Wrong codes count toward the 3.2 lockout,
because six digits is a small space. A code's time step is stored on
acceptance and anything at or before it is refused, so a code seen over a
shoulder is worthless once typed.

`RequireTotpForAdmins` is enforced in `AdminRequirementHandler`: an
un-enrolled administrator gets 403 on every admin route at once, `me`
reports `totpRequired`, the admin shell says why and links to the
profile; the enrollment endpoints live under `/auth/me`, outside the
policy, so the way out is always open. Such an admin cannot turn TOTP
back off while the rule stands.

**Sudo mode.** Destructive administration (changing who is an admin,
flipping the public-spaces switch, purging a page, removing a block)
calls `AuthEndpoints.RequireSudo`, which passes only if the session
authenticated within `Auth:SudoMinutes` (5; shorter than the fresh-login
window on purpose). Otherwise the endpoint returns 403 with
`code: reauth_required`. The SPA's `request()` recognizes that code, opens
the re-authentication dialog (password, or a code for enrolled accounts),
calls `/auth/reauth`, which re-issues the cookie with a fresh `auth_time`
on the *same* session, and retries the original request once. Several
requests failing together share one prompt. A wrong answer counts as a
failed sign-in.

**Argon2id, pinned.** 64 MiB, 3 passes, 4 lanes, 32-byte output, written
down in `Argon2PasswordHasher` rather than left to library defaults that
have changed between versions. `NeedsRehash` reads the parameters out of
the encoded hash; a successful sign-in, the one moment the plaintext is
in hand, upgrades a weaker hash in place, so raising the parameters
later upgrades every account over time without a forced reset.

### Email (`Infrastructure/Email`, dev-plan 4.1–4.3)

One sender, `SmtpEmailSender` over MailKit, configured from `SiteSettings`
at send time (host, port, TLS mode, credentials: the password decrypted
through Data Protection). With `EmailEnabled` off or settings incomplete
it declines with a reason rather than throwing; callers decide what that
means (a test send reports it; a password-reset request stays silent, as
it must). Messages are plain text; `EmailTemplates.Html` makes the HTML
twin (escaped, line breaks, bare URLs linked): no template engine, every
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

### Public read mode: the anonymous principal (spec, dev-plan 5.1, designed 2026-09-09)

**What it is for.** A space can be published so that anyone (no account,
no sign-in) can *read* it: the game-wiki case. It is gated twice: the
instance-wide `AllowPublicSpaces` switch (0.2, the 3.3 kill switch) and
the space's own `IsPublic`. Both must be on; the per-space flag is kept
when the switch is off, so re-enabling restores the previous state.

**Who may publish.** A site administrator, not a space owner: exposing
content to the internet is an instance-level risk. `PUT
/api/admin/spaces/{key}/public` needs the admin role, sudo mode (3.5),
and the instance switch on to publish (unpublishing is always allowed).
It is audited (`space.published` / `space.unpublished`) and always raises
a 3.3 security event and alert, in both directions: publishing exposes
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

**And the instance has to look like what it is (5.5).** The two switches
above decide what an anonymous *request* may read. They also decide what
the SPA shows a visitor before any request is made, which used to be a
separate question and answered wrongly: with nothing published, `/` still
rendered the public shell and a Spaces page saying "Nothing is published
for public reading."

`GET /api/instance` (anonymous, covered by the global rate limiter that
already bounds every session-less caller) answers `{ instanceName,
needsOwner, publicReading, allowPublicRegistration }`, where
`publicReading` is the same conjunction the permission service applies:
the switch, and at least one non-archived public space. `InstanceProvider`
fetches it once beside the session check, and `SessionGate` sends a
visitor with no session to `/login` whenever `publicReading` is false,
carrying `from` so a deep link still arrives where it meant to once they
sign in. A link to a public page on an instance that publishes nothing
therefore lands on sign-in, which is the truthful answer: nothing is
public. The login page offers "Browse what is public" only when there is
something to browse, and hides the sign-up link when registration is
closed (an `?invite=` token shows it regardless, because the invite is its
own authorization).

This is also the one endpoint that tells an anonymous caller anything
about the instance, which is why it is four fields and a test asserts that
it is exactly those four. `needsOwner` is what 10.2's setup wizard will
key off; the first account becomes the owner.

**Masking.** Anything anonymous may not see is **404, never 403**: the
rule that already protects restricted pages from signed-in users. That
includes private spaces by key. The SPA therefore says "sign in to view
this, or it may not exist", not "this exists but is private".

**What opens (5.2) and what stays closed.** Opened to anonymous readers,
each still permission-checked through the service: space by key, the
public spaces list, the page tree (filtered), a page, its labels, its
attachments (list and download, checked through the page), search
(scoped to public spaces), export (Markdown/HTML: "take your docs with
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
minute for cached readers: acceptable, and documented. Signed-in
responses stay uncached. The 3.2 anonymous limiter applies. Page views
are recorded with `UserId = null`, so the dashboard counts them.

**Discovery.** `robots.txt` allows `/spaces/{key}` for each public key
and disallows `/api`; `sitemap.xml` lists every publicly viewable page
with its `lastmod`. For a public page URL, a small middleware injects the
page's `<title>`, a description and Open Graph tags into the SPA shell it
already serves: decided per request through the same anonymous check,
so a private page's title never leaks into a shared link preview. Full
server-side rendering is out of scope; if search indexing beyond titles
matters later, that is the option.

### Space icons (`Features/Spaces/SpaceIcons.cs`, `components/SpaceIcon.tsx`, dev-plan 6)

Three columns on `Space`: `IconKind` (`None | Emoji | Image`), `IconValue`
and `IconColor`. `None` is not "no icon": it is the generated default, the
key's first letter on a tile colored by a stable hash of the key, so every
space has an icon from the moment it is created with no storage and no
round trip. That is the same reasoning as generated avatars, and it reuses
their twelve colors: identical job, and two palettes doing one job would
drift apart.

`IconValue` carries the emoji for `Emoji` and the stored picture's content
hash for `Image`, not the storage key, which is derived from the space id
(`ProfileMediaService.KeyFor`) and would only be a duplicate. `IconColor`
is an index into the client's palette rather than a hex value, so the
palette can be restyled without rewriting rows.

**Icons are rounded squares; avatars are circles.** At tile size that shape
is the only thing telling a reader whether they are looking at a person or
a place, so it is a deliberate distinction, not styling.

Pictures go through the Phase 0.4 pipeline unchanged (re-encoded to a
256px WebP, EXIF stripped, SVG refused, decoded dimensions bounded) so
everything said about avatar uploads holds here too. They are set only
through `PUT /api/media/space-icons/{key}`, never through the JSON update,
which would otherwise let a space be pointed at an arbitrary stored key.
Switching to an emoji or back to the default deletes the stored bytes
rather than orphaning them.

Reading an icon follows the space's own visibility, unlike an avatar (which
any signed-in user may fetch): a space nobody may see must not confirm its
existence through its icon, so the route is `AllowAnonymous` plus
`CanViewSpaceAsync`, and the answer is 404 either way. That also makes a
public space's icon readable with no account, which is what the public
listing (5.3) needs.

The emoji rule is about shape rather than membership: short, no control
characters, at least one non-ASCII character. "Is this an emoji" has no
stable answer worth encoding, the set changes every Unicode release, and
what actually matters is that the value is a glyph rather than prose or
markup, because it renders inline wherever the space appears.

### Embeds and link previews (dev-plan Phase 7 Wave E)

**The allowlist is enforced twice, and that is the whole design.** An embed
is a third-party iframe on a page everyone else reads, so:

1. `GET /api/embeds/resolve` refuses a host that is not on
   `SiteSettings.EmbedAllowlist`, and returns the URL that may be framed.
2. The CSP's `frame-src` is built from the *same* list, per request
   (`SecurityHeadersMiddleware` reads the settings), so a browser refuses an
   off-list frame even if a client bug put one in the DOM.

The client never decides what may be framed: it asks and frames what it is
told. Emptying the allowlist turns embeds off entirely, in both places at
once.

**Host matching (`EmbedAllowlist.IsAllowed`) is on a label boundary, never a
plain `EndsWith`.** `.youtube.com` must admit `www.youtube.com` and refuse
`evil-youtube.com` and `youtube.com.attacker.net`. This is four lines and it
is the entire trust decision, so it is tested with the hostile cases rather
than the happy ones.

**Providers narrow, they do not merely permit** (`EmbedProviders`). A
YouTube watch page becomes the no-cookie embed player; a Google Doc becomes
its `/preview`. A host on the allowlist with *no* provider rule frames as
pasted, which is what makes "allowlist our internal Grafana" work with no
code. The iframe itself is sandboxed to scripts, same-origin, presentation
and popups; never top-level navigation.

**Link previews** (`LinkPreviewService`) fetch Open Graph tags through the
3.4 egress guard and never around it, cap the response at 256KB, and cache
per normalized URL: a week for a success, an hour for a failure, so a dead
link is not an outbound request on every page view. An `og:image` is used
only when it is an absolute https URL. Unfurling needs an account (it makes
an outbound request); resolving does not (an embed on a public page is part
of the page).

**Gotcha: a new settings column needs its default on the migration, not
just on the property.** `SiteSettings.EmbedAllowlist`'s C# initializer only
runs when a *new* settings object is constructed. On an instance that
already has its singleton row, an `AddColumn` with `defaultValue: ""`
silently turned embeds off on upgrade. Every test builds a fresh database
and so never takes that path; it was found by upgrading a running instance.
The same trap applies to every future setting.

### Technical content: diagrams, math, charts (dev-plan Phase 7 Wave F)

- **Mermaid is a code-block language, not a node.** Choosing it switches
  `CodeBlockView` from highlighting to drawing. The source therefore stays
  an ordinary fenced block in the document and in both exports, there is no
  second node type to paste into or render, and "what is in this code block"
  stays one decision. The source is *hidden*, not unmounted, when the
  diagram shows, ProseMirror needs its view of the node to keep it
  editable, which needs an explicit `.code-block__body[hidden]` rule,
  because `display: flex` out-specifies the user agent's `[hidden]`.
- **Both libraries load on demand.** Mermaid is ~500KB and KaTeX ~280KB with
  its fonts; each is a dynamic `import()` whose promise is cached at module
  scope, so ten diagrams share one load and a page with none downloads
  nothing. Vite splits Mermaid per diagram type.
- **Charts read a table already on the page, by ordinal.** `source: 2` means
  "the second table", because ProseMirror nodes have no stable identity and
  an id would have to be minted, stored and kept unique through copy-paste,
  and an author thinks in "the second table" anyway. The data is never
  copied into the chart, so editing the table redraws it. Deliberately *not*
  a Wave D dynamic block: the table is in the document, so a server round
  trip would be slower, would miss unsaved edits, and would need a fourth
  result shape the contract does not have. The chart is plain SVG and
  flexbox rather than a charting library: four types over one table is a
  few dozen lines against another ~150KB in the bundle.
- **Exports stay readable outside the app, and reach nothing.** An embed
  and a smart link become plain links (never an iframe; a `javascript:` URL
  becomes no link at all: document JSON is stored as the client sent it).
  Math exports as `$…$`. A chart names the table it charts.
  A page with a Mermaid diagram carries the renderer **inlined**: the web
  build produces a single-file bundle (`npm run build:mermaid` →
  `wwwroot/export/mermaid-standalone.js`, gitignored, ~3MB) and
  `ExportEndpoints` reads it once and inlines it. No CDN, and no dependence
  on this instance being reachable either: an exported file is meant to be
  something you keep.
  Three details that are easy to get wrong: the bundle is built with
  `publicDir: false` (otherwise Vite copies the app's favicon and icon
  sprite into its output too); its entry must not use Mermaid's
  `startOnLoad`, which only listens for `DOMContentLoaded` and so never
  fires for a script inlined at the end of the body: it calls
  `mermaid.run()` directly when the document is already ready; and the
  inlined text has `</script>` escaped, so a future Mermaid containing that
  sequence cannot end the script tag early and break every exported file.
  Where the bundle is missing (tests, a dev API with no built SPA) the
  export ships the diagram source alone, which is still readable.

### MCP server (spec, dev-plan 8.4, designed 2026-09-11)

**What it is for.** An AI assistant (Claude Code, Claude Desktop, anything
speaking the Model Context Protocol) reads and writes this wiki through a
small, typed tool surface instead of scraping HTML or guessing at REST
calls. The user's own token is the credential, so an assistant can do
exactly what that person can do, and nothing more.

**The decisions, and why each is the way it is.**

1. **In-process, in .NET, on the official SDK** (`ModelContextProtocol.AspNetCore`,
   Streamable HTTP at `/mcp`). Not a Node sidecar: a sidecar would have to
   call the REST API with a forwarded token: a second hop, a second
   place permissions could be got wrong. In-process, a tool calls the same
   `IPermissionService` and `CurrentUser` every endpoint does, so an MCP
   call cannot see or change anything the same token could not through
   REST. **Stateless mode**: every request carries its own token and is
   authenticated on its own, so there is no session to pin behind Caddy
   and nothing to leak between callers.

2. **Token only, never a cookie.** `/mcp` requires the `ApiToken`
   authentication scheme explicitly. A browser session is never accepted
   there, so a page in someone's tab cannot drive the assistant surface:
   the CSRF concern does not arise because the credential cannot be
   ambient. The token must belong to an active user; the handler already
   enforces that.

3. **Token scope, finally: `ApiToken.ReadOnly`.** Tokens had no scopes.
   Now a token is minted read-only or not (`POST /api/api-tokens` takes
   `readOnly`; the listing shows it; existing tokens stay full-access, so
   nothing silently narrows). The handler adds one claim,
   `tesria:token_scope` = `read` | `write`; cookie sessions carry no such
   claim and are unrestricted. **Enforced in one place for REST**
   (`TokenScopeMiddleware`: a `read` token making an unsafe-method request
   under `/api` gets `403 { code: "read_only_token" }`) **and checked by
   each MCP write tool** (the transport is all POST, so the middleware
   excludes `/mcp` and the tools ask `McpAccess.RequireWrite`). A scope
   only the MCP server honored would not be a scope.

4. **Markdown is the content contract.** Reads return the page as
   Markdown (the *same* Markdown the export produces, with dynamic blocks
   snapshotted as the caller) because an assistant reasons in Markdown and
   the export renderer already exists. Writes accept `content` as Markdown,
   converted server-side (Markdig → ProseMirror JSON) over the subset the
   editor's own Markdown export emits: headings, paragraphs, bold/italic/
   strike/code, links, bullet/ordered/task lists, code blocks with a
   language, blockquotes, tables, horizontal rules, images. Everything the
   editor can hold but Markdown cannot say (panels, status, layouts, dynamic
   blocks) is out of reach through Markdown *by design*: an assistant
   writes body text; a person enriches it. `contentJson` is the escape
   hatch for a caller that has ProseMirror JSON (copying a page, 8.5
   packs): exactly one of the two must be given. `get_page(format: json)`
   returns the JSON for that purpose.

5. **One write path.** `create_page`/`update_page` do exactly what `POST`/
   `PUT /api/pages` do (validation, position, search text, audit,
   watcher notifications, mention notifications, webhooks) because they
   call the same code. That code is extracted from `PageEndpoints` into a
   `PageWriter` service used by both; the endpoint tests are the safety net
   for the extraction. 8.5's importer needs the same writer.

6. **Errors never reveal what the caller may not see.** A page the token's
   user cannot view is "not found" to a tool, exactly as it is 404 to REST.
   Forbidden edits say so plainly ("no edit rights on this space"). A
   read-only token calling a write tool is told how to mint one that can.

**The tool surface.** Names are `verb_noun`, snake_case, as MCP clients
expect. Read tools work with any token; write tools need a `write` one.

| tool | scope | arguments | returns |
|---|---|---|---|
| `list_spaces` | read |: | spaces the user may view: `key`, `name`, `description`, `isPublic` |
| `get_space_tree` | read | `spaceKey` | the page tree the user may see, nested `{ id, title, children }` |
| `search_pages` | read | `query`, `spaceKey?`, `limit=20` (≤50) | `{ id, spaceKey, title, snippet }[]`, permission-filtered like `/api/search` |
| `get_page` | read | `pageId`, `format=markdown\|json` | `title`, `spaceKey`, `parentPageId`, `labels`, `version`, `updatedAt`, `content` |
| `find_pages_by_label` | read | `label`, `spaceKey?` | `{ id, spaceKey, title }[]` |
| `list_labels` | read | `spaceKey` | `{ name, pages }[]` over visible pages only |
| `create_page` | write | `spaceKey`, `title`, `content?` \| `contentJson?`, `parentPageId?` | the new page's `id` and URL |
| `update_page` | write | `pageId`, `content?` \| `contentJson?`, `title?`, `changeComment?` | the new `version` |
| `add_page_label` / `remove_page_label` | write | `pageId`, `label` | the page's labels |

**Deliberately not tools:** trash/purge (irreversible; a person's job),
permissions and restrictions, space creation, anything under `/admin`,
attachment upload (binary over MCP is a poor fit today; a later `resources`
surface is the right home). An assistant that needs those is asking a
person to do them.

**Server metadata.** `serverInfo.name = "tesria"`, the assembly version,
and an `instructions` string telling the client what the wiki is, that
content is Markdown, and that "not found" may mean "not permitted".

**Known gap: the collaborative document is never reconciled with the
page (found 2026-09-13).** Hocuspocus loads a page's Yjs state from
`CollabDocuments` by name and the editor seeds it from the page only when
it is empty (`CollaborativeEditor.tsx`). Nothing on the write side (
`PageWriter`, the REST update, the MCP `update_page` tool) touches that
row. So once a page has been opened in the editor, a write from anywhere
else is invisible to the next editor session, and Update from that
session overwrites it. The fix needs a decision: invalidate the document
when a page is written outside the editor (simple, loses a live session's
unsaved edits if the two collide), or carry the page version in the
document and re-seed on mismatch (keeps both, more moving parts). Until
then, do not rely on API or MCP writes to a page that people also edit in
the browser.

**Retrieval quality** (2026-09-11). Three things make the tools usable as
a context source rather than merely correct:

- `SearchSnippets` returns the passage that *matched* (`ts_headline` on
  Postgres, a window around the first matching word elsewhere), marked in
  `**bold**`. Without it a snippet was the page's opening line and an
  assistant had to fetch every result to find out why it matched.
- `get_page` returns the heading `outline` and accepts a `section`,
  reusing Wave A's anchors. Top-level headings only: slicing mid-panel
  would produce something that is not a document.
- Hits carry a `score`, omitted rather than faked where the database
  cannot rank.

Semantic search is deliberately *not* here; see `roadmap.md` for the
evidence, the decisions it needs and the size at which it earns its keep.

**Client setup** (documented in the API space): an MCP client is pointed at
`https://<instance>/mcp` with header `Authorization: Bearer <token>`.

**Adding a tool (Opus, against this contract).** A `[McpServerTool]` method
on `TesriaTools` taking its arguments as parameters (the SDK derives the
JSON schema) plus DI services; write tools call `McpAccess.RequireWrite`
first; permission checks go through `IPermissionService` exactly as the
matching endpoint's do; a not-viewable target throws `McpException("… not
found")`. Three tests per tool: the result; the leak test (a token whose
user cannot view the target gets "not found", and a listing omits it); and,
for write tools, that a read-only token is refused before anything changes.

**Shipped 2026-09-11.** All ten tools, and two pieces of plumbing the
contract required:

- **`PageWriter`** (`Features/Pages/PageWriter.cs`) is now the only place a
  page is created or updated. `PageEndpoints.Create`/`Update` are thin
  translations of its result into HTTP; the MCP tools translate the same
  result into a tool response. That is what makes "a page written by an
  assistant is indistinguishable from one written in the browser" true
  rather than aspirational: the audit entry, the watcher and mention
  notifications and the webhook all come from the one code path. The
  existing endpoint tests were the safety net for the extraction.
- **`MarkdownToProseMirror`** converts over exactly the subset the export
  emits, so a page survives read → edit → write. **Gotcha found by the
  round-trip test:** Markdig models `[x] done` as a `TaskList` inline
  followed by the literal `" done"`: the separating space belongs to the
  marker. Dropping the marker without it makes every round trip indent the
  text one space further, compounding on each edit.
  Note also that two `-` lists separated only by a blank line are *one*
  list in CommonMark, and a list where any item has a checkbox becomes a
  task list (promoting is lossless; demoting would throw checkboxes away).

### Dynamic blocks (spec, dev-plan Phase 7 Wave D, designed 2026-09-10)

**What it is for.** Children display, Recently updated, Content by label,
Attachments, Change history, Contributors, Excerpt include, Include page,
Page properties report, Labels lists, Task report and Page tree are all
the same thing: *a block whose content is the answer to a query, computed
when the page is looked at*. Confluence ships them as twelve macros. Here
they are twelve **kinds** of one node, one endpoint, one fetching node
view and one export snapshot, so the twelfth kind costs what the second
did: a query.

**The decisions that make that true, and why each is the way it is.**

1. **One node: `dynamicBlock { kind, params }`.** An atom block with no
   content (`Node.create({ atom: true })`, `src/web/src/editor/dynamicBlock.ts`).
   `kind` is a string from the catalog; `params` is a flat
   `Record<string, string>`: flat because it travels as a query string,
   strings because the server, not the document, decides what a value
   means. **Nothing the query returns is ever written into the document.**
   A stored copy of "children of this page" is wrong the moment a child
   is added, and a stored copy of "pages with label X" is a permission
   leak the moment a page is restricted. The document holds the question;
   the answer is computed for whoever is asking, each time.

2. **One result shape, not one per kind.** Every kind answers with a
   `BlockResult` in one of three *neutral* shapes, and there is exactly
   one renderer for those shapes in the SPA (`DynamicBlockView.tsx`) and
   one in the exporter (`ProseMirrorRenderer`, the `dynamicBlock` case):

   | shape | for | carries |
   |---|---|---|
   | `list` | Children, Content by label, Labels lists, Page tree, Contributors | `items[]`, each `{ title, href?, subtitle?, children?[] }`: nested for trees |
   | `table` | Recently updated, Attachments, Change history, Task report, Page properties report | `columns[] { key, label }` + `items[]` with `cells{ key → cell }` |
   | `document` | Include page, Excerpt include | `document`: a ProseMirror JSON string of the included content |

   A `cell` is one of `{ text, href? }`, `{ date }`, `{ user }` or
   `{ checked }`; the renderers decide how a date or a user is drawn, once.
   **A kind is therefore a query and nothing else**: no React, no HTML,
   no Markdown. This is what the dev-plan's "eight separate node types
   would be eight times the work" warning was about; the neutral shape
   is the fix. If a future kind genuinely needs a fourth shape, add the
   shape (two renderers) rather than special-casing the kind.

3. **One endpoint: `GET /api/pages/{hostId}/blocks/{kind}?param=value…`.**
   The *host* is the page the block sits on. It is the context for kinds
   that need one (children *of this page*, attachments *of this page*) and
   it is the permission anchor for all of them: the caller must be able to
   view the host, else **404**: the masking rule the rest of the API
   uses. Then the kind runs. Unknown kind → 400; a param that fails its
   kind's validation → 400 with the field named; unknown params are
   ignored (a newer document against an older server should degrade, not
   break). `.AllowAnonymous()`, because a public page's blocks are part of
   the page; the anonymous principal (5.1) falls out of `PermissionService`
   with no extra code. Cache headers match a page read: anonymous
   `public, max-age=60`, signed in `private, no-store`.

4. **Permission filtering is the kind's problem, with one helper to make
   it hard to get wrong.** The rule: **a page the caller cannot view must
   not influence the result at all**, not its title, not its existence,
   not a count that includes it. Every kind that lists pages does the same
   two-pass filter search and the page tree do: narrow in SQL to
   `ViewableSpaceIdsAsync()`, then `CanViewPageAsync` each candidate and
   stop once `limit` *visible* rows are in hand (`BlockContext.VisibleAsync`
   does the loop; kinds call it instead of writing their own). Kinds that
   aggregate (contributors, counts) aggregate over the filtered set.
   Assignee `me` for an anonymous caller is empty, not an error.

5. **Export snapshots at export time, as the exporting user.**
   `ProseMirrorRenderer` stays static and database-free. The export
   endpoint walks the document for `dynamicBlock`s in order
   (`DynamicBlocks.Collect`), resolves each through the same
   `IDynamicBlockService` the endpoint uses (same caller, same filtering)
   and hands the renderer an `IReadOnlyList<BlockResult?>` in document
   order; the renderer pairs the nth block with the nth result, the same
   way it pairs the nth heading with its anchor. A block that failed or
   is unknown renders as a placeholder naming the kind, never as a failed
   export. An exported file is a snapshot and says so: the block's
   `generatedAt` is rendered as a footnote.

6. **`document`-shaped kinds do not recurse.** Include page and Excerpt
   include render the included page's content with *its* dynamic blocks
   as placeholders: depth 1, on both sides. A page that includes a page
   that includes it is otherwise an infinite export and an infinite
   render. On the client this happens for free: the nested read-only
   editor has no host page in `editor.storage` (below), so its blocks
   show "Shown on the page". On the server `DynamicBlocks` is told to
   render the included document with an empty results list.

7. **The node view learns the host page from `editor.storage`, not from
   props.** Node views are constructed by the schema, which is shared by
   every editor instance, so they cannot take React props. The same
   problem the slash menu's Image item had was solved by stashing
   callbacks on `editor.storage.slashCommand`; dynamic blocks do the same
   with `editor.storage.dynamicBlock.getPageId` (`setDynamicBlockStorage`).
   `Editor`/`CollaborativeEditor` set it from a `getPageId` prop: the
   editor passes its draft-aware resolver, `PageView` passes the page id.
   Where nobody sets it (version-history previews, template previews)
   the block renders a quiet placeholder, which is right: history is not
   live.

8. **Params are edited by one generic form.** The client catalog
   (`dynamicBlockKinds.ts`) declares each kind's params as a schema (
   `{ key, label, type: 'select' | 'number' | 'text' | 'labels' | 'page', options?, default }`)
  and `DynamicBlockMenu` renders whichever kind is selected from that
   schema. A new kind gets a form by declaring its params; nobody writes a
   menu. The slash menu and the **+** menu list the catalog, so a kind
   added there appears in both. Client defaults mirror server defaults;
   the server is authoritative and validates.

**All twelve kinds shipped 2026-09-10.** The contract held: no kind needed
a fourth result shape, a renderer change, or any React. Two static container
nodes came with them, for the two kinds that read *content* rather than
rows: `excerpt` (what `excerpt-include` takes) and `pageProperties` (the
two-column table `page-properties-report` collects). Both are plain
containers with no node view, so export renders their contents as ordinary
content. `BlockDocuments` holds the three content readers they share.

**Adding a kind.**

1. Server: a class implementing `IDynamicBlockKind` in
   `Features/Blocks/Kinds/`: `Kind` (the URL name) and
   `RenderAsync(BlockContext)`. Read params through `ctx.Int/Str/Enum`
   (validated, defaulted, capped); list pages through `ctx.VisibleAsync`.
   Register it with `AddScoped<IDynamicBlockKind, …>()` in `Program.cs`.
2. Client: one entry in `DYNAMIC_KINDS` with its param schema.
3. Tests, three per kind: the result for a normal caller; **a leak test**:
   a page restricted from the caller appears nowhere in the result (title,
   count, or child); and an export snapshot containing the rendered rows.

**The kinds, in the order to build them.** Params are `name=default`.

| kind | shape | params | query, and the permission note that matters |
|---|---|---|---|
| `children` *(built with the mechanism)* | list | `depth=1` (1–3), `sort=position` (position\|title\|updated) | Live children of the host, recursively to `depth`; a hidden parent hides its subtree (the tree's own rule). |
| `recently-updated` | table | `scope=space` (space\|tree), `limit=10` (≤50) | Current pages by `UpdatedAt` desc; columns title, updated by (last version's author), when. Over-fetch then filter: the tenth *visible* page may be the fortieth row. |
| `content-by-label` | list | `labels` (required, comma list), `match=any` (any\|all), `scope=space` (space\|all), `limit=25` | Pages carrying the label(s). `all` = every named label present. |
| `attachments` | table |: | The host's attachments: name (download href), size, uploaded by, when. Host-anchored, so no extra filter. |
| `change-history` | table | `limit=10` (≤50) | The host's versions desc: version, author, when, comment. |
| `contributors` | list | `scope=page` (page\|tree) | Distinct version authors, most versions first; over the *visible* pages of the tree. `subtitle` = "n edits". |
| `include-page` | document | `page` (required, page id) | The page's current content, if the caller may view it: else the block is empty with the standard placeholder, **not** an error that names the page. Depth 1 (decision 6). |
| `excerpt-include` | document | `page` (required) | The content of the first `excerpt` node on that page (a static `block+` container node, added with this kind, rendered as a subtle frame in the editor and as nothing in export). Same visibility rule as include-page. |
| `page-properties-report` | table | `labels` (required), `limit=25` | Pages with the label whose content has a `pageProperties` node (a static container around a two-column table, added with this kind); columns are the union of first-column keys, cells the second column's text. |
| `labels` | list | `mode=page` (page\|popular\|related), `limit=20` | `page`: the host's labels; `popular`: labels by visible-page count in the space; `related`: labels co-occurring with the host's. Counts over visible pages only. |
| `task-report` | table | `scope=tree` (tree\|space\|all), `assignee=any` (any\|me\|user id), `status=open` (open\|done\|all), `limit=25` | `taskItem` nodes with the Wave C `assigneeId` attr, walked from candidate pages' current content in-process after the visibility filter. Fine at wiki scale; note in the kind that a jsonb containment prefilter (`@> '{"type":"taskItem"}'`) is the first optimization if it ever is not. |
| `page-tree` | list | `root=host` (host\|space), `depth=3` (1–6) | The visible tree under the host or the space, same filtering as `/api/pages/tree`. |

**Naming.** Kinds are kebab-case in URLs and documents; the client
catalog's display titles are Confluence's ("Children display",
"Recently updated"…) so a Confluence user finds what they expect.

### Roles and administrators (spec, dev-plan 0.1, designed 2026-09-08)

> **Update 2026-09-09:** group management (create/edit/delete/membership)
> and reading the audit log are administrator operations; group listing
> stays open to any signed-in user for the permission picker. In the SPA,
> Groups and Audit are Admin tabs and API tokens live on the profile.

> **Update 2026-09-20 (dev-plan 10.1):** a third role, `Owner = 2`, sits
> above `Admin`. Everything below still holds for administrators; what the
> owner adds is at the end of this section.

Two roles, one enum: `User.Role` is `Member = 0 | Admin = 1`. An enum, not
a bool, so a future `Viewer` or `Moderator` is a new value rather than a
migration of a bool.

**Who becomes admin.** The first account on an empty instance, whether it
arrives via `/register` or via OIDC provisioning, is created as `Admin`.
Registration runs the "is the table empty" check and the insert inside one
serializable transaction so two racing first registrations cannot both win
(SQLite, used by tests, serializes writes anyway). The migration that adds
the column also **promotes the earliest-created user** on existing installs,
so no instance is left with content and nobody able to administer it. On
this dev instance that is the owner's account, not the docs bot.

**The owner (dev-plan 10.1).** `Owner = 2` is the account that owns the
instance. It does everything an administrator does, plus the two things
only it can: change anyone's role, and hand the instance to someone else
(`POST /admin/users/{id}/transfer-ownership`, sudo, audited, and a Critical
alert to every administrator). Exactly one exists at a time:

- The first account on an empty instance is the owner, by the same
  serializable "is the table empty" check as before.
- `OwnerSeed`, a startup step, gives an upgraded instance its owner: the
  longest-standing active administrator, audited as `owner.assigned`. It
  also stamps `SiteSettings.SetupCompletedAt`, so an instance that predates
  the setup wizard (10.2) is never sent through it.
- The owner cannot be demoted, suspended, or assigned: the role endpoint
  refuses both directions with "Ownership is transferred, not assigned",
  and a transfer swaps both roles in one save, so the seat is never empty.
  That is what replaced the old "cannot demote the last administrator"
  rule.
- An administrator cannot act on the owner's account at all: no password
  reset, no revoking its sessions or tokens. Each of those would otherwise
  be a way to take the instance or lock its owner out of it.

Because the ordering is meaningful, every administrative check reads
"`>= Admin`" rather than listing both roles: the policy handler, the
recipients of admin notifications and alert email, the dashboard's
administrator count, the two-factor requirement, and the SPA's nav and
admin shell. `AuthPolicies.RequireOwner` is the second policy, with the
same two-factor rule as `RequireAdmin`.

### Instance rights (dev-plan 11.1)

The tier above is the ordering. What a person may actually *do* is their
**role**: a named set of rights. Three built-in roles ship, one per tier
(User, Administrator, Owner), an instance may add **custom roles** to the
user and administrator tiers (11.2), and `User.RoleId` points at one whose
tier always matches `User.Role`.

- **The catalog is code** (`Infrastructure/Permissions/InstancePermissions.cs`):
  29 assignable rights, each with a key, an area, a label, a description and
  the lowest tier that holds it by default. Three more are **reserved to the
  owner** and never stored as grants: changing tiers, transferring
  ownership, and editing administrator or owner rows. They are added to the
  owner's effective set in code, so no configuration can remove them and no
  owner can lock themselves out.
- **The grants are data**: `Roles` and `RolePermissions`, cached for 30
  seconds by `PermissionCache` and invalidated on every write, the same
  arrangement as `SiteSettingsCache` and for the same reason.
- **Routes name their right.** `RequirePermission("backups.policy")` builds
  a `perm:<key>` policy on demand through `PermissionPolicyProvider`; the
  handler checks the right and, for callers in the administrator tier,
  the two-factor rule from 3.5. `RequireAdmin` is no longer used by any
  route. A test walks the endpoint metadata and fails if an `/api/admin`
  route names nothing, with two documented exceptions: the settings pair
  (the read needs any settings right, the write is checked **field by
  field**, since one request may touch several areas) and the roles routes
  (reaching the matrix is "may see it or may edit any row", so an owner who
  has taken `permissions.view` from their own role can still undo it).
- **Rights are additive over space permissions, never a bypass.**
  `pages.delete_any` says a person may delete other people's pages at all;
  the space's own Edit grant still decides where. Deleting a page checks
  `pages.delete_own` or `pages.delete_any` depending on who wrote it.
- **Anonymous readers** get exactly what the built-in User role holds, so a
  visitor is never more privileged than a member. That is how `pages.export`
  reaches the public export route.
- **API tokens follow their owner's role.** Withdrawing `tokens.use` makes
  existing tokens fail authentication rather than deleting them, so granting
  it back restores them. MCP rides on tokens, so it is covered.
- **Promotion** is its own right, `users.promote_admins`, off for
  administrators by default. It only promotes: demoting an administrator
  stays with the owner's reserved `roles.assign_tier`, so two
  administrators cannot unmake each other. One route,
  `PUT /admin/users/{id}/role`, is reached by either right, so it is
  checked in the handler rather than by a route policy, and the
  endpoint-metadata test lists it as an exception.
- **Who may edit what.** An administrator may shape user-tier roles
  (`permissions.edit_user_tier`); only the owner may touch administrator or
  owner rows. Every save is sudo, audited as a diff
  (`permissions.changed`), and raises `permissions.expanded` when a role
  gains rights: Critical for an administrator row, Warning for a user-tier
  role gaining an administration right.
- **`RoleSeed`** creates the built-ins at startup and attaches every account
  to one. It never edits a role that already exists, so an owner's changes
  survive a restart.
- **Custom roles** (11.2) are the same rows with `Key = null` and
  `BuiltIn = false`. They start as a copy of another role of their tier,
  can be renamed and deleted, and are refused deletion while anyone holds
  one, so nobody is ever left pointing at a role that is gone. Creating one
  needs the right that edits that tier, and copying from a higher tier
  needs the right that edits *it*, so an administrator cannot mint a
  user-tier role holding administrator rights. Nothing about a custom role
  changes a tier, which is why the promotion rules above are untouched by
  it: assigning within a tier needs `users.assign_roles`, crossing tiers is
  still 10.1's promotion or demotion.

**How the role is checked.** `CurrentUser.IsAdminAsync()` reads the row
(one indexed primary-key lookup, cached for the request) rather than
trusting a claim. A role claim would be stale until the next sign-in;
reading the row means a demotion takes effect on the demoted user's very
next request. When `SecurityStamp` lands (dev-plan 1.1) the check can move
to a claim validated against the stamp, until then, the lookup is the
correct and cheap answer. `RequireAdmin` is an authorization policy over
that check; `/api/auth/me` returns `role` so the SPA can show admin
navigation.

**Deleting a space** (11.3) is the one action whose gate is not the ordinary
sudo window. `DELETE /api/spaces/{key}` carries the space key typed back and
the password in the same request, and verifies the password itself through
`AuthEndpoints.VerifyPasswordOrCodeAsync`, which is the reauth path's own
logic: a locked account is refused, and a wrong answer counts toward lockout.
The two answers catch two different mistakes. The key proves the right space
is on screen, which is the error people actually make; the password proves
deliberateness, which "you signed in four minutes ago" does not. An account
with no password (provisioned through SSO) answers with a one-time code.

Unlike `/auth/reauth`, this does **not** refresh the sudo window: proving the
password here buys this one deletion and nothing else, so a sudo action right
afterwards still asks. That is the conservative direction, and deliberate.

The right, `spaces.delete`, says a person may destroy spaces at all, not that
they may destroy any particular one: a space they cannot view is 404, and
reaching it means recover-access first, which is audited. The rows go in one
transaction and the files afterwards, so the failure mode is orphaned bytes
rather than a half-deleted space; `space.deleted` in the audit log keeps the
key, name, counts and bytes that the deleted rows no longer can, and the
Critical alert fires whoever did it. Two things have no foreign key to cascade
from and are deleted by hand: `CollabDocuments`, keyed by the page id as a
string, and `Watches`, which name their target by type and id. Notifications
are deliberately left: they are a person's own history, and a link to a
deleted page 404s gracefully.

The collaboration sidecar has to be told, in its own way. Its store hook
writes only when the page still exists and closes the document's connections
when it does not, and a 15-second sweep closes connections for any open
document whose page is gone, which is what reaches an editor that is open but
idle. Polling rather than a push from the API keeps the sidecar's only inbound
surface the websocket.

**Admins do not silently bypass permissions.** This is the decision the
plan left open, and the answer is Confluence's own: a site admin sees
exactly what their grants allow, like anyone else. What they have that
others don't is a **recover-access** action,
`POST /api/admin/spaces/{key}/recover-access`, which writes them an
explicit `SpaceOperation.Admin` grant on that space. From then on the
existing rules apply unchanged: an explicit space admin can view and edit
the space and is not blocked by page restrictions (that rule already
exists in `PermissionService`). Recovery is audited as
`space.access_recovered` and, once dev-plan 3.3 exists, raises a security
event visible to every other admin. The reasons:

- A silent bypass lets any admin read any team's private space and leaves
  no trace. Explicit recovery gives the same safety valve with a record.
- It reuses the permission logic that already exists instead of adding a
  second "unless admin" branch to every check: fewer places to get wrong.
- Revoking the grant afterwards returns the admin to normal, which a
  silent bypass could never offer.

**What admins can see without recovering access:** metadata, not content.
The admin Spaces page (dev-plan 2.4) lists every space (key, name, owner,
counts, archived, public) through admin-only endpoints that never return
page content. Search, the page tree and page bodies stay permission-checked
for admins exactly as for members.

**Instance-level operations** (`/api/admin/*`: settings, users, spaces
listing, recover-access, later the security page) are gated by
`RequireAdmin` alone; they are about the instance, not about any space's
content.

**Tests the implementation must include:** first registered user is Admin
and the second is Member; first OIDC-provisioned user on an empty instance
is Admin; a Member gets 403 on an admin route; an Admin gets 404 (not 403,
per the masking rule) on a private space they hold no grant for; after
recover-access they can read it, a `space.access_recovered` audit row
exists, and revoking the grant restores the 404; the existing suite still
passes: several tests register two users in sequence, so assert nothing
about them changed except the first one's role.

### Avatars (`components/Avatar.tsx`, dev-plan 1.2)

Every user has an avatar from the moment they register, with nothing stored:
`Avatar` renders an inline SVG of their initials on one of twelve backgrounds,
chosen by an FNV-1a hash of their id. Not a sum of char codes: user ids are
hex GUIDs, which share an alphabet and a length, exactly the case where a weak
hash clusters. The twelve colors all carry white text at 4.5:1 or better
(measured), and all are dark enough to read on both the light and dark page
grounds, so a generated avatar needs no per-theme treatment.

`User.AvatarVariant` records an explicit pick from the twelve; null means
"derive it from the id". It is stored as an index rather than a color so the
set can be restyled later without rewriting rows, and it survives an upload,
so removing a picture returns to the color the user chose rather than to the
derived one.

An uploaded picture always wins over a variant. Uploads are cropped to a
square in the browser before being sent, using `createImageBitmap`, which
decodes off the main thread and honors EXIF orientation, without it a
portrait phone photo arrives sideways. The crop is not cosmetic: the server
center-crops too (0.4), so cropping here is what makes the stored result match
what the user was shown. It is also downscaled to 512px first, so a 12MP phone
photo is not uploaded whole to produce a 256px thumbnail.

`avatarIdentity.ts` holds the colors and helpers, separate from `Avatar.tsx`,
which exports only the component: React Fast Refresh needs component-only
modules, and the linter enforces it.

**Where avatars appear today:** the topbar and the profile page. Comments and
version history return only an `AuthorId` and do not render author identity at
all yet, so avatars there wait until they show names. The user directory
(`GET /api/users`) does carry `avatarHash`/`avatarVariant` already, for the
admin users list in dev-plan 2.2: the existing people picker is a `<select>`,
whose options cannot contain markup, so it cannot show them.

### Session revocation: the security stamp (dev-plan 1.1)

A cookie scheme is stateless by design: the cookie *is* the proof, so nothing
on the server can normally take it back before it expires. `User.SecurityStamp`
is what makes revocation possible. It is issued into the cookie as a claim at
sign-in and compared against the stored column on every request, in the cookie
handler's `OnValidatePrincipal`. Rotating the column therefore invalidates
every outstanding cookie for that account on its **next request**, not at the
cookie's next expiry.

Changing a password rotates it, which is the point of changing a password you
believe someone else has. The session that made the change is re-issued with
the new stamp, so the person doing it is not signed out along with everyone
else. Suspension (dev-plan 2.2), admin force-logout (3.3) and 2FA enrollment
(3.5) all reuse this one mechanism rather than adding their own.

The same validation also rejects a cookie whose account has become
`Suspended` or been deleted, so those take effect immediately too.

Two consequences worth knowing:

* **Cookies issued before the stamp existed carry no claim and are rejected**,
  which signs everyone in once on deploy. That is the safe direction: treating
  a missing claim as valid would mean a pre-existing cookie outliving the
  password change meant to kill it.
* **API tokens are unaffected**: they authenticate through a different scheme
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
stays in environment variables: those are secrets and topology, fixed before
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
second replica would keep its copy until the TTL expired: the short TTL is
the bound on that staleness.

The SMTP password is encrypted with ASP.NET Data Protection, whose keys
already live in this database (`DataProtectionKeys`), so a database restore
stays self-consistent. It is **write-only over the API**: responses carry
`smtpPasswordSet: bool` and never the value. `PUT /api/admin/settings` treats
every field as optional (an omitted field keeps its stored value, so a caller
can change one setting without clobbering the rest) with one addition for the
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

**`User.LastSeenAt`**: stamped by `LastSeenMiddleware` after authorization,
throttled by the singleton `LastSeenTracker` to at most one write per user per
five minutes. "Active in the last 7 days" needs coarse resolution only, so a
write per request would be a lot of work to learn almost nothing. The update
is by primary key with no prior read, and failures are swallowed: knowing when
someone was last active is never worth failing their request over. The tracker
is in-process, so a restart costs one extra write per user, and it prunes
itself past 10,000 entries.

**Login events**: `user.login` attributed to the account, and
`user.login_failed` attributed to nobody. The failure case deliberately
records neither the user id nor the attempted address: an audit log every
admin can read should not become a list of addresses somebody guessed, and a
failure must not confirm which addresses exist. The per-account counter that
brute-force protection needs is dev-plan 3.2's job, not this log's. Recording
these required `IAuditLogger.RecordAs(actorId, …)`, because sign-in happens
before the request has a principal.

**`PageView`**, one row per read, written after the permission check so a
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
`RemoteIpAddress` is the container address of the proxy: verified on the
running stack, where three requests from two different clients all logged
`172.18.0.7`. The value is recorded anyway so the history exists and becomes
correct the moment 3.0 ships. **Do not build per-IP logic on it before then**:
a rate limiter reading this would see the whole world as one address.

### Profile media (`Infrastructure/Storage/ProfileMedia.cs`, dev-plan 0.4)

Avatars and space icons go through the existing `IAttachmentStorage` under
their own key namespaces (`avatars/…`, `space-icons/…`) rather than a second
storage abstraction, so the S3 implementation that interface reserves a slot
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
trusting the declared content type, which is attacker-controlled, so an SVG
labeled `image/png` must not get through. It is a script-bearing document
format and there is no reason to accept one for a 256px square. The prebuilt
avatars in dev-plan 1.2 are SVG, but this application generates those; it
never accepts one.

**Library choice: SkiaSharp (MIT).** ImageSharp 3.x and later moved to the Six
Labors Split License, which would complicate the Apache 2.0 release dev-plan
8.2 intends; SkiaSharp and its Linux native assets are both MIT. The runtime
container is glibc (Ubuntu 24.04, glibc 2.39) and the package ships a matching
`linux-arm64` build: verified in the container, not just on the build host,
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

## Exports are captured, not regenerated (dev-plan 12)

An export used to be a second rendering of the document: `ProseMirrorRenderer`
walked the ProseMirror JSON in C# and emitted HTML against a fifteen-line
stylesheet. That is why exports looked nothing like the page. Two renderers
drift, and eight of the node types are React node views whose output only a
browser can produce at all.

**PDF and HTML are now photographs of the real page.** The sidecar loads
`/export/pages/{id}`, a route rendering the same `<Editor editable={false}>`
in the same `.paper` under the same `index.css` as the reading view, with no
topbar, sidebar or comments. It waits for the page's own ready signal, and
then prints it or serializes its DOM. There is one renderer, and an export cannot
drift from the page without the page breaking too. Markdown is still rendered
by `ProseMirrorRenderer`, because it is a genuinely different target.

**The ready signal** (`web/src/export/ready.ts`) is two conditions, both
required: nothing known to be outstanding (a Mermaid diagram that has neither
drawn nor failed, a dynamic block still loading, math not yet typeset, an
image in flight, a font not loaded), and then a quiet period with no DOM
mutations at all. The second condition covers work this file has never heard
of, so a node view added later is handled without anyone remembering to teach
it. `data-export-ready` lands on `<html>` and the sidecar waits for it, with
a timeout that captures anyway rather than hanging.

**Print rules live in `index.css`**, beside the screen rules they modify,
under `@media print`. That is the point of capturing: there is one place a
table is styled, and the paper variant is next to it.

**The posture change.** The sidecar used to run with its network switched off
and be handed a self-contained document. It now reaches the app, and nothing
else: a `page.route` allowlist aborts every request that is not this app's
origin or a `data:` URI, and the compose network gives it nowhere else to go.
It still never reaches the internet. One wrinkle worth knowing: Chromium
upgrades a plain-http navigation to https whenever the host is a *name*,
whatever `--disable-features=HttpsUpgrades` says, so the sidecar resolves the
app's hostname to an address and navigates to that. Bare addresses are exempt.

**The render token** (`Infrastructure/Export/RenderTokens.cs`) is how the
sidecar's browser reads the page. An HMAC over `{ user | anonymous, scope,
exp }` signed with `Pdf:SharedSecret`, fifteen minutes, no database row, and
not counted against the token mint limit a fifty-page site export would
exhaust. It yields the exporting user's own principal with a read-only scope
claim, so `TokenScopeMiddleware` already refuses writes and every permission
check downstream is the one that would have run for that reader anyway:
masking is inherited rather than reimplemented. `RenderScopeMiddleware` holds
it to the page or space it was minted for, so one that escapes into a log is
worth almost nothing.

**A space as a static site** (12.2) is the same capture run over every page a
chosen audience may see, written into a zip in Cloudflare Pages shape: one
directory per page mirroring the tree, `assets/site.css` verbatim, real asset
files, internal links rewritten to relative paths, and a link to a page that
is *not* in the export turned into plain text rather than a guaranteed 404.
Those links name `index.html` rather than ending at the directory, so an
export also works opened straight off the filesystem: a server would have
served the index file, and `file://` has nothing that does.
The default audience is **anonymous**, rendered as a reader with no account
through `IPermissionService.AsAnonymous()`, which is the leak-proof choice
for a documentation site: it cannot contain a private page by accident,
whatever the exporter can see.

**The chrome around an exported page** (`Features/Export/SiteChrome.cs`) is
the one thing in Phase 12 that is *built* rather than captured, and the
exception is deliberate. The capture route has no chrome by design, so there
is nothing there to photograph; and the two pages a site needs that are not
captures at all, its index and its 404, need exactly the same chrome. One
builder covering all three beats a React version plus a C# version of the
same bar. It is also the half of an export that is not content: which pages
are in the site, what they are called, where they live and which one you are
on are all the exporter's own answers.

What stops it drifting is that it emits the *application's* class names
(`.topbar`, `.brand`, `.space-layout`, `.sidebar`, `.tree`, `.theme-menu`)
and ships the application's compiled stylesheet, so restyling the app
restyles every export with nothing to keep in step. Only the markup's shape
and a few icon paths are duplicated, and each says where it was copied from.
One rule an export needs for itself: the app hides `.sidebar` under
`--bp-mobile` because `.space-actionbar` covers its jobs, and an export has
no actionbar, so `.space-layout--export` brings it back stacked above the
page. A PDF gets none of this: it is paper.

The only JavaScript in the output is the theme script, which applies the
stored theme and accent before first paint and then drives the appearance
menu (three modes, six accents) through `data-theme-*` hooks, because the
React component's own behavior cannot survive a capture that strips scripts.
The wordmark is the instance name, which is the half of instance branding
that already exists; a replaceable mark is the other half, and
`SiteChrome.Brand` is where it will arrive.

The **width toggle** is the app's own, moved: the reading view keeps it in the
page action bar, which an export does not have, so it sits in the top bar.
The page's stored `fullWidth` decides the width the file opens at, the script
drives the class on `[data-export-width]` afterwards, and the reader's choice
persists per export origin. A PDF gets neither the control nor the column
cap, because the sheet is the width.

Two more things the export draws differently from the app, both on purpose. The
space's generated tile is the theme accent rather than one of the twelve
per-space colors, because those exist to tell spaces apart in a list and an
export is one space; the app is untouched. And the PDF footer's page
numbering is a single flex item, because as separate items `space-between`
spread "1 of 4" across the whole page.

## Backups that leave the machine (dev-plan 9.2)

Phase 9.1 made backups visible; 9.2 makes them survive the machine. Three
target types, at most one of each, declared in `.env` as `OFFSITE_CLOUD_*`,
`OFFSITE_NAS_*` and `OFFSITE_REMOVABLE_*`.

**Why the credentials are in `.env` and not the admin page.** Data Protection
keys live in the database, so a storage key held there would travel inside
every backup along with the means to decrypt it: each backup would carry the
key to delete itself. The sidecars read `.env`; the app never holds a value
and has no code path that could return one. What reaches the database is a
row per target with secrets reduced to a 16-character fingerprint, which is
what the Storage targets screen renders.

**Two tools, split by what each is for.** pgBackRest carries the *database*
to the cloud slot as `repo2`, with WAL streaming continuously so
point-in-time recovery exists off the box. restic carries the *uploads and
the logical dumps* to every slot, encrypted client-side, which is what means
the dumps do not leave in plaintext. A slot therefore holds up to two
repositories, which is why `BackupTargets` is keyed on `(Slot, Kind)`.

**A NAS is not a pgBackRest repository.** `archive_command` runs in the `db`
container, where nothing can check that a share is really mounted, and an
unmounted share is an ordinary empty directory that pgBackRest would happily
fill on the boot disk. SFTP is the documented recipe for PITR on a NAS,
because a connection failing is an error rather than a silent local write.

**The configuration is a generated file, never environment variables.**
pgBackRest rejects a variable that is defined but empty and Compose cannot
omit one, so passing `PGBACKREST_REPO2_*` through would stop WAL archiving on
every instance with no offsite target. `deploy/pgbackrest/offsite.sh` writes
a drop-in under `config-include-path` instead, from both the `db` container
and the sidecar. The `db` container needs an entrypoint wrapper for this,
written so a bad backup target can never stop the database starting.

**Path targets are bind mounts guarded by a sentinel.** A network share or a
drive is mounted on the host and bind-mounted in; the sidecar refuses to
write unless `.tesria-backup-target` is present on the far side. An unmounted
share leaves an empty directory behind, and backing up into it would not
fail, it would quietly fill the boot disk. A Docker `cifs` volume was
rejected because it stops the container starting while the share is down,
which would take the *local* backups with it. `Present` is kept separate from
`Enabled` because for a drive they are different questions: absent is normal
for a drawer and a fault for a NAS, and only the NAS alerts.

**What a dead remote costs, which is more than it looks.** WAL is
acknowledged to Postgres only once every repository has it, so an unreachable
`repo2` backs up `pg_wal` until `archive-push-queue-max` trips, and the
segments dropped then are gone from the *local* repository too. So an
unattended cloud outage can cost point-in-time recovery locally, not just
offsite. The archive-gap alert therefore fires on a backlog of three
segments, long before the limit, and pgBackRest is pinned at 2.59.1 because
before 2.59 that limit silently did not work while archive-push was erroring.

**The drill is the only check that answers the real question.** `restic
check` asks whether a repository is internally consistent. The drill
(`deploy/backup/offsite-drill.sh`, monthly) pulls the newest dump back out,
loads it into a throwaway database and counts the tables. A copy can pass the
first and fail the second, and one that fails the second is worth nothing,
which is why a failed drill is a *critical* alert and outranks everything
else on the card: a backup that fails is noticed, while one that quietly will
not restore looks healthy until the morning it is needed.

## What the backups page charts (dev-plan 9.3)

One pie per **filesystem**, not per agent: both sidecars normally write to
the same disk, and drawing it twice would double its free space. They are
grouped by the filesystem each agent reports (`df --output=source`), and an
agent that has not reported one is left out rather than assumed to share,
since assuming would be the double count again.

**Free space is measured through a host bind mount, not the container's own
volume.** Under Docker Desktop that volume sits on a sparse virtual disk
which reports the size it may grow to: on a Mac with 700GB free it will
claim 1.7TB. A bind mount is passed through the host's filesystem, so `df`
on one reports the host's real figures, and both sidecars already mount
their scripts that way. On a Linux host the two are normally the same
filesystem and this changes nothing. The distinction matters because the
warning exists to fire *before* the disk fills, and the sparse number would
have fired late or never.

The other numbers, what the backups occupy and what the live wiki occupies,
are `du` on the agent's own paths, taken on a ten-minute clock rather than
every heartbeat.
`df` and `du` are two commands moments apart and will disagree; `df`'s free
space is treated as authoritative and the backups are fitted into what is
used, so the slices always sum to the disk rather than to more than it.

The low-space warning is **two backup sets**, not a percentage, because a
percentage means the wrong thing at both ends of the range this product runs
on. Cloud storage has no free space at all, so its card charts composition,
the database repository against the files one, and never invents a
denominator.

`components/PieChart.tsx` is the one pie in the product, extracted from the
editor's chart node so that both draw the same thing and there is no chart
library.

## Instance branding (dev-plan 13.1)

An owner can give the instance its own name, logo, favicon and colors.
Every default reproduces Tesria, and one projection, `BrandView.From`, is
what the SPA, the page shell and the exports all read, so they cannot
disagree about what "branded" means.

**The branding arrives before any script runs.** A forced theme that arrived
with the JavaScript bundle would be a flash of the wrong colors on every
load, so the server writes the branding into the page it sends. Every
application route, `/` included, is served by one endpoint
(`Features/Public/SpaShell.cs`), which replaced the static-files fallback.
It writes the tab title, the favicon links, a `<style id="brand-accent">`
block and attributes on `<html>`: `data-theme-lock`, `data-accent-lock`,
`data-accent-default` and `data-brand-*`.

**The inline script never changes per instance.** The CSP allows the theme
bootstrap in `index.html` by its SHA-256, computed at startup from the file
on disk. Writing a color or a lock into that script would change its hash
and silently stop it running. So the script reads the attributes instead,
and a test renders a fully branded shell from the real `index.html` and
compares the script byte for byte.

**A custom accent is two colors and a derivation.** `AccentColors` works in
OKLCH, where "the same hue, lighter" is a straight line. It derives the hover
color, two tints, a border tint, and the text color on buttons (chosen by
measured contrast) from one color per mode, and checks the WCAG 4.5:1 rule
the six built-in accents meet. Colors are stored only as normalized
`#rrggbb`, because they are written into a stylesheet on every page. The
owner may keep a color that fails the check. The admin page offers the
nearest passing shade, and the audit entry records the override.

**An uploaded SVG has two defenses, and either would do alone.**
`SvgSanitizer` rebuilds the file from the parse, keeping only allowlisted
elements and attributes, and `url()` only when it points inside the file. It
refuses DTDs, entities and anything unparseable. Independently, a branding
SVG is only ever displayed through `<img>`, where it cannot run script or
fetch anything, and it is served with `default-src 'none'; sandbox` so that
opening its URL directly is inert too. Exported files, which have no CSP at
all, rely on the `<img>` rule, which is why `SiteChrome` never inlines a
logo. Raster images are re-encoded by SkiaSharp, keeping their shape within
1024×256, as lossless WebP. Favicons become 32, 180 and 512 pixel PNGs, drawn
from a sanitized SVG by Svg.Skia in this process.

**Names.** The brand name is its own field. The instance name keeps the
places that identify the installation: the tab title (`Instance - Space /
Page`, by `BrandTitle` and its SPA twin `title.ts`), link previews, email,
the authenticator entry, and the export footer. The two title
implementations share their test cases.

**The image.** Svg.Skia brought HarfBuzz, and the publish was
framework-dependent with no runtime identifier. That copied every native
library for about fifteen platforms into a Linux image. The Dockerfile now
publishes for the build's own architecture, which took the app image from
1,070 MB to 455 MB.

## Restoring the wiki, from the page that took the backup (dev-plan 9.4)

Everything above is about *taking* backups. This is the one thing that spends
one, and it is shaped almost entirely by a single fact.

**The job status lives in the thing being replaced.** `BackupJobs`,
`BackupAgents`, `SiteSettings`, the sessions, the audit chain and the
data-protection keys are all rows in the database a restore replaces. The old
`restore.sh` dropped that database outright, so a restore run from the page
would have destroyed the record of itself, and the page watching it would have
read the restored past as the present. Three rules follow.

**1. Restore beside, then swap.** The dump is restored into `<db>_restore`
while the wiki stays up and readable, verified there, and swapped in with two
renames inside one `psql` session. Connections are *refused* before they are
terminated, because the application reconnects within milliseconds and
terminating alone would race the reconnect. The database it replaces is renamed
to `<db>_pre_restore` rather than dropped, which is the undo; the attachments
move to `.pre-restore/` inside the same volume, so that swap is renames too and
a crash leaves both copies rather than half of each.

**2. The sidecar remembers what the database cannot.** Every restore has a
directory on the sidecar's own volume (`/backups/restores/<jobId>/`) holding
the request, the phases, the log and a CSV export of the tables that describe
the *disk* rather than the wiki. Those are re-imported afterwards, because a
dump from last week would otherwise make the page forget every backup taken
since, including the safety one this restore just took.

**3. The application restarts itself.** A restored database may predate a
migration, carries no grants for the runtime role (`--no-privileges`), and has
settings this process has cached. Startup already does all of that in the right
order and is the path every deploy exercises, so `RestoreCompletion` polls the
job and calls `StopApplication` rather than re-running those steps at runtime.
It also writes `backup.restored` at startup, keyed on `LastRestoreJobId` and
skipped when an entry for that job exists, so any number of restarts produce
exactly one record, in the restored database's own chain.

**Maintenance is held in two places on purpose.** `SiteSettings.RestoreJobId`
survives the application restarting mid-restore and is what the sidecar can
see; `RestoreState`, a singleton, is what the application can still read during
the seconds of the swap and the minutes of a point-in-time restore, when the
database cannot answer at all. `MaintenanceMiddleware` reads the singleton,
refuses every unsafe method with 503 and a body the SPA turns into an overlay,
and lets the restore's own endpoints through so they can give their precise
refusals instead of a blanket one.

**The collab sidecar has to forget.** An open editor holds its document in
memory and writes it back on the next keystroke, which after a restore would
put post-backup content into the restored wiki. The application posts
`/maintenance` and the sidecar closes every connection and drops every
document; the flag expires on its own after thirty minutes so a restore nobody
finished cannot switch co-editing off for ever. The backstop, for a restore run
from the runbook instead, is the existing sweep: it compares
`SiteSettings.LastRestoredAt` with its own start time and exits, because
restarting is the only reliable way to forget everything at once.

**Point-in-time recovery inverts the ownership.** pgBackRest writes into the
data directory with Postgres stopped, and stopping Postgres needs to happen
inside the `db` container: a sidecar would need the Docker socket, which no
sidecar gets. So `deploy/db/entrypoint.sh` supervises its own database. It
starts the image's entrypoint as a child, forwards signals to it, and polls one
directory on the `pgsocket` volume, which `db` and the `pgbackrest` sidecar
share and nothing else does. **That volume is the authorization model**: the
web tier has no mount and no path to that file, so a restore can only be asked
for by the sidecar that validated the job. Everything needing judgment stays
in the sidecar (bounds, safety backup, WAL switch, carry-across); the
supervisor stops, runs what it was handed, and starts again, ignoring a request
it has already handled or one older than ten minutes so a stale file cannot
restore twice.

Interposing also let the container shut down properly: Postgres reads SIGTERM
as a *smart* shutdown and waits for every client to disconnect, so
`docker compose stop` used to wait out its timeout and get killed. The
supervisor forwards SIGINT instead, which is the fast shutdown.

**The gate** is in `Features/Admin/RestoreEndpoints.cs` and is the substance of
the feature: a right only the owner holds by default, the label typed back, the
password in the request rather than the sudo window, a safety backup that
cannot be skipped, one at a time, and a Critical alert to every administrator
afterwards. The kept copy ages out under the retention policy, treated as a
backup taken at the moment of the restore.

## Wiki packs: a space that outlives its instance (dev-plan 8.5)

A site export (12.2, above) is for people; a **pack** is for Tesria. It is the
same space as documents rather than as pages: `Features/Export/WikiPack.cs`
holds the format, and two endpoints sit on either side of it, one exporting a
space into one and one reading a space back out.

The two properties the format is built for both come from the reason it
exists. The manual was lost with the database it lived in, so a pack has to
be something a repository can hold: **output is canonical**, with sorted JSON
keys, a fixed entry order and 1980 timestamps in the zip headers, so the same
space always packs to the same bytes and a one-word edit diffs as a one-word
edit. And it has to be readable by an instance that has never met this one,
so **every id in it is a join key and nothing more**: an import mints its own.

What a pack does *not* carry is the part worth knowing. Neither space
permissions nor page restrictions travel, because their principals are ids on
another instance and carrying them by name is how a stranger with the right
display name ends up with access; the manifest records that restrictions
existed, as counts, and the import says so in words so somebody goes and sets
them. `IsPublic` does not travel, because publishing is 5.5's two-step opt-in
and a zip file does not get to take it. Authorship does not travel either:
every row on the target belongs to the importer, and the original authors are
kept as display names in `authors.json` and in the import's audit event.
Display names only, no addresses, because this file gets committed.

An export walks pages one at a time through `CanViewPageAsync`, the same way
12.2 does, so a pack cannot contain a page its exporter could not open; the
manifest counts what was left out without naming it. Unlike a site there is
no page cap, because there is no per-page render to pay for. The zip is
spooled to a temporary file opened `DeleteOnClose` rather than written
straight to the response: `ZipArchive` writes synchronously and finishes on
Dispose, and Kestrel refuses synchronous writes.

An import treats the archive as hostile and the database as all-or-nothing.
`WikiPack.Read` checks the format version before it interprets anything else,
matches entry names against the shapes the format defines (so zip slip fails
by not being on the list rather than by being detected), and enforces the
entry-count and uncompressed-size ceilings while reading. Everything after
that lands in one transaction, with attachment and icon bytes written to
storage first and swept up again if it does not commit, because a
half-imported space is worse than none. Page documents go through
`PageContent.TryNormalize`, the one door every stored page passes through, and
an attachment's content type is re-derived from its bytes exactly as an upload
is. Pages and versions are saved in two steps inside that transaction, since a
page points at its current version and a version points at its page.

`PackRewriter` is the pure half, and has fixture tests for the same reason
8.6's diff does: its mistakes are all quiet. It maps page, attachment and
comment ids into the content, and where the pack refers to something the
target does not have it keeps what a person wrote and drops the
machine-readable half: a mention loses its `userId` and keeps its label, a
task keeps its assignee's name, a comment mark with no comment behind it is
removed rather than left coloring text that answers nothing. A link to a page
that is not in the pack is left exactly as it was, which is the opposite of
the site export's rule, because on a re-import into the same instance it still
resolves and a link that 404s honestly beats one quietly redirected.

## Writes from outside the editor (dev-plan 8.6)

A page has two stores: the published version in Postgres and the Yjs document
in `CollabDocuments` that the editor actually edits. Anything that writes the
first without touching the second (the API, `PageWriter`, the MCP
`update_page` tool) used to be invisible to an open or saved draft, and the
next Update wrote that draft back over it.

Such a write is now treated the way a second person's typing is: it lands in
the document, visibly, and the human decides. `editor/externalEditMarks.ts`
holds the two marks (`externalInsert`, `externalDelete`) and the commands
that resolve them; `editor/externalEdits.ts` holds the block-level diff that
produces them and the Yjs write that applies it. Neither mark can reach a
published version: `PageContent.TryNormalize` strips both, on the way in,
for every door into a page write.

**The sidecar shares the editor's schema rather than restating it.** It needs
one to turn stored JSON into Yjs, and a second copy would silently drop any
node added to `extensions.ts` and forgotten here. `vite.schema.config.ts`
bundles `getSharedExtensions` plus `externalEdits.ts` into one headless ESM
file, built inside the collab image, with `yjs` and `y-prosemirror` left
external because two copies of Yjs in one process do not share types.

**The write path tells the sidecar**, after the commit, at
`POST /pages/{id}/reconcile` on the sidecar's own port, guarded by the
`Collab:Secret` the two already share (`ICollabNotifier`). It is best effort
with a short timeout: the page is saved by then, and a sidecar that is down
only means the reconciliation waits for the document's next load. The sidecar
applies it with `openDirectConnection` **only if the document is already
open**, so a page nobody is editing is not loaded into memory just to be
edited.

The source is decided by how the caller authenticated, never by what it says:
a browser session is `editor`, an API token is `api`, and the same token at
`/mcp` is `mcp`. An `editor` write records the version and draws nothing,
because publishing and then carrying on typing is ordinary and a diff would
strike through the words the human is still writing.

**`meta.version`** is a `Y.Map` in the shared document naming the page version
that document was last reconciled to. The client seeds it on first open; the
sidecar compares it on load and reconciles on mismatch. A document with no
`meta.version` is adopted rather than reconciled, which is what keeps every
draft that predates the feature from lighting up on the first load after a
deploy.

**Publishing is a decision.** The editor runs `acceptExternalEdits` as the
last thing before sending, so pressing Update accepts whatever is pending;
the banner above the editor (counting pending runs, with Accept all and
Reject all) is for deciding first. It also sends `baseVersion`, the
`meta.version` its draft was reconciled to, and the API answers **409** if
the page has moved past it. That 409 carries the page as it stands, because
the editor reconciles against the body to show the difference. `baseVersion`
is optional: API and MCP callers hold no draft that could be stale, so
last-write-wins stays right for them.

Two things worth knowing if you touch the diff. It compares blocks by
canonical JSON *after* putting both sides through the schema, because
ProseMirror materialises default attributes and stored JSON does not, so a
direct comparison marks everything as changed. And the Yjs write goes through
y-prosemirror's `updateYFragment`, the minimal-change applier the
collaboration extension already uses, so untouched blocks keep their CRDT
identity and other people's cursors survive.

## Frontend (`src/web`)

- Unit tests (`npm test`, vitest) cover **logic, never rendering**. The block
  diff in `editor/externalEdits.ts` is the first and the shape of the rest:
  a pure function with edge cases a browser walk cannot cover honestly.
  Components, routing and layout deliberately have none, because a
  mounted-and-asserted component passes while the real page is broken, which
  is the failure this repo has actually shipped. Those get a live walk (see
  CLAUDE.md).


- React 19 + TypeScript, built with Vite.
- In development, Vite serves the SPA on `:5173` and proxies `/api` to the API
  on `:5291`, so the frontend always uses same-origin relative URLs: identical
  to production.
- In production, `npm run build` output is copied into the API's `wwwroot`
  during the Docker build.
- Routing is React Router 7; a typed `api/client.ts` wraps all REST calls and
  an `AuthContext` holds the session.

### Asking before a destructive action (`components/ConfirmDialog.tsx`)

No native `confirm`, `prompt` or `alert` is used anywhere in the SPA, and
none should come back. A browser is free to refuse a native dialog: in an
embedded one (a desktop app's pane, a WebView, a preview) `confirm` returns
false and `prompt` throws, and either way the guarded action never runs
with nothing on screen to explain the silence. That is exactly how Resolve
on the security page came to look dead.

**Grep for the bare spelling, not just `window.`** Most call sites in this
codebase were written as `confirm(...)`, not `window.confirm(...)`, and a
search for the qualified form found five of thirteen and looked conclusive.
The check that works is `(^|[^.\w])(confirm|prompt|alert)\s*\(`.

`useConfirm()` returns an `ask(request)` that resolves true only on
confirmation, and the `dialog` node to render. Because the question is JSX
rather than a string it can carry the specifics that make the answer
obvious: how many accounts still hold the role being deleted, how many
pages and attachments a publish is about to expose. Escape cancels, a
second question answers the first "no" rather than stranding its promise,
and unmounting resolves rather than hanging. It shares the
`.recovery-prompt` shell with `ReauthDialog`, which is the other modal the
admin pages put in front of a consequential action, and the two compose:
confirm first, then the server asks for the password.

### Responsive layout

Two breakpoints, documented as CSS custom properties in `index.css`'s
`:root` (`--bp-mobile: 640px`, `--bp-tablet: 1024px`): CSS can't read a
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
the full-width table breakout math (see the Editor section below): change
it in one place, not both, or they drift apart and a full-width table
overflows the viewport by the difference.

### Theming (`theme.ts`, `components/ThemeToggle.tsx`)

Light/dark/system, expressed to CSS as a `data-theme` attribute on `<html>`:
absent means "system" (the `prefers-color-scheme` media query decides),
`light`/`dark` are explicit overrides. index.css defines the light palette on
`:root`, the dark palette twice, once inside `@media (prefers-color-scheme:
dark)` guarded by `:root:not([data-theme="light"])`, once under
`:root[data-theme="dark"]`, which is what lets an explicit choice win in
both directions.

The accent color is a second, independent axis on the same mechanism: a
`data-accent` attribute driving every `--primary*` token. Each accent is
defined twice (light and dark), never derived: the contrast requirement pulls
the two in opposite directions. Note that `:root[data-accent="x"]` ties on
specificity with the dark base `:root:not([data-theme="light"])`, which is why
per-accent blocks exist for *every* accent including the default: a
higher-specificity dark block has to exist for each, or an explicit accent
choice would pull the light palette into dark mode.

`index.html` carries a small inline, synchronous script that re-applies the
stored preference before first paint; a deferred or module script runs too
late and the page visibly flips. **The storage key and attribute logic are
duplicated between that script and `theme.ts`: change them together.**

Every color resolves through a custom property. `--surface` (raised: cards,
`.paper`, popovers, the topbar, inputs) is separate from `--bg` specifically
because they are identical in light mode and must differ in dark. Two
deliberate exceptions: the code block keeps its own dark palette in both
themes, and content colors the *author* chose (a `tableCell`'s
`backgroundColor` attr, a `highlight` mark's `color`) are stored in the
document and cannot be re-themed without discarding that choice, so dark mode
pins dark ink on those elements rather than restyling them. Panel icons are
`mask-image`, not `background-image`, so one `--panel-icon` token per type
re-tints them instead of needing a second set of data URIs.

### Editor (`src/web/src/editor`)

TipTap v3 (ProseMirror) provides the block WYSIWYG. Documents are stored as
ProseMirror JSON in `PageVersion.ContentJson`.

- **`extensions.ts` is the single source of truth for the schema** (node/mark
  types): both `Editor.tsx` (single-user, and read-only rendering via
  `editable={false}`) and `CollaborativeEditor.tsx` (Yjs-backed) import from
  it rather than declaring their own extension list. This matters because Yjs
  requires every collaborator to share one exact ProseMirror schema: any new
  node/mark type is added here, once, never inline in either editor component.
- **Custom node views** (`CodeBlockView.tsx`) render a React component in
  place of a node: used for the syntax-highlighted code block's language
  picker/copy button/line-number gutter.
- **Floating menus** (`@tiptap/react/menus`'s `BubbleMenu`): `LinkMenu.tsx`
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
  toggle): fixed-position overlays that track proximity to each `<table>`
  in the document (not DOM ancestry, since the buttons render outside the
  table's own DOM), sharing one hover/selection-tracking hook,
  `useHoveredTable.ts`. `TableControls` uses `TableMap.positionAt()` (from
  `@tiptap/pm/tables`) to translate a clicked row/column index into the
  right ProseMirror cell position before running the standard add/delete
  row/column commands. Table width itself lives on the `table` node as
  `width` (px) and `layout: 'default' | 'full-width'` attrs (`extensions.ts`,
  same `.extend()`-and-disable-the-stock-one pattern as `CodeBlock`):
  independent of column-border dragging (stock `prosemirror-tables`,
  unaffected) and of the page's own full-width setting (`Page.FullWidth`).
  **Gotcha:** prosemirror-tables' `TableView` (active whenever a table is
  `resizable`, which is always, in both edit and read-only rendering) only
  applies a node's rendered `style`/`data-*` attributes once, in its
  constructor: its own `update()` (used for every subsequent attribute
  change on an already-mounted table) recalculates the colgroup but never
  re-touches them. Schema `renderHTML` alone is only correct on a fresh
  mount (a page load, an export); anything that changes a table's attrs live
  (`TableWidthControls`) must also apply the same DOM effect directly right
  after the transaction commits, or the change is invisible until the next
  reload.
  On touch/no-hover input (`matchMedia('(hover: none) and (pointer:
  coarse)')`, not viewport width, a touchscreen laptop at desktop width has
  the same problem a phone does), `useHoveredTable` switches its reveal
  trigger from mouse proximity to "does the current selection sit inside a
  table," since hover doesn't exist there, tapping to place the cursor is
  the natural touch equivalent.
- **Table cell backgrounds** (`TableCellMenu.tsx`): Confluence's per-cell
  chevron, in the top-right of whichever cell holds the cursor, opening a
  "Background color" palette. Cursor-driven rather than hover-driven, so
  deliberately *not* sharing `useHoveredTable` with the two controls above:
  the menu belongs to the cell being edited, not whichever one the mouse
  passed over. The color is a `backgroundColor` attr on both `tableCell` and
  `tableHeader` (`extensions.ts`, via a shared mixin, same
  `.extend()`-and-disable-the-stock-one pattern as `Table`), and unlike the
  `table` node's `width` a plain inline `style` is safe here: `TableView`
  rewrites only the table's own width and colgroup, never cell styles, so
  there's nothing to clobber it. The Cell/Row/Column scope buttons widen the
  written rect via `TableMap.cellsInRect()` and apply every cell in one
  transaction, rather than moving the user's selection to a `CellSelection`
  and calling `setCellAttribute`: the cursor stays where it was.
- **Panels** (`panelExtension.ts`): Confluence-style callouts. `panelType`
  is exactly ADF's own set (`info`/`note`/`warning`/`success`/`error`);
  Confluence's legacy Info/Tip/Note/Warning macros map onto it, with the old
  Tip macro being today's `success`, so no sixth type is needed. The
  type-specific color and icon live in `index.css` (`.panel--*`) keyed off
  the rendered `data-panel-type`, which keeps the icon a `::before`
  pseudo-element, ProseMirror owns this node's children and would fight an
  injected element, and means read-only rendering gets the icon with no node
  view to mount. `PANEL_TYPES`/`PANEL_LABELS` are exported so the toolbar
  popover and the slash menu can't drift apart.
- **Structural blocks** (dev-plan Phase 7 Wave A): table of contents,
  expand, status, date, decision and layouts, each one node type in
  `extensions.ts` with a matching case in `ProseMirrorRenderer`.
  - **Heading ids are derived, not stored.** `headingAnchors.ts` slugifies a
    heading's text (lower-case, non-alphanumerics collapsed to hyphens,
    duplicates suffixed `-2`) and applies the result as a ProseMirror *node
    decoration*, so it is recomputed from the document on every change and
    never serialized into the saved JSON. Storing ids instead would survive
    a rewording but would also duplicate on paste, drift between Yjs
    collaborators and need a migration for every existing page. The price of
    deriving is that the rule exists twice: here and in
    `Features/Export/HeadingAnchors.cs`, because an exported file has to
    resolve the same `#slug` a saved link points at. `HeadingAnchorTests`
    pins the two together; change one, change both.
  - **`tableOfContents` stores nothing.** Its node view reads the headings
    live and the exporter re-derives them at export time, so the document
    never carries a stale copy of its own outline. Both sides build the same
    tree: each heading nests under the nearest shallower one before it.
  - **Layout sections stack but never nest.** `layoutSection` is deliberately
    *not* in the `block` group, and the only thing that admits it is the
    document itself: `getSharedExtensions` disables StarterKit's Document
    and registers `Document.extend({ content: '(block | layoutSection)+' })`.
    That single line is what stops a section appearing inside a panel, an
    expand or another column, with no per-node guards anywhere. Column
    widths are percentages applied as flex-grow weights (`--column-width`),
    so the browser shares out the gap and the numbers need not total 100.
    **Gotcha:** a section's own width (centered/wide/full) reuses the page's
    `--page-pad` breakout, which is also what a full-width *table* uses, so
    that table rule is scoped to direct children of the content root
    (`.editor__content > .ProseMirror > …`), or a full-width table inside a
    column would bleed out of the column instead of filling it.
  - **Status and date never let document data reach a style attribute.** A
    status stores a color *name* out of a fixed set (the palette lives in
    `index.css` and, inlined, in the renderer); a date stores an ISO
    calendar date and is formatted per reader. Dates are parsed by hand
    rather than with `new Date(iso)`, which reads a bare date as UTC
    midnight and shows the day before to anyone west of Greenwich.
  - **Bubble menus for inline atoms re-select their node.**
    `selectedNode.ts` exists because `updateAttributes` rewrites the node's
    markup and a `NodeSelection` does not survive that: it collapses to a
    text cursor, which would close the very menu doing the editing on every
    keystroke.
- **Mentions, and the permission seam they needed** (dev-plan Phase 7 Wave
  C). The `mention` node stores the user's id *and* a snapshot of their
  display name; `Infrastructure/Mentions` reads the ids straight out of the
  saved document, so there is no mention table to fall out of step with the
  content. On save, `PageEndpoints` notifies everyone mentioned now who was
  not mentioned in the version being replaced.
  **Gotcha:** capture the previous content *before* setting
  `page.CurrentVersionId`: EF's navigation fix-up repoints
  `page.CurrentVersion` at the new version, and the diff would then compare
  the content against itself and never notify anyone.
  Each recipient is checked with `IPermissionService.AsUser(userId)` before
  anything is queued, because the notification carries the page title and a
  mention must not become a way to leak the title of a restricted page.
  `PermissionService` was written entirely against the request's own
  identity; `AsUser` adds a `_asUserId` override and every rule now reads a
  single `UserId` property, so an evaluation for someone else cannot fall
  back to the caller's rights. It returns a fresh instance because the
  principal cache is per-user.
- **All three suggestion plugins need distinct `pluginKey`s.**
  `@tiptap/suggestion` defaults to one shared `suggestion$`, so a second
  plugin throws "Adding different instances of a keyed plugin" at editor
  construction and the whole editor fails to mount. The slash, mention and
  emoji suggestions each pass their own key; anything added later must too.
  Everything else about them is shared (`editor/suggest/`): positioning,
  scroll tracking and outside-click dismissal all come from Suggestion's own
  managed `mount()` API.
- **Text color stores a name, highlight stores a hex** (dev-plan Phase 7
  Wave B). The asymmetry is deliberate. A highlight is a *background*: dark
  mode keeps the text on it readable by pinning the ink (`[data-theme="dark"]
  … mark[style*="background-color"]` in `index.css`), so the background
  itself can be any hex and survive export with no stylesheet. Colored
  *text* has no equivalent escape: a hex dark enough to read on white is
  invisible on the dark background, and CSS cannot lighten a color it
  cannot see. So `textColorMark.ts` stores one of eight color *names*,
  `index.css` re-points them per theme (`--text-color-*`), and the export
  renderer inlines the light-theme value. The same property that makes it
  theme-aware also makes it injection-proof: no value from the document ever
  reaches a `style` attribute.
- **Indent is an attribute, not a wrapper.** `textFormatting.ts` adds
  `textIndent` to the same block types `TextAlign` is configured for: keep
  the two lists in step. A wrapper node would have to be nested N deep and
  would fight list lifting. Both the editor and `ProseMirrorRenderer`
  recompute the `margin-left` from a clamped 0–4 integer rather than echoing
  the stored value. `clearFormatting` deliberately avoids `clearNodes()`:
  unwrapping a list, panel or layout column is a structural edit, not a
  formatting one.
- **`.toolbar--bubble` must paint its own surface.** Since the one-row
  toolbar rebuild, `.toolbar` is a transparent, full-width row that lives
  inside the page action bar (which supplies the background). Any floating
  copy of it (the selection bubble, the image hover bar, the layout bar)
  therefore needs its own background, border and padding, and has to undo
  `.toolbar`'s `nowrap`/`width: 100%`. Shipping one without that makes a
  menu you can see the page through.
- **Color palettes** (`palette.ts`, `ColorPalette.tsx`): the swatch grid is
  shared by the highlight dropdown and the cell-background menu; only the
  tiers differ (highlight drops the bold tier, which doesn't hold `--text`
  legibly). Values are Atlassian's own light/medium/bold palette, matching
  the fixed palette Confluence offers instead of a hex input. They're stored
  *in the document* (a cell attr, or the `highlight` mark's `color`, hence
  `Highlight.configure({ multicolor: true })`), not as CSS classes, so they
  survive export and read-only rendering with no stylesheet. The export
  renderer whitelists them to plain hex before they reach a `style`
  attribute (`ProseMirrorRenderer.IsSafeCssColor`), since document JSON is
  stored as given and an unvalidated color would be CSS injection into
  exported HTML.
- **`ToolbarPopover.tsx`** is the always-visible popover trigger (highlight
  palette, panel picker). Not to be confused with `ToolbarDropdown.tsx`,
  which looks similar but exists *only* as the mobile collapsed form of a run
  of buttons and is `display: none` above `--bp-mobile`.
- **The slash command menu** (`slash/`) is a custom `Suggestion`-based
  extension (the same primitive `@tiptap/extension-mention` is built on):
  there's no pre-built importable slash extension. Positioning, scroll/resize
  tracking, and outside-click dismissal are handled by `@tiptap/suggestion`'s
  own managed `mount()` API (Floating UI-based), which meant no separate
  positioning library (e.g. tippy.js) was needed.
- **Inline comments**: a `comment` mark (`commentMark.ts`) highlights a text
  range and links it to a real `Comment` row via a `commentId` attr; images
  can't carry marks, so an image comment has no in-document highlight.
- **Draft/publish**: a new page gets a real (invisible) `Page` row (
  `Status = PageStatus.Draft`, reusing an enum value that existed unused
  since Phase 2) the moment the editor mounts, via `POST /pages/draft`. This
  gives image uploads (which need a real page id) somewhere to attach to
  before the user has saved anything. `POST /pages/{id}/publish` makes it
  real (fires the normal "page created" audit/notification/webhook side
  effects, exactly once: a retried publish is a safe no-op) and mutates the
  existing version 1 in place rather than creating a confusing empty-v1/
  real-v2 pair. The global EF Core query filter on `Page` excludes drafts
  (`Status != PageStatus.Draft`), matching the existing soft-delete filter
  pattern; permission checks already used `IgnoreQueryFilters()` for
  trash/restore, so they resolve drafts correctly with no extra code.

### Page tree drag-and-drop (`components/PageTree.tsx`)

The page tree, rendered identically in the desktop sidebar and the mobile
`SpaceHome` inline copy (same component, same data, see Responsive layout
above), is the only way to reorder or reparent pages once created; there is
no separate "move" dialog. Dragging only happens in **Reorder mode**,
toggled per-tree-instance by a compact icon button next to the "📑 Pages"
heading (`PageTree.tsx`'s `editMode` state): a flat pencil (`PencilIcon`,
same stroke-icon language as the editor toolbar and the topbar bell) when
off. Outside it, rows are plain `StaticRow` links with no dnd-kit hooks and
no `touch-action` override at all, not just visually inert, structurally
incapable of starting a drag. This exists because the first version made
every row a drag source all the time: on mobile, `touch-action: none`
(needed so a touch-drag isn't raced by the browser's own scroll gesture)
meant any swipe that happened to start on a page title reordered it instead
of scrolling the list, which was exactly backwards.

Reorder mode is a **batch edit**, not one-drag-one-save: drags apply to a
local draft tree (`draftTree` state, seeded from the `tree` prop and frozen
against further prop updates until the session ends: see the `useEffect`
guarded on `!editMode`) and nothing reaches the server until an explicit
**Save**; **Cancel** discards the draft and never sends a request at all.
This replaced an earlier single-toggle "Done" button that committed each
drag immediately: reparenting a page is very often the first of several
related moves, and re-entering Reorder mode before each one made that
workflow tedious. Each completed drag both updates `draftTree` (via
`applyMove`, a pure function that removes the dragged node, with its
subtree intact, and reinserts it under the new parent at the new index,
letting the existing `flatten()` recompute correct depths for the whole
moved subtree for free) and appends `{pageId, parentPageId, index}` to a
`pendingMoves` queue. **Save** replays that queue as sequential
`PUT /api/pages/{id}/move` calls, in the order the moves were made, each
against whatever the server now holds. That ordering guarantee is what
makes replay safe without needing to diff the draft against the original
tree: every intermediate state Save produces is one the draft itself
already passed through, and validated a parent choice against, while the
user was dragging, so replaying in the same order converges to the same
tree. A failure mid-replay aborts the remaining queued moves, surfaces an
error, and refetches the tree so the UI reflects however far Save actually
got, never a state the user hasn't seen. Rows aren't links while
editing (`DraggableRow` renders a `<div>`, not a `NavLink`): mid-batch, a
stray click on a row would otherwise navigate away and abandon whatever
hasn't been saved yet.

Within a single Reorder session, a page row is itself the drag source (no
separate handle icon: dnd-kit's `distance: 4` activation constraint tells
a click from a drag). Dragging it up or down reorders it among siblings;
dragging it horizontally while over another row changes its nesting depth,
reparenting it. Rather than live-shuffling the rest of the list, a line
shows where the row would land, a 2px line with an 8px circular terminal
bleeding 4px past its own left edge, matching Atlassian's own drop-indicator
spec
([atlassian.design/components/pragmatic-drag-and-drop/design-guidelines](https://atlassian.design/components/pragmatic-drag-and-drop/design-guidelines)),
with the line's left offset (`marginLeft`) doubling as the nesting-depth
indicator. An earlier version used an always-visible grip-icon handle with
live-reordering; both the extra element and the nested flex row it required
turned out to be the source of a mobile layout-overflow regression, so the
row-is-the-handle + static-list-plus-line design fixed both the UX
complaint and the bug at once.

Built on `@dnd-kit/core` + `@dnd-kit/sortable` (chosen over
`react-beautiful-dnd`/`react-dnd` for native touch support via Pointer
Events, so the same code drives both the mouse-driven desktop tree and the
touch-driven mobile one: no separate touch handling). The tree is
flattened to `{id, parentId, depth}` for the drag session; `project()`
derives the dragged row's new depth from horizontal drag distance, clamped
between the row above's depth+1 (can't skip a nesting level) and the row
below's depth (can't leave a gap): the standard "sortable tree" projection
technique. The dragged row's own descendants are excluded from that working
list for the duration of the drag (computed against `draftTree`, so this
still holds correctly across several drags in one session, not just the
first), so a subtree can never be dropped inside itself; the backend's
cycle check (`WouldCreateCycleAsync`) is the backstop, not the only guard.

`PUT /api/pages/{id}/move` takes `{ parentPageId, index }`, where `index` is
a slot among the destination's *current* siblings (0 = first), not a raw
`Position` value the frontend has to compute or guess at. The endpoint
resolves that group, inserts the moving page at `index`, and renumbers the
whole group's `Position` densely (0..n-1) in one pass, so a drag can never
collide with or leave a gap relative to its new neighbors.

## Real-time collaboration (`collab/`)

A small **Node + Hocuspocus/Yjs** sidecar provides simultaneous editing. The
editor engine is JS-only, so this is the one piece deliberately kept outside the
.NET app (PLAN §1) rather than reshaping the main stack.

- **Authorization.** The sidecar cannot evaluate the permission model, so the API
  is the gatekeeper: `GET /api/pages/{id}/collab-token` checks the caller may
  *edit* the page and returns a short-lived HMAC-signed token bound to that page
  id. The sidecar only verifies signature, expiry, and that the document being
  opened matches, so a forged token, or a valid token replayed against another
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
  schedule, via the `backup` sidecar.
- **In-app safety nets**: page version history + rollback, and soft-delete/trash
  with restore.
- **Offsite**: not implemented. Dev-plan 9.2 holds the research and the
  decisions it waits on.

### The admin section and its contract with the sidecars (dev-plan 9.1)

**The database is the contract.** The two backup sidecars already connect as
the database owner (they must, to dump and to archive), so they report there
instead of through a new port, secret or shared volume. The app reads what
they write through its least-privilege role and never touches a backup file:
plaintext dumps and the encrypted repository stay out of the web process.
There is no download endpoint.

| Table | Written by | App role | Holds |
|---|---|---|---|
| `BackupAgents` | each sidecar, one row (`logical`, `physical`) | read-only | heartbeat, `NextRunAt`, interval, tool version, disk, `WalArchivedAt` (from `pg_stat_archiver`), the policy it last applied, `PolicyObservedAt` |
| `Backups` | the sidecar that owns them | read-only | the inventory, mirrored every minute from the disk or `pgbackrest info`; rows are never deleted (`RemovedAt` + `RemovedReason` = `retention` or `missing`); `LastVerifiedAt`/`LastVerifyOk` |
| `BackupJobs` | the app appends `requested` rows; the sidecars append their scheduled runs and own every update | append-only | the queue and the run log: status, error, `ResultJson` (what was produced and removed), `LogTail` |

The vocabulary columns are text, not integer enums, because bash writes them
(`BackupNames` in `Domain/Backup.cs` holds the strings). The policy lives on
`SiteSettings` (`BackupRetentionEnabled`, `BackupKeepCount`, `BackupKeepDays`,
`BackupPolicyChangedAt`). A null `BackupPolicyChangedAt` means no policy, and
the sidecars remove nothing; `BackupPolicySeed` sets it on the first start
after upgrading, from `BACKUP_RETENTION_DAYS`.

**The sidecars** share one loop, `deploy/backup/common.sh`, mounted into both
at `/opt/tesria/common.sh`; `deploy/backup/run.sh` and
`deploy/pgbackrest/run.sh` supply the parts specific to each. Every
`BACKUP_POLL_SECONDS` (60) a sidecar reads the policy, syncs its inventory,
claims `requested` jobs (`FOR UPDATE SKIP LOCKED`) and runs a scheduled backup
when `NextRunAt` has passed. A background loop writes the heartbeat and touches
`/tmp/heartbeat` for the compose healthcheck, so a long backup does not look
like a dead agent. Nothing runs under `set -e`: a failure is a job row, retried
after 15 minutes, doubling to at most 6 hours. JSON is parsed by Postgres
(neither image has `jq`), and listings reach psql through a file and `\copy`,
since a long history outgrows a command-line argument. Before the app has
migrated (the tables do not exist) a sidecar backs up on the interval and
removes nothing.

- *Logical:* a **cycle** is one stamp shared by the dump and its uploads
  archive (`BACKUP_STAMP`). Files from before 9.1 were stamped separately;
  an archive with no exact match joins the latest dump up to five minutes
  before it. Orphaned `*.tmp` files older than an hour are removed after a
  successful backup.
- *Physical:* full or incremental is decided from the inventory (a full when
  the newest is `BACKUP_FULL_EVERY_DAYS` old), not from a counter in memory,
  which used to reset on every restart and take a full each time.

**Retention.** A backup is kept if it is one of the newest *N* or started
within the last *D* days, and removed only when outside both. Disabled keeps
everything. The rule exists twice, in `BackupRetention.Plan` (C#: the preview,
the tests) and in `common.sh` (SQL: what deletes); change one, change both.
Logical removes whole cycles. Physical runs pgBackRest's native
`expire --repo1-retention-full=K` with `K = max(N, fulls within D days)`;
`pgbackrest.conf` sets `repo1-retention-full=9999999` so the automatic expire
after each backup removes nothing.

**The grace period** is enforced by the sidecar, not the app, because the app
role can write `SiteSettings`. A saved policy stricter than the one an agent
applied (pruning turned on, a smaller *N* or *D*, or none applied yet) waits
24 hours from when that agent first sees it, restarting if the policy changes
again. Until then the agent keeps anything either policy keeps. Looser
policies apply at once. The app only displays the resulting time
(`BackupRetention.EffectiveAt`), and saving a stricter policy needs sudo and
raises `backup.retention_reduced` to every admin.

**Alerts.** `BackupMonitor` (every 5 minutes) computes the same status the page
shows (`BackupStatus`) and raises the `backup.*` kinds in the table above
through `ISecurityDetector`. While an unresolved alert of the same kind for the
same agent exists, it raises no new one.

## Decisions

- **.NET + React** over a single-language stack: strongest backend reliability
  and data tooling, which suits the data-safety priority. The one gap (
  real-time co-editing, whose ecosystem is JS-native) is deferred to Phase 5
  and will be isolated in a small Node/Hocuspocus sidecar rather than reshaping
  the main stack.
- **Same-origin SPA hosting** (API serves `wwwroot`) keeps deployment to a
  single app container and avoids CORS in production.
