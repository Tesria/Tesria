# Changelog

All notable changes to Tesria are recorded here.
Format loosely follows [Keep a Changelog](https://keepachangelog.com/).

## [Unreleased]

Development builds now say **0.7.0-dev**: 0.6.0 is released, so what comes
after it is the next minor version.

## [0.6.0] - 2026-09-24

The first public release: Tesria's repository opens, with its docs at
[tesria.com/docs](https://tesria.com/docs). The entries below are the full
record; these are the highlights.

**Upgrading from 0.5**

- `APP_DB_PASSWORD` is now required in `.env`: Compose will not start
  without it. It was already set on any instance that followed the README.
- The stack's network now has a fixed address range, so the upgrade needs
  the stack recreated once: `docker compose down`, then
  `docker compose up -d --build`. **Never `down -v`**, which deletes the
  wiki. If `10.203.0.0/24` clashes with a VPN or your network, set
  `TESRIA_SUBNET` first.
- Keep the database names your `.env` already has. The example now suggests
  `tesria`, for new installs only.

**Highlights**

- **Hardening:** the database owner's password left the app (a one-shot
  migrate service holds it); live editing ends the moment someone's access
  does; an optional allowlist for where pictures may come from; every new
  account is audited; the two-factor challenge works once; forwarded
  addresses are trusted only from the stack's own network; reset emails go
  out in the background; the version is shown only to signed-in people.
- **Real visitor addresses** for sign-in limits, alerts and the audit log:
  through Tailscale, and under Docker Desktop on a Mac or Windows with one
  setup command. Refused-request alerts now say what was refused and by
  which browser.
- **Administration:** an About tab (the version, every dependency with its
  license, and an on-demand check for known vulnerabilities), and an API
  tokens tab with what people's tokens and AI assistants have been doing.
- **Exports** show their progress as they run.
- **Docs:** the whole manual at tesria.com/docs, including a section for
  developers; single sign-on is labeled beta until people report on their
  providers.

### Real addresses for Tailscale visitors (2026-09-24, Opus 5.5)

Everyone who came in through the Tailscale sidecar was recorded as the
sidecar's own address. Tailscale's Serve names the tailnet visitor in
X-Forwarded-For (replacing whatever the visitor sent), but Caddy believed
that header from nobody. The sidecar now has a fixed address on the
stack's network (`TESRIA_TAILSCALE_ADDRESS`, default 10.203.0.250), Caddy
trusts X-Forwarded-For from that address only, and Caddy passes the app a
single address, the visitor it worked out (`header_up X-Forwarded-For
{client_ip}`), so the app's one-hop trust is unchanged. Verified live: a
visit over the tailnet was recorded with the visitor's tailnet address, a
visit through the network with its own address, and a forged
X-Forwarded-For was ignored on both paths.

### Refused-request alerts explain themselves; real addresses behind Docker Desktop (2026-09-24, Opus 5.5)

A "spike of denied requests" alert from 192.168.65.1 could not be
explained: it counted refusals without recording them, and that address is
Docker Desktop's gateway, which every device (the Mac, a PC, a phone)
shares when Tesria runs under Docker Desktop.

- **The alert says what was refused:** the most-refused paths (ids folded
  into `{id}`, query strings never kept), how many were 401 or 403, how many
  carried a session cookie, an API token or neither, and which browsers sent
  them ("Chrome on Windows"). Kept in memory for the alert's five minutes,
  bounded per address and in total.
- **A shared address is recognized** (`Proxy:SharedClientAddresses`, by
  default Docker Desktop's gateway): its alerts say so, the card offers no
  Block button, and the server refuses to block any range that covers it,
  which would lock every device out.
- **Real addresses behind Docker Desktop** (`deploy/docker-desktop/`): a
  small Node forwarder on the host takes ports 80 and 443 and hands each
  connection to Caddy with the visitor's address in front (the PROXY
  protocol); a compose override moves Caddy to ports only the host can
  reach; the Caddyfile believes that line only from `PROXY_PROTOCOL_FROM`,
  loopback by default, so nothing changes without the override. Tested live
  on the owner's Mac: a request to the Mac's network address was recorded as
  that address instead of 192.168.65.1. Found in testing: a port published
  on 127.0.0.1 arrives from the stack's own gateway, not Docker Desktop's, so
  the override trusts `TESRIA_SUBNET` as well.
- **One command turns it on or off:** `install-macos.sh` (a launchd login
  item) and `install-windows.ps1` (a Task Scheduler task and a firewall
  rule), each with an undo. Installed on the owner's Mac, and verified on
  the owner's Windows PC: a sign-in attempt from the Mac was recorded with
  the Mac's own address. The first Windows version ran in a visible console
  window that closing would have stopped; the task now runs hidden from
  startup. The forwarder's "am I being
  run" check split its path on `/` and would have done nothing on Windows;
  it now compares resolved paths. Docs: *Real visitor addresses with Docker
  Desktop*, and the two settings in the configuration reference.

### The docs pack is a release download, not a committed file (2026-09-24, Opus 5.5)

Every export of the docs added a 17 MB zip to every clone (six in a day).
At the owner's request the pack leaves the repository: `docs/site/*.zip` is
ignored, and the new `scripts/docs/release-docs.sh` uploads the pack and the
static site to a GitHub release. With no argument it refreshes the standing
**docs** pre-release, which always holds the latest export; with a version
(`release-docs.sh v0.6.0`) it attaches them to that release, now step 4 of
Making a release.

Also: the screenshot harness measured a cropped shot's boxes before the
full-page capture made the window as tall as the page, so on a page that
lays out by window height the marks and the crop landed about 50 pixels off
(the Google and Microsoft sign-in pictures, whose button was cut off). A
cropped shot now takes the page's height first, and the window is put back
after.

### The last "confluence" database names (2026-09-24, Opus 5.5)

`.env.example` now suggests `tesria` for the database owner and name
(new installs only: an existing install keeps what its `.env` has, and the
file now says so). The point-in-time-recovery self-test read
`POSTGRES_USER`/`POSTGRES_DB`, which its container never has, and fell back
to `confluence`: it worked only on instances that kept the old names. It
now reads `PGUSER`/`PGDATABASE`, which Compose gives it, and fails clearly
without them; verified with a passing run. The development fallback
connection string uses `tesria`.

### Single sign-on is labeled beta (2026-09-24, Opus 5.5)

The owner has no identity provider to test against, so single sign-on
(OIDC) ships as a beta: the docs say so where it is set up and listed, ask
administrators to keep a password account, and ask them to report how it
went with their provider in a GitHub issue.

### The Support space is now Docs (2026-09-24, Opus 5.5)

Renamed at the owner's request to match where it will be published,
tesria.com/docs (tesria.com/support is now where people support Tesria).
The space is **Docs** with the key `DOCS`; the key was changed in place
(one SQL update on the owner's instance, since the app cannot change a
key), so page ids, history and watches are unchanged, and the publisher
rewrote every page's links. The exported pack now imports as `DOCS`, and
the site's title reads "Tesria - Docs". Wording that named "the Support
site" now says "the docs", in the pages, in the app (the mail provider,
email settings and Tailscale cards) and in the repository. The shared
`site()` helper now keeps an existing space's name and description in step
with its script, which also updated the Tesria Demo space's description.
The tooling was renamed to match, at the owner's request ("lets just do it
right"): `scripts/docs/` with `publish-docs.sh` and `export-docs.sh`,
`DOCS_SHOTS` for retaking pictures, the pack at `docs/site/docs-pack.zip`
(the static site beside it, still not committed), and `DocsPage` for the
mail presets' page title. Earlier entries below were updated to the new
paths so their commands still work; their wording is left as it was.

### 14.3 Enterprise hardening (2026-09-24, Opus 5.5, reviewed by Fable 5.1)

Every Must fix and Should fix from the known gaps in `docs/security.md`:

- **The database owner's password left the app** (gap 10). A one-shot
  `migrate` service applies migrations, fills in the audit chain and
  provisions the least-privilege role, then exits; the app and the
  collaboration service start after it and hold only the app role. In
  production the app refuses to start with migrations pending, and refuses
  to run as the owner.
- **Live editing ends when access does** (gap 11). A save that changes
  someone's access (suspension, sign-out, password change, role, group,
  space permission, page restriction) asks the collaboration service to
  close the connections it touches. The editor reconnects with a new token,
  and if the app refuses one it says why: signed out or no longer allowed,
  or the page is gone. Tokens last ten minutes (were 30) and expired
  connections are closed.
- **Pictures can be limited to listed hosts** (gap 2): Administration,
  Settings, Images, off by default. The CSP, exported pages (as a `<meta>`
  CSP) and the editor all follow it.
- **Every new account is audited** as `user.registered`, with how it was
  made (gap 3).
- **The two-factor challenge works once** (gap 6).
- **Forwarded headers are trusted only from this stack's own subnet**
  (gap 7): the Compose network has a fixed subnet, `TESRIA_SUBNET`.
- **Password reset emails are sent in the background** (gap 12), so the
  answer takes the same order of time whether or not the address has an
  account.
- **The version is shown only to signed-in callers** (gap 1): health,
  instance info and the OpenAPI document.
- **The egress guard reads IPv4 inside NAT64 and 6to4 addresses** (gap 14),
  and refuses a few more reserved ranges.
- Administration, About: the support card now points to
  [tesria.com/support](https://tesria.com/support) (GitHub Sponsors and
  Ko-fi) instead of Patreon.

**Upgrading.** `APP_DB_PASSWORD` is now required in `.env`; Compose will not
start without it. The network change needs the stack recreated once:
`docker compose down`, then `docker compose up -d`. **Never `down -v`**,
which deletes the database. If `10.203.0.0/24` clashes with a VPN or your
LAN, set `TESRIA_SUBNET` first.

**Found while verifying.** Hocuspocus's own way of closing a connection only
closes the document over a socket that stays open. The browser then stops
being accepted but still shows "Live" and never asks for a new token. The
collaboration service now closes the socket too, which makes the editor
reconnect.

**Model trial record.** Opus 5.5 designed this item; Fable 5.1 reviewed the
design before it was built, at the owner's request, and caught things the
Opus design had missed or got wrong. The plan moved the seeds into the
migrate step and did not make the app refuse to start with migrations
pending. It named a trigger that does not exist ("tokens revoked") and
missed group, role, password and space-permission changes, and it gave the
editor no way to tell "reconnecting" from "refused". It hid the version in
two places and left it in the OpenAPI document, and claimed the reset timing
would be identical. It kept used two-factor challenges in memory, where a
restart would reopen them. It did not warn against `down -v`, nor check
carrier NAT and other reserved ranges beside NAT64, and it left exports
out of the image rule. Every correction was folded in before building;
they are in `docs/dev-plan.md` under 14.3.

### 14.1 Pre-release audit complete, not released (2026-09-24, Opus 5.5)

- **History rewritten** before an outside review: every commit and the
  `v0.5.0` tag carry the owner's GitHub no-reply address; a personal
  address and a NAS path were scrubbed from old versions of documents; old
  versions of the Support pack were removed from history (a clone is 33 MB
  of history instead of about 200). A fresh clone was scanned for personal
  details, credential values and secret-shaped strings: none outside test
  fixtures. Anyone with an older clone must clone again.
- `CLAUDE.md` split: conventions stay, owner-specific notes move to a
  gitignored `CLAUDE.local.md`.

### 14.1 The second security review, fixed (2026-09-24, Opus 5.5)

Three reviews of everything built since the 2026-09-09 review, run in
parallel (accounts and administration; exports, packs and public reading;
MCP, collaboration, backups, egress and deployment). Fixed:

- **Mail credentials followed the server** (high). Changing the SMTP host,
  port or encryption kept the saved password and the Gmail or Microsoft
  sign-in, so a stolen administrator session could point mail at its own
  server and receive them. A move now forgets both.
- **A token could reset its own account through Administration** (high):
  the account guard covered `/api/auth` but not the admin actions on
  oneself. Refused with `token_not_allowed`.
- Only the owner can change the public address that emailed links, reset
  links included, are built from (`owner_only`).
- Reactivating a suspended administrator, and clearing the owner's lockout,
  follow the same protection as suspending.
- Login checks a password against something even for an unknown address, so
  it no longer answers faster for one; a closed instance says "by
  invitation" before it says an address is taken; invites are limited to 30
  an hour per person, a malformed address is refused before the invite is
  made, and only people who may see the user list learn that an address
  already has an account.
- **Single sign-on did not last past the first page**: its cookie lacked the
  session and security-stamp claims every request checks. It now signs in
  exactly as a password does. (Not exercised against a real provider.)
- Pack exports leave out the words of deleted comments; pack imports refuse
  a JSON file over 32 MB and one file named by two attachments.
- Site exports spool to a temporary file instead of memory; the PDF service
  captures at most three pages at once (`PDF_MAX_CONCURRENT`).
- Render tokens are signed with a key only the app holds, made at startup,
  instead of the secret the PDF service also has; verified export captures
  no longer share the anonymous rate limit.
- Live content and the MCP label tools check that a page is readable (not
  someone else's draft, not in the trash), as REST does. The admin activity
  list hides a tool's error text along with the title of a page the viewer
  may not read.
- The backup service refuses a restore time or backup label that is not
  exactly the shape the app writes before it reaches pgBackRest's command
  line.
- Token usage is kept after a token is revoked (reported by the owner: the
  tab read zero once the test tokens were gone). Migration
  `TokenUsageOutlivesToken`.

Recorded in `docs/security.md` as known gaps instead: the database owner's
password in the app's environment, live-editing connections that outlive a
permission change, reset-email timing, single-instance state, and NAT64.
Dependencies: `scripts/audit.sh` clean. Tests: `SecondReviewTests` (7) and
additions to `AdminTokenTests`.

### Phase 17: developer docs on the Support site (2026-09-24, Opus 5.5)

- **Developers**, a section at the end of the Support site, holding REST API
  and MCP and four new parts: **Code examples** (list, search, write a page,
  change it safely with `baseVersion`, label it; in JavaScript and Python
  with nothing to install, from `docs/examples`, both run against a live
  instance before publishing), an **API reference** generated from the
  OpenAPI document at publish time (every request, by area), **How Tesria is
  built** (five pages, two Mermaid diagrams), **Contributing** (five pages)
  and **Project documents** (code of conduct, governance, getting help).
- The repository gains `CONTRIBUTING.md`, `CODE_OF_CONDUCT.md`,
  `GOVERNANCE.md` and `SUPPORT.md`, matching those pages.
- Fix: the README's "Local development (without Docker)" could not work
  (the database container publishes no port, and the fallback connection
  string still used the project's old name). It now describes the Docker
  loop.
- `WRITING.md` gains the rule that every page says how to reach the screen
  it names.

### Administration: an API tokens tab (2026-09-24, Opus 5.5)

Asked for by the owner: "I have no idea who has created tokens and how
often they are in use", then "keep an eye on what agents are doing", then
that it deserves its own tab.

- **Administration, API tokens**: every token on the instance with its
  owner, name, first characters, read-only or full access, last use (when
  and from which address), requests in all, the last 7 days split into API
  requests and assistant tool calls with how many changed something, and
  expiry. Cards for the week and two 30-day charts above it; a filter by
  person or token. Needs `users.view`; **Revoke** one token needs
  `users.manage`, with the same protection as the Users tab (the owner's
  tokens only by the owner, another administrator's only with the right the
  owner gives). The owner of a revoked token is told in the bell and by
  email; the audit log records `token.revoked_by_admin`.
- **What assistants did**: every MCP tool call, logged by a filter around the
  tools (`McpActivity`): tool, whose token, page or space, success or the
  error. Search text is not kept. A page is named only to an administrator
  who may read it. Kept 90 days, and kept after a token is revoked.
- Counting: a day row per token (`ApiTokenDays`, upserted in one statement),
  REST counted after the response so a refused change is a request and not
  a change; `ApiToken.UseCount` and `LastUsedFrom`. Pruned hourly after 90
  days. Migration `TokenActivity`. People see their own tokens' request
  counts on their profile too. Five tests.
- Support: a page for the tab, with pictures made from example data (the
  harness's `mock`), so no real account appears.

### Support: every page says how to get where it sends you (2026-09-24, Opus 5.5)

The owner: pages assumed the reader knew where the screens were.

- The API tokens page starts with **Where to find them** (your picture at
  the top right, then the API tokens card, with a picture of each) and has
  **Revoking a token** step by step with a picture.
- Two helpers for every section: `adminAt('Users')` ("Admin, Users (Admin
  is in the top bar; in a narrower window it is under More, and on a phone
  in the ☰ menu)") and `profileAt('Sessions')`. Every page that named an
  Administration tab or a profile card uses them, every Administration page
  starts with how to open its tab, and every profile page with how to reach
  its card.
- REST API and MCP moved under a new **Developers** section at the end of
  the site (dev-plan 17), with the same page ids, so links still work.

### Administration: an About tab (2026-09-24, Opus 5.5)

Asked for by the owner: the version, "a full dependency list with
attribution", a way to see whether any dependency has an active CVE ("if a
new zero day hits they can easily go to the about and see if they are
exposed"), and a Patreon link.

- **About**: Tesria's version (and what it was upgraded from), links to
  tesria.com, the source and the third-party licenses; a thank-you message
  with **Support Tesria on Patreon**; and every dependency that ships (348:
  the server's NuGet packages, the web app's, the collaboration and PDF
  services' npm packages, and 8 container images), each with its license.
  The container images have their own section (the owner's request), each
  with what it is for (runs the app, builds it, optional, testing only) and
  a `docker scout cves` command to copy; the packages are filtered
  separately. Needs `dashboard.view`.
- **Check for known vulnerabilities** (needs `security.view`) asks OSV.dev
  about each of the 332 packages in one batch, then fetches each advisory
  it names (id, CVE aliases, severity, summary, link). Only on the button:
  it sends package names and versions to an outside service. The result is
  kept with its date and who checked; offline says so and keeps the last
  result. Audited (`dependencies.checked`). Verified live: no known
  vulnerabilities on 2026-09-24. Container images are pointed at
  `docker scout cves` or Trivy.
- `scripts/deps/build-manifest.mjs` writes `src/Api/About/dependencies.json`
  (from lockfiles and `dotnet list package`) and `THIRD-PARTY-NOTICES.txt`
  (each package's own license file: 278 of 332 have one; the rest are
  credited by license and link). Both are embedded in the app. CI fails
  when the manifest is stale. Migration `DependencyCheck`. Three tests.
- Password resets carry the **Through Tailscale** link too, in the email
  and in the link an administrator makes, as invites do. One test.
- `docs/security.md`: every known gap has a verdict (acceptable, should
  fix, must fix); the fixes are dev-plan 14.3.

### Invites carry the Tailscale address too (2026-09-24, Opus 5.5)

Asked for by the owner, to invite family in another state through the
tailnet rather than putting Tesria on the internet.

- When the Tailscale sidecar knows Tesria's tailnet address, a new invite
  shows two links, **At this address** and **Through Tailscale**, each with
  its own Copy. Same invite, still single-use. An emailed invite carries
  both, the tailnet one explained. `IssuedInviteResponse.TailnetUrl`; two
  tests.
- Support: the Invites page says so, and the Tailscale page gains
  "Inviting someone who is not on your network": share the device from the
  Tailscale admin console, send the Through Tailscale link.

### 20.1 Export progress (2026-09-24, Opus 5.5)

- **Export as a site and Export as a pack show a bar**: the stage ("Capturing
  pages", "Writing pages", "Copying files"), done out of total, the page or
  file being worked on and the time so far; then the download, in MB. Checked
  live on the Support space (178 pages).
- **Cancel**, beside the bar, aborts the request, which stops the export on
  the server; leaving the page does the same.
- How: the export still runs in its own request. The page makes up an id,
  sends it as `?progress=`, and polls `GET /api/export-progress/{id}`
  (`ExportProgress`, in memory, per exporter, forgotten ten minutes after its
  last change). A job table was the alternative and was not worth it: it
  would move exports out of the request that holds their permissions.
  Four tests.
- Fix found on the way: the page-tree style's hidden radio buttons were as
  wide as the window, so space settings scrolled sideways on a narrow
  screen.
- The Support pages for both exports mention the bar and Cancel, and their
  version tables say what changed in 0.6 (sections can now export
  `changes`, a note per page).

### Tailscale's logo, checked against its guidelines (2026-09-24, Opus 5.5)

- The owner checked Tailscale's logo rules: naming an integration in a
  dashboard or documentation is allowed; never as Tesria's own branding,
  never suggesting Tailscale made or endorses Tesria, with clear space and
  the logo's own colors and proportions. Recorded in
  `src/web/public/brands/tailscale/README.md`.
- The card's heading gave the logo 4px of room (a more specific heading
  rule won); it now has 14px, more than half the logo's height.
- The notice on the card and the Support page says "not affiliated with
  or endorsed by Tailscale". The Support picture is retaken.
- `DOCS_SHOTS=name,name` retakes only the named Support pictures, so
  one changed screen does not mean reshooting a whole section.

## [0.5.0] - 2026-09-24

The first numbered release: everything built since the project began on
2026-07-22. Builds before it reported 0.2.0 and were never released. The
entries below are the full record; these are the highlights.

**Upgrading from a preview build**

- API tokens now expire. Tokens made before 0.5 expire 90 days after the
  upgrade; their owners are told a week ahead. New tokens choose 30 days,
  90, a year or never.
- A token can no longer manage its account (its tokens, password, sessions
  or two-factor), and a page's render token reaches only that page.
- Where two-factor is required for administrators, an administrator without
  it has no administration rights until they turn it on.
- Two new rights: resetting another administrator (Owner only, unless the
  owner grants it) and instance-wide templates (Administrators).

**What 0.5 brings**

- Writing: a block editor with panels, layouts, tables, code, diagrams
  (Mermaid), math, charts, galleries, video, animations, embeds and twelve
  kinds of live content; templates; page emoji.
- Working together: live co-editing, tracked changes from scripts and AI
  assistants, comments, mentions, watching, notifications, full history.
- Organizing and sharing: spaces, permissions, page restrictions, labels,
  trash, search, a numbered or bulleted page tree with a filter; exports as
  Markdown, HTML, PDF, a whole website or a wiki pack; opt-in public reading.
- Integrations: a REST API with expiring tokens, webhooks, OpenAPI, and a
  built-in MCP server.
- Running it: one Compose file with automatic HTTPS, a setup wizard,
  backups with point-in-time recovery, restore and undo from the browser,
  offsite copies (cloud, network drive, removable drive) and restore drills;
  email through your own server or by signing in to Gmail, Outlook and
  others; invites by email; optional access from anywhere with Tailscale.
- Security: roles with assignable rights, an Owner role, two-factor sign-in,
  single sign-on, rate limits and lockouts, security alerts, a
  tamper-evident audit log, and a Trust this device page for servers on
  your own network.
- Versions: one version number shown everywhere, recorded in every pack and
  audit-logged on upgrade; packs from 0.5 on keep importing into later
  releases; every Support page says which version it applies to.

### Fix: an exported site's footer and sidebar (2026-09-24, Opus 5.5)

Reported by the owner from a site opened from disk: the footer only
appeared after scrolling to the end of a page, and at the end of a long
page the sidebar slid up under the top bar.

- **One cause for both.** A captured page's own closing tags end the
  two-column layout early, so the footer landed after it, and the sidebar's
  sticky container stopped 113 pixels short of the end of the page.
- **The footer is now a band pinned to the bottom of the window**, like the
  top bar, and the layout leaves room for it; the sidebar is exactly the
  height between the two bars, so it has nowhere to slide. Checked on a
  long page, the index and a phone width.
- The footer no longer names Tesria twice: "Exported on September 24, 2026
  from Tesria 0.5.0", or "Exported from Acme Wiki on ..., with Tesria
  0.5.0" for an instance with its own name.

### Phase 16: versions and releases (2026-09-24, Opus 5.5)

- **One version number** (16.1): semantic versioning from 0.5.0, set in
  `src/Api/Api.csproj` and stamped by the release tag
  (`TESRIA_VERSION` → `InformationalVersion` in the Docker build; a local
  build says `0.5.0-dev`). `AppVersion` is the one place it is read:
  `/api/health`, `/api/instance`, the OpenAPI spec, a "Tesria version" card
  on the admin dashboard, the footer of an exported site, and a pack's
  `generator`.
- **Upgrades are recorded**: the running version is kept in site settings
  (migration `InstanceVersion`), and a start on a different version writes
  `instance.upgraded` to the audit log with where it came from. The first
  start with versioning records nothing, since there is nothing to compare.
- **Packs that keep working** (16.3): `PackUpgrades`, steps from one pack
  format to the next that run on the pack's JSON before it is read, the way
  migrations run on a database. Empty today (format 1 is the only one); the
  path is proven with a pretend format 2. A pack from a newer format is
  refused naming what made it ("made by Tesria 0.9.0 (pack format 2)").
  `tests/Api.Tests/Packs/` holds a pack made by 0.5.0, and every pack there
  must import; a release that raises the format adds one made by the
  release before it. The import result says which Tesria made the pack.
- **Docs that say which version they describe** (16.2): the Support
  publisher ends every page with a small table (Applies to, Updated,
  Changes), kept in the committed `scripts/docs/page-versions.json`.
  "Applies to" moves only when a section says so (`since`), so a typo fix
  does not make a page look newer than the feature. The Release notes page
  has a full entry for 0.5.
- **Releases from a tag**: `.github/workflows/ci.yml` runs the backend and
  frontend checks on every push and pull request; `release.yml` runs them
  on a `v*` tag, builds the image with the tag's version, and publishes a
  GitHub release with this file's section for it (the highlights, when a
  section is too long for a release).
- Tests: `VersioningTests` (4), five pack-upgrade tests in `WikiPackTests`,
  and `Every_pack_a_release_has_made_still_imports`.

### Phase 19: reaching Tesria from anywhere with Tailscale (2026-09-24, Opus 5.5)

- **An optional `tailscale` service** (Compose profile `tailscale`, off
  unless started with `docker compose --profile tailscale up -d`). It joins
  the tailnet as `TS_HOSTNAME` (default `tesria`) with `TS_AUTHKEY` from
  `.env`, and Tailscale Serve answers `https://<name>.<tailnet>.ts.net` with
  a certificate Tailscale provisions, forwarding to Caddy. Funnel is turned
  off in `deploy/tailscale/serve.json`: nothing is published. Userspace
  networking, so no extra privileges. Verified live on the owner's tailnet.
- **A Tailscale card in Settings**: connected or not, the tailnet address,
  and the device key's expiry, with the steps to disable it (a device drops
  off the tailnet when its key expires, after 180 days by default). Read
  from a status file the sidecar's health check writes; the app never gets
  Tailscale's control socket. `GET /api/admin/tailscale`, 4 tests.
- **Support**: "Reaching Tesria from anywhere with Tailscale", step by step
  (HTTPS for the tailnet, an auth key, `.env`, starting it, the address,
  turning off key expiry), with what to do for people who already run an
  app connector or subnet router; `TS_AUTHKEY` and `TS_HOSTNAME` in the
  Configuration reference and `.env.example`.
- Tailscale's logo (wordmark and icon, unmodified, from its media kit) in
  `src/web/public/brands/tailscale`, with a trademark note.

### 14.1 The security findings, fixed (2026-09-24, Opus 5.5)

Every finding listed under 14.1, plus four worse ones the review of them
turned up. The owner answered the four decisions first.

- **API tokens expire.** Chosen when a token is made: 30 days, 90 (the
  default), a year, or never. Tokens from before get 90 days from the
  upgrade, not "never". A week before one expires its owner is told, once,
  in the bell and by email (`TokenExpiryNotifier`; a system notification,
  since "never about your own action" swallowed the first version).
- **A token cannot manage its account** (`TokenAccountGuardMiddleware`):
  only `GET /api/auth/me` under `/api/auth`, and nothing under
  `/api/api-tokens`. Worse than listed: saving the profile with a token
  used to hand back a fresh browser session that passed every "confirm your
  password" check, and "sign out other sessions" from a token signed out
  every one of them.
- **Render tokens are held to their page or space.** The route filter's
  comment said anything unlisted was refused; the code let it through, a
  space token was not held to its space, and `/mcp` was open to it. Now the
  list, taken from what a full site export actually requested, is all it
  may reach. A full Support export ran with no refusal.
- **`POST /pages/{id}/publish` handed any published page to anyone** signed
  in, restricted or not, before any check (not on the list; found by the
  review). Fixed.
- **Drafts and trashed pages** are no longer readable through versions,
  attachments, labels and comments: `CanReadPageAsync` answers not found for
  trash, and allows a draft to its author and its space's editors. Comments
  on a draft notify nobody and fire no webhook.
- **Hidden spaces answer 404, not 403**: webhooks, space permissions, page
  restore and purge, restriction removal, template create and delete, page
  move and copy.
- **Email addresses** in `/api/users` and group member lists only for those
  who may see the user list, and for yourself.
- **Administrators acting on each other**: suspending, signing out,
  revoking tokens, resetting the password or turning off two-factor of
  another administrator is the owner's, or needs the new right **Manage
  administrators' accounts**, off for administrators by default, so an
  owner who steps back can let them recover each other. Never the owner's
  account.
- **Instance-wide templates** need the new right **Manage instance-wide
  templates**, held by administrators; the choice is hidden without it.
- **Two-factor for administrators, everywhere**: an administrator who must
  have two-factor and has not set it up holds no administration rights in
  any check, including the ones handlers make themselves (the settings form
  let such an administrator switch the rule off).
- **Comment edit and delete** re-check that the author can still see the
  page.
- **The audit list** fills its page with entries the caller may see, by the
  chain's sequence, instead of cutting to the limit first.
- The favicon no longer names the owner's site in its source comment,
  which every export shipped.
- 13 tests in `PreReleaseAuditTests`.

### Support site: how far back each backup copy can take you (2026-09-24, Opus 5.5)

- **How backups work** gains a section that says in words what the
  diagrams only showed (the owner noticed it reading the Offsite copies
  diagram): what each copy can bring back, why a network drive gets the
  daily backups but not every database change, and which to use.

### Fix: a PDF in a file block downloaded instead of showing (2026-09-24, Opus 5.5)

- The file block shows a PDF in the browser's own viewer by framing it,
  but it framed the download address, which says "attachment" and, like
  every response, refuses to be framed. So opening a page with a PDF on it
  downloaded the file, for every reader (the owner found it on the Support
  site's File or video page).
- New `GET /api/attachments/{id}/view`: PDFs only, `inline`, frameable by
  this site's own pages (`X-Frame-Options: SAMEORIGIN`, `frame-ancestors
  'self'`), with the same visibility check as the download. The download is
  unchanged. Tested.
- Exports: a site export already rewrites the frame to the PDF file beside
  the page, and now rewrites the view address too; a page exported to PDF
  hides frames when printing and shows the file's link, as before.

### Phase 18: email through the providers people already have (2026-09-24, Opus 5.5)

- **18.1 Provider presets.** A **Provider** list at the top of Settings →
  Email and in the setup wizard: Gmail, Outlook or Microsoft 365, iCloud
  Mail, Zoho Mail, Fastmail, Proton Mail, and the sending services Amazon
  SES, Postmark, Mailgun, SendGrid, Brevo, Resend and SMTP2GO. Choosing one
  fills in the server, port and encryption and says in a line what goes in
  the username and password; the fields stay editable, and Other is the old
  form. One table (`MailProviders`, `GET /api/admin/settings/email/providers`)
  feeds the app, the wizard and the Support pages. The wizard's Email step
  also gains the encryption and the password it lacked.
- **18.2 Sign in with Microsoft** and **18.3 Sign in with Google**, for the
  mail server, in place of a password: the administrator's own app
  registration (client ID and secret, stored protected), the authorization
  code flow with PKCE, a refresh token stored protected and turned into an
  access token for each send (SASL XOAUTH2, which MailKit already speaks, so
  no new container). The signed-in mailbox becomes the username and From
  address; a personal Microsoft account sends through Outlook.com's server
  and a Microsoft 365 one through Microsoft 365's. Google returns only to
  public domains, so an instance on a LAN name finishes by pasting the
  address of the page that did not load. Starting a sign-in needs sudo; the
  callback is anonymous but acts only on a single-use, ten-minute state
  bound to its administrator. A refused renewal is shown on the settings
  and raised as a new alert, **Email stopped: the mail sign-in was
  refused**; an expired Microsoft secret is said in words, and a new secret
  keeps the sign-in while a new client ID drops it. 19 tests, including a
  real XOAUTH2 exchange with a fake SMTP server.
- **18.4 Support pages.** Sending with Gmail and Sending with Outlook or
  Microsoft 365 rewritten around the sign-in (the Google Cloud project and
  the Microsoft Entra registration, step by step, with why each step
  matters), a new **Sending services** page, and Email (SMTP) rewritten
  around the Provider choice. Settings tables are built from the presets.
  Facts checked on 2026-09-24 against each provider's own documentation.
- Yahoo and AOL are left out at the owner's word (their OAuth is closed to
  new applications anyway).

### Support site: security alerts, backup diagrams (2026-09-24, Opus 5.5)

- **Security (administration)** now shows the alerts dashboard and the
  notification bell with alerts in it, explains how administrators hear of
  an alert (the bell, and email at once whatever their other choices), and
  walks through dealing with one in four steps (the owner's request). The
  pictures are the real screens fed example alerts: the screenshot harness
  gains `mock`, which answers chosen API calls with example data, since the
  real alerts name real accounts and addresses.
- **How backups work** and **Offsite copies** each gain a Mermaid diagram of
  the design (the owner's request): the two backup services and where their
  copies go, and which copies reach which offsite target.
- Fix: an alert with no details ended in a stray separator ("by Sam Okafor
  ·") on the Security tab.
- Screenshot harness: sticky bars are made static before boxes and crops are
  measured; the email settings pictures show only the Email section, since a
  scrolled full-page capture still shifted under its boxes.

### Email an invite (2026-09-24, Opus 5.5)

- **Invites can be emailed** (the owner's request): once an address is
  typed and the server sends email, the invite form offers **Email the
  invite to …** (ticked) with a **Message** box holding a short default note
  that names the inviter. Edit it freely; Tesria adds the link below it, with
  the address it works for and the date it expires, so it cannot be left out
  or mistyped. The subject names the inviter and the instance.
- The token exists in plain text only when the invite is made, so the email
  goes then or not at all. A mail server that refuses leaves the invite in
  place: the page says why and still shows the link to copy.
- `GET /api/admin/invites/email` says whether the server sends and gives the
  default text; `POST /api/admin/invites` takes `sendEmail` and `message`
  (up to 2,000 characters) and answers `emailed` and `emailError`. The
  message goes out as plain text with an escaped HTML twin.
- The invite form gives the address its room (it was a 120px column), and
  the button follows the message.

### Fix: the setup wizard said a new instance had upgraded (2026-09-24, Opus 5.5)

- The wizard's roles step showed the roles page's notice that a default
  "changed when this instance upgraded", with a second Keep these defaults
  button. A new instance has upgraded from nothing, and the wizard has its
  own button. The notice now stays on Administration → Roles, for instances
  that did upgrade.
- Verified live on the scratch instance with its new owner: the first
  space's key skips leading digits ("2026 Team handbook" gives `TEAMHA`)
  and stops following the name once edited by hand.
- The setup wizard's roles picture on the Support site is cut to the step
  rather than a whole window.

### Fix: the Sessions list grew without end (2026-09-24, Opus 5.5)

- The profile's Sessions list showed every session ended in the last week,
  not "a few" as its comment meant. An account that scripts sign in to had
  hundreds, a list about 13,000 pixels tall, which pushed API tokens past
  what Chromium can capture: three Support pictures, including the one under
  REST API on the Features page, came out blank (the owner found it). Now
  every live session and the five most recently ended.
- The Support publisher also ends the example account's other sessions
  before taking pictures, and the Sessions picture uses example addresses
  and browsers instead of Docker's own.
- `SECURITY.md` gives brianintheloopdev@gmail.com as the security contact.

### 15.6 The Support site, rewritten (2026-09-24, Opus 5.5)

- Every Support page rewritten to the rules the owner set reviewing the
  first version (`scripts/docs/WRITING.md`): written for someone new, with
  what a thing is and why they would want it before any steps; numbered
  steps with the control boxed; one picture per row with a shadow, taken in a
  narrow window so it reads on a phone, and none of text the page already
  says; lists rather than tables for explanations.
- Every element page has the same shape: the slash commands first, then
  every variant live on the page (all four chart types, every panel type,
  list styles, status colors) with when to use it.
- A **Features** page, second at the top level, grouped by what people do,
  each feature with a picture or a short animation.
- New pages: Setting up a phone or tablet, Page emoji, Moving and copying
  pages, How live content works, Opening Tesria by name, and, under
  Email (SMTP), **Sending with Gmail** and **Sending with Outlook or
  Microsoft 365** (the owner's request, for people without a mail server of
  their own). The Microsoft page says plainly that a personal Outlook.com
  account cannot be used: since September 2024 Microsoft accepts only an
  OAuth sign-in there, and Tesria signs in to a mail server with a password.
  A Microsoft 365 account works while its administrator allows
  Authenticated SMTP, which Microsoft turns off by default at the end of
  December 2026. Both checked against the providers' own pages on
  2026-09-24.
- Then (the owner's request) **Sending with Apple iCloud Mail**, **Sending
  with Zoho Mail**, **Sending with Fastmail** and **Sending with Proton
  Mail**, each with its settings table, where to make its app password (or
  Proton's SMTP token), the plan it needs, and its usual errors; the Email
  (SMTP) page links to all six.
- The pilot pages the owner approved moved into the sections they belong
  to, and every reference to another page is a link (`pageLink`).

### Fixes the rewrite turned up (2026-09-24, Opus 5.5)

- **Mentions in comment notifications** showed as the raw
  `@[Name](user:id)` in the bell and its email; they read "@Name" now, in
  new notifications and old ones. Webhooks keep the token, for the id.
- **The page properties report** showed a status or a date as an empty
  cell, because both keep their value in attributes; the task report had
  the same blind spot for dates in a task. Both read them now.
- **`/chart` offered Diagram first**, because "flowchart" contains "chart".
  The slash menu ranks exact matches first, then words that start with what
  was typed (`editor/slash/match.ts`, tested).
- **Wrong tips:** Ctrl or Cmd with ] indents text but does not nest a list
  item (Tab does), and search reads titles and text, not labels.
- **Admin → Roles** warned that any role gaining rights is announced to
  every administrator; only an administrator role is, so the warning shows
  only then.
- **Admin dashboard:** a page an administrator may not see showed as
  "Deleted page" among the most viewed; it says "A restricted page" now.
- **Backup runbook** (`docs/backup-recovery.md`, `deploy/backup/restore.sh`):
  restoring on a new machine now starts the stack first (the script compares
  migrations with the live database, so an unstarted one refused every
  dump) and restarts the app afterwards; the `docker compose cp` copied the
  folder into itself; the dry run set its variable on the host instead of
  in the container, so it restored for real; and the pgBackRest restore from
  the offsite copy ran against a running database in a read-only mount. The
  offsite restore onto a new host is marked untested.
- `NOTICE` lists Svg.Skia, Markdig, Scalar and the MCP SDK;
  `docs/architecture.md` no longer calls the collaborative-document gap open
  (8.6 closed it).

### 15.9 Filter the page tree (2026-09-23, Opus 5.5)

- A **Filter pages** box at the top of every space's page tree, and of an
  exported site's sidebar (the owner's request). Typing shows the pages
  whose title, or number in a numbered tree, contains the text, with their
  parent pages dimmed for context and the match highlighted; case and
  accents are ignored. The filter stays while you open its results, the
  current page highlighted in it (the owner chose keeping it over clearing
  it), and is remembered for the browser tab, per space; × or Escape clears
  it, and Enter opens the first match. Numbers keep their full-tree values while
  filtering.
- A toggle beside the box, **Show the pages under each match**: type
  "elements" and see Elements with every page under it (the owner's
  request). On by default; turning it off is remembered in that browser.
- One rule in two places: `treeFilter.ts` in the app (tested) and the
  export's own script, which each exported link now carries its depth for.

### 15.8 Numbered and bulleted page trees (2026-09-23, Opus 5.5)

- **Space settings → Page tree**: Plain, Numbered or Bulleted, each with a
  preview. Numbered is outline numbering like a numbered table of contents
  (1, 1.1, 1.2, 2); Bulleted changes the bullet by level.
- The markers are drawn, never stored: no title, page address or search
  result contains them. They are worked out from the tree's order as it is
  drawn, so moving or adding a page renumbers everything at once, even
  while dragging in Reorder mode before it is saved (the owner's request).
  A title that wraps lines up under its own first word.
- **The space sidebar can be resized** (the owner, once the numbers took
  room): drag the handle on its right edge, or focus it and use the arrow
  keys; double-click or Home puts it back to 260px. Between 200px and 560px,
  never more than half the window, and remembered in that browser like
  hiding the sidebar. Phones keep their menu.
- The same rule numbers an exported site's sidebar (`SiteChrome.TreeMarkers`
  and `treeMarkers.ts`, tested with the same cases), and wiki packs carry
  the setting (optional, so older packs import as plain).
- Migration `SpaceTreeStyle`. The Support space is numbered, and its section
  emoji from 15.7 are taken off again: the owner preferred numbers.
- **Templates are easier to find.** Space settings → Templates opens with
  three steps for making one, where it had one grey line; the owner looked
  there for a way to create a template and did not find it. The Save as
  template form has labels and a real Save button, and a drop-down under a
  label (such as Start from a template) sits on its own line and matches
  the text boxes. Support gains a **Templates** page.
- Support gains a **Features** page, second in the tree: everything
  Tesria does, grouped by what you are trying to do (writing, organizing,
  working together, sharing, security, running it, developers, phones),
  each with why you would want it and a link to the page that explains it.
  No other products named and nothing unbuilt, at the owner's choice.
- Support gains **Opening Tesria by name**: why a name rather than a
  number, finding it, why a `.local` name can take a few tries, and how to
  make it instant (awake and wired, router settings, a fixed address, the
  hosts file, a name server such as Pi-hole with a `.home.arpa` name).

### 15.7 Page emoji (2026-09-23, Opus 5.5)

- A page can have an emoji, shown large above its title and before its name
  in the page tree, so sections stand out (the owner's request, for the
  Support site's tree). Anyone who may edit the page sets it from the page:
  **Add emoji** appears when you hover the title, and opens a picker that
  searches the editor's emoji by name and takes any other pasted in.
  Choosing saves at once; it is page metadata like the width, so it makes
  no new version.
- The picker has three groups: **Emoji**, **Numbers** (1️⃣ to 🔟) for pages
  read in order, and **Bullets** (• ▪ ▸ ➤ ◆ and others) for a plain marker.
  A search covers all three.
- Carried by copies, wiki packs (an optional field, so older packs import
  unchanged, and checked on the way in) and exported sites' sidebars.
- Migration `PageEmoji`.
- Animations (a video set to play as one) now show at their own size with
  a picture's shadow, rather than stretched across the column. The Panels
  clip was recorded at 2x, which Playwright does not scale: the page sat in
  the top-left quarter of the video. It is recorded at 1x in a narrow
  window instead.

### 15.5 Trust this device (2026-09-23, Opus 5.5)

- **`http://<server>/trust`**, a page that walks anyone through trusting the
  server's own certificate, over plain HTTP so a new device opens it with no
  warning. It guesses the device (Windows, Mac, Linux, iPhone or iPad,
  Android) and the address, and gives numbered steps: a script with the
  address already written in, or the certificate and the Settings path on a
  phone, then a link to check it worked. Firefox gets its own note, and an
  IP address gets an explanation of why a name works better.
- Three ways in: **Profile → Trust this device** for anyone signed in (the
  owner's suggestion: most people click past the warning once and sign in),
  the sign-in page ("Did your browser warn that this site is not secure?"),
  and the address itself, which is the way for phones and for browsers that
  will not let anyone click past the warning. All only on servers with their
  own certificate. With the public
  Caddyfile the page says there is nothing to set up.
- `deploy/scripts/trust-ca.sh` and `trust-ca.ps1` each have one marked line
  to edit, `TESRIA_ADDRESS`; the page fills it in. The address is checked
  against a strict pattern first, since the script runs as an administrator.
- Windows gets one line to paste into PowerShell rather than a script to
  run. The owner's first try on Windows was refused by PowerShell's
  execution policy, which blocks downloaded scripts by default and can be
  locked by an employer; a typed command is not affected. It trusts the
  server for the current Windows account (`CurrentUser\Root`), so it needs
  no administrator. The script stays for trusting it machine-wide.
- Profile and the sign-in page link to `/trust` on the connection already
  in use, rather than switching to plain HTTP: whoever sees those links got
  past the warning already, and `http://` by name was unreachable from the
  owner's Windows machine while HTTPS worked.
- Opened by a number such as 192.168.1.50, the page now says plainly to
  open Tesria by its name afterwards: the owner trusted the certificate on
  Windows and Chrome still said "Not secure", because a number can never
  match a certificate issued for a name. It names the server when Admin →
  Settings → Public address holds a real name. "Check it worked" explains
  `chrome://restart` and the two certificate errors Chrome can show.
- `/ca.crt` is now served as `application/x-x509-ca-cert`, named
  `tesria-ca.crt`. Without the type, an iPhone showed the certificate as
  text rather than offering to install it.

### 10.5 steps 6 and 7: the Support site, written and exported (2026-09-23, Opus 5.5)

- 165 pages in the Support space, written by `scripts/docs/publish-docs.sh`
  with every picture taken on a desktop and a phone from the Tesria Demo
  space.
- `docs/site/docs-pack.zip` is the space as a wiki pack, committed so the
  site survives anything that happens to an instance. Import it from
  Spaces → Import a pack. `scripts/docs/export-docs.sh` regenerates it
  and exports the static site, checked against Cloudflare's limits.

### Phase 15: what the Support site found missing (2026-09-23, Opus 5.5)

Designed and built by Opus 5.5 from the owner's answers (dev-plan Phase 15).

- **15.1 Access.**
  - Administrators can turn off another account's two-factor: never the
    owner's, and another administrator's only by the owner. It asks for the
    password, signs the account out everywhere, and raises a Warning alert.
  - Three built-in groups, Owner, Admins and Users, nested, with membership
    worked out from each account's tier at check time. They cannot be edited
    or deleted. A custom group already using one of those names is renamed,
    with an audit entry.
  - An imported pack now starts private to the importer, and the result
    screen goes straight to "Who should have access?".
  - Administration rights can no longer be given to user-tier roles. The
    server refuses them, the Roles tab shows a dash, and a startup step
    removes any held already, with an audit entry.
- **15.2 Editor.**
  - Tables: header row and header column on and off, merge and split cells,
    and Delete table, all in the cell menu.
  - Images: drag to resize (a percentage of the column), left, center, right
    and full-width alignment, a caption, and editable alt text. The Markdown
    export carries the caption.
  - File or video: an Upload button in the block.
  - Smart link: a real inline form that sits in a sentence. Inline and Card
    convert between the two.
- **15.3 Pages and collaboration.**
  - Comments: threads can be resolved and reopened, and resolved threads fold
    away and lose their highlight. Mentions work in comments.
  - History: compare any two versions.
  - Move a page, with its sub-pages, to another space. Copy a page, with or
    without its sub-pages. A copy duplicates its attachments, so its pictures
    do not depend on the original. The dialog opens above the page and
    closes the page menu behind it.
  - A label index at /labels.
  - Space settings → Permissions: "Make this space open again".
- **15.4 Confirmations.** Suspend, Sign out, Revoke tokens, invite Revoke,
  unblocking an address, removing a group member, and the Security tab's
  mitigations each ask first.
- Migration `CommentResolution` adds `ResolvedAt` and `ResolvedById` to
  comments.

### 10.5 step 6: fixes the Support site's fact-finding turned up (2026-09-23, Opus 5.5)

Documenting every screen meant reading every screen's code. These were
fixed before any of it was photographed.

**Security**
- Single sign-on created accounts even when registration was invite-only.
- The single sign-on return address accepted `/\host`, an open redirect.
- Watchers were notified, and could be emailed, about pages they could not
  view.
- A read-only API token could get a live-editing token and change a page.
- Renaming a page through the API or MCP, without sending content, emptied
  it.
- The trash, and the dashboard's Most viewed, showed titles of restricted
  pages.
- Deleting a group left its grants behind. They now go with it, and the
  delete is refused where that would open a space or page to everyone.

**Bugs**
- Error messages dropped the server's explanation, so people saw "Request
  failed (429)." or "You do not have permission to do that.".
- Leaving the welcome tour midway was refused by the CSRF check, so the tour
  came back every session.
- Turn off two-factor was offered to administrators who must keep it.
- Edit, + New and the export options showed to people who could not use
  them.
- The Insert menu deleted selected text before wrapping it. The slash menu
  had no Normal text, and headings had no h1 to h6 aliases and switched
  themselves off.
- Galleries did not tile.
- Code block controls stayed live for readers.
- The emoji menu opened on a bare colon.
- Picture files showed as file cards in File or video.
- Embeds refused an address typed without https://.
- On phones, a table's buttons covered the text above it.
- Search and notifications returned short lists, because they were cut to
  their limit before permission filtering.
- Restoring a version notified nobody, fired no webhook, and did not reach
  open editors.
- Permanently deleting a page, or discarding a draft, left its files on
  disk.
- Webhook event names were not checked.
- A wiki pack over 100 MB was refused by Caddy.
- Exported sites showed links to pages left out of the export with no
  explanation.
- The admin Spaces tab failed for roles without a settings right.
- Expired address blocks stayed listed, and blocked the same range from
  being added again.
- Alerts showed their internal names, in the app and in emails.
- A settings refusal named an internal field, such as AllowPublicSpaces.
- The setup wizard misplaced its checkboxes, and never checked off Welcome.
- The tour's recording used the key DEMO, the Tesria Demo space's key, for
  a temporary space it deletes. A failed setup would have deleted Tesria
  Demo. Its spaces are now TOUR, and a failed setup stops the run.
- Scalar's API reference tried to reach api.scalar.com on every load. It no
  longer does, and its developer toolbar is hidden.
- Wording across many screens, including `NOTICE`'s British "licences".
- The setup wizard's first space: the key follows the name as you type
  until you edit it yourself, and never starts with a digit. It filled in
  only while empty, so after the name's first letter it stuck ("Team
  handbook" gave T).
- Typing in a text box no longer zooms the page on an iPhone or iPad. The
  16px rule that prevents it lost to any box styled smaller by its own class
  (the new page filter), and did not apply to iPads at all; it now wins, on
  any touch screen.
- Profile used a 900px column of 480px cards, one long narrow strip on
  any wide screen. Its cards now take the window's full width, one to a
  row like the admin pages, grouped as account, security and preferences;
  the fields inside keep a readable width.
- Panel and decision icons, and task checkboxes, now line up with the first
  line of text on every browser. The icons sat a fixed distance from the
  top, and the editor's paragraph margin out-ranked the panel's own rule,
  so the text began half a line lower; task checkboxes were nudged down a
  fixed amount that matched Chrome and missed on an iPhone. Each is now
  centered on one line's height (CSS `lh`), and measured within 1.5 pixels
  in both Chrome and Safari's engine.
- On a phone, a picture resized smaller than the column now fills it
  rather than shrinking twice.
- Selecting a picture, or working in a panel or table, inside a layout
  column showed the layout's menu on top of that block's own menu. Only the
  innermost menu shows now; the layout's returns in plain text.
- Admin → Settings ran off the right edge of a phone: its columns had a
  26rem minimum, wider than the screen.
- The screenshot harness painted the sticky top bar into the middle of a
  cropped picture whenever a step had scrolled the page.
- Exported pages and sites: Expand blocks were captured shut and their
  toggle did nothing without the application, so no FAQ answer could be
  read. The export's script now opens and closes them, and wires code
  blocks' Copy button. Live blocks lost their editing header and Refresh
  button, and nothing is shown selected.

### 8.5 Wiki packs: export and import a space (2026-09-21)

A space can now be exported as a **pack** and read back into any Tesria: the
documents themselves, with their history, attachments, comments, labels and
templates, in a zip the repository can hold. This is what makes the rebuilt
manual (10.5) survivable; the last one did not, because it lived only in a
database. The design entry below is Fable's and still describes what was
built. What shipped:

- **The format** (`Features/Export/WikiPack.cs`), version 1: a manifest, the
  space, the authors, one JSON file per page and the attachment bytes.
  Canonical output, so the same space always packs to the same bytes and a
  one-word edit diffs as a one-word edit. The reader checks the format version
  first, matches every entry name against the shapes the format defines, and
  enforces a 20,000-entry and 500 MB uncompressed ceiling while reading.
- **Export**: `GET /api/spaces/{key}/export/pack`, and "Export as a pack" in
  space settings beside "Export as a site". It walks pages one at a time
  through the exporter's own permissions, so a pack cannot carry a page its
  author could not open, and the manifest counts what was left out without
  naming it. No page cap: unlike a site there is no per-page render to pay for.
- **Import**: `POST /api/spaces/import`, and "Import a pack" on the spaces
  list. One transaction, with the bytes swept up again if it does not commit.
  Every id re-minted, every document through `PageContent.TryNormalize`, every
  attachment's type re-derived from its bytes, ten imports an hour per user.
  The same key twice is a 409, never a merge.
- **`PackRewriter`**, pure and fixture-tested: it maps ids into content and,
  where the target has nothing to map to, keeps what a person wrote and drops
  the machine-readable half. A mention keeps its name and loses its user id, a
  comment mark with nothing behind it goes rather than coloring text that
  answers nothing, and a link to a page outside the pack is left alone.
- **What deliberately does not travel**: permissions, `IsPublic`, `Archived`,
  drafts, the trash, watches, webhooks and authorship. An imported space is
  private, live, and attributed to whoever imported it; the original authors
  are kept as display names in the pack and in the audit event, and the import
  says in words how many restrictions the source had so somebody sets them
  again.

Three findings are worth recording, because each was invisible to the layer
above it.

**The HTTP tests caught what the unit tests could not.** The export threw on
every real call: `ZipArchive` writes synchronously and finishes on Dispose,
and Kestrel refuses synchronous writes to a response. The unit tests write to
a `MemoryStream`, which does not care. It is spooled to a temporary file now.

**The live walk caught what the tests could not.** A pack unzipped and zipped
back up was refused, because `zip -r` writes a directory entry per folder and
this writer never does, so every test built its zip with the writer and they
all agreed with each other and with nothing else. That is exactly how 10.5 is
meant to import the manual. Directory entries are skipped now.

**And one thing left as it is.** Two exports of an unchanged space differ by
one line, `exportedAt`, and nothing else; the content is byte for byte
identical. A pack that travels outside a repository should say when it was
made, so the field stays, but it does mean a re-export always shows as
changed.

Tests: 19 rewriter fixtures, 13 HTTP round trips (importing as a *different*
user, which is what makes the attribution and permission assertions mean
anything), on top of step 1's 27 format tests.

### Fix: an invite used while registration was open stayed "Unused" (2026-09-23, Opus 5.5)

Registration looked an invite up only when public registration was closed,
so with it open an invite link created the account and the invite was never
spent: it read "Unused", named no account, and still worked for someone
else. An invite is now spent whenever one comes with a registration, and the
Invites tab says who each one created ("used by Alex Rivera, 9/22/2026"). With
registration open, a token that does not match is ignored, as before.
Invites used before this fix could not be linked after the fact. Found by
the owner.

### 10.5 step 5: the Tesria Demo space (2026-09-23, Opus 5.5)

`scripts/demo/seed-demo.sh` builds **Tesria Demo**, the space the support
site's pictures are taken from: a fictional team wiki (Kestrel Labs
launching Kestrel Sync 2) with a plan, meeting notes, a checklist, an
architecture page and an FAQ; a Reports page of live content reading it;
and an Element gallery with one page per editor element. It signs in as two
of the fictional people, so history, comments and contributors show two,
and it can be run again safely: it changes only what differs. Images and a
PDF are drawn by the script rather than committed as files.

**The interface is in US English**, at the owner's request: Math (typing
"maths" still finds it), color, gray, centered, labeled, canceled, defense,
catalog and toward, in every label, tooltip, screen-reader label and API
message people read. Stored values keep their spelling, because pages
already hold them (a status or text color is still stored as `grey`), and so
do identifiers and CSS class names, which nobody reads and which would break
for nothing. The support site is written in US English from the start.

Then **everything else, at the owner's request**, so the repository reads
one way throughout: every document (this changelog and the plan included),
the README and project notes, `.env.example`, code comments, test names,
internal names (`normalizeTocOptions`, `LabeledPage`, `BeginEnrollment`,
the `branding__color` CSS classes) and two public API fields: the roles
matrix's `catalogue` is now `catalog`, and the restore-cancel response's
`cancelled` is now `canceled`, changed on both sides before anything
outside depends on them. Kept on purpose: the stored color value `grey`
and the CSS names built from it, because existing pages hold it; the audit
action `backup.restore_cancelled`, which existing audit entries hold under
the hash chain; HTML's own `aria-labelledby`; and "maths" and "favourite"
as search synonyms. Done by a script with protected terms, then checked by
a scan for anything left, which found two names inside longer ones and
nothing else. All 808 backend tests and the frontend's 50 pass.

### Fix: new accounts could skip their recovery codes (2026-09-22, Opus 5.5)

Found by the owner creating the Demo accounts: three of four went straight
to the welcome tour and never saw their recovery codes, though the Users
tab said each had 8. Registration signed the account in before handing the
codes to the page, and two redirects fired in between: the tour gate
(once per tab, which is why one of the four got through) and the register
page's own "already signed in" guard. The codes existed on the server and
were never shown.

The page now has the codes before the session changes, and the tour gate
leaves `/register` alone, so the tour follows "Continue". Continuing also
records the codes as saved, which the register page had never done (only
the setup wizard did). Because of that, every account created through the
page had codes never confirmed saved, so the reminder that asked only
accounts with *no* codes now also asks those, once per sign-in: "I have my
codes" takes the person's word, "Make new codes" replaces them. The Users
tab marks such accounts **not saved** beside the count.

Not walked live, because that means creating an account; covered by a
test of the saved flag, and the owner's next registration is the check.

### 10.5 step 3: system requirements, measured (2026-09-22, Opus 5.5)

Nothing in the repository said what Tesria needs to run, so it was
measured rather than guessed: about 550 MiB of memory at rest for the whole
stack and 665 MiB at the peak of a run of PDF and site exports; about
5.2 GB of disk for the images, 3.5 GB of it the PDF renderer's Chromium; and
a browser floor of Chrome and Edge 111, Firefox 121 and Safari 16.2, set by
the CSS the app relies on. The support site will ask for 2 cores, 2 GB (4 GB
to build the images on the same machine) and 20 GB of disk, with the
measurements behind it. The figures and the method are in the plan.

### 10.5 step 2: videos that play as animations (2026-09-22, Opus 5.5)

**File or video** can show a video as an **animation**: silent, looping,
starting by itself, with no player controls, the way a GIF behaves at a
fraction of a GIF's size. Choose "Show as: An animation" beside the file,
or insert **Animation** from the slash or Insert menu. A small pause button
shows on hover (always, on a touch screen), and anyone whose system asks for
reduced motion sees it paused. Exported sites carry all of it, with the clip
copied beside the page.

Checked in the editor, the reading view, an iPhone simulator and an
exported site opened from plain files. Safari on iOS plays the WebM clips
the screenshot harness records, so there is no second format to make. The
ordinary video player also stops showing a black box on iOS before it is
played.

### 10.5 step 1: the fixes before the support site (2026-09-22, Opus 5.5)

The owner rescoped 10.5 from "rebuild the manual" into a support site for
tesria.com (a public **Support** space exported as a static site, and a
private **Tesria Demo** space to shoot it from) and asked for every gap the
inventory found to be fixed first, so nothing is documented broken or
recorded twice.

**The editor**
- **Smart link** can be given an address (it was inserted empty, with no
  way to fill it) and switched between Card and Inline.
- Panels, expands, decisions, excerpts and page properties get a bar with
  **Remove**, which keeps everything inside; the old unset commands lifted
  only the block under the cursor and split a longer panel in two. A panel's
  type changes from the same bar.
- **Inline maths** in the slash and Insert menus, and an Inline / Own line
  switch while editing an equation.
- **Include page and Excerpt include pick a page by searching**, not a raw
  id. A pasted id still works.
- Headings 4 to 6 in the Style and slash menus; Justify in Alignment.

**Include page crashed the whole editor** whenever it showed a page, found
the moment the picker made it easy to reach. An editor nested in another
mounts late, and two things touched it before it had: the reading view's
content sync and the inline-comment popover. Both now wait for it.

**Spaces and administration**
- **Space settings → Templates**: every template offered in the space,
  with rename and delete where the viewer may. Anyone holding "Manage
  spaces" can now remove an instance-wide template, which before only its
  author could, so one left by somebody who had gone stayed for ever.
- Groups can be renamed.
- **Get access** on the Spaces tab, for the audited endpoint that had no
  button. On an open space it grants nothing: the endpoint used to add the
  space's first grant there, and the first grant closes a space to
  everyone else.
- **Invite people**, a page for anyone holding "Create invite links"
  without the admin area, where that right did nothing.

**Mobile, added by the owner while this was under way**
- A phone held sideways fills the screen: the page asks for the whole
  display (`viewport-fit=cover`) and pads its bars and sidebar clear of the
  notch, instead of sitting between two empty bands.
- The **space sidebar can be hidden**, on any screen wide enough to have
  one, and stays hidden on that device until shown again.
- The **date popup** fits its box on iOS, which drew the field at its own
  minimum width.
- On a phone held upright, **tables keep readable columns and scroll
  sideways** instead of squeezing to a letter a line. The minimum and
  maximum are set on what is inside each cell, because browsers ignore
  `min-width` on a cell (Chromium honored it, Safari did not, which is how
  the first attempt passed in one and failed in the other).

**Smaller**
- Mentions and security alerts read in words in the bell and in emails
  (a mention showed as `user.mentioned`).
- The mention tip stops claiming mentions work in comments; the
  two-factor switch stops saying two-factor has not shipped; the setup
  wizard follows the server's 8-character rule; the "Create spaces" right
  stops saying the creator administers the space.
- Warning notices, such as "This instance has no offsite backup", had no
  style at all and now look like warnings.

**Verified** in a browser, reading each result back from the page (heading
4, the panel bar and type change, removal keeping the text, Smart link's
address, inline maths, the page picker, the Style and Alignment menus,
the draft discarded on Close, the sidebar hidden, remembered and shown
again), and on an iPhone 18 Pro simulator for the date popup and phone
tables. Landscape could not be rotated from here; the layout was checked
with the notch's insets set by hand at the phone's landscape size.

**A slip, recorded for the Opus 5.5 trial.** Writing the new template
tests overwrote an existing `TemplateTests.cs`, deleting five tests: the
search for existing template tests missed it. The full suite's count
falling from 802 to 801, when it should have risen, is what gave it away;
the five are restored beside the new three.

### Fix: restores that were undone stayed "running" for ever (2026-09-22, Opus 5.5)

Five restores from the 9.4 walk still said Running… on Recent runs, and the
backups page polled them every five seconds for as long as it was open. Each
had been undone. An undo puts back a copy of the wiki taken in the middle of
the restore, which holds that restore as "running", and carrying the job
history across added missing rows without updating existing ones, so the
stale row won. Any restore had the same flaw in a quieter form: an older
backup holds the job that took it as "running", and a job that was
"requested" at the time would have been run again.

Carrying job history across a swap now keeps whichever copy of a job is
further along. Each backup agent also closes any job of its own left marked
running, since it runs one job at a time: a restore or undo takes its
outcome from what it recorded in its restore directory, anything else is
marked interrupted, and the restore currently in progress is never touched.
The five closed as succeeded with the finish times their logs recorded.
Found by the owner.

### Fix: admin table rows with actions were misaligned (2026-09-22, Opus 5.5)

The last cell of a row in the admin tables (Log on Recent runs; the actions
on Users, Spaces, Security, Sessions and the backups list) sat out of line
with the rest of its row, its divider at a different height from the
others. The cell itself was a flex container, which takes a `<td>` out of
table layout, so it stopped sizing with its row. The flex layout now lives
on a wrapper inside the cell. Found by the owner on the backups page.

Recent runs also shows a connection test's answer, the same sentence as the
Storage targets card, where it used to say only "Done."

### Fix: the offsite copy of the files ran every minute (2026-09-22, Opus 5.5)

The backup sidecar copied the uploads and dumps to the cloud and the network
drive on **every pass of its loop, once a minute**, rather than after each
local backup. Each copy kept a new snapshot for the whole retention window
and ended with a 5% read check, which against a real cloud provider would
have downloaded the repository about 72 times a day. No real cost was
incurred: no instance had a live cloud target configured.

A scheduled target is now copied once per local backup: when a local backup
has finished since its last copy, or when it has never been copied, or when
`.env` now points it somewhere new. A restart does not copy again.

Fixing it exposed a second fault: **an unreachable target held the whole
backup sidecar** for up to fifteen minutes at a time, because restic retries
a refused connection that long, and every queued job (Back up now, restore
tests, restores) and the other targets waited behind it. Each copy now
checks the target first with a 30-second limit, reports why it could not
start on the Storage targets card, and waits 15 minutes
(`OFFSITE_RETRY_MINUTES`) before trying again.

Verified against MinIO over 25 minutes, with an outage in the middle: three
snapshots where the old loop would have taken twenty-five.

### Storage targets: Test connection and a cloud budget; dashboard cards (2026-09-22, Opus 5.5)

**Test connection** is back on every Storage targets card. It asks the backup
agents to reach the target now and open its repository, changing nothing,
and the card shows each agent's answer in plain words: connected and how
many snapshots or backups it holds, or what is wrong and which `.env`
setting to look at. The cloud is answered twice, once for the database
repository and once for the uploads and dumps, because different sidecars
write them and either can be the broken one. The app still holds no
credential: the test runs in the sidecars, as a job, like Copy now.

Each failure was produced against MinIO and its wording checked: a wrong
key, a wrong secret, a missing bucket, an unknown host, a wrong passphrase,
an empty path (reported as "no repository yet", which is a success for a new
target), and an unclaimed network drive. Two things the walk found:

- **restic never fails fast on a bad key.** It retries a refused key or an
  unreachable host quietly for minutes, and its final line is only "unable
  to open config file". The test stops at 30 seconds and reads the reason
  out of restic's retry lines instead.
- **A wrong pgBackRest passphrase made the answer unreadable.** pgBackRest
  quotes the bytes it could not decrypt, which are not UTF-8, and Postgres
  then refused the whole JSON document. Everything outside printable ASCII
  is dropped before it is parsed.

A failed test raises no alert and does not mark the agent's last run as
failed: nothing was being backed up.

**`OFFSITE_CLOUD_BUDGET_GB`** (optional, in `.env`) gives the cloud card's
chart a denominator: what is stored against what is left of the budget, and
"Over budget by" in red once it is passed. It is a number to watch, not a
limit: nothing is refused, removed or alerted on for going over.

**The dashboard's Most viewed and Most active editors** are now cards like
the tiles above them, with the counts right-aligned. A page deleted since it
was viewed now reads "Deleted page" rather than linking nowhere under an
empty badge.

### 12.3 Turn a space's exports off, format by format (2026-09-22, Opus 5.5)

An administrator can now turn a space's exports off in **Space settings →
Exports**, for a space more sensitive than the rest of the instance. There
are five switches, all on by default: Markdown, HTML, PDF, Website and Wiki
pack. The request named three. The other two, a page as a single HTML file
and the whole space as a pack with its history, are here because leaving them
open would defeat the point.

- A new right, **Control a space's exports** (`spaces.exports`), which
  administrators and the owner hold by default.
- Off means off for everyone, administrators and the owner included. The
  export routes answer 403 with `export_disabled` before any rendering
  starts. Changes are audited as `space.exports_changed`.
- The page view only offers the downloads a space allows. Space settings
  hide the website and pack sections when those are off.
- It stops downloads, not reading: anyone who can read a page can still copy
  it, and the settings page says so.

Tests: 11 new.

### 13.1 Instance branding (2026-09-22, Opus 5.5)

An owner, and anyone the owner grants the new right to, can now brand the
instance from **Administration → Branding**. Every default is Tesria's, and
nothing changes until someone sets it on purpose. What shipped:

- **A brand name**, separate from the instance name, in the header, on
  every sign-in card and in exports. Renaming the instance no longer touches
  the header.
- **Logos:** SVG, PNG, JPEG or WebP, with an optional dark-mode logo. In the
  header the logo sits at the bar's height. On the sign-in card it shows
  side by side with the name (the default), stacked above it, logo only or
  name only. The Branding tab warns when a raster logo is too small to stay
  sharp on the sign-in page.
- **SVG is sanitized and then only ever shown as an image.** The sanitizer
  rebuilds the file from an allowlist and refuses anything it cannot parse,
  DTDs and entities included. The file is served under a sandboxing policy
  and is never inlined as markup, in the app or in an export, so a bug in
  the sanitizer still cannot run script.
- **A favicon:** SVG, PNG, ICO, JPEG or WebP, drawn to 32, 180 and 512 pixel
  PNGs. SVG ones are rasterised in the app process by Svg.Skia, so there is
  no new container.
- **Theme and accent:** light only, dark only, or people's choice; a custom
  accent with a color per mode, from which the other five tokens are
  derived; and a lock that holds everyone to one accent. A color that is
  hard to read is shown with its contrast ratios and the nearest shade that
  passes. The owner can keep their own color anyway, and the audit log
  records that choice.
- **No flash of the wrong theme.** The server writes the title, favicon,
  accent stylesheet and theme locks into the page before sending it. The
  inline theme script reads them as attributes, so its CSP hash is the same
  on every instance, and a test proves it byte for byte.
- **Tab titles** now read `Instance Name - Space Name / Page Name` (or
  `Instance Name - Section`). Before this the app never set a title, so
  every tab said "Tesria".
- **Exports carry the branding** as it was when they were made: logos and
  favicon as files in a site, or inline in a single HTML file, plus the
  accent, the locks and the title. They also no longer point at the
  instance for their favicon, which they always used to.
- **Reset to Tesria** removes all of it and deletes the files. It leaves the
  instance name alone.
- **Subtle attribution:** one muted "Powered by Tesria" under a branded
  sign-in card, and a version line at the foot of Administration.
- **The app image went from 1,070 MB to 455 MB.** The API is now published
  for the image's own architecture instead of shipping native libraries for
  about fifteen platforms.

Tests: 59 new (787 backend, 50 frontend, all green). They cover the right,
nothing changing until set, the title rules on both sides, the shell and
its unchanged script, color normalization and the contrast checks, nine
hostile SVGs and four malformed ones, raster sizing, GIF and
decompression-bomb refusal, the favicon set, reset, and the export chrome.
One slip was caught before commit by an existing test: the file upload
routes were first mapped without the right.

### Design 13.1: instance branding (2026-09-22, Opus 5.5)

At the owner's request, the owner (and anyone the owner grants the right)
can brand the instance. That means a brand name for the header, a logo,
including SVG, with an optional dark-mode version, a favicon, a custom
accent color per theme, and locking the theme or accent for everyone.
Exports carry the branding as it was when they were made. The brand name is
separate from the instance name, and nothing changes until someone sets it
on purpose. A custom color that fails the contrast check gets a better
shade suggested, but the owner can keep theirs. Branding reaches the
page before first paint through the server-rendered HTML shell, while the
inline theme script stays byte-identical so its CSP hash still matches. SVG
is sanitized by an allowlist and only ever displayed through `<img>`, so a
sanitizer bug still cannot run script. A custom accent must pass the same
4.5:1 contrast checks as the built-in six. The first item designed under the
new model gate. Full design in `dev-plan.md` as 13.1, with every decision
answered the same day. Tab titles become `Instance Name - Space Name / Page
Name`. A Reset to Tesria button undoes all branding. Attribution stays
subtle: one muted line under a branded sign-in form, and a version line at
the foot of Administration.

### Model gate: Opus 5.5 designs and implements (2026-09-22)

The owner retired the Fable-designs, Opus-implements split, on trial, after
Anthropic's launch page reported Opus 5.5 at Fable 5.1's level on most work
at lower cost. New plan items are tagged `Model: Opus 5.5`. Earlier tags stay
as history, and Fable remains an optional second opinion. The rule is
rewritten in `CLAUDE.md` and at the top of `dev-plan.md`.

### 9.4 Restore from the admin page (2026-09-22)

Any backup on the Backups page can now be restored, not just tested. Logical
dumps and point-in-time recovery both, behind a gate stronger than deleting a
space. What shipped:

- **A right of its own**, `backups.restore`, held by the **owner** by default
  and grantable to a role. Every restore also asks for the backup's label typed
  back and the password in the same request, and a wrong answer counts toward
  locking the account.
- **A safety backup first, always.** It cannot be skipped, and the restore does
  not start if it fails.
- **Restore beside, then swap.** The dump goes into a new database while the
  wiki stays up and readable, is checked there (tables, accounts, and no
  migrations this build has never run, so a backup from a newer Tesria is
  refused), and is swapped in with two renames. The database it replaces is
  renamed, not dropped: that is the undo.
- **The application restarts itself** afterwards, which is how a restored
  database gets its migrations, its runtime role grants and a clean pool.
- **Maintenance while it runs**: reads pass, writes get 503 with a body the SPA
  turns into an overlay, API tokens and MCP get the same answer, and the collab
  sidecar closes every document so an open editor cannot write post-backup
  content back into the restored wiki.
- **Point-in-time recovery** replaces the whole cluster, so the `db` container
  now supervises its own Postgres and acts on a request file that only the
  `pgbackrest` sidecar can write. It forwards signals, so `docker compose stop`
  behaves as before and in fact shuts down faster (fast shutdown rather than
  smart).
- **Undo and the kept copy.** While a kept copy exists the page offers Undo and
  Remove. It ages out under the retention policy like a backup taken at the
  moment of the restore, which was the owner's call.
- **The record.** `backup.restored` is audited and raised as a Critical alert
  to every administrator, written after the restore so it lands in the restored
  database's own chain, and exactly once however many times the app restarts.
  The audit chain monitor explains the shorter chain instead of warning.
- **Four bugs found by running it against a real instance**, three of them
  invisible to any test: the application could not learn the restore had
  finished (its signal needed a column the restored database did not have,
  and then a table its role had no grant on; the answer is the database's
  OID, which needs neither); `restore.sh` runs as a child process and so had
  none of the shared helpers it was calling; `common.sh` clobbered the
  directory every restore writes its record to; and the carry-across created
  a temporary table outside a transaction, which Postgres dropped
  immediately.
- **Two sidecar bugs found and fixed on the way**: an unknown job kind fell
  through to "take a backup", so an older sidecar handed a restore would have
  backed up and reported success; and a sidecar restart failed every running
  job, which is right for a backup and wrong for a restore past its point of no
  return.

Tests: 29 new (728 backend total, all green), covering the right, the typed
label, the password and lockout, one-at-a-time, the time bounds, the preview
counts and blocks, the maintenance middleware, cancel in each state, the
startup paths, the kept copy's expiry under the policy, and the audit chain
explanation.

### Design 9.4: restore from the admin page (2026-09-22, Fable)

At the owner's request: the backups page could test a restore but never
perform one, and only the newest backup was reachable by hand. Now any
backup on the page can be restored, logical or point-in-time, behind a
gate stronger than deleting a space: a right of its own that only the owner
holds by default, the label typed back, the password in the request, a
safety backup that cannot be skipped, an audit entry on each side of the
restore and a Critical alert to every administrator. The design's center is
that the job status lives in the database being replaced, so a logical
restore goes into a new database and is swapped in by rename (the old one
is kept as the undo), the sidecar carries the backup history across from
its own volume, and the app restarts itself afterwards. Point-in-time
recovery needs Postgres stopped, so the `db` container gains a supervisor
that stops and starts its own database on a request only the `pgbackrest`
sidecar can write. The previous copy is kept as the undo and ages
out under the retention policy like any backup, the owner's call. Full
design in `dev-plan.md` as 9.4; the five decisions were answered the same
day.

### Design 9.3: space charts on the backups page (2026-09-21, Fable)

At the owner's request: a pie chart of backups against other usage against
free space, one per backup target. The sidecar already measures free and
total bytes; the one missing number is what the backups themselves occupy,
and that is the whole data-model change. The editor's existing SVG pie is
extracted and reused, so there is one pie in the product and no new
dependency. Cloud storage has no free space, so its card charts composition
and estimated monthly cost instead of inventing one. The same numbers drive
a low-space warning whose threshold is two backup sets, not a percentage.
Full design in `dev-plan.md` as 9.3.

### 9.3 Space charts on the backups page (2026-09-22)

Administration → Backups now shows how much of the disk the backups
themselves take, against everything else and what is free, and the cloud
target shows what it is holding as a composition of its two repositories
with a rough monthly cost.

- **One chart per disk, not per agent.** Both backup agents normally write
  to the same disk, and drawing it twice would double its free space on the
  screen and invite somebody to read two charts as two disks.
- **The low-space warning is measured in backup sets**, not a percentage.
  Ten per cent of a 100 GB disk is not enough for one backup; ten per cent
  of a 10 TB NAS is headroom nobody needs to hear about. The threshold is
  two sets: the next backup plus the one it replaces.
- **Cloud storage has no free space**, so its card charts composition
  instead of inventing a denominator, and the cost estimate carries the date
  its prices were checked, because a stale price shown as fact is a small
  lie on a card whose job is to be trusted.
- **One pie in the product.** The editor's SVG pie was extracted and shared
  rather than adding a chart library; the editor still renders the same
  geometry and colors, checked rather than assumed. It gained a fix on the
  way: a pie of one slice used to draw a degenerate arc, which is invisible.

**Corrected the same day, after the owner checked it against macOS.** The
chart said 1.6 TB free; his Mac said 761 GB. `df` on the container's own
volume reports Docker's *virtual* disk, which under Docker Desktop is sparse
and reports the size it may grow to rather than the space the host can still
give it. For a warning meant to fire before backups fill the disk, that is
the worst way to be wrong. A host bind mount is passed through the host's
filesystem, so `df` on one reports the real figures, and both sidecars
already have such a mount; the measurement uses it now and matches the
operating system. Along with it: a slice for the live wiki itself, a drop
shadow so the cards lift off the section behind them, and the administration
area using the width it is given instead of the 900px measure meant for
prose, which was making tables scroll sideways on a large display. The
Settings tab needed that separately, since its forms carry a 480px form
width and left most of a wide display empty; they are a grid now.

The slice colors were wrong on the first pass and the owner said so: gray
read as *disabled* rather than as a slice, and the wiki and its backups were
near enough in shade to be taken for each other. Four distinct hues now,
with their own tokens and dark-mode values, and the pie has the same drop
shadow as the cards.

A test caught the bug worth having tests for: when `df` and `du` disagree,
which they will, being two commands taken moments apart, clamping each
number separately made the slices sum to more than the disk. `df`'s free
space is authoritative now and the backups are fitted into what is used, so
the three always add up.

### 9.2 step 6: the restore drill, and how to come back (2026-09-22)

The last step, and the one that makes the rest trustworthy. **9.2 is
complete.**

**The drill.** Every offsite target is now restored for real on a schedule,
monthly by default: the newest dump is pulled back out of the target, loaded
into a throwaway database, counted and dropped. Nothing live is touched, one
target per pass, last in the pass so that proving a copy never delays taking
one.

It deliberately asks a different question from the integrity check that runs
after every backup. `restic check` asks whether a repository is internally
consistent; the drill asks whether it still turns back into a database. A
copy can pass the first and fail the second, and this was demonstrated
rather than assumed: against a repository restic called clean, in its words
"no errors were found", the drill failed. That is why a failed drill is a
**critical** alert and outranks everything else on the target card. A backup
that fails is noticed; one that quietly will not restore looks healthy until
the morning somebody needs it.

**"The machine is gone".** `backup-recovery.md` gained the chapter the rest
of it exists for: the server is destroyed and all you have is an offsite
copy and the passphrases, and here is the path back, step by step, ending
with what to check before calling it done. `architecture.md` gained the
section on how the whole thing is put together and why.

One bug worth recording. A `trap ... RETURN` used for cleanup reads tidily
and does nothing: the trap fires after the function has returned, its locals
are gone, and under `set -u` the body dies on the first variable it touches,
silently skipping the cleanup it exists for. The drill creates a database
and restores a whole dump, so it would have filled the disk a drill at a
time. Cleanup is explicit now.

### 9.2 step 5: the Storage targets screen (2026-09-22)

Administration → Backups now shows where copies of this instance are kept,
one card per configured target, built entirely from what the backup sidecars
publish. Keys and passphrases appear as fingerprints: the credentials live in
`.env`, are read only by the sidecars, and the application never holds one.

A cloud card shows its two repositories separately, the database against the
uploads and dumps, since they are written by different sidecars. A removable
drive gets a Copy now button. An instance whose only target is a removable
drive is told plainly that it has no offsite backup.

Test connection was dropped from the plan. The sidecars already test every
target on every pass and publish the result, so a card is never more than a
minute stale, and a button that re-ran what had just run would only be a
second way of saying the same thing.

Three bugs the live walk found that the shell testing could not:

- **A stale restic lock wedges a repository permanently.** A sidecar killed
  mid-run never releases its lock, and every later run then fails on a
  repository that looks broken but is only locked. Runs now clear a stale
  lock first, which makes a container restart during a backup survivable.
- **`cat config` failing does not mean the repository is missing.** A lock
  or a slow share look the same from outside, so an `init` refused for
  already existing is now read as "there but unreadable this time" rather
  than reported as a broken target.
- **Absence was being stored as a message**, which then sat on the card
  after the drive came back, reading "not plugged in" next to a green
  Healthy. Absence is a state; the screen says it in its own words, and a
  target coming back clears whatever it said while it was away.

Walked as an administrator with cloud, network drive and removable drive all
live, and as a plain member, who is refused both the overview and the copy
endpoint.

### 9.2 step 4: backups to a removable drive (2026-09-22)

The classic offline copy. Same path mechanism as a network drive, with three
differences, each because the drive is absent most of the time: it is never
scheduled, only asked for; retention is a count with no time window, so a
drive plugged in twice a year is not pruned for having been in a drawer; and
it finishes with `check`, then `sync`, then says so.

An offline copy is not a schedule, so an instance whose only offsite target
is a drive is told in words that it has no offsite backup, rather than shown
a reassuring green card.

Two things found by running it, both now in the runbook:

- **"Safe to remove" is about the data, not the eject.** `sync` flushes
  every byte, so pulling the drive cannot lose the backup, but the sidecar
  holds a bind mount that keeps the drive busy, so the operating system
  refuses to eject until `docker compose stop backup`.
- **The FAT32 warning is silent on macOS.** Docker Desktop passes a bind
  mount through its own file sharing, so the container sees a generic
  filesystem type whatever the drive really is. It reports properly on
  Linux, and a warning that is sometimes silent beats one that guesses.

Verified with a mounted disk image behaving as a removable volume: an
unclaimed drive reported absent with nothing written, a queued Copy now job
copied and verified and reported safe to remove, and the copy restored
byte-identical with 36 tables.

### 9.2 step 3: backups to a network drive (2026-09-22)

The uploads and the dumps can now also go to a NAS on the LAN, as an
encrypted restic repository on a mounted path. Tesria mounts nothing: the
share is mounted on the host, where the system already handles credentials
and reconnects, and Tesria is given the path.

- **A sentinel file decides whether anything is written.** An unmounted
  share leaves an ordinary empty directory behind, and backing up into that
  would not fail: it would quietly fill the boot disk while appearing to
  work. `claim-target.sh` writes `.tesria-backup-target` once, and the
  sidecar refuses any path without it. A mount-point check cannot do this
  from inside a container, where a bind mount is always a mount point.
- **A bind mount, not a cifs volume**, deliberately. A named network volume
  stops the container starting while the share is down, which would take the
  *local* backups down with it.
- **`Present` is separate from `Enabled`**, because for a path target they
  are different questions. Only the NAS raises an alert when absent; a
  removable drive that is unplugged is in a drawer, not broken, and alerting
  on that would train people to ignore the whole class.
- The runbook gained the **SFTP recipe** for PITR on a NAS (a connection
  rather than a mount, so the NAS being down is an error instead of a silent
  local write) and the NAS-snapshot recipe for immutability.

One platform note now in the runbook: on macOS, Docker Desktop has to be
allowed to share the mounted directory, and until it is the mount **hangs**
rather than failing, which looks exactly like a stuck backup.

Verified against a real NAS over SMB: unclaimed reported absent with nothing
written, claiming started backups, and the copy restored byte-identical with
36 tables and no plaintext in the stored files.

### 9.2 step 2: the uploads and the dumps go offsite, encrypted (2026-09-22)

restic now carries the uploads volume and the logical dumps to the
configured offsite target, which is what finally satisfies 9.2's
prerequisite that **the dumps are encrypted before anything copies them off
the box**. Local backups are untouched: 9.1's dump, tarball, inventory and
restore path all work exactly as before, and this runs after them, because
the rule is local first then replicate.

- **restic reads the uploads volume and the dump directory directly**, not
  the nightly tarball. A freshly compressed archive is new bytes end to end
  every cycle, so shipping it would have defeated deduplication entirely.
- **Retention is the admin page's policy, translated**, so both copies
  expire together instead of drifting apart: "keep the newest N and
  everything from D days" becomes `--keep-last N --keep-within Dd`. The rule
  is in C# with tests and mirrored in bash. With retention off there are no
  arguments and `forget` does not run, because `forget` with no rules
  deletes every snapshot.
- **`BackupTargets` gained a `Kind`**, `database` or `files`. A slot holds
  two repositories written by two sidecars, and the NAS and removable slots
  coming in steps 3 and 4 have only the files half.
- `check --read-data-subset=5%` after every run, rather than reading the
  whole repository back out of object storage each time.

Verified by restoring: a dump came back from the offsite repository
byte-identical to the local original and listed 36 tables, and the uploads
matched the live volume. The encryption was checked against MinIO's own
stored files rather than taken on trust, with a control to prove the scan
could find plaintext if it were there.

### 9.2 step 1: the offsite cloud repository (2026-09-22)

Backups can now go to S3-compatible storage as a second pgBackRest
repository, with WAL streaming to it continuously so point-in-time recovery
exists off the machine. Backblaze B2 is the documented default. Configured
in `.env` under `OFFSITE_CLOUD_*` and read only by the backup sidecars; the
admin page will show fingerprints, never values.

- **pgBackRest is pinned to 2.59.1**, and the version is a safety fix rather
  than housekeeping. Before 2.59, `archive-push-queue-max` did not take
  effect while archive-push was erroring (pgbackrest#2629), which is exactly
  when it is needed.
- **The configuration is a generated drop-in, not environment variables.**
  pgBackRest rejects a variable that is defined but empty, and Compose
  cannot omit one, so the obvious approach would have stopped WAL archiving
  on every instance that has no offsite target.
- **Three findings from testing a real outage**, now in the runbook. WAL is
  acknowledged to Postgres only once every repository has it, so a dead
  remote backs up `pg_wal`. The queue limit does save the disk, confirmed by
  experiment. And the important one: **WAL dropped at the limit is lost from
  the local repository too**, so an unattended cloud outage can cost
  point-in-time recovery locally, not just offsite. The archive-gap alert
  therefore fires at a backlog of three segments, long before the limit.
- **MinIO behind a compose profile** for testing with no cloud account, with
  a certificate, because pgBackRest has no plain-HTTP mode for S3.

Verified end to end: WAL and a full backup to the repository, the offsite
copy unreadable with the local passphrase (the separate-passphrase rule
holding in practice), an outage raising `backup.offsite_archive_gap`, and a
clean recovery afterwards.

### Design 9.2: offsite backups to cloud, NAS and removable media (2026-09-21, Fable)

The owner answered the seven decisions 9.2 had waited on since 2026-09-17,
added removable media to its scope, and offered a NAS for testing. The
full design is in `dev-plan.md`; the decisions that shape it are these.

**Every offsite secret stays in `.env` and is read only by the backup
sidecar.** The admin-page alternative was analyzed and put to the owner,
and it turns on one fact from the code: Data Protection keys live in the
database, so a UI-stored key would travel inside every backup along with
the means to decrypt it, and a key that can delete is the fatal case. The
owner chose `.env` for simplicity and safety. The screen shows fingerprints
the sidecar publishes; the app never holds a value.

**Three fixed slots, cloud, NAS and removable, each with its own
passphrase.** pgBackRest carries the database to the cloud slot (`repo2`,
PITR off the box); restic carries the uploads and the logical dumps to
every slot, encrypted client-side, which is what finally takes the dumps
off the box in something other than plaintext. **A NAS is not a pgBackRest
repository by default**, because a mounted path cannot be one safely from
the database container; SFTP is the documented recipe for a LAN-only
instance that wants PITR on its NAS.

**One mechanism for the two path targets.** NAS and removable drive are
both a host mount bind-mounted into the sidecar with a sentinel file on the
target itself, so the sidecar always starts and an absent mount is reported
rather than written into. They differ only in policy: the NAS is scheduled
and its absence an alert; the drive is on demand only, and an instance
whose only target is a drive is told in words that it has no offsite
backup.

### Design 8.5: wiki packs (2026-09-21, Fable)

The owner settled that the rebuilt manual's source of truth is the wiki, which
makes a portable, committable export a prerequisite rather than a nicety: the
last manual was lost with the database it lived in. This is the design for
that export and its import; Opus implements it next. The full entry is in
`dev-plan.md`, and the decisions that matter most are these.

**A pack carries content and structure, and no identities and no
permissions.** Every id in it is re-minted on import. Authors are recorded
by display name only, in a file, and every row on the target is attributed to
the importer, because matching identities across instances is an identity
decision and attributing words to the wrong real person is worse than
attributing them to the importer. Neither space permissions nor page
restrictions travel, since carrying their principals by name is how a
stranger with the right display name would end up with access; the manifest
records that restrictions existed, as counts, so the importer knows to set
them. `IsPublic` does not travel either: publishing is 5.5's two-step opt-in,
and a zip must not bypass it.

**Full version history travels; drafts and the trash do not.** Comments
travel with their threads and tombstones, and since what ties an inline
comment to text is the `commentId` on a mark inside the document, comment
ids are rewritten in content along with page links, attachment links and
mentions. A link to a page that is not in the pack is left as it was: on a
same-instance re-import it may still work, and a link that 404s honestly
beats one silently destroyed.

**Output is canonical**, sorted keys and fixed ordering, so the manual's pack
can be committed unzipped and a one-word edit diffs as a one-word edit. That
is a format requirement, not a nicety, and it is what 10.5 depends on.

**Import is atomic and treats the zip as hostile**: format checked first,
entry names validated against zip slip, size and count caps, every document
through the same `TryNormalize` door every page write uses, every
attachment's type re-derived from its bytes, and one transaction so a
failure leaves no half-space.

### The manual is gone, and the plan now says so (2026-09-21)

Finishing dev-plan 8.6 meant adding two sections to the user manual, and the
manual is not there. This database has four spaces and none of them is it;
the API documentation space went the same way; the oldest retained logical
backup, from 2026-09-17, already lacks both. Nothing in the repository held
a copy, because the manual was a wiki rather than a file.

Restoring 47 pages by point-in-time recovery in order to add two sections
would be the wrong trade, and the pages would be wrong anyway: they describe
a build from 2026-09-11, before the owner role, instance rights, custom
roles, the setup wizard, the tour, capture-based export, static sites, and
the very feature those sections were meant to document.

So **10.5 rebuilds the manual**, 8.6's last step folds into its "working
together" chapter, and the phase is recorded as shipped in its first five.
The media is specified as light theme with the blue accent, which is already
what `scripts/screenshots/` seeds before first paint, and every picture must
be regenerable from a committed spec, because a screenshot nobody can
reproduce is one that will be wrong after the next redesign.

CLAUDE.md's handoff notes, which still described both spaces as living
content, now say what actually happened. The lesson is worth more than the
pages were, and is written down in both places: **content that lives only in
the instance is content one reset deletes.** That is an argument for
sequencing the rebuild after 8.5's wiki packs, so a rebuilt manual has a
committed export to come back from.

### Deciding what to do with an assistant's change (2026-09-21)

Step 5 of dev-plan 8.6. Steps 3 and 4 made an outside write *visible*; this
makes it decidable, and closes the overwrite it started from.

**A banner above the editor** counts what is waiting and offers Accept all
and Reject all. It sits with the connection status rather than inside the
page, because it is about the document rather than any one place in it. You
can also just edit inside a highlighted run: it is ordinary text that happens
to carry a mark.

**Pressing Update accepts.** Whatever an assistant or the API changed is
resolved as the last thing before the content leaves: its deletions really
go, its insertions become ordinary text, and that is what gets published.
The banner is for people who want to decide first.

**A publish that would overwrite an unseen change is refused.** The editor
sends the version its draft was last brought up to date with; if the page has
moved past it, the server answers 409 and the answer *carries the page as it
now stands*, so the editor reconciles against it and shows the difference
instead of just being told no. That is what a status code alone cannot do.

Sending the version is optional on the wire, deliberately: an API or MCP
caller holds no draft that could be stale, so last-write-wins stays right for
them and no existing script breaks.

The non-collaborative editor gets none of this. With no shared document there
is nowhere for an outside write to land, so it is always looking at exactly
what it loaded.

### An assistant's write appears in the editor you have open (2026-09-21)

Step 4 of dev-plan 8.6, the live case. Step 3 caught up a page you reopened;
this one shows the change while you are looking at it, without a reload.

After a page write commits, the app tells the collaboration sidecar, beside
the webhook dispatch and for the same reason: it is an outbound call about
something that has already happened. The sidecar applies it to the document
if somebody has it open, and otherwise does nothing, because a page nobody is
editing is reconciled on its next load anyway. That keeps the sidecar's
memory a function of how many people are editing rather than of how busy the
API is.

The call is best effort with a short timeout. The page is already saved by
then, and a sidecar that is down or restarting just means the reconciliation
waits for the next load. A healthy API refusing to save because an optional
live-editing service is unwell would be the worse trade.

**What a highlight says depends on how the caller signed in**, not on
anything it says about itself: a browser session is the editor, an API token
is the API, and the same token used against `/mcp` is an assistant. A write
from the editor records the version and draws nothing, which is not laziness:
publishing and then carrying on typing is ordinary, so by the time the
notification arrives the draft is legitimately ahead of the page, and
diffing would strike through the words you are still writing and attribute
them to somebody else.

### An assistant's write is no longer lost when you reopen a page (2026-09-20)

Step 3 of dev-plan 8.6, and the first with teeth. Until now, a write from the
API or MCP changed the published page and never touched the Yjs document the
editor actually edits, so reopening a page you had drafted and pressing
Update wrote your stale copy straight over the assistant's work. That case is
closed.

**The sidecar reconciles a stale document before anyone opens it.** It keeps
`meta.version` in the shared document, the page version that document was
last brought up to date with, and compares it with the page's current
version when it loads. If the page has moved on, the difference arrives as
tracked changes: what the write removed struck through, what it added
highlighted, labeled with who and how long ago. Nothing is discarded and
nothing is silently kept as if it were newer.

**The schema is built, not copied.** The sidecar needs the editor's schema to
turn stored JSON into Yjs, and a second copy of a schema is a slow-motion
bug: a node added in `extensions.ts` and forgotten in the sidecar would be
dropped from every document it reconciled, quietly. So the collab image now
builds the real thing from `src/web` at image build time.

**A document with no `meta.version` is adopted, not reconciled.** Every draft
that existed before this shipped is in that state, and their unpublished
edits are not an assistant's changes; marking them all up on the first load
after deploying would be noise in the one feature whose job is to be
believed.

Two bugs only running it could have found. ProseMirror materialises every
attribute a node type declares, so a paragraph that comes out of the CRDT
carries `textIndent: 0` while the same paragraph as the API stored it carries
none. Compared directly, every block of every document read as changed, on
every reconcile. Both sides go through the schema before anything is compared
now, with a regression test.

And the one that mattered more: a reconcile that was not written back was
re-run from the same stale state next time the document loaded, and since
Yjs merges rather than replaces, the second run's insertions landed beside
the first's and the assistant's paragraph appeared twice. Hocuspocus only
stores a document that changed while somebody was connected, so a reconcile
nobody then edited was simply forgotten. It is written immediately now.
Found by restarting the sidecar with a page open, which is how the fix is
verified too.

Hover labels also stopped printing raw ISO timestamps and say "2 minutes ago"
like they were meant to.

### Tracked changes from outside the editor: the diff (2026-09-20)

Step 2 of dev-plan 8.6, and still inert: nothing calls this yet. Steps 3 and
4 are what will.

`editor/externalEdits.ts` works out what an outside write did to a page and
shows it inside an open draft. The diff is block-level, so a rewritten
paragraph reads as the old one struck through followed by the new one
highlighted, which is a diff anyone has read before. Character-level merging
inside a paragraph is a refinement for later, and the block version is the
one that cannot lie: a word-level merge of two genuinely different sentences
produces a third sentence nobody wrote.

Two blocks are "the same block" by canonical JSON: keys sorted, null and
empty attributes dropped. That last part is not fussiness. TipTap writes a
paragraph as `attrs: { textAlign: null }` and the API writes the same
paragraph with no attrs at all, and without canonicalising, every reconcile
would declare every block changed.

The Yjs write is a diff, not a replacement. It goes through
y-prosemirror's own `updateYFragment`, the minimal-change applier the
collaboration extension uses for every keystroke, so blocks nobody touched
are left alone in the CRDT and other people's cursors stay where they were.
A wholesale replacement would look identical in a screenshot and clobber
anyone typing at the time.

**Vitest joins the web package** for this, and CLAUDE.md now says where the
line is: logic yes, rendering never. A mounted-and-asserted component passes
while the real page is broken, which is the exact failure this repo has
shipped before and the reason routing and layout still get a live walk. The
33 tests here cover the two cases the plan names by name, a draft that
already carries pending marks and a block with nothing a mark can sit on,
plus the properties that matter in the CRDT: untouched blocks keep their
identity, the whole reconciliation is one update, and it reaches a second
peer.

### Tracked changes from outside the editor: the marks (2026-09-20)

First of the six steps in dev-plan 8.6. Inert on its own: nothing produces
these marks yet, and the next steps are what make them appear.

**The problem it is the start of.** A page has two stores, the published
version in Postgres and the Yjs document the editor actually edits. The API,
`PageWriter` and the MCP `update_page` tool write the first and never touch
the second, and the editor only seeds the second when it is empty. So once a
page has been opened in the editor, a write from anywhere else is invisible
there, and pressing Update writes the stale draft back over it. Every
assistant-written change is one browser edit away from being lost.

**What landed.** Two marks in the shared editor schema, `externalInsert` and
`externalDelete`, carrying the source, the actor and the time, so a highlight
can say *Added by MCP · Docs Bot* on hover. Green for added, struck red for
removed, the convention every diff uses; the source changes the label, not
the color, so "what changed" and "who did it" do not compete for the same
channel. Both are non-inclusive, so typing at the edge of a highlighted run
produces your own ordinary text rather than more text attributed to a bot.

`acceptExternalEdits` and `rejectExternalEdits` resolve them, including the
case worth the extra pass: a block the write deleted entirely is re-inserted
with every character marked, so accepting has to remove the block rather than
leave an empty paragraph behind.

**A published version can never carry them.** `PageContent.TryNormalize` is
the single door every page write goes through, and it strips both marks
there rather than trusting every client to have accepted first. The text is
kept, deliberately, including text marked as deleted: this is a safety net,
not a merge, and it cannot know what the human meant. Skipped entirely unless
the raw JSON mentions the marks at all, since a page save is hot and almost
no document has them.

One thing fell out of it: the print rules that hide "somebody is working on
this" highlighting named `.comment-mark`, a class nothing renders, so a
commented passage had been printing with its yellow ground despite the export
docs saying otherwise. Both selectors are now the ones actually rendered.

### An exported site works from the filesystem (2026-09-20)

Reported by the owner: unzip a site export, open it, click a link in the
sidebar, and Chrome shows a listing of the folder instead of the page.

Every link pointed at a directory (`../frontend-architecture/`). A web server
answers that with the directory's index file; nothing does that for
`file://`, so the browser listed the folder. Links name `index.html` now, in
the sidebar, in the page body and on the brand, which works in both places
and costs a hosted site nothing. The 404's brand link stopped pointing at
`/`, which on a filesystem is the root of the disk.

So "unzip it and open index.html" is now a real way to read an export, and
the export panel says so.

### The width toggle comes back in an HTML export (2026-09-20)

The reading view lets you switch a page between its normal column and full
width; the capture route had no action bar, so an export lost both the
control and the page's own setting, and every exported page was full width
whether or not it was written that way.

An HTML export now opens at the width the page has in the wiki, and carries
the toggle to change it. It sits in the top bar beside the appearance menu,
because an export has no action bar to put it in. Like the theme, the
reader's choice is theirs and sticks across the pages of a site; until they
make one, each page opens at its own width. Both labels ship in the markup
and the script shows one, since a captured page has no React left to
re-render the text.

It is hidden on a phone, where full width and normal are the same thing,
which is the reason the application hides its own there.

**PDFs keep no width control and no column cap.** The sheet is the width, and
capping the text at 900px would leave an A4 page with margins nobody asked
for.

### Two export tweaks (2026-09-20)

**PDF page numbers.** The footer was a flex row whose page number, the word
between them and the total were each their own item, so `space-between`
spread them the width of the page: "Every element    1    of    4". The
numbering is one item now, so it reads "1 of 4" at the right margin with the
title still on the left.

**The space tile in an export** is drawn in the theme's accent instead of one
of the app's twelve per-space colors. Those colors exist to tell spaces
apart in a list, and an export is one space by definition, so the color
carries no information there and may as well match the rest of the page. It
follows the reader's accent and light/dark with everything else, and the
letter uses the same token a filled accent button does, so the contrast is
the one already tuned per theme. **Inside the app nothing changes**: spaces
keep their own colors, where they still do their job.

### An exported page looks like the product it came from (2026-09-20)

Reported by the owner the same day the capture work shipped: the HTML export
was the page and nothing else, which is faithful but does not look like
Tesria. It now carries the application's own chrome.

**The top bar**, on both the single-file HTML export and a published site:
the mark and the instance name on the left, the appearance menu on the right.
Not a row of three buttons, the real menu: system, light and dark with the
system row naming what the operating system currently resolves to, and the
six accent swatches. A single file's brand is not a link, because there is
nowhere in one file to go.

**The sidebar**, on a published site: the space's icon, its key, its public
badge and its name, then the page tree under the PAGES heading, indented by
depth with the page you are on marked. The site's index and its 404 carry it
too, so a reader who lands on a broken link can still navigate.

The markup is the application's own class names against the application's own
compiled stylesheet, so restyling the app restyles every export with it. One
rule an export needs for itself: the app hides the sidebar on a phone because
its action bar covers the same jobs, and an export has no action bar, so the
sidebar stacks above the page there instead of disappearing. That would have
shipped a site with no navigation at all on a phone, and only opening one at
375px showed it.

**PDFs are unchanged.** A PDF is paper: no bar, no sidebar, no theme, still
forced light. The render route now distinguishes the two, so an HTML export
keeps the reader's theme (and can change it) while a PDF stays print.

The wordmark is the instance name, which is half of the instance branding the
owner wants: an administrator already sets it and it already travels. The
replaceable mark is the other half; `SiteChrome.Brand` is the seam it will
arrive through, and `docs/roadmap.md` records what is left to build.

### Exports that look like the page (2026-09-20)

Reported by the owner: exports flatten elements and look nothing like the
rendered page, tables worst of all. Three causes, and none of them was a
table bug. The exported stylesheet was fifteen lines with no table rule in
it. `ProseMirrorRenderer.cs` rendered thirty-five node types a second time in
C#, copying colors out of `index.css` by hand. And eight node types are
React node views whose output only a browser can produce.

**PDF and HTML are now captured from the real page.** The sidecar loads a
chrome-free route rendering the same read-only editor, in the same `.paper`,
under the same stylesheet as the reading view; waits for that page to signal
it has finished drawing; and prints it or serializes its DOM. Tables keep
their column widths, header styling, cell colors and spans. Panels keep
their colors and icons. Diagrams are diagrams, maths is typeset, charts are
charts, live blocks carry their data. Markdown is still rendered from the
document, because it is a genuinely different target.

Print rules now live in `index.css` beside the screen rules they modify:
rows, panels and images do not split across pages, an expand prints open, an
embed prints as its card, and editing furniture (the language dropdown, the
copy button, resize handles) is gone from the paper.

**A space can be published as a static site.** `GET
/spaces/{key}/export/site` returns a zip in Cloudflare Pages shape: one
directory per page mirroring the tree, the compiled stylesheet verbatim, real
asset files, internal links rewritten to relative paths, `404.html`. The
default audience is what the public can already read, which is the leak-proof
choice for a documentation site: it cannot contain a private page by
accident, whatever the exporter can see. Light, dark and system come with it,
through the app's own theme script and a toggle, which is the only JavaScript
in the output.

The sidecar's posture changed to make this possible, and is worth knowing:
it used to run with the network off and be handed a document; it now reaches
the app service and nothing else, enforced by a request allowlist and by the
compose network. It authenticates with a short-lived, read-only, page- or
space-scoped render token that grants nothing its user did not already have.

Building the fixture that all of this is measured against turned up four
real bugs, all fixed and covered: a mention of a user id with no account
returned 500 on save; a half-committed page create left a page that was
invisible and could never be edited again; version numbers came from the
current-version pointer, so a page missing it collided with itself forever;
and the Markdown export did not sanitize link hrefs, so a stored
`javascript:` URL came out of an exported file as a working link.

**The second C# renderer is gone.** With nothing reaching it, `ToHtml` and
the four `RenderHtml*` methods were deleted, along with thirteen helpers that
only they used (found by walking what `ToMarkdown` can actually reach, not by
eye). `InlineAssets.cs` went too: a captured export inlines its own assets in
the browser. `ProseMirrorRenderer.cs` is 726 lines where it was 1145, and it
renders exactly one format. Its header now says why nothing in it should grow
an HTML path again: an export that should look like the page is a capture of
the page. The table-of-contents tests moved to Markdown, where what they pin
is which headings are listed, in what order and how numbered; bullet shapes
belong to the stylesheet now and are covered by the fidelity matrix.

`docs/export-fidelity.md` has the matrix: thirty-four element types, counted
in the reading view, the print rendering and the exported site, all
identical.

### The welcome tour and tips (2026-09-20)

A new account is shown `/welcome` once: five screens, each one of 10.4's
clips beside three sentences, covering spaces and pages, writing, working
together, finding things, and the profile. Leaving it by any route counts as
skipping, including closing the tab, so nobody is asked twice. Profile →
Tour and tips can reopen it.

**Tips** teach the things the product will not otherwise mention: type `/`
for blocks, select text to comment on exactly those words, Ctrl or Cmd and K
for a link, drag pages to rearrange them. Fifteen of them, each fired by a
condition that makes it worth saying at that moment: the third page you
create suggests templates, a page somebody else wrote suggests Watch, a
second visit to an uncommented page suggests commenting.

The restraint is the feature. One at a time, at most three a day, each one
only once, and never over a dialog, inside the setup wizard, during the tour,
while a menu is open, or while the editor has a selection. A tip whose
control is not on screen is passed over rather than queued. Every tip carries
**Got it** and **Turn off tips**, the latter with ten seconds of undo.

The counters behind the triggers stay in the browser, keyed by user id.
Losing one costs a repeated tip, and the alternative is telling the server
how often somebody opens the editor. Dismissals do go to the server, so
retiring a tip holds across devices.

Accounts that existed before this shipped were marked as having skipped the
tour by the migration: nobody is shown a tour of a product they already use,
and tips arrive on for everyone.

One thing only running it could have shown: at 375px the clip loaded, stayed
paused and displayed nothing at all, because the `autoplay` attribute is a
request rather than a guarantee. `Clip` now asks the video to play and falls
back to the still when that is refused, which is also what somebody who has
asked for reduced motion gets.

### First-run setup for the owner (2026-09-20)

A new instance opens on `/setup` instead of a sign-in form: the steps down
the left, one step's form on the right, and a finished step you can go back
to. It creates the owner account and its recovery codes, names the instance,
asks who can join and whether anonymous reading is on (5.5), puts the rights
matrix (11.1) in front of the owner to approve, and settles the backup
retention policy (9.1). Email, two-factor and a first space can be skipped.

Every step saves through the endpoint that already owns its setting; the two
new routes only record that a step was answered. **Completion is checked
against evidence rather than clicks**: whether the recovery codes were
acknowledged, whether settings were written, whether the matrix was reviewed,
whether a policy was saved. Clicking through every step without doing any of
them returns 409 naming the earliest one outstanding, and the wizard jumps
there. The server also refuses to record a required step as skipped, so the
client cannot decide otherwise.

The wizard is convenience, not enforcement. `/register` still works and still
makes the first account the owner, and the API is not blocked while setup is
unfinished. An instance upgraded from before the wizard existed is stamped
complete and never sees it.

Two bugs came out of running it on a genuinely empty instance, neither
visible from reading the code. `SetupGate` sitting beside the outlet raced
`SessionGate`'s redirect and lost, so a fresh instance landed on `/login`.
And `POST /auth/register` returns a different shape from `/auth/me`, with no
permissions and no role name; the SPA had been setting that partial object as
the session, which left a newly registered owner holding no rights at all.
That one was not new to the wizard: it affected every registration since
permissions existed.

`scripts/scratch-instance.sh` brings up a throwaway instance on port 8099
under its own compose project, which is how the first-run path gets tested
without touching a real one.

### Onboarding media harness (2026-09-20)

The screenshot harness records clips now, not just stills. A shot with
`record` gets its own browser context with video on, carries the signed-in
session across as storage state (so no sign-in shows in the opening frames),
and its poster PNG is captured from the state the clip ends in, which is
what stops a still and its clip drifting apart. New steps: `typeSlowly`,
`moveTo`, `dragTo`, `deleteSpace`, `skipCapture`, and a shot-level `css`.

`scripts/screenshots/onboarding.sh` runs `onboarding.json` in both themes
into `src/web/public/onboarding/` and fails if any clip passes 600 KB or the
set passes 8 MB. The set is twelve clips and two stills, light and dark,
52 files, 7.6 MB. The wizard (10.2) and the tour (10.3) embed them.

The spec builds the space it films: `DEMO` with three pages, plus two more
so the spaces list looks like a list, and deletes all three afterwards.
Deleting a space needs the key typed back and the password in the same
request (11.3), so `deleteSpace` supplies both from the harness's own
credentials rather than putting a password in a JSON file.

Looking at the first recordings caught two leaks that nothing else would
have: the spaces list filmed every real space on the instance, and `@`
resolved to a real person. The spaces clip now hides non-demo cards before
its first frame, and the mention types `@Demo` so it lands on the recording
account. `docs/onboarding.md` has the media table, the shot each file comes
from, and what to re-record when the UI moves.

### Anonymous reading is opt-in twice (2026-09-20)

An instance that publishes nothing now looks like one. Before, a visitor
with no account got the public shell and a Spaces page reading "Nothing is
published for public reading", both with the instance-wide switch off and
with it on but nothing marked public. Now they get the sign-in page.

Anonymous reading exists only when **Allow public spaces** is on **and**
at least one non-archived space is public, which is the rule the server
has always applied per space; this makes the first screen agree with it.
A deep link to a page still carries where it was going, so signing in
lands the reader there rather than on the spaces list.

`GET /api/instance` is the one thing the SPA may ask before a session
exists: the instance name, whether the instance still needs its owner
(10.2 will read that), whether there is any public reading, and whether
sign-up is open. Four fields, with a test that asserts it is exactly those
four and leaks no space key or address. The login page uses the last two:
"Browse what is public" appears only when there is something to browse,
and "Create one" only when registration is open, unless the URL carries an
invite token, which is its own authorization.

Signed-in behavior is unchanged. The "Nothing is published" copy stays
for the case it still describes: a signed-in user who can see no spaces.

### Fix: eight more confirmations that did nothing (2026-09-20)

The previous entry claimed no native confirmation was left in the SPA. That
was wrong: the search behind it looked for `window.confirm`, and eight call
sites use the bare `confirm(...)` spelling. All eight were dead in an
embedded browser, where the call returns false and the guarded action never
runs: purging a page, deleting a comment, deleting an attachment, revoking an
API token, deleting a group, deleting a webhook, restoring a version, and
moving a page to the trash.

They now use `ConfirmDialog` like the rest, and the question says what
actually happens: deleting a group names the space access that goes with it,
deleting an attachment says embeds stop rendering, restoring a version says
nothing in the history is lost. Restoring a version and moving a page to the
trash are reversible, so they are ordinary confirmations rather than
destructive ones, and focus starts on the affirmative button instead of
Cancel.

The search that found them covers `confirm`, `prompt` and `alert` in any
spelling, and all thirteen were then exercised in the running app.

### Deleting a space (2026-09-20)

Space settings gains a **Danger zone** with **Delete this space**, shown only
to a role holding the new-in-11.1 `spaces.delete` right (administrators and
the owner by default). It destroys the space and every page in it, with all
versions, comments, attachments, labels, restrictions, webhooks, templates
and watches. The Trash does not hold any of it: only a backup taken before
the deletion still does, and the dialog says exactly that rather than
claiming there is no way back at all.

The dialog asks two things, for two different mistakes. Typing the space key
proves the right space is on screen, which is the error people actually make.
The password, verified by the server in the same request, proves
deliberateness. The ordinary five-minute sudo window is not enough here: "you
signed in a few minutes ago" is not "you mean it". A wrong password counts
toward lockout exactly as it does at sign-in, and an account with no password
(provisioned through SSO) answers with a one-time code instead.

A space's own administrator still only archives, which is reversible. The
right is an instance one, not a space permission, and it is not a way into a
space you cannot see: that is a 404, and an administrator who needs to reach
one uses recover-access first, which is audited.

The rows go in one transaction and the attachment files afterwards, best
effort, so a crash between them leaves orphaned bytes rather than a
half-deleted space; those are logged by storage key for the runbook's sweep.
`space.deleted` in the audit log keeps the key, name, page and attachment
counts, bytes and who did it, which is the only surviving description of what
was destroyed, and a Critical alert fires whoever did it.

The collaboration sidecar learned not to resurrect what has been deleted. Its
store hook now writes only when the page still exists, and a 15-second sweep
closes connections for any open document whose page is gone, which is what
reaches an editor left open and idle.

### Custom roles (2026-09-20)

A role is now a named set of rights within a tier, not just the one
built-in per tier. **New role** on Administration -> Roles takes a name, a
description, the tier and a role to copy from (the tier's built-in by
default), and the new column joins the matrix like any other. Custom
columns can be renamed in place and deleted; the built-ins can be neither.

Who may create what follows 11.1: a user-tier role needs
`permissions.edit_user_tier`, an admin-tier role needs the owner's
reserved `permissions.edit_admin_tier`, and copying from a higher tier
needs the same right as editing it. Nobody creates an Owner-tier role.

On the Users page a role picker appears next to the tier whenever that
tier has more than one role to choose between, listing only the roles the
viewer may assign. Assigning within a tier needs `users.assign_roles` and
leaves the tier alone, so alerts, the two-factor requirement and the
owner's reserved powers are unaffected; crossing tiers is still the 10.1
promotion or demotion, still audited as `user.role_changed` with both role
names. Both paths ask for the password again.

Deleting a role that someone still holds is refused with "N account(s)
holds this role. Move them to another role first." rather than silently
stranding people on a role that no longer exists.

### Fix: confirmations that silently did nothing (2026-09-20)

Five admin actions guarded themselves with `window.confirm`, which is the
same trap as the Resolve bug below: an embedded browser may refuse the
dialog and simply return false, so the action never runs and nothing
appears on screen to say why. Deleting a role, resetting a role,
publishing or withdrawing a space, and transferring ownership were all
one browser setting away from looking broken.

They now use an in-page confirmation (`components/ConfirmDialog.tsx`),
which also lets the question carry more than a line of plain text: the
delete dialog says whether anyone still holds the role, and publishing
names the page and attachment counts that are about to become readable by
anyone. Escape cancels, and a canceled question resolves rather than
leaving its promise hanging.

### Fix: Resolve did nothing on a security alert (2026-09-20)

Reported by the owner: pressing **Resolve** on Administration -> Security
had no effect, on acknowledged alerts too. The button asked for the
optional resolution note through `window.prompt`, which throws where a
browser refuses dialogs (the in-app browser always, and Chrome once
someone has ticked "prevent this page from creating additional dialogs").
The click died there, before it ever called the endpoint, which is why
Acknowledge worked and only Resolve looked dead.

The note is now an inline field on the alert, with Resolve and Cancel,
Escape to dismiss, and the note left out entirely when it is blank. The
endpoint was never at fault; a test covers resolving with no note, which
is the path this makes reachable. Two alert kinds added since the label
map was written, `owner.transferred` and `permissions.expanded`, were
showing their raw keys and now read as sentences.

Four alerts had accumulated on this instance while the button was broken.
One of them, a `permissions.expanded` from testing 11.1, was resolved with
a note as the live check.

### Administrators can be allowed to promote (2026-09-20)

A 29th right, `users.promote_admins`, **off for administrators by
default**: an owner has to decide to allow it, because promoting is how an
administrator would widen the circle that can act on the instance.

It only promotes. Demoting an administrator stays with the owner's
reserved right, so two administrators cannot unmake each other, and the
owner's own account remains out of reach either way. Promotion still
raises the `admin.promoted` alert whoever does it. On the Users page the
Make admin and Demote buttons are gated separately, each saying why when
it is disabled.

`PUT /admin/users/{id}/role` is now reached by either right, so its check
moved into the handler; the endpoint-metadata test lists it as the third
documented exception.

Adding this right showed that `RoleSeed` had no way to hand out a right
introduced after an instance was built: it never edits an existing role,
so a new one would have arrived switched off for everyone, the owner
included, with nobody told. `SiteSettings.SeededPermissionKeys` now records
what has been handed out, and a key that is new against that record reaches
the roles whose defaults include it. A key that is simply unrecorded (the
first start after this change) is recorded and not granted, because an
owner may already have taken rights away and a seed must never undo a
decision. Two tests cover both directions.

Tests: 517 pass, three new.

### Roles with assignable rights (2026-09-20)

Dev-plan 11.1, implemented by Opus 5 against the spec Fable 5.1 wrote the
same day. The instance role enum becomes the **tier**; what a person may do
is their **role**, a named set of rights.

- **A catalog of 28 rights in code**, from `spaces.create` to
  `backups.policy`, each with a label and a description, grouped by area.
  Three more are reserved to the owner and never stored: changing tiers,
  transferring ownership, and editing administrator or owner rows.
- **Grants live in the database** (`Roles`, `RolePermissions`), cached for
  30 seconds and invalidated on write. `RoleSeed` creates the three
  built-ins at startup and attaches every account; it never edits a role
  that already exists.
- **Every administrative route names its right.** `RequireAdmin` is gone
  from the routes, and a test walks the endpoint metadata to prove nothing
  was missed. Settings are checked **field by field**, so an administrator
  may be allowed to fix the email server but not to open registration; a
  request touching anything they may not change is refused whole, naming
  the right.
- **Content rights too:** creating spaces, exporting, API tokens, and two
  delete rights. **Users can no longer delete pages other people created**,
  only their own. That is the one behavior this changes on upgrade, and
  the Roles tab says so until the owner reviews it.
- **Rights are additive over space permissions, never a bypass.**
- **Anonymous readers** get what the built-in User role holds, so a visitor
  is never more privileged than a member.
- **API tokens follow their owner's role:** withdrawing `tokens.use` makes
  existing tokens inert rather than deleting them, and granting it back
  restores them. MCP rides on tokens, so it is covered.
- **Administration → Roles:** the matrix, with rows grouped by area and a
  column per role. Administrators may shape user roles; only the owner may
  touch administrator or owner rows, and those columns are locked with the
  reason. Saving is sudo, previewed as "gains and loses" per role, audited
  as a diff, and alerts every administrator when a role gains rights.
- The SPA renders from `/auth/me`'s new `permissions`: which admin tabs
  exist, whether Delete appears on a page, whether the profile offers API
  tokens, whether New space is there. Every one is enforced server-side.

Two things found while implementing, both fixed:
- The Roles routes were first gated on `permissions.view`, which is
  assignable. An owner who cleared their own row would have had no way back
  to the matrix. Reaching the tab is now "may see it, or may edit any row",
  and the owner's edit right is reserved, so the way back is never closed.
  A test covers it.
- `PageDetailResponse` gained `createdById`: without it the SPA could not
  tell whether "delete pages you created" applied to the page in front of
  you.

Tests: 514 pass, 16 new in `InstancePermissionTests`.

**Verified live** on this instance: the app rebuilt, the seed created the
three roles (Owner and Administrator with all 28 rights, User with four)
and attached all four accounts. The Roles tab renders as the owner with
every column editable, the upgrade banner about page deletion, and the
reserved group listed without checkboxes. Unchecking **Change the
retention policy** for Administrator and pressing Review changes reported
"Administrator (1 account) Loses: Change the retention policy"; the change
was discarded rather than saved, since saving needs the owner's password
and would alter the live configuration. The tab gating for a restricted
administrator is covered by tests rather than a browser pass: it needs a
second account signed in, which the assistant cannot do.

### Design: roles with assignable rights (2026-09-20)

Dev-plan Phase 11, written by Fable 5.1 at the owner's request, after
10.1 shipped. Nothing is implemented yet. The owner decided: the owner
edits every role's rights and administrators edit only user-tier roles;
the owner is subject to the matrix except for roles, ownership and the
matrix itself; users get "delete pages you created" by default but not
"delete pages created by others".

- **11.1 Instance rights:** a catalog of 28 assignable rights and 3 reserved to the owner, in code; grants in the
  database, one built-in role per tier (the existing enum becomes the
  tier), every administrative route named by its right, settings checked
  per field, delete rights layered over space permissions, tokens going
  inert when the right is withdrawn, and a Roles tab with a reviewed,
  sudo-guarded, audited, alerting save. Defaults change one thing on
  upgrade: users can no longer delete pages made by others.
- **11.2 Custom roles:** named rights sets within the User or Admin tier,
  assignable without changing anyone's tier.
- **11.3 Delete a space:** an instance right (`spaces.delete`, administrators
  and the owner by default) from the space's settings page, confirmed by
  typing the key and the password in one request, audited with the counts
  of what was destroyed, and alerting every administrator.
- **10.2** gains a required wizard step where the owner reviews the matrix.
  Phase 11 now precedes the rest of Phase 10 in the order of execution.
- **5.5 Anonymous access is opt-in twice** (added the same day): an
  instance publishes nothing unless the switch is on *and* a space is
  public, and until then anonymous visitors land on sign-in rather than an
  empty Spaces page. The wizard's registration step gains the switch, off
  by default. One anonymous endpoint, `GET /api/instance`, carries the
  facts the SPA needs before a session exists.

### The Owner role (2026-09-20)

Dev-plan 10.1, implemented by Opus 5 against the spec Fable 5.1 wrote the
same day. `UserRole.Owner = 2` sits above `Admin`, and every administrative
check now reads "this role or above" rather than naming both.

- **Exactly one owner, always.** The first account on an empty instance is
  the owner (local or SSO). On an existing instance, a startup step
  (`OwnerSeed`) promotes the longest-standing active administrator and
  audits it as `owner.assigned`. On this instance that is the owner's own
  account.
- **Only the owner changes roles.** `PUT /admin/users/{id}/role` moved to a
  new `RequireOwner` policy, which carries the same two-factor rule as
  `RequireAdmin`.
- **Ownership moves only by transfer.**
  `POST /admin/users/{id}/transfer-ownership` (sudo, audited) makes the
  target the owner and the caller an administrator in one save, so the seat
  is never empty and there are never two. It raises a Critical
  `owner.transferred` alert to every administrator, including the one who
  just gave it away. Nothing is rotated: the role is read from the row on
  every request, so both sessions simply mean something different from the
  next request onwards.
- **The owner cannot be demoted, suspended, or assigned.** That replaces the
  old "cannot demote or suspend the last administrator" rule, which existed
  because the seat could otherwise be emptied.
- **An administrator cannot act on the owner's account**: no password reset,
  no revoking its sessions or tokens. This was not in the spec and is the
  one thing added while implementing: without it the role guards are
  theatre, since a reset link is a way into the account and repeated session
  revokes keep the owner out of their own instance. The UI hides those
  actions on the owner's row and the server refuses them.
- **UI:** an owner badge, the owner first in the user list, a **Transfer
  ownership** action with a confirmation naming the person, and role
  controls disabled for administrators with the reason on hover.
- `SiteSettings.SetupCompletedAt` arrives with this item, though it belongs
  to 10.2: the seed stamps it so an upgraded instance is never sent through
  the setup wizard.

Tests: 498 pass, 12 new in `OwnerTests`. Two tests in `AdminPanelTests` that
encoded the last-administrator rule were removed, and the assertions that
the first account has role 1 became role 2 across four suites.

**Verified live** on this instance, in all three states (the owner signed
each account in; the assistant does not enter passwords):

- **Upgrade:** the app rebuilt, the seed promoted the owner, chained its
  `owner.assigned` entry and stamped `SetupCompletedAt`.
- **Member** (`dnd-tester`): no Admin entry in the nav, `/admin` shows the
  refusal page, and the editor round trip is real: a page created,
  published, reopened, edited, saved, trashed and purged. All the space and
  content routes render with no `Uncaught` in the console.
- **Administrator** (`claude-assistant`): all nine admin tabs render; the
  owner's row shows the badge and carries no actions; Demote and Make admin
  are disabled with "Only the owner changes roles"; no Transfer ownership
  anywhere. The server refuses what the UI hides, from a real admin
  session: promote 403, transfer 403, reset the owner's password 403,
  revoke the owner's sessions 403, suspend the owner 400, while
  `/admin/spaces` still returns 200. The dashboard counts 2 administrators,
  the owner included.
- **Owner:** Transfer ownership appears on every other active row, with the
  confirmation "Make <name> the owner of this instance? You become an
  administrator, and only they will be able to change roles or hand it
  back." Declining it changed no roles. The transfer itself was not run on
  this instance; `OwnerTests` covers it.
- **Signed out:** unchanged, with private content masked and `/admin/*`
  redirecting to sign-in.

Worth knowing for future walks: the automated browser auto-dismisses
`window.confirm`, so a Delete click appears to do nothing. Stub `confirm`
for that click, the same class of gotcha as the `Enter` versus `Return`
note in `CLAUDE.md`.

### Fix: a sleeping laptop woke up to two false "agent offline" alerts (2026-09-20)

On 2026-09-17 at 09:06:51 UTC `BackupMonitor` raised `backup.agent_offline`
for both agents in the same second, and both had normal heartbeats a minute
later. The host (Docker Desktop on a laptop) had been asleep: every
container's clock jumped together, and the monitor's timer fired before the
sidecars' 60-second heartbeat loops caught up. The monitor now notices when
more than two intervals (10 minutes) have passed since its previous pass,
logs that the process was suspended, and skips the `backup.agent_offline`
and `backup.overdue` checks for that one pass. Failed jobs are still
reported on it, and the next pass judges with fresh heartbeats. New test
`A_suspended_host_does_not_make_the_agents_look_offline` in `BackupTests`
(26 pass). The two alerts from the 17th are still open on this instance.

### Design: Owner role, first-run setup, and onboarding (2026-09-20)

Dev-plan Phase 10, written by Fable 5.1 at the owner's request. Nothing is
implemented yet. The owner decided that Owner powers are ownership only
(promoting and demoting administrators, transferring ownership) and that
four setup steps cannot be skipped: recovery codes, instance name and
address, registration mode, and the backup retention policy.

- **10.1 Owner role:** `UserRole.Owner`, exactly one, transferable, never
  demotable; the first account is the owner; on upgrade the earliest active
  administrator becomes it and the wizard is marked complete.
- **10.4 Media harness:** the screenshot harness records silent WebM loops
  with PNG posters, both themes, from a demo space it creates and removes.
- **10.2 First-run setup:** `/setup` on an empty instance, eight steps, the
  four required ones enforced by evidence the server holds.
- **10.3 Tour and tips:** a five-screen welcome tour for new accounts and
  fifteen contextual tips, at most three a day, dismissable for good, with
  a profile toggle; existing accounts get tips only.

### Backups in the admin portal (2026-09-17)

Dev-plan 9.1, implemented by Opus 5 against the spec Fable 5.1 wrote
earlier the same day (the plan's Fable → Opus split; no override).
**Administration → Backups** shows both backup agents, sets one retention
policy for both, and runs a backup or a restore test on demand. The
dashboard gains the Health tiles 2.5 promised.

**The contract.** The two sidecars write `BackupAgents` (heartbeat,
schedule, disk, the policy they applied), `Backups` (the inventory,
mirrored from disk or `pgbackrest info` every minute; rows are never
deleted) and `BackupJobs` (the queue and run log). The app role can read
the first two and only append to the third
(`DatabaseRoles.ReadOnlyTables`, `AppendOnlyTables`). The app never
touches a backup file and has no download endpoint. The policy lives on
`SiteSettings`; `BackupPolicySeed` sets it once from
`BACKUP_RETENTION_DAYS` on upgrade (keep 3, and that many days).

**The sidecars were rewritten** on a shared loop, `deploy/backup/common.sh`.
Fixes to problems found on this instance:
- A failed backup retried after 24 hours; it now retries after 15 minutes,
  doubling to 6 hours, and nothing exits on failure.
- Every pgBackRest restart took a full backup and expired the oldest; the
  schedule and the full/incremental choice now come from the database.
- The logical sidecar did not wait for the database after a restart
  (this morning's missed backup); it does now.
- Six 0-byte `.tmp` files from interrupted dumps were removed on the first
  run, and a failing `backup.sh` no longer leaves one.
- A dump and its uploads archive now share one timestamp. Older pairs a
  second apart are matched up.
- Both sidecars have compose healthchecks.

**Retention** keeps a backup if it is one of the newest *N* or within *D*
days; off keeps everything. pgBackRest expires with its native
`--repo1-retention-full=K`, `K = max(N, fulls within D days)`;
`pgbackrest.conf` now says `9999999` so nothing else expires. A stricter
policy needs sudo, raises a Critical `backup.retention_reduced` alert, and
the sidecars wait 24 hours from first seeing it, keeping anything either
policy keeps until then.

**Alerts** (`BackupMonitor`, every 5 minutes): `backup.failed`,
`backup.overdue`, `backup.agent_offline`, `backup.restore_test_failed`,
`backup.disk_low`, one open alert per problem per agent.

Choices made while implementing, beyond the spec:
- During the grace period an agent enforces the union of the old and new
  policies (the larger *N* and the larger *D*), rather than the old policy
  alone. It never removes anything either policy would keep.
- `WalArchivedAt` comes from `pg_stat_archiver.last_archived_time`, not a
  file's mtime in the repository. It is cheaper, and it is Postgres's own
  record of the last segment pgBackRest accepted.
- A physical backup's size is its repository **delta**, so an
  incremental's size is what it adds, and the per-agent total is real disk
  use. The full sizes are in `DetailJson`.
- A second Back up now or Test restore is refused (409) only while a job
  of the **same kind** is pending for that agent, so a scheduled backup
  does not block a restore test.
- Any successful backup, manual included, sets the next scheduled run one
  interval later.

`BACKUP_S3_ENABLED` (which only ever logged a line) is gone from compose,
`run.sh` and `.env.example`; the `S3_*` lines say they are reserved for 9.2.
New: `BACKUP_FULL_EVERY_DAYS` (default 7). Your own `.env` still has
`BACKUP_S3_ENABLED`; it is harmless and can be deleted.

**Verified.**
- `dotnet test`: 487 passed, including 25 new in `BackupTests` (the
  retention table, grace, validation, sudo, audit, preview, queue, status,
  dashboard, monitor, seed, role lists).
- `npm run build` and `npm run lint` pass, with only the existing warnings.
- Live, after rebuilding: the migration applied, the policy was seeded
  (3 / 14), and both sidecars registered and took a startup backup (an
  incremental, not a full). Both waited on the grace period and removed
  nothing; the logical one cleaned the six orphans.
- **Back up now** ran on both agents, and the page updated itself when they
  finished.
- **Test restore** passed on both: 34 tables restored from the dump, and
  pgBackRest restored `--set` to a valid cluster.
- The policy preview (keep 2 / 7 days) listed the same two cycles that the
  sidecar's SQL selects on the live inventory.
- The failure and deletion paths ran against a scratch database and fake
  files inside the `backup` container, then both were removed:
  - a failed backup (retry after 15, then 30 minutes);
  - retention removing whole cycles;
  - a stricter policy waiting, then applying once its day was up;
  - keep forever.
- pgBackRest's `expire` step has not run live: every full backup on this
  instance is inside the policy.
- Walked `/admin/backups` at 375 px (no sideways scroll) and in dark mode.
  Every `/admin/*` tab and the signed-in routes were walked as the admin;
  the signed-out routes were walked in a fresh browser context, with private
  content masked and `/admin/backups` and `/profile` redirecting to sign-in.
- **Not walked as a member**: that needs a member's password, which the
  assistant does not enter. `BackupTests.Members_cannot_see_or_change_backups`
  covers the API. Creating, saving and purging a page was also skipped,
  since this change does not touch the editor routes.

**What happens next on this instance:** the grace period ends at about
06:18 UTC on 2026-09-18. The first backup after that applies keep 3 / 14
days, which removes the 2026-09-02 and 2026-09-04 dump cycles. The old
sidecar's `find -mtime +14` would have removed those as well.

### Design: backups in the admin portal, and the offsite plan (2026-09-17)

Dev-plan Phase 9, written by Fable 5.1 per the model gate (the owner
asked for the feature, Opus gathered the facts from the live stack and
the offsite research, then stopped and handed over). Nothing is
implemented yet.

- **9.1 Backups admin section** (Fable → Opus, ready for Opus): the
  database is the contract between the app and the two backup sidecars
  (three new tables the sidecars write and the app reads; the policy on
  `SiteSettings`). One retention rule for both systems, as the owner
  chose: a backup is removed only when it is outside both "newest N" and
  "last D days"; retention off keeps everything. A 24-hour grace period
  on reductions, enforced by the sidecars rather than the app. The
  sidecars are rewritten to poll, persist their schedule (no more full
  backup on every restart), retry failures in minutes rather than a day,
  clean up orphaned temp files, and run "Back up now" and "Test restore"
  jobs. Six new alert kinds. The dashboard's Health tiles from 2.5 land
  here.
- **9.2 Offsite backups** (Fable → Opus, unscheduled): the research on
  pgBackRest dual repositories, restic, NAS mounts, immutability,
  credentials, costs and restore drills, with a recommendation (local
  first, then replicate; B2 as the documented default; restic for the
  dumps and uploads) and the seven decisions the owner still has to make.

### Fix: dashboard charts dropped today (2026-09-17)

The admin dashboard's Sign-ins, Failed sign-ins and Pages created charts
read flat zero on this instance. None of the data was missing: logins and
failed logins are still audited on the real sign-in path, and pages still
carry their creation time. The daily series (`Daily()` in
DashboardEndpoints.cs) started at `now − rangeDays`, so its last bucket was
yesterday. Anything from today was counted and then had nowhere to land.
On an instance whose telemetry began today (as this one's did, right after
the dev-plan migrations were applied) every chart was empty. The range is
now whole UTC days ending with today. Views per day had the same bug.

The chart's hover label had a matching off-by-one of its own. A bare
`2026-09-16` parses as midnight UTC, which is the evening before anywhere
west of Greenwich, so the label showed the previous day. It now parses
the date as local midnight.

The existing dashboard test only checked each series' length, which the
bug never changed. The new test (`The_dashboard_charts_end_today_and_include_todays_activity`,
at 30- and 1-day ranges) signs in, fails a sign-in, creates and views a
page, and asserts all four series end today with those counts. It fails
without the fix.

### Inline comments open where they are (2026-09-17)

An inline comment could only be read by scrolling to the Comments tab and
working out which one it was. Clicking highlighted text, in the reading
view or the editor, now opens that comment's thread in a popover under
the text (InlineCommentPopover.tsx), with Reply, Edit and Delete: the same
component as the Comments tab, now exported from CommentsPanel.tsx, and
the tab reloads when a comment changes from the popover or the selection
bubble. Escape, the X or a click elsewhere closes it; highlighted text
shows a pointer. The popover renders into the document body, so in the
editor it is not inside the page's own <form>, and both comment forms
stop their submit from propagating: a reply posted from the editor was
checked not to save the page or trigger the leave prompt.

Also moved the router's Root component out of main.tsx, which had brought
back a fast-refresh lint warning.

### Table of contents: section numbers keep the bullet style (2026-09-17)

Turning on section numbers used to drop the bullets whatever style was
chosen. The chosen style now stays alongside the numbers: Bullet, Mixed,
Circle, Square and None all apply as picked. The one exception is
Numbered, which would print two numbers per line ("1." and "1.1"), so
there the outline numbers replace the list's own. Editor and export
changed together; two tests added.

### Editor bar at tablet widths; table controls over settings panels (2026-09-17)

- **Insert ran into Full width.** Between the phone breakpoint and a wide
  desktop, the sidebar leaves the editor bar ~400px, and Style, Insert,
  Full width, Update and Close do not fit; the menu triggers are fixed and
  overflowed into Full width. The bar is now a size container: Full width
  drops to its icon under 720px of bar, and leaves under 440px, where it
  makes no visible difference. Checked at 661, 800, 1024 and 1440px wide:
  no overlap at any of them.
- **A table's controls drew over a table of contents' settings panel**
  when the panel floated over the table. Table hover is detected by
  position, so pointing at the panel counted as pointing at the table.
  Every floating editor menu now carries `.floating-menu`: the hover
  detection ignores the pointer while it is over one, and they stack above
  the table controls.

### Table of contents options, and fifteen light-theme colors that were never set (2026-09-16)

- **Table of contents options, as Confluence Cloud documents them.** Select
  a table of contents in the editor and a settings panel opens:
  **Display as** (vertical or horizontal list), **Bullet style** (Bullet,
  Mixed, Circle, Square, Numbered, None), **Heading levels** from–to,
  **Include section numbers** (outline numbering, 1, 1.1, 1.2), and under
  Advanced **Indent headings** (a CSS length), **Include / Exclude headings
  with** (case-sensitive, `*` and `?` wildcards, `|` between
  alternatives), **CSS class name**, and **Exclude in PDF export**. The
  editor (`tocOptions.ts`) and the exporter (`ProseMirrorRenderer`'s
  `TocOptions`) implement the same rules; `TocOptionsTests` pins them.
  A table of contents with no options set renders exactly as before, in the
  editor and byte-for-byte in exports. Indent and class name are validated
  rather than escaped, since they land in style and class attributes.
  "Exclude in PDF export" is a print rule on the exported HTML, which is
  what the PDF is printed from, so it also drops out of a paper print.
- **The green "Live" dot was missing in the light theme, and so were
  fourteen other colors.** Since 2026-09-08 the light palette defined
  `--success`, `--danger-soft`, `--primary-soft`, `--primary-softer`,
  `--primary-soft-border`, `--surface-sunken`, `--mark-bg`,
  `--comment-bg`, `--selected-cell`, `--resize-hover`, `--swatch-border`,
  `--shadow-sm/md/lg` and `--img-shadow` as *themselves*
  (`--x: var(--x)`), which is invalid, so each computed to nothing: no
  popover or card shadows, no tint behind active toolbar buttons and tree
  rows under the default blue accent, no selected-cell, comment or mark
  highlight, no soft red behind errors. The dark palette was fine, and the
  non-blue accents masked the tints, which is how it went unseen. Values
  restored from the literal colors those rules used before they were
  tokenized; the green is the palette's own.

### Leaving the editor asks first; tables and headings on a phone (2026-09-16)

- **Navigating away from the editor now asks.** Tapping the profile avatar
  mid-edit used to leave instantly, with no obvious way back. Any
  navigation out of the editor that isn't its own Publish/Update or Close (
  a link in the top bar, the page tree, the browser's back button, an
  iPhone's swipe-back) opens a dialog: **Publish/Update and leave**,
  **Leave unpublished** (an existing page's edits stay in its draft for
  next time), or **Stay in the editor**. On a brand-new page the middle
  choice is **Discard page**, because an unpublished new page is reachable
  from nowhere. Reloading or closing the tab gets the browser's own prompt.
  This needed the app to move from `<BrowserRouter>` to a data router
  (`createBrowserRouter`): only a data router supports `useBlocker`, and
  only a blocker sees the back button and swipe-back. Route walk after the
  change: all 21 signed-in routes and 9 signed-out ones render as before,
  redirects intact, no page errors, and a page created, published and
  purged.
- **Table controls stay on the table.** The row/column add and delete
  buttons, the width controls and the cell menu were `position: fixed`
  from viewport rectangles, which drifts off the table on an iPhone
  whenever Safari pans or zooms the page. They are positioned inside the
  editor's own box now. At phone width the row controls were also at
  `left: -6px`, partly off-screen; in a narrow gutter the add buttons now
  sit on the grip strip.
- **Heading 1 looked broken because the Style menu lied.** The toolbar
  re-rendered only when content changed, so after moving the caret the
  menu still marked the style of wherever the caret had been: on page
  load, the document's first line, usually a Heading 1. And the style items
  *toggled*, so choosing Heading 1 on a line that already was one turned
  it back into normal text. The toolbar now re-renders on every
  transaction, and the style items set rather than toggle (Normal text is
  the way back). Toolbar commands also restore the editor's last selection
  if focus was taken from it, as iOS can do.
- **"Aa Style"** replaces "Aa Text Style" on a phone, and the editor row
  fits on one line at 375px.

### Toolbar menu triggers, and close buttons on the phone popups (2026-09-16)

The Text Style trigger was permanently blue: it borrowed the toolbar's
"active" look, and a text menu always has a current style. Both menu
triggers now carry a plain border and never the accent state: a menu is
not a state. The theme and notification popups get the same phone-only X
as the hamburger, and the notification dropdown is pinned to the viewport
on a phone the way the theme panel already was, so it cannot run off the
edge either.

### Phone toolbar wording, and the Live bar on the breadcrumb's indent (2026-09-16)

On a phone the two toolbar menus now say what they are: **Aa Text Style**
and **+ Insert**, icons kept. Wider screens are unchanged: the text menu
shows the current style there and the row carries every button, so the
plus alone is enough. The Live bar moved out of the paper to sit directly
under the breadcrumb on the same indent, where page chrome belongs.

### iOS: sticky bars follow the keyboard; Return in the title; the Live bar (2026-09-16)

- **The bars really were scrolling away on an iPhone, and it was the
  keyboard, not zoom.** Two screenshots from a real device showed it: with
  the keyboard up, the top bar was gone, then the toolbar too. iOS Safari
  keeps two viewports; sticky and fixed elements attach to the *layout*
  viewport, and when the keyboard shrinks the *visual* viewport Safari
  scrolls the visual one inside the layout one to keep the caret in view,
  taking anything pinned to the layout top out of sight. The app now
  listens to `window.visualViewport` (useVisualViewportOffset.ts) and,
  only while it reports an offset, translates the top bar and the editor
  bar down by that amount. The link dialog moved to a portal at the
  document root, because a transformed bar would otherwise become the
  containing block of its fixed overlay.
- **Return in the title published the page.** The title is the editor
  form's only text `<input>`, so Return was HTML's implicit submission:
  on a phone, the obvious way to leave the title. Return now moves the
  caret to the first line of the body.
- **The "Live: changes are shared as you type" line** sat between the
  title and the body and read as the document's first line. It is a small
  bordered bar above the title now, under the breadcrumb, aligned with
  the title (CollabStatus.tsx); the collaborative editor reports its
  connection state upward instead of rendering it.

### iOS: the vanishing toolbars were Safari's input zoom (2026-09-14)

Reproduced in the iOS Simulator (iPhone 17 Pro, iOS 26.5) rather than
guessed at. Signing in zoomed the page and the zoom stayed after
navigating on: the Spaces page rendered wider than the screen with
"New space" and "Sign out" cut off. Cause: `label { font-size: 0.85rem }`
plus `font: inherit` on inputs put every field inside a label at 13.6px,
and iOS Safari zooms the page into any control it focuses below 16px, and
keeps that zoom. In the editor, the change comment or the link dialog's
fields did the same, after which the sticky bars sat partly outside the
zoomed viewport: the "both toolbars get hidden, sometimes" report.

Fix: form controls are 16px at phone width. Also checked on the device:
with the editor focused and the page scrolled, both bars stay docked on
the current build. The `container-type` move from the previous entry was
not the cause, headless WebKit kept the bars docked with the old rule
re-injected, but it is harmless and stays.

Not yet confirmed on the device: a focused input no longer zooming. The
simulator tool's injected taps do not move focus into web form fields on
this page (a tool limitation; the same taps work on a real phone), so the
final check needs a human tap.

### Links: one dialog, reached from Insert, with display text (2026-09-14)

- **Link lives under + → Insert**, first in the list, and no longer on the
  toolbar row. `Cmd/Ctrl+K`, the selection bubble's link button and the
  Edit button on a link's own bubble all open the same dialog.
- **The dialog has two fields**: the address and the words that carry it.
  A selection prefills the display text; a selected address prefills both.
  Opening it on an existing link prefills both from the link and adds
  **Remove link**. An address typed without a scheme gets `https://`.
  Headings on this page are listed beneath for anchor links, as before.
- **Tapping a link in the editor** shows its bubble: the address (opens in
  a new tab), Edit, Remove. Editor only; a reader's tap follows the link.
- **A bug of the first cut, caught before it shipped:** the Insert menu
  hands an item the current selection as the range to delete (right for a
  typed `/link` query, which is what the slash items expect). Choosing Link
  with a paragraph selected therefore deleted the paragraph from the
  collaborative document. The item now removes only a slash query. The one
  page it happened to (in the manual) had its collaborative document reset
  so the editor re-seeds from the published version; the published page
  was never affected.
- **iOS: the action bar scrolling away, a best-informed fix.** The content
  column was a size container (`container-type: inline-size`, for the
  full-width layout rules) and the sticky action bar was its descendant.
  WebKit has been seen to lose `position: sticky` on descendants of a size
  container, which matches "both bars disappear sometimes while scrolling"
  on iPhone. The container moved to a wrapper that starts *below* the bar
  (`.page-column`), same width, so the `100cqw` maths is unchanged. Not
  yet verified on iOS: the simulator needs `xcode-select` pointed at Xcode
  first, which needs an administrator's password.

### Phone toolbar: the two menus and nothing else (2026-09-13)

At phone width the editor row is **Aa** and **+** beside the page
buttons. Nothing is measured there any more: every text control,
including the link, lives in the text menu, and the link's URL form,
with the same headings-on-this-page list as the row's popover, unfolds
in place under its item. Above the breakpoint the row still keeps
whatever fits. Separators go with the controls they separate, so no
stray rule is left between the two menus.

### The phone menu has a close button (2026-09-13)

An X at the top right of the hamburger panel. Tapping outside or pressing
Escape already closed it, but neither is discoverable, and a menu that
covers the screen needs a visible way out. Phone only: above the
breakpoint the same element is inline in the top bar and the button is
hidden.

### Mobile, second pass: docked bars, two toolbar menus, a banded hamburger (2026-09-13)

Five more things at phone width, each verified with the screenshot
harness at 390px (and the toolbar change at 1440px, where it is inert).

- **The theme popup ran off the left edge.** It hung off the toggle's
  right edge, and on a phone the toggle sits mid-bar. It is pinned to the
  viewport at that width now.
- **The editor toolbar and the page action bar scrolled away.** Both went
  `position: static` on a phone to avoid fighting the space bar for the
  same sticky slot. The space bar is now hidden on every route that
  renders one of them (the new-page editor included, which it was not)
  so both bars stay docked under the top bar, as on desktop.
- **The toolbar has two menus, and they mean different things.**
  Everything that has left the row for want of space now goes into the
  **Aa** menu, grouped as Style, Format, Color and Paragraph (the color
  palettes unfold in place); the **+** menu holds only things to insert.
  On a wide screen the Aa menu is just the block styles, because nothing
  has overflowed. The previous pass had put overflow under **+**, which
  made "insert" mean "and also some formatting".
- **The hamburger has three bands**, the same shape as the desktop
  sidebar: Spaces, Admin, search, the space's name and **+ New page** stay
  at the top; **Space settings** stays at the bottom; only the page tree
  between them scrolls. The whole menu was one scroll before, so on a
  long space the way back to Spaces was a swipe away.

### Design: API and MCP writes become tracked changes in a live draft (2026-09-13)

The stale-collaborative-document gap found on 2026-09-13 has a design now,
as dev-plan **8.6** (Fable half done; Opus implements). An API or MCP write
lands in an open draft the way a second person's typing does (added text
highlighted, removed text struck through, both labeled with their
source) and publishing accepts it. A document nobody has open is
reconciled the same way when it is next loaded, and publish carries the
version it was reconciled to so a missed notification cannot overwrite.
One reconcile function, one schema, three callers. The plan entry has the
full shape, the implementation order and the verification.

### Mobile: the phone menu carries the space's pages, and the toolbar finally fits (2026-09-13)

Three things the recent chrome and theme work had left broken at phone
width, found by shooting every route at 390, 640, 700, 768, 900, 1100,
1440 and 1800 pixels with the screenshot harness.

- **The editor toolbar overlapped Update and Close on a phone and a
  tablet.** Only the thirteen formatting buttons could leave the row; the
  text-style dropdown, the two color palettes, alignment, link and the
  `+` menu were fixed, and together they were already wider than a phone.
  Alignment, text color and highlight are collapsible now (alignment
  becomes three items in the `+` menu; each palette unfolds in place
  under its item, so a phone still has every color), and the text-style
  trigger shrinks to **Aa** under a container query on the editor's own
  row: a narrow reading column on a wide screen counts too. At 390px the
  row reads `Aa · B · link · +` beside Update and Close; at 640px the
  marks and both palettes are back; at 1800px everything is. The overflow
  list in the menu is in display order (italic first), not loss order.
- **The overflow measurement never counted the separators**, which is why
  the `+` chevron sat under Full width on a 1440px screen while everything
  supposedly fit. Counted now.
- **Changing page on a phone took three taps**, hamburger → Spaces → the
  space → the page, because the menu knew nothing about the space you
  were in. `SpacePage` now publishes its tree through a small context
  (`spaceNav.ts`) and the menu renders it, with `+ New page` above and
  Space settings below, phone only. The menu scrolls inside itself, since
  a tree is taller than a phone.
- **Admin tables were wider than a phone and their rows grew to fit a
  vertical stack of actions.** At phone width the cells stay on one line
  and the table scrolls sideways, so rows are rows again.

Checked and left alone: the space layout at 700px (a 260px sidebar beside
a 440px column, nothing overflowing), the settings and admin tab rows
(they already scroll sideways), and the dashboard, security, audit,
profile and search views, which stack correctly.

Found while doing this and **not fixed here**, because it is a design
decision: the collaborative document for a page is loaded by name from
`CollabDocuments` and never reconciled with the page's current version.
The API, `PageWriter` and the MCP `update_page` tool write the page and
leave that document alone, so the next person to open the editor sees the
last *editor* state, not the page, and pressing Update would write it
back over the API's changes. It showed up as a broken image in the
editor (a manual page whose screenshot attachment had been replaced by
script). See the note in `docs/architecture.md`.

### Manual: light-theme screenshots, one per feature, and the admin half (2026-09-11)

**Every screenshot retaken in the light theme with the default blue
accent**, and the count taken from 27 to **86** so that each feature has a
picture of itself. The gap was the authoring UI: the manual could render a
panel or a status lozenge live on the page, but a reader could not see the
menu that produces one. Now they can: the insert catalog, the slash
menu, the color palettes, the cell options, the status and date pickers,
the live-block settings panel, the image hover menu, the link popover.

**The admin section is complete**, with all eight tabs photographed and
the prose filled out: what each dashboard number is for and what it is
*not* for, the full list of audited actions by area, the leaving-checklist
for suspending an account, and why the base URL matters more than it
looks. Taking those needed an administrator, so the documentation bot was
promoted to one: at the repository owner's explicit request, recorded
here because a standing admin account is a standing risk, and it can be
demoted from **Administration → Users** whenever the docs are done.

**Section titles carry an emoji** (📘 🚀 ✏️ 🗂️ 💬 📤 👤 🛠️ 🔌). Forty-odd
leaf pages in one column gave the eye nothing to catch on; eight marked
rows break the tree into sections at a glance.

Harness changes behind all of it, in `scripts/screenshots/`:

- **Appearance is seeded before first paint** (`SHOT_THEME`, `SHOT_ACCENT`)
  rather than clicked afterwards, so the first shot of a run is not in
  whatever the last run left behind.
- **A clip is in page coordinates; `boundingBox()` is in viewport
  coordinates.** Every shot below the fold was silently clipping an empty
  region until the scroll offset was added.
- **Typing in the editor reaches the collaborative document immediately**,
  saved or not, so a shot that types has to put the document back. The
  first attempt used a blanket `Control+Z`, which walked back through the
  whole Yjs history and emptied the page, breaking every later shot of it.
  It now deletes exactly what it typed, and the typing shots run last.

### Fix: only the Details tab had a breadcrumb (2026-09-11)

The breadcrumb still matched the pre-move URLs (`/spaces/:key/permissions`
and friends), so after those became tabs of Settings the other three
matched nothing and rendered no crumb at all, and the page jumped a line
every time you changed tab. One match for the whole settings section now,
and a two-part crumb: **Space settings / Permissions**.

### Space sidebar: three bands, and settings absorbs its three neighbors (2026-09-11)

Two problems, one shape.

**The sidebar scrolled with the document.** It was an ordinary grid item,
so reading a long page carried the space's name, the + New page button,
the PAGES heading and the settings links off the top of the screen: the
navigation disappeared exactly when a reader was deepest into a page and
most likely to want it. It is now its own scroll container: pinned under
the 52px topbar, exactly as tall as the rest of the viewport, with a head
and a foot that stay put and only the tree's rows scrolling between them.
`align-self: start` is the part that is easy to miss: a grid item
stretches to its row's height by default, and a sticky element as tall as
its container has nothing to stick within.

**Permissions, webhooks and trash were siblings of settings** because all
three were built before a space had a settings page to put them in. That
left the sidebar doing two unrelated jobs, and the administrative one
crowding out page browsing as a tree grows. They are now the three tabs
beside Details under **Space settings**, in the same tabbed shell the
admin area uses, reached by one sidebar entry pinned to the bottom above
a rule. The old URLs redirect, so bookmarks and links written into pages
still land in the right place.

Walked as a signed-in member and signed out, at desktop and phone widths,
across every space route plus search, labels, profile and the admin
refusal: no uncaught errors on any of them. A real page was created,
edited, deleted and purged through the moved Trash tab; the purge stops
at the sudo-mode password prompt, which is the intended behavior for an
irreversible action.

**Not** walked as an instance administrator: that needs an administrator's
password, which this assistant does not have and should not be typing.
Nothing under `/admin` was touched by this change, the route tree that
moved is entirely under `spaces/:key`, but the admin tabs are worth a
glance from someone who can sign in as one.

The user manual described the old arrangement, so it was corrected in the
same pass: five pages reworded and five screenshots retaken. A manual that
documents a layout the product no longer has is worse than no manual.

### A user manual, written in Tesria (2026-09-11)

A new **Tesria User Manual** space (`MANUAL`): 47 pages covering getting
started, every block in the editor, organizing a wiki, working together,
sharing and exporting, accounts, administration, and the two integration
doors (REST and MCP). Written as real pages rather than as Markdown in
`docs/`, so it is searchable, labeled, exportable and editable in the
product it documents, and so it dogfoods the features it describes: the
section index pages use children displays, the labels page ends with a
labels list, the live-blocks page demonstrates a content-by-label block.

**27 screenshots**, cropped to what they are about and, where it helps,
annotated with circles and arrows. They are taken by a Playwright harness
([`scripts/screenshots/`](../scripts/screenshots/README.md)) running from
the **PDF sidecar's image**, which already carries a Chromium matched to
its Playwright: no new dependency.
Two things about that harness are worth recording, because both cost time:

- **It runs inside Caddy's network namespace** (`--network
  container:tesria-caddy-1`), so `https://tesria.localhost` is this
  instance through the real proxy. Going straight to the app container
  fails twice over: the session cookie is `Secure`, so plain HTTP silently
  drops it and every shot comes out signed-out; and `/collab` is routed by
  Caddy, so the collaborative editor never loads a page's content and
  every editor screenshot is an empty document with a toolbar over it.
  Chromium also upgrades a *named* host to HTTPS on its own and will not
  be talked out of it by `--disable-features=HttpsUpgrades`: only an IP
  literal or a real HTTPS endpoint gets past that.
- **Annotations are drawn in the DOM before the capture**, as an SVG
  overlay positioned from `getBoundingClientRect()`, not painted onto the
  PNG afterwards. Circles and arrows come out as crisp as the UI under
  them, and they are described per shot in the spec rather than by
  pixel-pushing.

Screenshots of the admin area are **not** in the manual: taking them would
have meant granting the authoring account instance-admin, and that is not
a privilege to hand out unasked. Those four pages are written without
pictures; the harness is checked in and can fill them in later.

Fixture: a **Manual Bot** account (`manual-bot@tesria.local`) authors the
space, the same arrangement as the API space's bot.

### Fix: search snippets showed their `**` markers in the browser (2026-09-11)

Regression from the retrieval work earlier the same day. `SearchSnippets`
marks the matched words with `**` because the same snippet is served to
the MCP tools and to anything else reading the API, where HTML would be
the wrong thing to send, but the search results page rendered it as text,
so a reader got `**webhook**` on the screen.

Un-marked at the edge, in `SearchPage.tsx`, into `<mark>` elements: by
splitting the string, never by `dangerouslySetInnerHTML`, because a
snippet is page content and page content is not markup this app trusts.
Styled as a weight change rather than a highlighter block: a snippet can
carry a dozen matches and a row of yellow bars is harder to read than the
sentence was.

### The space sidebar's emoji are now drawn icons (2026-09-11)

`📑 ⚙ 🔒 🪝 🗑` were the only pictures in the app the app did not draw
itself: full-color glyphs, a different weight and shape on every
platform, and no relationship to the chosen accent.

`NavIcons.tsx` replaces them with five outline SVGs in the same language
as `BrandMark` and the editor's icon set: 24×24 box, 1.8px stroke, round
caps and joins, `fill: none`. Everything is `currentColor`, so `.nav-icon`
points them at `--primary` and they follow the theme *and* the accent for
free, the same trick `.brand__mark` already used. Verified live across all
six accents.

Two of the drawings are decisions rather than transcriptions. **Webhooks**
is one event fanning out to two subscribers, not a hook: a hook says
nothing about what a webhook does, and does not survive 16px. **Pages**
started as the brand's rhombus without its stack and was changed to a
plain sheet with a folded corner: the rhombus read as a shape, not as a
document; it means something in the logo, where the stack gives it
context, and nothing beside a page tree.

### Retrieval: snippets that show the match, sections, and a score (2026-09-11)

Groundwork for using the MCP server (8.4) as a context source, and a
straight improvement to search in the browser too.

- **A snippet is now the passage that matched.** It was the first 200
  characters of the page, so searching "webhook" and being shown a page's
  opening sentence told a reader, and an assistant, nothing about why it
  came back; the only way to judge relevance was to open every result.
  Postgres's `ts_headline` does this properly and has no EF binding, so
  `SearchSnippets` is the one place this app writes SQL by hand. The
  matched words come back marked in `**bold**`: plain text rather than
  HTML, because these snippets go to an assistant as often as to a browser.
  Shared by REST search and the MCP tool, computed *after* the permission
  filter so nothing is prepared for a page that will not be returned.
- **`get_page` returns a heading outline, and can return one section.**
  `section: "deployment"` gives that heading and everything under it, up to
  the next heading of the same or a higher level: reusing the anchors
  Phase 7 Wave A already derives, so "the section this `#link` points at"
  and "the section to fetch" are the same thing. Verified live: a 4,385-
  character page down to 2,739 for one section. Top-level headings only;
  slicing mid-panel would produce something that is not a document, so a
  nested heading is listed in the outline but refused as a section.
- **The relevance score is exposed** on MCP search hits, so a client can
  decide what is worth reading. Null where the database cannot rank, and
  omitted rather than sent as a fake `0`: "unranked" is not a score of
  zero.

Semantic search, the actual fix for synonym and paraphrase queries, is
now a written-up entry in `roadmap.md` with the evidence, the two design
decisions it cannot dodge, and the size at which it becomes worth doing.
The wiki is 58 pages and 31 KB of text today, which is why it is not worth
doing yet.

Also added to `roadmap.md`: a **roadmap planner** timeline block modeled
on Confluence's macro, with its data model and the four design questions
it raises.

### MCP server: the ten tools, one write path (dev-plan 8.4, Opus half) (2026-09-11)

The tool surface Fable specified, built against the contract:
`list_spaces`, `get_space_tree`, `search_pages`, `get_page`,
`find_pages_by_label`, `list_labels` (read) and `create_page`,
`update_page`, `add_page_label`, `remove_page_label` (write).

**`PageWriter` is now the only place a page is created or updated.**
`PageEndpoints.Create`/`Update` became thin translations of its result into
HTTP; the MCP tools translate the same result into a tool response. That is
what makes "a page written by an assistant is indistinguishable from one
written in the browser" true rather than aspirational: the audit entry,
the watcher and mention notifications and the webhook all come from one
code path. The existing endpoint tests were the safety net for the
extraction and stayed green throughout.

**Markdown converts over exactly the subset the export emits**, so a page
survives read → edit → write. The round-trip test caught a real defect:
Markdig models `[x] done` as a task-list inline followed by the literal
`" done"`, so the separating space belongs to the marker: dropping the
marker without it made every round trip indent the text one space further,
compounding on each edit. Also learned the hard way: two `-` lists
separated only by a blank line are *one* list in CommonMark.

Errors keep the masking rule. Writing to a page you cannot see is "not
found", never "forbidden": the latter confirms it exists. A read-only
token is refused by every write tool *before* anything runs, and a test
asserts the page is unchanged afterwards.

The API space gained a page covering connecting a client, the tools, the
Markdown contract and what the server deliberately will not do (delete,
permissions, admin, attachments).

Sixteen tests for the tools and the converter, on top of the seven from the
design half.

### MCP server: the contract, token scopes, and `/mcp` (dev-plan 8.4, Fable half) (2026-09-11)

*The plan says to load the `claude-api` skill before designing the tool
surface; it is not enabled on this account, so the design is from the MCP
specification and the official C# SDK's conventions directly.*

The contract is in `architecture.md` ("MCP server"). The decisions that are
expensive to reverse, and why:

- **In-process, in .NET, on the official SDK, stateless.** Not a sidecar:
  a sidecar would call REST with a forwarded token: a second hop and a
  second place permissions could go wrong. In-process, a tool runs the same
  `IPermissionService` every endpoint does, so an assistant sees exactly
  what its token's owner could. Stateless, so every request stands on its
  own token and nothing pins to a session behind the proxy.
- **Token only.** `/mcp` accepts the `ApiToken` scheme and nothing else. A
  browser session is never a credential there, so a page in someone's tab
  cannot drive the assistant surface: there is no CSRF question because
  the credential cannot be ambient. Tested.
- **Token scopes, finally: `ApiToken.ReadOnly`.** Minted with `readOnly`,
  shown in the listing and the profile, defaulting to full access so nothing
  narrows silently on upgrade (existing tokens are unchanged). One claim on
  the principal; **one middleware** refuses unsafe REST methods from a
  read-only token with `403 read_only_token`: enforced by HTTP method in a
  single place rather than per endpoint, because a scope checked per
  endpoint is one forgotten on the next endpoint. MCP write tools check
  the same claim first, since their transport is all POST.
- **Markdown is the content contract.** `get_page` returns the same
  Markdown the export produces, with dynamic blocks resolved as the caller
  (a children list arrives as links, not a placeholder). Writes will accept
  Markdown converted server-side over the subset the export emits, with
  `contentJson` as the escape hatch for exact copies.
- **Errors never reveal what the caller may not see.** A page you cannot
  view is "not found" to a tool, as it is 404 to REST; a listing omits it.

Built with the spec so it is proven rather than hypothetical: the scope end
to end (domain, claim, middleware, minting, three tests) and `/mcp` with
`list_spaces` and `get_page` (four tests driving it as a client would:
JSON-RPC over Streamable HTTP, initialize, tools/list, a call, and the leak
case). The remaining tools, the `PageWriter` extraction, the Markdown
converter and the API-space page are Opus work against the contract.

### OpenAPI spec and a self-hosted API reference (dev-plan 8.3) (2026-09-10)

`GET /api/openapi.json`: OpenAPI 3.1, generated from the routes, so it
cannot describe an endpoint that does not exist. A browsable reference is
at `/api/docs`. Both are open: the shape of an API is not a secret, every
endpoint still enforces its own permissions, and an operator who disagrees
can block two paths at the proxy.

Two things the generator could not know, both now in the document:

- **Both ways of authenticating.** A token (`Authorization: Bearer`) and the
  SPA's session cookie, the latter noting that unsafe requests also need
  `X-Requested-With: Tesria`, the CSRF defense, and that everything you
  may not see answers 404 rather than 403.
- **Which endpoints actually need one.** An endpoint is open two ways here:
  an explicit `.AllowAnonymous()` (Phase 5's public routes) *and* simply
  never having asked for authorization: `/api/health` does the latter, and
  describing it as needing a token would be a lie the generator cannot
  catch. Both now report no security requirement.

**The reference is a third-party UI inside an app with a strict CSP**, which
took care. Its own JavaScript is served from this origin (no CDN), its
default web fonts are turned off rather than silently blocked, and the one
inline `<script>` on its page runs under a **fresh per-request nonce** that
`SecurityHeadersMiddleware` mints only for `/api/docs`: rather than opening
`unsafe-inline` for the whole app, which would undo the reason that policy
exists. Tests pin that the nonce appears only on that path and differs every
request.

Some of its sidebar features call the vendor's hosted service. This app's
`connect-src 'self'` blocks them, which is the behavior we want from a
documentation page: the button leading to them is hidden so nothing broken
is put in front of a reader, and the CSP remains the backstop.

The API space gained a page describing the spec, the reference, both auth
schemes and how to generate a client, so the prose documentation and the
machine-readable one stay in step.

### PDF export, and a license (dev-plan 8.1, 8.2) (2026-09-10)

**PDF export (8.1)**: the one claim on the brand page that was not true.
A Playwright sidecar renders the *same* print-ready HTML the html format
returns, so there is one renderer and a PDF cannot drift from the page. A
real browser engine is the only honest way to do this, and a ~400MB
Chromium has no business in the app image, so it is a sidecar like collab.

The sidecar runs with **its network switched off**: `offline: true` plus a
route handler that aborts everything but `data:`. That is only possible
because the HTML export is now genuinely self-contained: diagrams carry
their own renderer (last commit) and **images are now inlined as data
URIs**. That last part fixes a real bug in its own right: an exported HTML
page referenced `/api/attachments/…`, so its images were broken the moment
the file left the app. Inlining is bounded (4MB an image, 20MB a document);
past the budget an image keeps its URL, as before. Markdown is deliberately
unchanged: a data: URI is unreadable in a text file.

Where no renderer is configured, `?format=pdf` answers **503 with advice**,
"export as HTML and print it", rather than a dead end or a 500. The
same if the sidecar is down.

**Caught immediately, and it is the classic one:** `playwright-core` was
declared as `^1.56.0` and resolved to 1.63.0 against a v1.56.0 image, so
every render failed with "Executable doesn't exist". The library version
and the image tag must be the *same* version; both are now pinned exactly,
with a comment saying to bump them together or not at all.

**License (8.2)**: the brand page says Apache 2.0 and the repo had no
`LICENSE` file. Added, with a `NOTICE` listing the third-party components,
and SPDX identifiers in both `package.json`s and the `.csproj`. Public
visibility is still the user's call; the license file existing is a
precondition for that, not a consequence.

Verified end to end: a real PDF of a page carrying dynamic blocks, an
excerpt, page properties and a Mermaid diagram: the diagram renders as
*vector text* in the PDF, so the live blocks and the drawing both survive.

### Editor parity Waves E and F: media, embeds, diagrams, maths, charts (dev-plan 7) (2026-09-10)

**Phase 7 is complete.**

**Wave E: embeds and media.** An embed is a third-party iframe on everyone
else's page, so the allowlist is enforced **twice**: `/api/embeds/resolve`
refuses a host that is not on it, and the CSP's `frame-src` is built from
the same list, so even a client-side bug cannot frame an off-list site. The
client never decides what may be framed: it asks, and frames exactly what
it is told to.

Host matching is deliberately tiny and gets the hostile cases in tests: a
plain `EndsWith` would admit `evil-youtube.com` and
`youtube.com.attacker.net`, so matching is on a label boundary. A known
provider is also *narrowed* (a YouTube watch page becomes the no-cookie
embed player, a Google Doc becomes its preview) while an allowlisted host
with no provider rule frames as pasted, which is what makes "allowlist our
internal Grafana" work with no code.

Smart links fetch Open Graph tags through the 3.4 SSRF guard and cache them
(a week for a success, an hour for a failure) so a page of links is not a
page of outbound requests. The response body is capped at 256KB, and an
`og:image` is only used if it is absolute https. Unfurling requires an
account; resolving does not, because an embed on a public page is part of
that page.

Attachment-backed media (video, audio, PDF, file card) picks its rendering
from the file's own content type rather than an author's choice, so a .mp4
is a video wherever it appears. PDFs use the browser's own viewer in a
same-origin frame: no PDF.js in the bundle. A gallery is a *layout over
image nodes*, so every existing image affordance keeps working and the
export renders ordinary images.

**Wave F: technical content.** Mermaid is a code-block *language*, not a
node: the source stays an ordinary fenced block in every export and the
diagram is a view of it. KaTeX maths is one node with a `display` flag.
Charts read a table already on the page by its ordinal ("the second table"),
never copying the data: editing the table redraws the chart. Deliberately
not a Wave D dynamic block: the table is right here, so a round trip would
be slower, would miss unsaved edits, and would need a fourth result shape.

Both libraries load on demand: a page with no diagram never downloads
Mermaid's 500KB, and Vite splits it per diagram type. The main bundle is
unchanged. Charts are plain SVG and flexbox rather than a charting library.

Exports stay sane outside the app: an embed and a smart link become plain
links (never an iframe, and a `javascript:` URL becomes no link at all),
maths exports as `$…$`, and a chart names the table it charts rather than
duplicating it. A page with a Mermaid diagram carries **this instance's own
Mermaid bundle, inlined**: no CDN, and no dependence on this instance
still being reachable. An exported file is meant to be something you keep,
and a document that only renders while a server answers is not that. The
cost is ~3MB, only on pages that actually have a diagram; where the bundle
is missing the export ships the diagram source alone, which is still
readable. **Nothing in an exported file reaches the network**, and a test
asserts it.

**Found by upgrading a running instance, which no test could catch:** the
new `EmbedAllowlist` column defaulted to empty on an instance that already
had a settings row, silently turning embeds off on upgrade. The C# property
initializer only runs for a *new* settings object; the default now lives on
the migration's column too. Every test creates a fresh database and so
never took that path.

38 tests for the two waves.

### Editor parity Wave D: the other eleven kinds (dev-plan 7, Wave D, Opus half) (2026-09-10)

Against the contract Fable designed: **Recently updated, Content by label,
Attachments, Change history, Contributors, Include page, Excerpt include,
Page properties report, Labels list, Task report** and **Page tree**. Each
is one class implementing `IDynamicBlockKind`: a query, and nothing else.
The contract held: no kind needed a new result shape, a renderer change, or
a line of React.

Two static container nodes came with them, because two kinds read content
rather than rows: `excerpt` (what `excerpt-include` takes) and
`pageProperties` (a two-column table `page-properties-report` collects
across pages). Both are plain containers with no node view, so an exported
page shows its excerpt and its properties as ordinary content, which is
what they are.

The permission rule held everywhere, and the leak tests are the interesting
ones:

- **Page properties report** derives its *columns* from the pages it finds,
  so a restricted page could leak a column name ("Salary band") with no row
  behind it. It does not.
- **Contributors** and **Labels list** are counts: a contributor whose only
  edits are on a restricted page, and a label whose only pages are hidden,
  must not appear, and the counts of those that do must not include the
  hidden ones.
- **Task report** filters visibility *before* reading any content, so a
  restricted page's action items are never walked at all.
- **Recently updated** over-fetches and filters, so a run of restricted
  pages makes it look further down rather than return a short list.
- **Include page** on a page you cannot see reports "nothing to show", never
  an error naming the page: an error would confirm it exists.

Nineteen tests for the kinds, on top of the mechanism's eight.

Two fixes while building:

- A **draft host** (a brand-new page being composed) was invisible to the
  block endpoint, because the global query filter hides drafts: every block
  on a new page 404'd until Publish. Now `IgnoreQueryFilters()` with the
  soft-delete half reapplied by hand, the same pattern `SetLayout` uses.
- Candidate pages are ordered **in memory, not in SQL**: SQLite cannot
  `ORDER BY` a `DateTimeOffset`, and sorting here means both providers order
  identically rather than only Postgres being exercised.

### Editor parity Wave D: the dynamic-block contract, and Children display (dev-plan 7, Wave D, Fable half) (2026-09-10)

Wave D is "a block whose content is the answer to a query": Confluence's
Children display, Recently updated, Task report and nine more. The plan
splits it: Fable designs the mechanism, Opus adds kinds against it. This is
the Fable half: the contract, written into `architecture.md` ("Dynamic
blocks"), and the mechanism built end to end with **one** reference kind so
the contract is proven rather than hypothetical.

The decisions, each with its reason in the spec:

- **One node, `dynamicBlock { kind, params }`,** an atom that stores the
  question and never the answer. A stored copy of "children of this page"
  is wrong the moment a child is added and a permission leak the moment a
  page is restricted.
- **One result shape** (`list` / `table` / `document`) with exactly one
  renderer in the SPA and one in the exporter. A kind is therefore *only a
  query*: no React, no HTML, no Markdown. This is what makes the twelfth
  kind cost what the second did.
- **One endpoint,** `GET /api/pages/{host}/blocks/{kind}?…`, anonymous-
  capable, masking an unviewable host as 404. Unknown kind and bad params
  are 400s naming the field; out-of-range numbers are clamped so a document
  written against a looser server still renders.
- **Permission filtering is the kind's job, with one helper that makes it
  hard to get wrong** (`BlockContext.VisibleAsync`, the search/tree
  two-pass rule). A page the caller cannot view must not influence a result
  at all, not its title, not a count.
- **Export snapshots at export time, as the exporting user,** through the
  same service; a failed block is a placeholder, never a failed export.
  `document`-shaped kinds render their included page's own blocks as
  placeholders, depth 1, so an include of an include cannot recurse, on
  either side.
- **The node view learns its host page from `editor.storage`,** the same
  stash the slash menu's upload callbacks use; history and template
  previews, where nobody sets it, show "Shown on the page".
- **Params are edited by one generic form** generated from each kind's
  declared schema in the client catalog; the slash and + menus list that
  same catalog.

`children` (Confluence's Children display) is the reference kind: depth
1–3, three sort orders, a hidden parent hiding its subtree. Eight tests
cover the mechanism, including the leak test every future kind owes and an
export taken as a user who cannot see one branch. Verified live on the API
space's root page, which now carries a real Children display at depth 2,
kept deliberately as documentation. Remaining kinds are Opus work; the
per-kind params and queries are tabulated in the spec.

### Editor parity Wave C: mentions, emoji, action-item assignees (dev-plan 7, Wave C) (2026-09-10)

- **@mention.** A `mention` inline node carrying the user's id *and* a
  snapshot of their display name. The id is what the server diffs; the label
  is what keeps the mention readable in an exported file, in a page version
  from last year, and after the account is deleted: none of which have a
  directory to look the name up in. The `@` popup filters a directory
  fetched once per page load: a self-hosted wiki's user list is small, and a
  request per keystroke would make the popup lag behind the typing.
- **On save, newly mentioned people are notified** (`user.mentioned`).
  *Newly*: the mentions in the version being replaced are subtracted first,
  so fixing a typo on a page that names ten people does not ping all ten
  again.
  **Each recipient is permission-checked first.** The notification carries
  the page title, so mentioning someone on a page they cannot open would be
  a way to leak that title. They are told nothing rather than told and then
  given a 404. This needed a small seam in `PermissionService`
  (`AsUser(userId)`): the service was written entirely against the request's
  own identity, and every rule now reads one `UserId` property so an
  "as user" evaluation cannot fall back to the caller's rights.
- **`:emoji` suggestion** over a curated list of ~40, inserting the literal
  character. Nothing enters the schema, the renderer or the search index: an
  emoji is text that happened to be typed with a picker. Curated rather than
  the full Unicode table, which is ~1,900 entries with several names each
  and a real payload for a feature whose job is three keystrokes. Needs two
  characters after the `:` before it opens, or every colon in a URL or a
  time would pop a menu.
- **Action-item assignees.** Typing `@name` in a task assigns it, exactly as
  Confluence does. The mention is the source of truth; `assigneeId` /
  `assigneeName` on the `taskItem` are a denormalized copy kept in step by a
  plugin, so Wave D's Task report can query "assigned to me" instead of
  walking every page's document tree. Neither the app nor the export draws
  the name a second time: the mention it came from is already in the item's
  own text.
- **Fixed while building it:** all three `@tiptap/suggestion` plugins
  (slash, mention, emoji) default to one shared plugin key, so adding the
  second threw "Adding different instances of a keyed plugin" and took the
  whole editor down with it. Each now has its own.

Verified live: the `@` popup with avatars, insertion by click and by Enter,
`:roc` → 🚀, and a task picking up its assignee from the mention typed into
it. The notification path is covered by tests, including one asserting that
a mention on a restricted page tells the recipient nothing at all.

### Editor parity Wave B: text color, scripts, indent (dev-plan 7, Wave B) (2026-09-10)

- **Text color**, stored as a color *name* out of eight, not a hex.
  Highlight can afford a hex because it is a background and the ink on top
  is pinned per theme; colored *text* has no such escape: a hex dark
  enough to read on white is invisible on this app's dark background, and no
  CSS rule can lighten a color it cannot see. A name can be re-pointed per
  theme (`--text-color-*`), which is what makes the feature work in dark
  mode at all, and it also means nothing from the document can reach a
  `style` attribute. The export renderer inlines the light-theme ink.
- **Subscript and superscript** (`Mod-,` / `Mod-.`), exporting as
  `<sub>`/`<sup>` in HTML and as raw HTML in Markdown, which most renderers
  pass through.
- **Indent / outdent** (`Mod-]` / `Mod-[`), as a `textIndent` attribute on
  the paragraph or heading rather than a wrapper node: an indent is a
  property of the block, and a wrapper would fight list lifting. Capped at
  four levels, and the rendered `margin-left` is computed from the clamped
  integer on both sides, never echoed from the document. `Tab` is
  deliberately untouched: it already moves between table cells and nests
  list items.
- **Clear formatting** (`Mod-\`) strips marks, indent and alignment, and
  turns a heading back into body text, but deliberately *not*
  `clearNodes()`, which would also unwrap a list, a panel or a layout
  column. That is a structural edit, not a formatting one.
- **Shortcut audit against Confluence's set.** Everything it lists was
  already bound by StarterKit, TextAlign or Highlight, headings
  (`Mod-Alt-1…6`), normal text (`Mod-Alt-0`), lists (`Mod-Shift-7/8/9`),
  alignment (`Mod-Shift-l/e/r`), highlight (`Mod-Shift-h`), strike
  (`Mod-Shift-s`), with one real gap: **`Mod-K` for links**, now bound. It
  opens the toolbar's link popover rather than editing the document, so the
  shortcut calls its subscriber directly instead of faking a transaction to
  get a React re-render (`linkShortcut.ts`).
- `@tiptap/extension-subscript` and `-superscript` added;
  `@tiptap/extension-text-style` was installed and then removed once text
  color became a name-keyed mark of its own. `scripts/audit.sh` clean.

Verified live in both themes: every color legible on each, indent clamping
at four levels under repeated presses, clear formatting leaving status and
date atoms and the paragraph itself intact, and `Cmd+K` opening the link
popover with the heading list.

### Editor parity Wave A: structural blocks (dev-plan 7, Wave A) (2026-09-10)

Seven of Confluence's structural elements, in the editor, the reading view
and both export formats. Started as Fable by user override and finished as
Opus (the plan tags the wave Opus).

- **Heading anchors.** Every heading gets an id derived from its text
  (lower-cased, non-alphanumerics collapsed to hyphens, duplicates suffixed
  `-2`, `-3`). *Derived, never stored*: the same choice Confluence makes:
  a stored id duplicates on paste, drifts between collaborators and needs a
  migration for every existing page. The cost is one algorithm written
  twice, in `headingAnchors.ts` and `Features/Export/HeadingAnchors.cs`,
  pinned together by `HeadingAnchorTests`. In the editor the ids are
  ProseMirror *decorations*, so they are recomputed from the document on
  every change and never serialized. `#slug` links scroll rather than
  navigate, both in the reading view and when a page is opened at
  `…/pages/{id}#slug`, and the link popover lists the page's headings to
  pick from.
- **Table of contents**: a block with no stored content. The node view
  lists headings live; the exporter builds the same nested list at export
  time. Nothing ever holds a stale copy of the page's own outline.
- **Expand**: collapsible section, title stored, open state not (it starts
  open while editing and closed for readers). Exports as `<details>`.
- **Status**: inline lozenge, one of Confluence's six color *names*; the
  color value never comes from the document, so a hostile `color` cannot
  reach a style attribute. **Decision**: a panel-shaped block with a fixed
  check icon. **Date**: an ISO calendar date rendered in the reader's own
  locale (parsed by hand: `new Date('2026-09-10')` is UTC midnight and shows
  the day before to anyone west of Greenwich).
- **Layouts**: `layoutSection` of two or three `layoutColumn`s, with
  Confluence's five presets and a per-section width (centered / wide / full)
  reusing the page's own `--page-pad` breakout. Sections stack but never
  nest: `layoutSection` is not in the `block` group and only the document
  admits it (`Document.extend({ content: '(block | layoutSection)+' })`),
  so no panel, expand or column can contain one. The full-width *table*
  breakout was rescoped to direct children of the content root at the same
  time, so a full-width table inside a column fills the column instead of
  bleeding out of it.
- All seven appear in the slash menu and the **+** menu from the one
  `SLASH_ITEMS` catalog, so neither can drift.

Also in this pass, from live review:

- The **+** insert trigger is a plain "+" sitting with the other toolbar
  icons rather than a labeled button pushed to the right edge, and the
  text-style dropdown reads "Normal text" with no icon: both matching a
  Confluence screenshot the user supplied.
- **Publish/Update and Close moved onto the toolbar row**, out of the bottom
  of the form (`form=` ties the submit button to the form it now sits
  outside of).
- **The breadcrumb moved below the page action bar**, on the reading view as
  well as the editor: the bar is the top edge of the page surface, and the
  breadcrumb belongs with the content. `SpacePage` suppresses its own copy on
  those routes and `PageEditor` / `PageView` render it. Routes with no action
  bar (settings, permissions, webhooks, trash) are unchanged: the breadcrumb
  is already the first thing on the page there.
- **Fixed: floating toolbar menus were transparent.** A regression from the
  one-row toolbar rebuild: `.toolbar` stopped having a surface of its own
  (the page action bar supplies it), so every `.toolbar--bubble` copy of it
  (the selection bubble, the image hover bar, the new layout bar) let page
  content show straight through. They now paint their own background, and
  wrap again on narrow screens.

Verified live at 1000×640: every block created, styled and exported;
heading links scrolled rather than navigated; every space route rendered
exactly one breadcrumb with a clean console; a page created, published,
trashed and permanently purged (sudo re-auth included). 315 backend tests
pass.

### Editor chrome: one-row toolbar with an Insert menu, borderless page (2026-09-10)

*Scope added by the user at the start of Phase 7, modeled on Confluence's
editor.*

The page is a continuous surface: no box, border or shadow around the
body, no rule under the title, body text aligned with the title: in the
editor and the reading view alike. The toolbar runs edge to edge in a
single row and **never wraps**. Text style ("Normal text", "Heading 1"…)
and alignment are dropdowns; block elements (table, image, code block,
quote, divider, the five panels) live behind **+ Insert**, and that menu
is generated from the same catalog the slash menu uses, so a block added
to one appears in both. When the toolbar's own width (a container query,
not the viewport's) runs short, the "Insert" and text-style labels drop
first, then formatting buttons move into the Insert menu's *Formatting*
section by measurement (`useToolbarOverflow`), losing lists first and bold
last. The link button never collapses: its popover anchors to it.

Two wrong turns on the way, both caught live: `overflow: hidden` as the
no-wrap guarantee clipped every dropdown into invisibility (nowrap alone
is the guarantee), and the overflow priority was applied backwards.

Also: the space settings page's "Use it" button sat below its input for
the same reason the token form's did (card label/button margins); fixed.

### Fix: page editing, trash, permissions and webhooks were broken for signed-in users (2026-09-10)

**A regression shipped with Phase 5 yesterday.** Nesting `ProtectedRoute`
inside the space's route put a bare `<Outlet />` between `SpacePage` and its
gated children, and a bare outlet starts a fresh context of `null`, so the
editor, trash, permissions and webhooks pages all threw on
`useSpaceContext()` and rendered a blank screen. `ProtectedRoute` now
forwards the context it was given, which is what a gate should do.

It went out unverified: Phase 5's live checks covered the anonymous reading
paths and page *viewing*, and never opened the editor as a signed-in user
after the route restructure. Found while opening the new space settings
page, which failed the same way. All five pages verified live after the fix.

### Feature: space icons (dev-plan 6) (2026-09-10)

Every space now has an icon: an uploaded picture, an emoji, or, the
default, its key's first letter on a tile colored by a stable hash of the
key, so nothing is ever iconless. Rounded squares, where avatars are
circles: at tile size that shape is the only thing distinguishing a place
from a person. Rendered in the spaces list (including the public listing),
the sidebar head, the breadcrumb and the mobile action bar.

Pictures reuse the avatar pipeline unchanged (re-encoded to a 256px WebP,
EXIF stripped, SVG refused) and can only be set through the upload route,
never the JSON update, which would otherwise let a space be pointed at an
arbitrary stored key. Switching away from a picture deletes it rather than
orphaning the bytes. Reading an icon follows the space's own visibility, so
a private space's icon is 404 to anyone who cannot see the space, and a
public space's icon is readable with no account.

Emoji are validated by shape rather than against a list: short, no control
characters, at least one non-ASCII character. A list would go stale every
Unicode release; this admits future emoji and keycaps and refuses prose and
markup.

**`/spaces/{key}/settings` is new**: `PUT /api/spaces/{key}` had existed
since Phase 2 with nothing in the UI reaching it, so the icon picker gave
the name and description form a home at last.

Tests (fifteen): the generated default, emoji round-tripping through the
listing, four emoji shapes accepted and five kinds of prose refused, upload
re-encoded and served as WebP, switching away clears the picture, SVG
refused, pictures refused through the JSON update, only space
administrators may change any of it, and a public space's icon readable by
anyone while a private one is not. Full suite: 301 passing.

### Feature: public read mode · anonymous access per space (dev-plan 5.1–5.4) (2026-09-09)

*5.1 is a Fable item; 5.2–5.4 are tagged Opus and ran as Fable by user
override. The design (the anonymous principal, masking, what opens and
what stays closed, caching, discovery) is in architecture.md and was
written before the code; the leak matrix was written before the routes
were opened.*

A space can now be **published**: anyone can read it, no account needed:
the game-wiki case. Two switches must both be on: the instance-wide
**Allow public spaces** (Settings; also the 3.3 kill switch) and the
space's own flag, set by a site administrator from Admin → Spaces
(sudo mode, audited, always a security alert in both directions, refused
while the instance switch is off). Withdrawing keeps the flag so
re-enabling the instance restores the previous state.

An anonymous request has exactly one capability: reading a public space's
*current, unrestricted* pages. Any restriction anywhere in a page's
ancestry hides it: "not for everyone" now includes the internet. Drafts,
trash, private spaces and archived spaces are 404, never 403. Opened to
anonymous readers, each still permission-checked: space, spaces list
(public only), page tree, page, labels on a page, attachments (list and
download), search (scoped to public spaces), export, and comments only
where the space allows them (read-only). Closed: version history, drafts,
trash, the user directory, groups, labels across spaces, watches, collab,
avatars, notifications, everything that writes.

Anonymous page reads carry `Cache-Control: public, max-age=60` and an
ETag (304 on match), so unpublishing takes effect within a minute; signed-
in reads are `no-store`. Views are counted with no user. `robots.txt`
allows public space paths and disallows `/api`; `sitemap.xml` lists
public pages; a public page URL gets its title, description and Open
Graph tags injected into the SPA shell for link previews: decided as
the anonymous principal whoever asks.

The SPA renders for anonymous readers: brand, search, theme menu and a
**Sign in** button (which returns them to the page they were on); no
edit, new, watch, reorder or comment controls; the page bar collapses to
Export. The spaces index lists public spaces only; a **public** badge is
shown to everyone so nobody edits a public page thinking it is internal.
Admin → Spaces gains a Public column with Publish/Withdraw (the
confirmation names the page and attachment counts that become visible)
and a per-space comments checkbox.

Tests: the nine-test leak matrix (`PublicReadTests`), plus three existing
tests re-pointed at routes that stay closed.

### Feature: security alerts and notifications by email (dev-plan 4.3) (2026-09-09)

*Plan tag: Opus. Run as Fable by user override.*

Notifications are now an outbox: the request that causes one writes the
row, and `NotificationEmailService` picks it up within a minute, so no
request ever waits on a mail server. **Security alerts reach every
administrator by email immediately, whatever their preference**: the
"email the admin group" requirement from Phase 3, now met. For their own
notifications, each person chooses on the profile page: Off (the
default), Immediately (one email per pass, with links), or Daily digest
(one email a day when something changed). A dead mail server produces one
audited failure per notification, not one a minute; nothing older than a
day is sent, so turning email on does not flood inboxes with history.

Tests (five): alerts to admins regardless of preference and only once;
immediate subscribers get links; digest subscribers get one message a
day; Off keeps the in-app copy and sends nothing; with email off the
outbox is left untouched.

### Feature: password recovery by email (dev-plan 4.2) (2026-09-09)

*Plan tag: Opus. Run as Fable by user override.*

`POST /api/auth/recover/email` emails a one-time reset link: 32 random
bytes, stored hashed, one hour, single-use, and a newer request kills the
older link. It answers **202 with the same body every time**: whether the
address has an account, whether email is on, whether the send worked:
none of it is told to the caller, who may be probing. Throttled per
address and per client. The reset page offers "Email me a link" only when
the instance sends email, with the recovery-code form one click away.

Tests (five): link arrives and resets once; unknown address gets an
identical answer and no email; email off sends nothing and is not offered;
a newer link invalidates the older; throttling.

### Feature: outbound email (dev-plan 4.1) (2026-09-09)

*Plan tag: Opus. Run as Fable by user override.*

`SmtpEmailSender` (MailKit) sends from the SMTP settings an administrator
fills in: no environment variables, no restart. Plain text is the
message; a minimal HTML twin adds line breaks and clickable links, nothing
else. With **Send email** off, or the settings incomplete, nothing is
attempted and the result says why; a delivery failure is an `email.failed`
audit entry (recipient and subject, never the body). Settings gained the
**Send email** toggle, **Send test email to me**, and a **Public address**
override for the links email will carry (default `https://$DOMAIN`).

Tests use a recording sender; five cover the test-email path, a refused
send, the real sender declining when off, the HTML twin, and the URL
resolution.

### Fix: creation forms inherited the editor panel's icon gutter; admin refusal is now a message (2026-09-09)

The layout's boxed-form class was `.panel`: the same class the editor's
Confluence-style panel node renders as (`.panel.panel--info`, stored in
page content). Every creation form (API tokens, groups, webhooks, spaces,
save-as-template) was getting that node's 2.75rem icon gutter, which is
why the token button looked misaligned. The layout class is now `.card`;
the editor's name cannot change without a content migration. The
one-field-one-button forms also get a two-column grid so the button sits
on the input's baseline instead of wrapping.

A member who reaches any `/admin` URL now sees a page saying the area is
for administrators and to ask one for the change or the role, instead of
being bounced to the spaces list. The server was already refusing.

### Navigation: Groups and Audit under Admin, API tokens under Profile (2026-09-09)

Groups and the audit log are instance administration and now live as
tabs under Admin (`/admin/groups`, `/admin/audit`); API tokens are personal
credentials and now sit on the profile page next to sessions and
two-factor. The old URLs redirect. The top bar for a member is just
Spaces; the More menu no longer renders when it would be empty.

The move came with the server rule it implied. Any signed-in user could
create or delete groups and change memberships, and could read the whole
audit log. Group *listing* stays open, the permission picker needs it,
but creating, editing, deleting and membership changes are administrator
operations, as is reading the audit log (administrators still do not
bypass space permissions, so the log is filtered for them too). Tests
updated accordingly; the "audit entries do not leak titles from
inaccessible spaces" test now proves it for a second administrator.

Also: tables inside profile/admin cards scroll within the card instead of
spilling past its edge, and time/address cells no longer wrap.

### Security: threat model, review, internet-readiness checklist (dev-plan 3.7) (2026-09-09)

`docs/security.md` is the page an operator reads before exposing an
instance: who attacks a self-hosted wiki and why; a table of what each
Phase 3 layer defends and, as importantly, what it does not; the known
gaps and accepted trade-offs, numbered so nobody rediscovers them as
surprises; and the internet-readiness checklist (`.env` values, ports,
accounts, operations) that Phase 5's public-spaces switch will link to.
`SECURITY.md` at the root is the disclosure path.

The review pass (manual; no review skill is available here) walked every
registered route: all `/api` routes require authorization except health,
register, both sign-in steps, recovery and OIDC status/login, each of
which is rate-limited where it takes a credential; the OIDC `returnUrl`
accepts only same-origin paths; invite and reset tokens are returned once
and never listed; the SMTP password is never returned. Nine findings
survived as documented gaps (version in `/api/health`, `img-src https:`,
unaudited registration, in-process counters, mutable alerts, reusable
TOTP challenge, trust-by-private-range, no email yet, dual-role recovery
codes).

### Security: dependency hygiene (dev-plan 3.6) (2026-09-09)

*Plan tag: Opus. Run as Fable by user override.*

`npm audit` on the SPA went from 38 findings (3 high) to zero:
`react-router-dom` 7.18.1 → 7.18.3 (the RSC CSRF advisory; patch-level, no
API change) and every `@tiptap/*` package 3.28.0 → 3.31.3 (a prototype-
pollution and a ReDoS advisory in `@tiptap/core`). The TipTap packages
peer-depend on each other at exact versions, so `npm audit fix` cannot
move them on its own: all thirteen ranges were raised together. Editor
verified live afterwards: toolbar, live collaboration, lowlight code
blocks, no console errors. One thing to know: `npm audit fix --omit=dev`
prunes devDependencies from `node_modules`; run a plain `npm install`
after it.

The collab sidecar now has a `package-lock.json` (zero findings) and its
image builds with `npm ci`, so it is reproducible and auditable. `.NET`
was already clean.

`scripts/audit.sh` runs all three audits (web, collab, .NET with
transitives) and exits non-zero on any finding: the release gate.
`.github/dependabot.yml` groups weekly updates per ecosystem; security
updates arrive ungrouped.

### Security: sessions, two-factor sign-in, sudo mode, pinned Argon2 (dev-plan 3.5) (2026-09-09)

*Plan tag: Opus. Run as Fable by user override.*

Every sign-in is now a `UserSession` row the cookie points at, so one
browser can be signed out without signing out all of them. Profile →
Sessions lists them (address, browser, last activity) with revoke;
sign-out revokes the row so a copied cookie dies; sessions expire after
14 days idle and 90 days regardless. Cookies from before this change are
rejected once: the same safe direction as the security stamp.

**Two-factor sign-in** with any authenticator app: scan a QR (or type the
key), confirm with a code, done: the recovery codes from registration are
the backup, so there is nothing new to save. Sign-in becomes two steps
for enrolled accounts; a recovery code works in the code's place and is
spent. A code cannot be used twice. Enabling signs every other device
out. Wrong codes count toward the lockout. The admin **Require two-factor
for administrators** switch is now enforced: an un-enrolled admin gets 403
on every admin route, is told why, and cannot turn TOTP off while the
rule stands.

**Sudo mode**: changing who is an admin, flipping the public-spaces
switch, purging a page or removing a block re-asks for the password (or a
code) unless the session signed in within the last five minutes. The SPA
handles it transparently: a dialog appears, the action retries.

Argon2id parameters are pinned (64 MiB, 3 passes, 4 lanes) and an older,
weaker hash is upgraded in place at the next successful sign-in.

Tests (eleven): per-session revoke, sign-out kills the cookie, absolute
lifetime, two-step sign-in with reuse refused, recovery code in place of
the authenticator, enabling signs others out, disabling needs a
credential, admins forced to enroll, sudo refusal and re-auth, re-auth
extends the window, hash upgrade on sign-in. Full suite: 262 passing.

### Security: SSRF guard, attachment types, CSRF header (dev-plan 3.4) (2026-09-09)

*Plan tag: Opus. Run as Fable by user override.*

Webhooks could target any URL the server could reach: the cloud metadata
address, the database, the collab sidecar. `EgressGuard` now refuses
private, link-local, loopback and reserved addresses, local host names,
credentials in URLs and non-http schemes, at two moments: when the webhook
is saved (checking every address the name resolves to) and again inside
the socket connect at delivery, so a name that changed its mind since
(DNS rebinding) is refused on the wire. Redirects are followed by hand,
three at most, each hop checked. `Egress:AllowedNetworks` opens a private
range deliberately. A refused attempt is still a security event.

Uploaded files are served as what their bytes say (PNG, JPEG, GIF, WebP,
PDF signatures win over the label); anything a browser might execute (
HTML, SVG, XML, scripts, or bytes that look like markup) is stored and
served as `application/octet-stream`. Downloads keep `Content-Disposition:
attachment` and `nosniff`.

Every state-changing `/api` request authenticated by the session cookie
must carry `X-Requested-With: Tesria`; the SPA sends it everywhere,
including the two multipart uploads. Bearer-token callers and sign-in are
exempt. Kestrel's body limit is set to 100 MB to match Caddy.

Tests (twenty-five, including theories): twelve refused targets, the
allow-list, the connect-time refusal, webhook creation refused and
recorded, six content-type decisions, an uploaded HTML file downloading
opaque with `nosniff`, and the CSRF header required / exempt / not needed
at sign-in. Verified live: cookie POST without the header → 403 with an
explanatory body; with it → 200; the SPA still performs state changes.

### Security: threat detection, admin alerts, blocklist (dev-plan 3.3) (2026-09-09)

*Plan tag: Fable → Opus. Both halves run as Fable by user override. The
design (signals, thresholds, alert lifecycle, what it deliberately does
not do) is in architecture.md and was written before the code.*

Thirteen detectors now watch the instance: failed-sign-in bursts and
credential stuffing per address, repeated lockouts per account, an
administrator signing in from an address never seen for that account, a
spike of 401/403s, mass page removal, API-token minting bursts,
registration bursts, every admin promotion, every flip of the
public-spaces switch, a webhook aimed at a private address, and a broken
audit chain. Bursts write one event at the threshold with the count, not
one per hit; discrete signals write every time. An alert-worthy event
creates a `SecurityAlert` (Open → Acknowledged → Resolved, with a note)
and one notification per administrator, with a one-hour cooldown per
(kind, key) so an ongoing attack is one alert an hour, not one a second.

`SecurityEvents` is append-only at the database layer, added to the
runtime role's revoke list, so the record of an attack cannot be tidied
away by the app. Alerts live in their own table precisely so acknowledging
one never needs an UPDATE on the append-only one.

**Admin → Security** now has an overview strip, the open alerts with
one-click mitigations relevant to each (block the address for 24 h, sign
the account out everywhere, revoke its tokens, suspend it), kill switches
(public spaces, registration, TOTP-for-admins), a blocklist (address or
CIDR, optional expiry, refuses to block your own address), and a recent
events timeline. The blocklist is enforced by middleware that runs right
after forwarded headers and before authentication: a blocked address gets
403 with or without a valid session. The bell links security alerts to
the page.

Tests (fourteen): each burst detector fires at its threshold and not one
below; the cooldown suppresses repeats; admins are notified; the
first-ever admin sign-in does not alert but a later new address does;
promotion and the public toggle always alert; a private webhook target
alerts; a tampered chain becomes a Critical alert through the daily
monitor; acknowledge/resolve; the blocklist refuses before auth and lifts
on removal; you cannot block yourself; members get 403 on all of it.
Verified live: five failures against distinct accounts from one host
produced a Critical credential-stuffing alert with the real client
address, a bell notification, an alert card with "Block <address>", and a
working Acknowledge; `DELETE FROM "SecurityEvents"` as `tesria_app` →
`permission denied`.

### Security: rate limiting and account lockout (dev-plan 3.2) (2026-09-09)

*Plan tag: Opus. Run as Fable by user override.*

Online brute force against `/api/auth/login` was unlimited. Now: a sliding
window per client address on sign-in, registration and recovery (default
10/min); a per-account lockout after 5 consecutive failures, doubling from
60 s up to 15 min and never permanent; a per-account limit on API-token
minting (20/h); and a global per-address limit for callers with no session
(300/min): the one Phase 5's public-read mode will lean on. Signed-in
users are not globally limited. 429s carry `Retry-After`.

A locked account gets the same empty 401 as a wrong password, even with the
right one, and the right password does not reset the counter while locked.
Success, recovery, or an admin unlock does. Failure counts persist on the
user row, so a restart is not a fresh budget.

All six limits are site settings, editable on the new **Admin → Security**
page, which also lists active lockouts with one-click unlock and hosts
3.1's "Verify now" for the audit chain. Users shows a `locked` badge.

Tests (ten) drive every limiter through spoofed proxy addresses, including
the one that proves two addresses no longer share a bucket, which is what
3.0 was for. Verified live: ten wrong sign-ins from one host → 401 ×10 then
429 with `Retry-After: 60`; Security page renders limits and a passing
chain verification.

### Security: least-privilege database role and audit hash chain (dev-plan 3.1) (2026-09-09)

The app no longer runs as the Postgres superuser. At startup it uses the
owner connection once, unpooled, to migrate, then creates `tesria_app`
(`APP_DB_PASSWORD`) with read/write on everything except `UPDATE`/`DELETE`
on `AuditLogs` and `PageViews`, and runs as that role from then on; so
does the collab sidecar. Provisioned by the app rather than an init script
so existing installs get it too, and re-granted every start so tables from
future migrations are covered. Empty `APP_DB_PASSWORD` falls back to the
owner with a warning rather than refusing to start.

Every audit row is now a link in a SHA-256 hash chain (`Sequence`,
`PrevHash`, `Hash`), linked inside `SaveChanges` under a Postgres advisory
lock so no code path can write an unchained row and no two writers can take
the same position. The 36 rows written before this existed were linked at
first start. `POST /api/admin/audit/verify`, `scripts/verify-audit-chain.sh`
and a daily in-process monitor walk the chain and name the first broken
link: an altered row, a missing row, or (monitor only) a chain shorter
than last time. Every row is also written to stdout as JSON under the
`Tesria.Audit` log category as it commits: a copy the database password
cannot reach.

Two round-trip hazards found by verifying against the real database: jsonb
re-orders keys and normalizes numbers, and `timestamptz` keeps microseconds
where .NET keeps ticks. Hashing is over a canonical form that survives
both, and all 36 stored hashes were recomputed independently in Python
from a `psql` dump to prove it. Live: `UPDATE "AuditLogs"` as `tesria_app`
→ `permission denied`.

Tests (six): contiguous sequences and a passing verify; an altered row is
named; a deleted row is named as a gap at its successor; legacy rows are
backfilled; canonical JSON is order/whitespace/number-spelling insensitive;
members cannot verify.

### Security: proxy trust, secure cookies, security headers (dev-plan 3.0) (2026-09-09)

*Plan tag: Opus. Run as Fable at the user's request: Phase 3 is security
work and the user chose to spend the larger model on all of it.*

The app now knows who the client is. `UseForwardedHeaders` runs first in
the pipeline and believes `X-Forwarded-For` / `X-Forwarded-Proto` from the
compose network's private ranges only (`Proxy:TrustedNetworks`), taking
only the nearest hop so a client cannot pick its own address by sending the
header. Before this, every request carried Caddy's container address: the
finding that made 1.3's recovery limiter key on email instead of IP, and
that would have made any per-IP limiter throttle everyone at once.

The session cookie is `Secure` unconditionally in Production
(`Security:AllowInsecureCookies` opts out, documented as unsafe). Every
response carries `nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy`,
`Permissions-Policy`, COOP/CORP and a Content-Security-Policy whose
`script-src` is `'self'` plus a hash of the inline theme script, computed
at startup from the `index.html` this process serves. `style-src` allows
inline styles on purpose, the editor writes them, and that trade-off is
recorded in the architecture doc.

`deploy/Caddyfile.public` is the internet-facing configuration: HSTS on,
the on-demand-TLS catch-all gone. Selected with `CADDYFILE=` in `.env`.

Tests (six) act as the proxy and as a stranger through a startup filter
that sets the connection address: forwarded address honored from loopback,
ignored from a public address, only the last hop believed, the cookie
turns `Secure` when the proxy says HTTPS, and the headers are on every
response. Verified live: headers present, collaboration websocket connects
under the CSP, inline theme script runs under its hash, no CSP refusals on
spaces, page view, editor or admin.

### Feature: recovery codes for existing accounts (dev-plan 1.3) (2026-09-09)

Recovery codes were only ever issued at registration, so every account that
predates them, both accounts on this instance, had none and no way back in
if its password were lost. They are now offered at sign-in: an account with
zero codes gets a dialog, and one click generates a set.

**No password is asked for right after signing in.** The sign-in is the
re-authentication; asking for the same password seconds after it was typed
proves nothing and mostly trains people to type passwords into prompts. The
window is 15 minutes (`Auth:FreshLoginMinutes`, overridable), carried in an
`auth_time` cookie claim. Editing your profile re-issues the cookie with the
*original* `auth_time`, so ordinary activity cannot extend the window.

Three rules the tests pin down:

* **A supplied password is always verified**, fresh session or not. Accepting a
  wrong one because the session happens to be recent would tell someone their
  password was right when it was not.
* **Outside the window the password is required**, so a tab left open on a
  shared machine cannot mint codes that work as a permanent password reset.
* **Omitting it is the supported path only while fresh.**

Two bugs found by running it rather than by building it:

* The dismissal was stored per tab, not per user, so one person clicking
  "Not now" silenced the prompt for whoever signed in next in the same tab.
* Refreshing the profile after generating took the remaining count off zero,
  which closed the dialog: destroying the only copy of the codes that had
  just been generated. The dialog now stays open whenever codes are on screen.

A stale session used to hit a dead end telling it to go to the profile page; it
now asks for the password in the dialog. Verified live end to end: prompt after
sign-in, one-click generation, codes displayed with download/copy, and the
prompt gone afterwards.

### Feature: admin panel (dev-plan 2.1–2.5) (2026-09-09)

`/admin`, visible only to administrators, with Dashboard, Users, Spaces,
Invites and Settings. The role check in the UI is convenience: every
`/api/admin/*` route enforces it server-side, and there is a test asserting a
member gets 403 on each.

**Users** shows role, status, recovery-code count, last-seen and avatar, with
promote/demote, suspend/reactivate, revoke-sessions, revoke-tokens and issue-
reset. Three guards matter more than the listing:

* **The last administrator cannot be demoted or suspended.** An instance with
  no admin has no way back: nobody could change settings, issue invites or
  restore access without editing the database by hand.
* **You cannot suspend yourself**, checked before the last-admin rule so the
  message is the accurate one.
* **Suspension rotates the security stamp**, so existing sessions die on their
  next request rather than lingering until the cookie expires. Verified live.

**Sessions and tokens revoke separately, deliberately.** A token authenticates
through a different scheme and a session revocation does not touch it, so
"lock this account out" needs both: there is a test proving the token still
works after sessions are revoked, and stops after tokens are.

**Spaces** is metadata only: key, owner, page count, storage, archived state.
Admins do not bypass space permissions, so this must not become a way around
that; a test asserts no page content appears in the response.

**Dashboard** is one aggregate endpoint rather than a page firing a dozen
requests: the counts are cheap but the round trips are not, and a single
response means the whole dashboard is consistent with itself rather than
assembled from twelve different instants. Range is clamped to 1–365 days.

Charts are hand-rolled SVG sparklines: no charting dependency added, since
the bundle is already 1.1 MB. Written against the `dataviz` skill: one series
means no legend and no categorical palette, color is a single token
(`--primary`, or `--danger` for failed sign-ins, which is a status signal
rather than another series), text wears text tokens rather than the series
color, marks are 2px with a surface ring on the hover marker, and every
sparkline has a hover crosshair reading out the exact day and value.

**Empty days are included in every series.** A sparkline built only from days
with activity silently compresses a quiet week into one point and reads as
steady use when the truth is the opposite.

**Failed sign-ins are on the front page on purpose:** a spike there is the
first visible sign of a brute-force attempt. Dev-plan 3.3 turns it into an
alert; until then somebody has to be able to see it.

Nine tests in `AdminPanelTests` (187 total). Verified live against real data:
23 pages, 15 recorded views, top page "Tesria REST API", 30 daily points per
series.

### Feature: author identity on comments and version history (dev-plan 1.5) (2026-09-09)

Comments and version history both returned a bare `AuthorId` and rendered no
author at all: a threaded discussion where you could not tell who said what,
and a history that could not answer "who changed this?" despite storing the
answer since Phase 1. Both now carry `AuthorName`, `AuthorAvatarHash` and
`AuthorAvatarVariant`, projected from the joined `User`.

Projected server-side rather than resolved by the client: a per-comment lookup
is N round trips, and a client-side directory fetch would hand the whole user
list to anyone who can read one page.

This completes the render list 1.2 could not finish: avatars now appear in
comments and version history as well as the topbar and profile.

Three tests (178 total). One of them changed shape during writing: the
"deleted author" case cannot be reached by orphaning a comment, because the
foreign key forbids it. The `?? "Deleted user"` in the projection is therefore
defensive only, and the test now covers what 2.2 will actually do: anonymise
the row, keeping the id so history stays attributable while the name no longer
identifies anyone.

**One bug worth recording.** The first version set `comment.Author` to a
detached `User` so the create response would carry the name. EF treats a
detached entity on a navigation as new and tried to INSERT it, tripping the
unique-email constraint and breaking six existing comment tests. Re-reading the
comment with the author joined is one extra query on create and is obviously
correct; the update path needed the same include.

### Feature: closed registration and invites (dev-plan 1.4) (2026-09-09)

`AllowPublicRegistration` was already enforced in 0.2; this adds the other way
in. `POST /api/admin/invites` mints a single-use registration link an
administrator hands over however they already communicate: the only way to
add a user to a closed instance with no email server.

An invite may be bound to an address. A forwarded link should not become a
registration for whoever received it, so a bound invite refuses any other
address; leaving it blank is the deliberate "give this to whoever needs it"
case. Invites expire (7 days by default, 1–90 configurable), are revocable,
and are spent **inside the same transaction that creates the account**, so a
failure part-way cannot burn an invite without producing a user.

Registration accepts `?invite=` from the query string, so the person following
a link never has to know a token exists. This closed a real gap: until now
anyone who could reach `/register` could create an account, which sat awkwardly
against the brand page's "control who sees what".

Seven tests in `InviteTests` (175 total).

**Two SQLite provider limitations hit while building 1.3 and 1.4**, both the
same shape and both already known to the codebase: the test provider cannot
translate a `DateTimeOffset` comparison or use one in `ORDER BY`. Reset-token
expiry is now compared in memory, and the invite list is ordered in memory.
`AuditEndpoints` had already worked around the ordering case by branching on
provider; these sets are small enough that ordering client-side unconditionally
is simpler than a branch.

### Feature: offline password recovery (dev-plan 1.3) (2026-09-09)

Eight single-use recovery codes minted at registration and shown exactly once,
plus an administrator-issued reset link. Neither needs email, which on a
self-hosted instance is usually not configured at all.

**Codes are SHA-256, not Argon2id: a deliberate departure from the plan.**
Argon2's cost exists to make guessing a *low-entropy* secret expensive; these
are 60 bits of cryptographic randomness, where a fast hash is already
unguessable. Argon2 would instead mean up to eight deliberately-slow
verifications per attempt: bad for the user and a free denial-of-service lever.
This matches how `ApiToken` already stores its secret, including the
fixed-time comparison.

**The rate limiter is keyed on email, not IP: also a departure.** Per-IP is
the obvious choice and is wrong today: every request arrives with Caddy's
container address, so an IP limiter would throttle the whole world as one
caller. Keying on the supplied email bounds guesses against any one account,
which is the actual threat, and is immune to the proxy problem. Dev-plan 3.2
adds real per-IP limiting once 3.0 makes client addresses real.

Codes normalize on redemption, dashes and case are stripped, because they get
written on paper and typed back months later. The alphabet omits O/0, I/1/L and
U for the same reason. Every failure returns an identical response whether the
account exists, the code is wrong, or the account is SSO-only, so this cannot
be used to discover which addresses have accounts.

Both recovery paths rotate the security stamp, signing out every existing
session. Recovery exists for when somebody else has your account; leaving their
session alive would defeat it.

`POST /api/admin/users/{id}/reset-password` issues a one-time, one-hour link an
admin hands over out of band. It deliberately does not set a password: an
administrator should be able to restore access without ever knowing the
resulting credential. Issuing a second link invalidates the first.

Frontend: codes are shown after registration behind an explicit "I have saved
these" gate with copy and download: the registration redirect had to be held
back, since it otherwise fired the moment the account existed and skipped past
them. A "Forgot your password?" link on sign-in reaches `/recover`; `/reset?
token=…` is the admin link. The profile page shows how many codes remain and
warns when there are none, which is every account created before this shipped,
verified live on this instance.

Twelve tests in `AccountRecoveryTests` (168 total).

### Feature: avatars (dev-plan 1.2) (2026-09-09)

Every user has an avatar from the moment they register, with nothing stored:
an inline SVG of their initials on one of twelve backgrounds, picked by an
FNV-1a hash of their id. Not a char-code sum: user ids are hex GUIDs, sharing
an alphabet and a length, which is exactly where a weak hash clusters. All
twelve carry white text at 4.5:1 or better (measured, not judged), and all are
dark enough to read on both page grounds, so no per-theme treatment is needed.

`User.AvatarVariant` records an explicit pick; null derives one from the id.
Stored as an index rather than a color so the set can be restyled without
rewriting rows, and kept when a picture is uploaded, so removing the picture
returns to the color the user chose, not to the derived one.

Uploads are cropped square in the browser before sending, via
`createImageBitmap`, which decodes off the main thread and honors EXIF
orientation, without it a portrait phone photo arrives sideways. The crop is
not cosmetic: the server center-crops too, so doing it here is what makes the
stored result match what the user was shown. Downscaled to 512px first, so a
12MP photo is not uploaded whole to produce a 256px thumbnail.

Verified live: the generated avatar rendered "AB" on the emerald variant
derived from the account id, picking swatch 3 persisted server-side and
re-rendered both the profile and topbar avatars in `#5b47ba`, and clearing it
returned to the derived one.

**Where avatars appear:** topbar and profile. Comments and version history
return only an `AuthorId` and render no author identity at all today, so
avatars there wait until they show names: adding names was outside this item.
`GET /api/users` does now carry `avatarHash`/`avatarVariant`, for the admin
users list in 2.2; the existing people picker is a `<select>`, whose options
cannot contain markup, so it cannot show them.

Three tests added (158 total), including that an uploaded picture wins over a
chosen variant while preserving it for when the picture is removed.

### Feature: edit your own profile (dev-plan 1.1) (2026-09-09)

A `/profile` page, reachable from the username in the topbar, with three
independently-submitting sections: display name, email address (requires the
current password, refuses a collision with 409) and password (requires the
current one, enforces the same minimum as registration from the same
constant). One combined form would either demand a password to rename
yourself or skip the check that protects the other two.

**The load-bearing part is `User.SecurityStamp`.** A cookie scheme is
stateless, the cookie *is* the proof, so nothing on the server can normally
take it back before it expires. The stamp is issued into the cookie as a claim
and compared against the stored column in `OnValidatePrincipal` on every
request, so rotating it invalidates every outstanding cookie for that account
on its next request. Changing a password rotates it, and the session that made
the change is re-issued with the new value so that person is not signed out
along with everyone else. Suspension (2.2), admin force-logout (3.3) and 2FA
enrollment (3.5) all reuse this rather than adding their own mechanism: the
same validation already rejects a cookie whose account has become suspended,
with a test proving it.

**Everyone is signed in once on deploy.** Cookies issued before the stamp
existed carry no claim and are rejected. That is the safe direction: treating
a missing claim as valid would mean a pre-existing cookie outliving the
password change meant to kill it. Verified on the running stack: the live
session was signed out on the first request after deploying.

**API tokens are deliberately unaffected**: they authenticate through a
different scheme and carry no cookie, so a password change does not revoke
them. A script's credential should not die because its owner rotated a
password, but it must be independently revocable, which it already is.

OIDC-provisioned accounts (no local password) can change their display name
but not their email or password: the identity provider owns those. The server
refuses with a message the UI shows, and the UI renders those sections
read-only rather than letting a submit fail.

Nine tests in `ProfileTests`, including that a second live session dies on its
next request while the one that changed the password keeps working.

### Feature: profile media storage (dev-plan 0.4) (2026-09-09)

Avatars stored through the existing `IAttachmentStorage` under their own key
namespace, so the S3 implementation that interface reserves a slot for will
cover them too. Keys are deterministic per owner, so a replacement overwrites
rather than accumulating orphans.

**Every upload is re-encoded to a fixed 256px WebP square, and that is the
security control rather than a convenience.** The stored bytes are always ones
this process produced: EXIF (often GPS) is stripped, polyglot files stop being
polyglot, and decoded dimensions are bounded: checked from the codec header
before any pixel buffer is allocated, so a decompression bomb is refused
rather than decoded first. **SVG is rejected by sniffing the bytes**, not by
trusting the declared content type, so an SVG labeled `image/png` does not
get through; there is a test for each of those framings.

**SkiaSharp (MIT) rather than ImageSharp**, because ImageSharp 3.x moved to
the Six Labors Split License and dev-plan 8.2 intends an Apache 2.0 release.
Verified in the runtime container and not only on the build host, since the
native-asset variant is exactly the thing that differs between them: the
container is glibc 2.39 and the package ships a matching `linux-arm64` build.
A real 101 KB 600×400 PNG uploaded to the running stack came back as a 1.2 KB
256×256 WebP.

Cache busting is a content hash on the URL, stored as `User.AvatarHash` so
serving costs no hashing and the SPA can build the URL from the session it
already has. Each version is its own URL, so the response can be cached
indefinitely without going stale.

**Scope note:** 0.4 also carries the avatar upload/delete endpoints, because
the validation, size cap and re-encode it specifies have no other home: a
storage pipeline with no way in cannot be tested end to end. Dev-plan 1.2 is
therefore the profile UI, the client-side crop, and the generated default
avatars, not the server half.

Eight tests in `ProfileMediaTests`, including that a user with no avatar and
an unknown user id are indistinguishable, so the endpoint cannot be used to
probe which ids exist.

### Feature: usage telemetry (dev-plan 0.3) (2026-09-09)

Three signals, recorded now so the admin dashboard (2.5) ships with real
history instead of an empty chart.

**`User.LastSeenAt`**, stamped by middleware after authorization and throttled
to one write per user per five minutes: "active in the last 7 days" needs
coarse resolution, so a write per request would be a lot of work for almost no
information. Update by primary key, no prior read, failures swallowed.

**Login events.** `user.login` is attributed to the account; `user.login_failed`
is attributed to nobody and records neither the user id nor the attempted
address. An audit log every admin can read should not become a list of
addresses somebody guessed, nor confirm which ones exist. This needed
`IAuditLogger.RecordAs(actorId, …)`, since sign-in happens before the request
has a principal.

**`PageView`**, one row per read, after the permission check so a refused read
is never counted, and browser sessions only: an API token is a script, and a
nightly export would otherwise dwarf every human in "most viewed pages". The
token test is the same one the Smart policy scheme uses, so the two cannot
disagree. `UserId` is nullable from day one because public read mode (Phase 5)
writes anonymous views into this table, and widening the column later would be
a migration on a table that is large by then.

**It also made a Phase 3 finding concrete.** Exercising login on the running
stack logged `172.18.0.7` for three requests from two different clients:
Caddy's container address, not the callers'. That is the forwarded-headers gap
in the security baseline, now demonstrated rather than inferred. The IP is
recorded anyway so the history exists and becomes correct when 3.0 ships;
`architecture.md` says plainly not to build per-IP logic on it before then.

Six tests in `TelemetryTests`, including that a token read and a refused read
are both uncounted, and that last-seen throttling is per user rather than
global.

### Feature: instance settings (dev-plan 0.2) (2026-09-09)

A single typed `SiteSettings` row an admin edits at runtime via
`GET`/`PUT /api/admin/settings`: instance name, `AllowPublicRegistration`,
`AllowPublicSpaces` (the Phase 5 kill switch, off by default), SMTP
host/port/username/password/from-address/TLS mode, `EmailEnabled`, and
`RequireTotpForAdmins`. Typed columns rather than key/value so EF validates
them and every shape change is a migration.

The **SMTP password is write-only over the API**: it is encrypted with the
Data Protection keys already stored in this database, responses carry only
`smtpPasswordSet`, and audit entries name which fields changed but never the
value. Every field on the update is optional (an omitted field keeps its
stored value, so changing one setting cannot clobber the rest) with one
addition for the password, where an empty string means "clear it", which
`null` cannot express.

Registration now honors `AllowPublicRegistration`, **except for the very
first account on an empty instance**. Otherwise an operator who closes
registration before anyone has signed up could never set the instance up.
There is a test for each half of that.

The cache is a singleton with a 30-second TTL while the service is scoped, so
a hot path like registration can read settings on every request. Invalidation
is in-process, which is a single-instance assumption: the short TTL bounds
how stale a second replica could get, and that is written down in
`architecture.md` rather than left implicit.

Eight tests in `SiteSettingsTests`, including that reopening registration
takes effect immediately (proving invalidate-on-save, not TTL expiry) and
that the password never appears in a response, in the stored column, or in
the audit log.

### Feature: instance roles and administrators (dev-plan 0.1) (2026-09-08)

`User.Role` (`Member = 0 | Admin = 1`): an enum rather than a bool, so a
future Viewer/Moderator is a new value, not a migration. The first account
created on an empty instance is Admin, whether it arrives through `/register`
or OIDC provisioning; the migration backfills existing installs by promoting
the earliest-created account, so no instance is left with content and nobody
able to administer it. Verified on this instance's real data: the owner's
account was promoted, the docs bot was not.

**Admins are not a permission bypass.** This was the design question the plan
left open, and the answer is Confluence's own: a site admin sees exactly what
their grants allow. What the role confers is access to `/api/admin/*` and an
audited `POST /api/admin/spaces/{key}/recover-access`, which writes the
caller an explicit space-admin grant, after which the *existing* rules apply
unchanged, including the one that already lets explicit space admins past
page restrictions. A silent bypass would let any admin read any team's
private space with no trace, would need an "unless admin" branch in every
permission check, and could never be revoked afterwards. Recovery is
idempotent, so a retry is neither a duplicate grant nor a second audit entry.

Registration's "is this the first account?" check and its duplicate-email
check both read the table before writing, so they now share one serializable
transaction, without it two simultaneous first registrations could each see
an empty table and both become admin.

`CurrentUser.IsAdminAsync()` reads the row rather than trusting a claim: a
role claim would be stale until the user's next sign-in, so a demotion
wouldn't take effect until then. One primary-key lookup, cached per request.

Six tests in `RoleTests`, including the two that pin the decision down: an
admin gets **404** (not 403, per the existing masking rule) on a private
space they hold no grant for, and revoking the recovered grant returns them
to no access.

Spec: `architecture.md` → "Roles and administrators". Written by Fable 5.1
under the plan's model gate, implemented by Opus 5.

### Fix: the topbar is three tiers now, not two (2026-09-08)

Between --bp-mobile and --bp-tablet the bar showed its full desktop layout (
brand, four nav links, a 420px-max search box and the right-hand cluster)
with no wrap fallback. It never actually fit there; it only appeared to
because `.brand` would quietly ellipsis. Adding the logo (a fixed 20px that
cannot ellipsis) and the appearance button used up that slack, and the band
tipped into real horizontal overflow, with "API Tokens" wrapping onto two
lines.

That wrap is also what made the links look top-aligned: a two-line link makes
the nav row taller, and its single-line neighbors then sit at the top of
it. `.topbar__link` is `inline-flex`, centered, and `white-space: nowrap` now,
so it cannot recur, but the real fix is giving the pressure somewhere to go.

**641–1024px is a proper middle tier.** Spaces stays visible; Groups, Audit
and API Tokens move behind a **More ▾** menu whose trigger reads as active
when the current route is one of them; search keeps a
150px floor (unpinned it collapsed to ~2px); and the username, the least
load-bearing thing in the bar, hides. Below 640 the existing hamburger is
unchanged, and its column lists all four links itself, so More hides there.

The secondary links are rendered twice on purpose (flat, and inside More)
with CSS choosing which set shows. That is the same convention the editor
toolbar already uses for its heading/list/alignment groups
(`.toolbar__flat` vs `.toolbar-dropdown`), not an accident.

Measured rather than eyeballed, at 1280 / 1025 / 1024 / 800 / 641 / 480: no
horizontal overflow at any width, every visible link sharing one top edge and
one 27px height, and search at 175px in the worst case (641px). The bar also packs left now (`justify-content: flex-start`, with
`margin-left: auto` on the right-hand cluster) instead of `space-between`.
Space-between split leftover width evenly into every gap, which floated the
nav somewhere between the brand and the search box on desktop and, with the
collapsible out of flow on mobile, parked the brand dead center. One rule
fixes both: the nav anchors to the brand, the brand to the hamburger, and all
the leftover sits in a single gap before the right-hand cluster, and the
search box, which had a 420px cap (260px in the middle tier), now has none,
so it is what fills that gap and grows with the viewport.

Those numbers
also showed the wordmark-hiding rule added with the logo was now dead weight,
with More absorbing the pressure there is more slack at 641px than the
word needs, so it is gone, and the brand reads "Tesria" at every width.

### Design: Tesria's brand mark in the favicon and topbar (2026-09-08)

Took the layers mark from the project's original brand page. It
needed no adaptation: the mark is already drawn in the same language as this
app's icon set (24x24 viewBox, 1.8 stroke, round caps and joins,
`currentColor`), so it dropped straight in.

The favicon replaces the scaffold's purple bolt. Because it is an SVG it can
carry its own `prefers-color-scheme` media query, so the mark is brand blue
(`#2496ed`) on a light browser chrome and brightens to `#6cb6f7` on a dark
one; where that isn't supported the plain `stroke` still applies, so the
fallback is the brand blue rather than nothing. Stroke is widened from 1.8 to
2 for the favicon only: at 16px, 1.8 on a 24 viewBox thins to about one
pixel and the middle layer lines start to drop out.

In the topbar the mark sits left of the wordmark and takes `--primary`, so it
follows both the light/dark theme *and* the chosen accent, while the wordmark
stays `--text`. That is the same split the brand page uses: colored mark,
neutral wordmark.

One detail worth keeping: `.brand`'s shrink-and-ellipsis behavior (added for
narrow phones, where the topbar has no wrap fallback) moved from the link to
the new `.brand__word` span, and the mark is `flex-shrink: 0`. Otherwise the
logo would have been the first thing squeezed out on a small screen.

Also added light/dark `theme-color` meta tags matching the two `--bg` values,
so mobile browser chrome tracks the app.

**The favicon tracks the accent too.** A favicon is a separate document that
can never read the page's custom properties, so a single themeable SVG is not
possible: the color has to be baked in per variant. Rather than shipping six
files that would drift from the palette the first time an accent is retuned,
`applyFavicon()` renders the mark to a data URI from `ACCENT_HEX` in theme.ts
and swaps the `<link rel="icon">` href. `public/favicon.svg` stays as the
pre-JS default. Which half of each pair is used follows the *operating system*
rather than the app's theme setting: the icon lives in the browser's tab strip,
so it should match that chrome, not the page: someone running the app in
forced light on a dark desktop still wants the light-on-dark mark in their tabs.
`startFaviconSync()` runs at startup so this applies on every route, including
the sign-in pages where the appearance menu isn't mounted.

One layout consequence, found by measuring rather than by eye: between
--bp-mobile and --bp-tablet the topbar shows the full desktop layout with no
wrap fallback, and already relied on `.brand`'s ellipsis to fit. The mark is a
fixed 20px and cannot ellipsis, so adding it tipped that band into real
horizontal overflow. The wordmark is therefore hidden below --bp-tablet,
leaving the mark alone, which gives back more than the mark costs, and reads
as a deliberate logo-only brand rather than the half-word truncation that
appeared first.

### Feature: appearance menu · theme popup + accent colors (2026-09-08)

The theme control is a popup now rather than a cycling button, with two
sections: **Theme** (System / Light / Dark, each with a one-line hint, System
showing what it currently resolves to) and **Accent color** (blue, teal,
green, purple, orange, magenta).

System remains the default for new users: nothing is written to storage until
an explicit choice is made, and re-picking System clears the key rather than
pinning today's resolved value. Same for the accent: blue stores nothing.

**Each accent is defined twice, for light and dark, rather than derived from
one value.** A hue dark enough to pass 4.5:1 as link text on white is far too
dark to read on a dark ground, and the reverse. Both sets were measured against
WCAG AA (light values against `#ffffff`, dark values against `--bg`) and
`--on-primary` (text on a filled accent button) is chosen by computed contrast:
white in light mode, dark ink in dark. Green is the clearest illustration:
`#1a6c45` in light, `#4bce97` in dark.

A subtlety worth knowing if you add a seventh accent: `:root[data-accent="x"]`
and the dark base `:root:not([data-theme="light"])` have *identical*
specificity. Blocks are therefore emitted for every accent including the
default blue, so a higher-specificity dark block always exists to win, without
it, choosing an accent explicitly would drag the light palette into dark mode.

The picker's own swatches read themed `--accent-dot-*` tokens, so each dot
previews the color that accent will actually produce right now, and the whole
row changes when the theme does.

The accent deliberately drives only the chrome. Panel colors are semantic
(a warning is yellow regardless), and table cell / highlight colors belong to
the document's author: neither follows the accent.

Known limitation: in light mode, orange is necessarily a deep rust (`#9a4d00`).
A brighter orange cannot reach 4.5:1 as link text on white, and `--primary` is
used for body-sized link text, so the accessible value is the one that ships.

### Feature: light/dark theme toggle (2026-09-08)

Three preferences, not two: **system** (the default, following the OS),
**light** and **dark**. A plain on/off switch loses "just follow my machine"
permanently the first time it is pressed, so the topbar control cycles
system → light → dark and shows the OS's current resolution while on system.

Mechanically it is the standard three-state pattern. `:root` carries the light
palette; `@media (prefers-color-scheme: dark)` applies the dark one *unless*
`[data-theme="light"]` is set; and a `:root[data-theme="dark"]` block lets an
explicit choice win in both directions, including dark-while-the-OS-is-light,
which the media query alone cannot express. `theme.ts` owns the attribute and
localStorage; `index.html` re-applies the stored value in an inline,
synchronous script before first paint, without which the page renders light
for one frame and then flips.

Getting there meant tokenizing the stylesheet: every color now resolves
through a custom property. `--surface` is new and carries the weight: it is
identical to `--bg` in light mode and deliberately lighter in dark, which is
what separates a card, the paper sheet or a popover from the page behind it.

Two things that needed more than a token swap:

- **Panel icons** were `background-image` data URIs with the stroke color
  baked in, which would have meant carrying a second full set for dark mode.
  They are `mask-image` now: the SVG supplies the shape, `--panel-icon`
  supplies the color, so one token per type re-tints all five.
- **Author-chosen colors** (a table cell's `backgroundColor`, a highlight
  mark's `color`) are stored *in the document* and are always light tints from
  `palette.ts`. A theme cannot restyle them without discarding the author's
  choice, but left alone in dark mode they are a light patch carrying light
  `--text`, i.e. invisible. Dark mode pins the ink dark on exactly those
  elements instead, so a colored cell reads identically in both themes.
  Verified against the API space's status-code table, where tinted and
  untinted cells sit side by side in one row.

The code block is deliberately **not** themed: it stays dark in both, the way
most editors and docs sites treat code.

Known gap: the toggle lives in the authenticated topbar, so it is not reachable
from the sign-in and registration pages. The *theme* still applies there (the
pre-paint script is route-independent); only the control is missing.

### Feature: panels, color palettes, and a toolbar alignment fix (2026-09-08)

Four editor gaps against Confluence, closed together.

**Panels** (`panelExtension.ts`): colored callouts, with `panelType` taken
from ADF's own set: info, note, warning, success, error. Confluence's legacy
Info/Tip/Note/Warning macros all map onto that set (the old Tip macro is
today's `success` panel), so all four names the request asked for have a home
without inventing a sixth type. Available from a toolbar picker and from the
slash menu, both generated from one exported `PANEL_TYPES`/`PANEL_LABELS` so
they can't drift. Color and icon live in `index.css` keyed off the rendered
`data-panel-type`, which keeps the icon a `::before` pseudo-element rather
than a child node ProseMirror would fight over, and gets read-only rendering
the icon for free.

**Table cell / row / column backgrounds** (`TableCellMenu.tsx`): Confluence's
per-cell chevron in the top-right of the cell holding the cursor, opening a
"Background color" palette. Cursor-driven, so deliberately not sharing
`useHoveredTable` with the hover-driven row/column and width controls. The
Cell/Row/Column scope buttons widen the written rect via
`TableMap.cellsInRect()` and apply the whole scope in one transaction, rather
than replacing the user's selection with a `CellSelection`: the cursor stays
put after coloring a row.

**Highlight colors**: `Highlight` is now `multicolor`, and the toolbar
button is a palette instead of an on/off toggle. Highlights stored before
this have no `color` attr and still render as a plain `<mark>`.

Both palettes are Atlassian's own light/medium/bold values, matching the
fixed palette Confluence offers rather than a hex input, and are stored *in
the document* so they survive export. The export renderer now whitelists a
color to plain hex before it reaches a `style` attribute: document JSON is
stored as given, so an unvalidated color was a CSS-injection route into
exported HTML.

**Fix: the insert-image icon sat 4.8px above every other toolbar button.**
That button is a `<label>` (it wraps a hidden file input), so the global
`label { margin-bottom: 0.6rem }` applied to it and to nothing else in the
row. `.toolbar` centers its children with `align-items`, which centers each
item's *margin* box, so 9.6px of phantom margin below the label lifted its
border box by exactly half. Measured before and after against the real
stylesheet: 4.80px of spread, now 0.00px. Fixed with `margin: 0` on
`.toolbar__btn` rather than on the one label, so any element type used as a
toolbar button is immune.

Export coverage for all of it (panels in HTML and Markdown, cell backgrounds
on both cell kinds, highlight color plus the legacy no-color case, and the
hostile-color rejection) is in `ProseMirrorRendererTests`.

### Fix: the full-width toggle did nothing on a brand-new page (2026-09-08)

`PUT /api/pages/{id}/layout` looked the page up through the default query
filter (`DeletedAt == null && Status != Draft`), so on an unpublished draft
it found nothing and returned 404. Every other draft-aware endpoint (
Publish, DeleteDraft, attachment upload) already opts out with
`IgnoreQueryFilters()`; SetLayout was the one that didn't.

The user-visible symptom was a toggle that flipped and immediately snapped
back: `PageEditor.toggleFullWidth` updates optimistically and rolls back on
error, and since layout is display metadata the rollback is silent. Full
width worked fine on any already-published page, which is why this survived
this long.

SetLayout now reads through `IgnoreQueryFilters()` with the soft-delete half
of the filter reapplied by hand, so a draft is reachable but a trashed page
still isn't. Publish never touched `FullWidth`, so the choice made while
composing now carries through to the published page unchanged. Covered by
`DraftPageTests.Full_width_can_be_toggled_on_a_draft_and_survives_publish`,
which asserts both halves (the 204 and the survival through publish).

### Design: page tree Reorder mode is now a batch edit (2026-08-19)

Feedback on Reorder mode: it committed each drag immediately and left
edit mode, which was fine for moving one page but tedious for reorganizing
several: reparenting a page is very often the first of several related
moves, and re-entering Reorder mode before each one added it up fast.

Discussed batch-editing (stay in Reorder mode, pile up changes, Save or
Cancel) against the existing immediate-commit-per-drag model before
building anything: batch editing means real complexity (a draft that
diverges from the server, conflict risk if the tree changes elsewhere
mid-session, partial-failure handling on save) that immediate-commit
doesn't have. Went with batch editing anyway, since it's what was asked
for.

Drags now apply to a local draft tree, `applyMove()` removes the dragged
page (with its subtree intact) and reinserts it under its new parent,
letting the existing `flatten()` recompute depths for the whole moved
subtree for free, and each drag also appends to an ordered
`pendingMoves` queue instead of calling the API. **Save** replays that
queue as sequential `PUT /api/pages/{id}/move` calls in the order the
moves were made; **Cancel** discards the draft without ever contacting
the server. Rows are no longer links while editing (a stray click could
otherwise navigate away and abandon an unsaved reorganization): dragging
is the only thing a row does in Reorder mode now. See
docs/architecture.md's "Page tree drag-and-drop" section for why replaying
moves in original order is safe without diffing the draft against the
original tree.

### Feature: show/hide toggle on every password field (2026-08-03)

Added a `PasswordInput` component (`components/PasswordInput.tsx`): a
password `<input>` with a flat eye/eye-off toggle button overlaid on the
right, same stroke-icon language as the rest of the app. Audited the whole
frontend for `type="password"` fields: there were exactly two, sign-in and
create-account, both now using it. No shared input component existed
before this, so `PasswordInput` is also where any future password field
(e.g. a change-password form) should start, rather than a bare
`<input type="password">`.

### Design: page tree Reorder toggle made icon-only (2026-08-03)

The "✏️ Reorder" button (see the previous entry) still read as heavier than
it needed to. Dropped the "Reorder" label, the pencil now stands alone,
and swapped the platform's own full-color pencil emoji for a flat
`currentColor` stroke icon (`PencilIcon` in `PageTree.tsx`), matching the
same icon language already used by the editor toolbar and the topbar bell
(`docs/CHANGELOG.md`'s notification-bell entry). The "✓ Done" label stays
as text once toggled on, a lone checkmark reads as ambiguous where "Done"
doesn't, so the button is icon-only at rest and label-plus-icon while
active, rather than jumping between two different visual languages.

### Fix: page tree dragging gated behind a "Reorder" mode (2026-08-03)

Reported after the drop-indicator redesign: on mobile it was too easy to
reorder a page by accident. Root cause was that every row was a drag
source all the time, and `touch-action: none` on the row (needed so a
touch-drag isn't raced by the browser's own scroll gesture) meant an
ordinary swipe-to-scroll starting on a page title got captured as a drag
instead: the exact ambiguity that makes "ends up moved when you didn't
mean to" so easy.

Added a compact `✏️ Reorder` / `✓ Done` toggle next to the tree's "📑 Pages"
heading (desktop sidebar and mobile alike, kept deliberately small per
request). Outside Reorder mode, rows are plain links with no dnd-kit hooks
and no `touch-action` override: scrolling through the tree behaves like
scrolling anything else, and there is no way to start a drag by accident
because nothing is listening for one. Reorder mode renders the draggable
version from the previous entry unchanged. The two tree instances (desktop
sidebar, mobile `SpaceHome` inline copy) hold this state independently,
which needs no special handling: they're never both visible at once.

### Design: page tree drag handle removed, real drop-indicator line added (2026-08-03)

Feedback on the initial drag-and-drop tree: the always-visible grip-icon
handle was "ugly" and ate row space, and Confluence's own tree shows a
horizontal line (with an indent preview) for where a drag would land,
instead of live-shuffling the rest of the list. Checked Atlassian's own
drag-and-drop design guidelines and a real Confluence sidebar recording
before redesigning
([atlassian.design/components/pragmatic-drag-and-drop/design-guidelines](https://atlassian.design/components/pragmatic-drag-and-drop/design-guidelines)).

Removed the separate handle entirely: the row (title) is now the drag
source itself, same as Confluence's own "implied draggable" sidebar rows;
dnd-kit's `distance: 4` activation constraint is what tells a tap-to-navigate
from a drag, so plain clicks still work. Replaced the live-reordering
sortable-list behavior with a static list plus a drop-indicator line (2px,
8px circular terminal bleeding 4px past its own left edge) rendered in the
gap where the row would land, whose horizontal offset also conveys the
target nesting depth: matching Atlassian's own drop-indicator spec.

This also fixed a real regression reported separately: mobile had gone back
to horizontal-scrolling on an iPhone. Root cause was the handle itself: a
fixed-width button nested in a new inner flex row per tree item, which was
enough to break the mobile-safe flex-shrink behavior this app had already
been bitten by once before (see the topbar/`.brand` fix earlier in this
changelog). Removing the wrapper and the handle brought the DOM back down
to one link per row, closer to the pre-drag-and-drop structure, which
resolved it; verified at both 375px and 320px viewports with long,
deeply-nested titles, with no horizontal overflow.

### Feature: drag-and-drop page tree reordering and reparenting (2026-08-03)

The page tree, desktop sidebar and the mobile `SpaceHome` inline copy alike,
now supports Confluence-style drag-and-drop: drag a row by its handle to
reorder it among siblings, or drag it horizontally over another row to
reparent it at a new nesting depth. This is now the only way to change a
page's place in the hierarchy; there's no separate move dialog.

Backend: `PUT /api/pages/{id}/move` changed from a raw `Position` int (which
the caller had to compute exactly, with no protection against colliding
with or leaving a gap relative to other siblings) to an `Index`, a slot
among the destination's current siblings, with the endpoint itself
resolving that sibling group and renumbering it densely. Added tests for
sibling reordering, reparenting, cross-space rejection, and the edit-rights
check (`Move_reorders_siblings_by_index`,
`Move_reparents_a_page_and_appends_to_the_new_siblings_by_default`,
`Move_rejects_a_different_space`,
`Move_requires_edit_rights_on_both_the_page_and_the_destination_parent`),
alongside the existing cycle-rejection test.

Frontend: added `@dnd-kit/core` + `@dnd-kit/sortable` (native touch support
via Pointer Events, so the same `PageTree`/`TreeItem` code drives both the
mouse-driven desktop tree and the touch-driven mobile one). A dragged row's
own descendants are excluded from the working list during the drag, so a
subtree can't be dropped inside itself client-side; the backend's existing
cycle check remains the authoritative guard. See
`docs/architecture.md`'s new "Page tree drag-and-drop" section for the
depth-projection algorithm.

### Fix: search couldn't find slash-joined words like "Hocuspocus/Yjs" or "OIDC/SSO" (2026-07-31)

Found while dogfooding the App Design space: searching "Hocuspocus" or "OIDC"
returned nothing even though both words were right there in page content
("Hocuspocus/Yjs sidecar", "OIDC/SSO: optional"). Root cause: Postgres's
`to_tsvector('english', ...)` parses `word/word` as a single compound
lexeme (`'hocuspocus/yjs'`) instead of splitting it, so only the exact
compound, never either half alone, was searchable. Fixed by normalizing
slashes to spaces in `SearchText` before it's indexed
(`PageEndpoints.BuildSearchText`), so `to_tsvector` tokenizes both halves
normally; backfilled the 8 existing pages whose indexed text contained a
slash. Added a regression test (`SearchTests.Slash_joined_words_are_indexed_as_separate_terms`)
that asserts directly on the stored `SearchText`, since the SQLite test
provider's plain-`LIKE` fallback can't reproduce a tsvector-specific bug.

### Design: replace the notification bell emoji with a flat stroke icon (2026-07-30)

The topbar bell used the platform's own 🔔 emoji: rendered in full color
(yellow) by the OS/browser, the one spot of color in an otherwise flat,
monochrome icon set (the editor toolbar's custom SVGs, `editor/icons.tsx`),
so it stood out against everything around it. Replaced with a small inline
SVG bell in the same visual language as those toolbar icons (24x24 viewBox,
1.8px stroke, `currentColor`, round caps): `var(--muted)` by default,
darkening on hover, same treatment as `.toolbar__btn`. Not added to
`editor/icons.tsx` itself since that module is explicitly scoped to the
editor toolbar's consumers; defined locally in `NotificationBell.tsx`
instead, being the only place it's used.

### Design: drop the space bar when viewing a page on mobile · the breadcrumb is the title now (2026-07-30)

`.space-actionbar` (space name / + New / ⋮) stayed visible even once you'd
navigated into an actual page, stacked right above that page's own
breadcrumb and Edit/⋮ row: a second, redundant header once you're that far
in. On mobile, viewing or editing a page now hides it entirely
(`.space-actionbar--hidden-on-page`, gated to `--bp-mobile`: desktop is
unaffected, that bar is already `display: none` there regardless); the
breadcrumb (already there) becomes the de facto title, immediately followed
by `PageView`'s Edit/+New/⋮ row.

`+ New` moves into that row, right after Edit: both `.btn--primary` now.
Mobile-only (`.page-actionbar__new-subpage`): desktop already has "+ New
page" permanently in the sidebar, so showing it a second time next to Edit
would just be noise there.

Considered folding Permissions/Webhooks/Trash into that page's `⋮` (as
Page/Space sections) so they'd stay reachable without the now-hidden space
bar. Went the other way: dropped them from the page menu entirely. They're
rare, admin-level actions; the page menu (Export, Watch, Save as template,
Delete) is opened far more often, and mixing an admin section into it adds
noise to the common case for the sake of an uncommon one. They're still one
tap further away (breadcrumb → space home → ⋮), which is a fair cost for
something used rarely, and matches real Confluence, which keeps space
administration in space-level screens rather than on every page's menu.

### Design: tree heading, mobile link color, and Edit promoted to a primary CTA (2026-07-30)

Polish pass on the space nav redesign above:

- The page tree (`PageTree.tsx`, shared by the desktop sidebar and the
  mobile inline copy on the space landing page) had no label at all: just
  a bare list, easy to lose track of what you're looking at. Added a
  `📑 Pages` heading above it in both places, since both render the same
  component.
- On mobile, tree links used the same near-black `.tree__link` color as
  desktop, but the two contexts mean different things: on mobile the tree
  only ever appears on the space landing page, so every link is purely "tap
  to go there": same as any other link, and should read as blue. On
  desktop the tree stays visible after you've navigated into a page, so
  black-by-default-with-blue-when-active means "here's where you are,"
  not "here's what's clickable": changing that would lose information,
  not add clarity. Scoped with `.space-home-tree .tree__link` rather than
  a prop, since the mobile/desktop distinction is already which wrapper
  renders it, not anything about the data.
- `PageView`'s Edit button was the only left-anchored control in a bar
  where everything else sits on the right, and its `.btn--ghost` styling
  made it recede next to actual secondary actions (Full width, ⋮) despite
  being the single most common thing to do with a page. Moved it into the
  right-anchored group as the first (leftmost) button there, and restyled
  it `.btn--primary` (the same blue as "+ New page"/"Post"): a real call
  to action instead of a ghost button no more prominent than "Full width."
  `.page-actionbar__secondary` gained `margin-left: auto` to anchor right
  correctly now that `PageView` has nothing left of it (a no-op for
  `PageEditor`, which still has its Toolbar in `__primary`).

### Design: real breadcrumb trail, one contextual create button, and a second sticky-bar collision fixed (2026-07-30)

Third round of feedback on the same-day space nav redesign: the single-level
"space name" link wasn't a breadcrumb at all once a page had its own
subpages, and a second sticky bar (`PageView`'s Edit/+Subpage row) was
fighting `.space-actionbar` for the same sticky slot while scrolling on
mobile, one visibly sliding over the other.

- **Real breadcrumb.** New `SpaceBreadcrumb.tsx`, rendered once in
  `SpacePage.tsx` right below `.space-actionbar` (non-sticky: the first
  thing in the normal scrolling content, on both mobile and desktop). Walks
  the already-loaded page tree (new `findTreePath` in `PageTree.tsx`) to
  build the full ancestor chain (`Space Name / Parent / Current Page`, only
  the current page non-clickable) rather than a single link back to the
  space. Falls back to a route label (`Permissions`/`Webhooks`/`Trash`/`New
  page`) on non-page routes; renders nothing on the space landing page
  itself, which already says where you are via its own heading.
- **One create button, not two.** Confluence's actual behavior: create from
  an open page makes a subpage of it; create from anywhere else makes a
  top-level page. `+ Subpage` (`PageView.tsx`) is gone: `.space-actionbar`'s
  `+ New` (mobile) and the sidebar's `+ New page` (desktop) now both compute
  the same contextual href in `SpacePage.tsx` (`?parent={currentPageId}` when
  viewing/editing an existing page, none otherwise), so both places behave
  identically instead of two different buttons doing two different things.
- **The second sticky-bar collision**: `.page-actionbar` (Edit/+Subpage/
  fullwidth-toggle/export, shared by `PageView.tsx` and `PageEditor.tsx`) was
  sticky at the same `top: 52px` as `.space-actionbar`: both mobile-only,
  both fighting for the same slot. Rather than hand-computing a second
  stacked offset (fragile: it'd need `.space-actionbar`'s exact rendered
  height kept in sync), `.page-actionbar` is simply `position: static` under
  `--bp-mobile` now, one sticky bar below the app topbar at this width,
  full stop. Unchanged on desktop, where `.space-actionbar` is hidden and
  there's nothing for it to collide with.
- Dropped the now-redundant ", {space name}" from the Permissions/Webhooks
  page headings and the "Space: {name}" footer on `PageView`, between the
  action bar and the new breadcrumb, the space name was appearing a third
  time on these pages.

### Design: follow-up pass on the space nav redesign · no icon, inline tree, breadcrumb, and a scroll-restoration fix (2026-07-30)

Three refinements to the same-day space-nav redesign below, from a second
round of real-device feedback:

- Dropped the 📄 emoji from the mobile action bar's space-name segment:
  just the title now, per feedback that page/space titles shouldn't carry
  a decorative icon (a user who wants one can put an emoji in the title
  itself).
- The mobile "open the page tree" toggle wasn't discoverable as a toggle at
  all: nothing about "📄 space name" read as "tap to see your pages."
  Replaced it with two things: `SpaceHome` (the space landing page) now
  renders the page tree **inline in the body** on mobile (new
  `PageTree` component, extracted from what was inline `TreeItem` code in
  `SpacePage.tsx`, shared with the desktop sidebar), so pages are visible
  the moment you land, no toggle to find. Every other space route now shows
  the space name in `.space-actionbar` as a plain breadcrumb link back to
  that landing page instead. Net effect: the mobile off-canvas drawer,
  `sidebarOpen` state, and backdrop are gone entirely: `.sidebar` is just
  `display: none` under `--bp-mobile` now, full stop.
- **The actual bug behind "the bar disappears until I scroll up when
  switching spaces"**: `<BrowserRouter>` (as opposed to the data-router
  APIs, `createBrowserRouter` + `<ScrollRestoration>`) never resets scroll
  position on navigation: the browser keeps whatever `scrollY` the
  previous page had. Landing on a shorter page already scrolled past its
  own height hides everything, sticky topbar included, since there's
  nothing left to stick to below the fold; you only see it again once you
  scroll back up into the new page's actual content. Not reproducible
  in-browser at this desk (this environment's Chromium happens to reset
  scroll on pushState on its own), but real Mobile Safari does not, and the
  symptom otherwise matches exactly. Fixed with a small `ScrollToTop`
  component (`useLocation` + `window.scrollTo(0, 0)` on every `pathname`
  change), mounted once at the router root in `main.tsx`: the standard
  fix for this well-known gap when not using a data router.

### Fix: shared topbar overflowed horizontally on real phones; redesigned space nav to drop the double-hamburger (2026-07-30)

Found via testing on a real iPhone (not the simulator) over LAN: Spaces,
Groups, Audit, API Tokens, Permissions, Webhooks, and Trash all failed to
fit at mobile widths: Sign out was clipped and the page scrolled
horizontally. Not reproducible in-browser at the same viewport width, which
pointed at a font-metrics difference rather than a layout bug per page.

- **Root cause**: `.topbar`'s `.brand` ("Tesria") is a flex item
  with no `min-width` override. Flex items default to `min-width: auto`:
  they refuse to shrink below their own text's intrinsic width no matter
  what `flex-shrink` says, a common flexbox trap. With no wrap fallback on
  `.topbar`, that one unshrinkable node was pushing the whole bar (shared by
  every page) past the viewport. The margin was only ~16-20px, thin enough
  that real iOS Safari's `-apple-system` Bold rendering (measurably wider
  per character than the sans-serif fallback available in-browser here)
  tips it over while desktop testing doesn't. Fixed: `.brand` now shrinks
  and ellipsizes instead of refusing to; `.topbar__right` and
  `.topbar__hamburger` got `flex-shrink: 0` so the controls themselves never
  get squeezed. Stress-tested down to a 240px viewport with zero overflow.
- Added the missing `-webkit-text-size-adjust: 100%` reset: iOS Safari
  auto-inflates text size in narrow columns it judges "readable"; standard
  practice, simply absent before.
- `.version__num`/`.version__comment` (Groups/Audit/API Tokens/Permissions/
  Webhooks/Trash list rows) gained `overflow-wrap: anywhere` defensively:
  `.version`'s `flex-wrap` only breaks between list items, not within one
  item's own unbroken text (a long group/page name, raw audit metadata).

### Design: single hamburger for space navigation, replacing a hidden double-menu (2026-07-30)

`SpacePage.tsx` had its own mobile hamburger (page tree + New page +
Permissions/Webhooks/Trash, all in one off-canvas drawer) stacked directly
under the app-level hamburger (`Layout.tsx`: Spaces/Groups/Audit/API
Tokens), two unrelated "☰" affordances on screen at once, and every space
action except Watch buried a tap deeper than necessary.

Replaced the space-level hamburger with an always-visible mobile action bar
(`.space-actionbar`, sticky under the topbar): a `📄 {space name}` button
that still opens the page-tree drawer (now holding *only* the tree), plus
an inline `+ New` button and a `⋮` overflow menu (reusing the existing
`OverflowMenu` component from `PageView.tsx`) for Permissions/Webhooks/
Trash. Only one hamburger exists anywhere in the app now. The desktop
sidebar (always visible, unaffected by any of this) keeps its own copies of
these links: new `.sidebar__quicklink` marker class hides just the
mobile-drawer duplicates so they're not offered in two places on a phone.

### Design: collapse editor toolbar's Heading/List/Alignment groups into dropdowns on mobile (2026-07-26)

The mobile toolbar (see the icon redesign entry below) still wrapped to 3
rows at 402pt: better than before, but still a lot of chrome above the
actual writing area. Grouped the three runs of related buttons users don't
need to see all at once, Heading (H1/H2/H3), list type (bullet/ordered/
task), and alignment (left/center/right), into a single dropdown trigger
each, collapsing the toolbar to ~2 rows on mobile.

- Added `src/web/src/editor/ToolbarDropdown.tsx`: a trigger button (current
  selection's icon + a small caret) that reveals a vertical menu of the
  full option set on click, dismissed via the existing `useDismissable`
  hook (same outside-click/Escape pattern as `OverflowMenu`).
- Desktop keeps the flat button rows: both forms are always mounted
  (`.toolbar__flat` / `.toolbar-dropdown`), and CSS picks one via
  `display: none` at `--bp-mobile`, following this codebase's established
  CSS-only responsive convention (no JS viewport check) rather than
  introducing one.
- The dropdown menu flips from left- to right-anchored when the trigger
  sits too far right for a left-aligned menu to fit: found by testing:
  the Heading/Alignment triggers land near the toolbar's right edge on
  mobile, and a naive `left: 0` pushed the menu off-screen.

### Fix: stale `index.html` served indefinitely due to missing Cache-Control (2026-07-26)

Flagged but not fixed in an earlier pass (see the 320px sweep entry below):
turned out to be actively biting: the iOS Simulator kept rendering the
*pre-icon-redesign* toolbar (plain text labels, "iOS blue") minutes after
the fix had shipped and the container had restarted, because the browser's
heuristic cache never re-checked `index.html`. Root-caused and fixed for
real this time: `Program.cs`'s `UseStaticFiles`/`MapFallbackToFile` now set
`Cache-Control: no-cache` on `index.html` specifically and
`public, max-age=31536000, immutable` on everything else (the
content-hashed `/assets/*` bundles, safe to cache forever since a content
change gives them a new filename). Verified via `curl -I` against the
rebuilt container.

### Fix: three real overflow/positioning bugs found in a full-route mobile audit (2026-07-26)

The user reported the login page and space-home view still looked broken
on mobile despite the earlier "audit all pages at 320px" pass, turned out
to be two separate things: (1) the Cache-Control bug above, serving a
stale pre-fix build, and (2) three genuine bugs a route-by-route sweep with
a scripted `scrollWidth > clientWidth` check (not eyeballing) turned up,
none of which the earlier pass had covered:

- **Notification bell dropdown opened mostly off the left edge of the
  screen.** `.notif__dropdown`'s `right: 0` was anchored to `.notif`, a
  wrapper sized to just the ~28px bell button, not to the actual
  right-hand button cluster (bell + username + sign-out) it visually sits
  in. Once the dropdown (288px wide) tried to right-align against a box
  that small, it necessarily spilled left past the viewport. Fixed by
  moving `position: relative` from `.notif` to `.topbar__right` (the whole
  cluster), so `right: 0` resolves against the cluster's actual right edge
  instead.
- **A brand-new, never-touched page showed the floating selection toolbar
  even with no text selected**, overlapping the title field and forcing
  ~14px of horizontal overflow. `SelectionBubbleMenu`'s `shouldShow` is
  only re-evaluated on `selectionUpdate`/`focus`/`blur`; an empty document
  that's never been focused never fires any of those, so the menu's
  floating-ui "open" state was stuck at its default (visible) instead of
  ever being told to hide. Fixed by adding an explicit `editor.isFocused`
  guard to `shouldShow`: semantically correct regardless of the root
  cause (a selection menu shouldn't show without focus) and immediately
  false right after mount, before any focus event.
- **The toolbar's link/comment popovers and the selection bubble menu
  itself could spill past either viewport edge on narrow screens.** Their
  anchor button lives inside a flex-wrap toolbar, so it can land anywhere
  horizontally, unlike a normal trailing "overflow menu" that's always at
  a row's end. Added `src/web/src/hooks/useEdgeAlign.ts`: measures the
  popover's actual rendered position via `useLayoutEffect` once it opens
  and applies a corrective inline `left` offset, clamping both edges to a
  16px margin. Also gave `.toolbar--bubble` its own `max-width` so it
  genuinely wraps on narrow screens: its floating-ui-managed wrapper sizes
  itself to the menu's *unwrapped* natural width and never shrinks, which
  otherwise defeats `flex-wrap` (the flex container is never actually
  narrower than its content). One residual, accepted gap: the bubble menu
  itself doesn't self-correct its own floating-ui-assigned position after
  the width cap makes it narrower (only the popovers nested inside it do):
  an early attempt to add that via a transform + `useLayoutEffect` created
  a feedback loop with floating-ui's own `autoUpdate` repositioning and
  hard-crashed the whole app (blank root, no console error) the moment any
  text was ever selected. Reverted; the width cap alone shrinks the
  overflow from ~55px to a single-digit-pixels edge case, worth trading
  for stability. All four `document.documentElement.clientWidth`-based
  measurements (not `window.innerWidth`): an early version used
  `innerWidth` and it read back an inflated value once something on the
  page was *already* overflowing, undercorrecting the very shift meant to
  fix that overflow.
- Also defensively hardened `.attachment` (shared by `AttachmentsPanel` and
  `GroupsPage`'s member list: no attachments/members long enough to
  reproduce it existed to test against, but the CSS gap was real): added
  `flex-wrap` and `overflow-wrap: anywhere` so a long filename or email
  can't force the row wider than the viewport.
- Verified via a full route-by-route sweep at 320px (login, register,
  spaces list, space home, new-page editor, trash, permissions, webhooks,
  page view/edit, all its tabs, search, audit, groups, api-tokens, plus the
  notification dropdown and every toolbar dropdown) with the scripted
  overflow check: all clean.

### Fix: `.row-between` header rows overflowed the viewport at mobile widths (2026-07-26)

Missed by the mobile/responsive overhaul below: found on the iOS Simulator
(iPhone 17 Pro, 402pt) navigating to a space with no page selected (the
"Select a page from the tree, or create a new one." empty state, e.g.
`SpaceHome.tsx`). `.row-between` (title + action button, shared by the space
header, the spaces-list header, and the history-preview header) neither
wraps nor lets its children shrink, so a long-enough title/button pair
forces the row, and the whole `.page-wrap`/topbar above it, wider than the
viewport, producing horizontal scroll and edge-clipped text. Fixed with a
`--bp-mobile` override adding `flex-wrap: wrap`, stacking the button below
the title when they don't both fit. Verified no `document.documentElement`
horizontal overflow at 320/375/402px.

### Design: touch-friendly icon toolbar, replacing text-label buttons (2026-07-26)

The editor toolbar (both the sticky top toolbar and the floating selection
bubble menu) used text-label buttons (`Highlight`, `Table`, `Link`, arrow
glyphs for alignment) with no explicit color: they inherited the browser's
default anchor-like blue, which read as "iOS blue links" rather than
deliberate UI, and had small, cramped hit targets (padding-only sizing, no
minimum touch target).

- Added `src/web/src/editor/icons.tsx`: a small set of custom 24x24 SVG
  icons (stroke-based, `currentColor`, consistent 1.8px stroke/round caps)
  for inline code, highlight (an actual highlighter-pen shape, not the
  word "Highlight"), bullet/ordered/task list, blockquote, code block,
  table, image, text alignment (left/center/right), link, and comment.
  Bold/Italic/Underline/Strikethrough deliberately keep their literal
  B/I/U/S glyph treatment: that's the actual standard for those four
  (Google Docs, Word, Notion), not a placeholder.
- `Toolbar.tsx` and `SelectionBubbleMenu.tsx` now render these icons
  instead of text/glyph labels, so the sticky toolbar and the floating
  bubble menu present the same visual language.
- `.toolbar__btn` in `index.css` now has an explicit `min-width`/
  `min-height: 36px` (was padding-driven, effectively ~28px), and a global
  `button { font: inherit; color: inherit; }` reset: the real root cause
  of the "blue" look was that no button anywhere set its own `color`, so
  unstyled buttons fell back to the browser/OS default link-blue tint.
  Buttons now default to `var(--muted)` and use the existing
  `.toolbar__btn.is-active` blue tint only when a mark/block is actually
  active, matching how the rest of the app already uses that color.
- Verified: computed button size is 36x36px for every toolbar and bubble
  menu button (including the image-upload `<label>`) at both desktop and
  mobile widths; confirmed on the iOS Simulator (iPhone 17 Pro) that the
  toolbar wraps to multiple rows at 402pt width and a real touch tap
  reliably lands on the intended button without mis-hitting its neighbors.

### Fix: two real mobile layout bugs the 320px width class of devices hit (2026-07-26)

Found via a full page-by-page sweep at 320px (iPhone SE-class width: the
earlier responsive pass had mostly been checked at 375px+, which happened to
mask both of these):

- **Login/Register pages rendered edge-to-edge with no margin, clipped on
  the right.** `.center` used `display: grid; place-items: center` to
  center the auth card. A CSS Grid item's percentage sizing (the card's
  `max-width: 100%`, meant to let it shrink on narrow screens) resolves
  against its own **auto-sized grid track**, which itself sizes to the
  item's intrinsic width. That's a circular reference: the track becomes as
  wide as the 340px card wants, so `max-width: 100%` of a 340px track is
  still 340px, never actually constraining anything. It happened to look
  fine at 375px+ purely because 340px + padding was still narrower than the
  viewport there. Fixed by switching to `display: flex`: flex containers
  resolve child percentages against the actual content box, not an
  auto-sized track, so the same `max-width: 100%` now works as intended.
- **Groups/API Tokens/Webhooks/Spaces-creation forms were unreadable**:
  `.form-inline`'s 4-column grid (`120px 1fr 1fr auto`) has no room at
  narrow widths; fields and the submit button overlapped/clipped. Fixed
  with a `--bp-mobile` override stacking it to a single column.

Also worth knowing about but **not** fixed in this pass, found in passing:
`index.html` has no `Cache-Control` header, so browsers apply heuristic
caching to it, after a deploy, a client can keep using a stale
`index.html` (with old content-hashed asset URLs) until that heuristic
expires. Worth an explicit `Cache-Control: no-cache` on `index.html`
specifically (content-hashed assets under `/assets/` can stay
long-lived/immutable) in a future pass.

### Fix: `/ca.crt` over plain HTTP redirected instead of serving the file, for {$DOMAIN} specifically (2026-07-26)

Found via iOS Simulator testing (installing the CA into the simulator's trust
store needs to fetch it first): `http://localhost/ca.crt` 308-redirected to
HTTPS instead of serving the cert, even though the Caddyfile clearly showed
the right route and a full container recreation didn't help. Root cause:
Caddy's automatic HTTPS inserts its own HTTP→HTTPS redirect for whatever
hostname it manages certs for ({$DOMAIN}), and that auto-inserted route
wins over routes in our own `:80` block for that specific hostname,
regardless of the more specific `/ca.crt` exception there. Confirmed by
testing the same request with a different `Host` header, which reached our
route fine: only requests for {$DOMAIN} itself were intercepted first.
Fixed with `auto_https disable_redirects` in the global options block: we
already redirect everything else ourselves in `:80`, so Caddy doesn't need
to add its own (cert automation for {$DOMAIN} is unaffected, only the
redirect route). Regression-tested: plain HTTP still redirects to HTTPS for
every other path, and LAN/mDNS access is unaffected.

### Mobile/responsive overhaul + per-table width (2026-07-25)

The app had zero responsive CSS before this: a fixed 260px sidebar, a
fixed-width page card, and hover-only editor chrome that doesn't exist on
touch. Alongside it, tables gained real Confluence-style width control:
previously only column-border dragging (stock prosemirror-tables) existed,
with no way to make a table itself wider than the page, or size it to a
specific width independent of the page's own full-width setting.

Added:
- **Responsive breakpoints** (`--bp-mobile: 640px`, `--bp-tablet: 1024px`,
  documented in `index.css`'s `:root`). Below `--bp-mobile`: the topbar's nav
  links collapse behind a hamburger dropdown; the space sidebar becomes an
  off-canvas drawer (hamburger toggle + backdrop, dismiss-on-outside-click/
  Escape via a new shared `useDismissable` hook, also now used by
  `OverflowMenu`); the page/editor "paper" card tightens its padding and the
  full-width toggle button hides (full vs. normal width is a distinction
  without a difference once the reading column already fills the viewport).
- **Per-table width**, matching real Confluence's model (researched against
  the live product): a `width` (px, from a new edge-drag handle) and
  `layout: 'default' | 'full-width'` (from a new toggle button) attribute on
  the `table` node, independent of the page's own full-width setting and of
  column-border dragging (unaffected). A full-width table bleeds out of the
  page's own padding via `TableWidthControls.tsx`, a floating overlay
  alongside the existing `TableControls.tsx` (same convention, not a
  NodeView: see architecture.md). Export renderer parity in
  `ProseMirrorRenderer.cs` (HTML gets the inline style; Markdown degrades
  silently, same as other display-only attrs).
- **Touch-adaptive editor chrome**: table controls (row/column insert-delete,
  width/full-width) now reveal via tap-to-place-cursor instead of
  hover-proximity on touch/no-hover input (`useHoveredTable.ts`, shared by
  both components), detected via `matchMedia('(hover: none) and
  (pointer: coarse)')` rather than viewport width: a touchscreen laptop at
  desktop width has the same no-hover problem a phone does. (Selection-driven
  UI (the slash command, selection bubble menu, and image hover menu despite
  its name) already worked on touch with no changes needed.)
- Default page reading width bumped 860px → 900px (round number, evokes a
  sheet of paper, requested alongside this work).
- Fixed-width UI sweep: notification dropdown, overflow-menu dropdown, and
  the link-editor URL field now clamp to the viewport instead of overflowing
  it; the page tabs row (Comments/Attachments/History/Restrictions) scrolls
  within itself instead of pushing the whole page wider (found via testing:
  a real ~40px page-level overflow existed on every page with this tab row,
  independent of anything table-related).

Fixed (found via testing, not pre-existing per se, introduced and caught in
the same pass):
- prosemirror-tables' `TableView` (active whenever a table is `resizable`,
  in both edit and read-only rendering) only applies a node's rendered
  `style`/`data-*` attributes once, in its constructor, its own `update()`
  (used for every subsequent attribute change on an already-mounted table,
  e.g. toggling full-width live) recalculates the colgroup but never
  re-touches them. A plain `renderHTML`-based approach alone isn't enough to
  keep the DOM in sync live; `TableWidthControls.tsx` also applies the same
  effect directly to the DOM right after the transaction commits. (Fresh
  mounts (a page load, an export) are unaffected and already correct via
  the schema alone.)
- The full-width breakout math reads a `--page-pad` custom property shared
  with `.paper`'s own padding: they'd briefly drifted apart during this
  work (mobile tightened `.paper`'s padding via a separate hardcoded value
  instead of the same variable), causing a small but real viewport overflow.
  Fixed by having `.paper` derive its padding from `--page-pad` too, and
  overriding the variable itself at the mobile breakpoint rather than
  hardcoding parallel values: keeps them impossible to drift apart again.

Known gaps, not addressed in this pass:
- The touch-reveal path (tap-to-show table controls) is verified correct by
  direct testing of its resolution logic; a live end-to-end confirmation on
  a real touch device wasn't completed (the iOS Simulator was unavailable,
  crash-looping, for the rest of this session).
- Several admin/settings pages (Groups' create-group form, likely Webhooks/
  API Tokens/Permissions too) use fixed-width multi-column form layouts not
  covered by this pass: found in passing, out of scope here.
- The topbar's nav links wrap awkwardly at exactly ~768px (between the two
  breakpoints): cosmetic, no overflow, not fixed in this pass.

### LAN/mobile HTTPS access + local CA trust scripts (2026-07-25)

Added, while setting up the Mac dev environment and looking ahead to open-
sourcing this project: a deployment with no real domain (the common case
for individuals/small teams evaluating it) previously only worked over
`https://localhost`; anything else (LAN IP, another local hostname) failed
the TLS handshake outright, since Caddy only had a certificate for the one
configured `DOMAIN`.

- **`deploy/Caddyfile`**: added a catch-all `:443` block using Caddy's
  On-Demand TLS with its internal CA, so any address the server answers on
  (LAN IP, `.local` hostname, `127.0.0.1`, ...) gets a certificate minted on
  first request: no need to enumerate hostnames, and it keeps working
  through DHCP IP changes. The original `{$DOMAIN}` block is untouched, so
  real-domain Let's Encrypt deployments are unaffected.
- **`/ca.crt` route** (both plain HTTP and HTTPS): serves the internal CA's
  public root certificate, so a device that hasn't trusted anything yet can
  still fetch it.
- **`deploy/scripts/trust-ca.sh`** (macOS/Linux) and **`trust-ca.ps1`**
  (Windows): one-time, per-device scripts that fetch `/ca.crt` and install it
  into the OS trust store, removing the self-signed warning everywhere that
  device reaches this server. Firefox and mobile need a short manual step
  instead (separate certificate stores): documented, not scripted.
- **`docs/tls-and-lan-access.md`**: user-facing guide covering both the
  real-domain (Let's Encrypt, including a DNS-01/no-public-exposure option)
  and no-domain/LAN paths.

### Editor UX overhaul (2026-07-25)

A ground-up pass on the block editor, going beyond the original PLAN.md
roadmap (which this repeats, this is not one of the numbered phases above).
Full details and file-level references are in the session's saved plan;
summarized here for the changelog record.

Added:
- **Draft/publish page lifecycle.** A brand-new page is now backed by a real
  (but invisible) `Page` row from the moment the editor opens: reusing the
  `PageStatus.Draft` enum value that existed unused since Phase 2, so no
  migration was needed. This lets image uploads work on an unsaved page
  (the attachment API needs a real page id); the draft becomes visible and
  fires its "page created" side effects (audit/notification/webhook) only
  when the user clicks "Create page" (`POST /pages/draft`,
  `POST /pages/{id}/publish`, `DELETE /pages/{id}/draft`).
- **Syntax-highlighted code blocks** (`@tiptap/extension-code-block-lowlight`)
  with a language picker, copy button, and optional per-block line numbers.
- **Tables** (resizable) and **task lists**, with hover-triggered
  Confluence-style row/column insert (+) and delete (×) controls on the
  table itself (researched against real Confluence's UX) rather than a
  persistent toolbar strip.
- **Images**: paste/drag-drop/toolbar upload (using the draft page id when
  the page is new), plus a dedicated hover menu (border, drop-shadow,
  comment), not the text-formatting bubble, which made no sense for images.
- **Underline, real link editing UI, highlight, text-align.**
- **Inline/anchored commenting**: a `comment` mark highlights the selected
  text and links it to a real `Comment` row (the backend's
  `AnchorJson`/`isInline` support existed since the comments feature landed,
  but the frontend never created anchored comments until now). Images get a
  comment without an in-document highlight (they can't carry text marks).
- **A floating selection bubble menu** and a Notion-style **"/" slash-command
  menu** for inserting blocks, built on `@tiptap/suggestion`.
- **A "paper" redesign**: title flows into the body inside one card instead
  of a boxed title above a bordered editor. The formatting toolbar and the
  page's primary actions (Edit/+Subpage, plus a new "⋮" overflow menu for
  exports/watch/save-as-template/delete) now live in one shared, full-width,
  sticky action bar below the app's topbar: consistent between view and
  edit mode, instead of a toolbar wedged between the title and the document.
- **Per-page full-width toggle** (`Page.FullWidth`, `PUT /pages/{id}/layout`),
  matching real Confluence's normal/full-width reading-width preference
  (researched: it's a per-page setting, not a session/URL setting).
- Every new node/mark type got export-renderer parity in the same phase it
  was added (`ProseMirrorRenderer.cs`), so exports never silently degrade.

Fixed:
- The code block's syntax highlighting was rendering as flat, uncolored
  text: lowlight was already producing `hljs-*` token spans, there was
  just no CSS coloring them.
- The table column-resize cursor never appeared: prosemirror-tables
  applies a `resize-cursor` class to the editor root while a column border
  is draggable, but nothing consumed it in CSS.
- A real bug affecting every popover rendered inside the editor (the link
  popovers, the link-edit form, the new comment popovers): submitting one
  also submitted the page's own outer save `<form>` and silently navigated
  away, because React bubbles synthetic events through the component tree
  regardless of BubbleMenu's DOM portal. Fixed with `stopPropagation()` on
  every affected popover's submit handler.

### Phase 5: Advanced (2026-07-24)

Added:
- **Real-time collaborative editing.** A Node + Hocuspocus/Yjs sidecar
  (`collab/`) lets several people edit a page simultaneously, with live remote
  carets showing who is where. The editor engine is JS-only, so this is isolated
  in a small sidecar rather than reshaping the .NET stack (PLAN §1).
  - **Authorization:** the sidecar cannot evaluate our permission model, so the
    API is the gatekeeper: it issues a short-lived HMAC-signed token only to
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
  never go to the person who made the change, and, reusing the same fix
  already applied to the audit log, are hidden if the recipient's access to
  the target is later revoked. SPA: a watch toggle on pages and spaces, and a
  bell in the top bar with unread count, a dropdown, and mark-as-read.
- **API tokens and webhooks: the public REST API.** Personal access tokens
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
- **OIDC / SSO: the last Phase 5 item.** Sign in via any standards-compliant
  OpenID Connect provider (Keycloak, Authentik, Google, ...) alongside local
  accounts, configured generically via `Oidc:Authority`/`ClientId`/
  `ClientSecret` (PLAN §1: "architected for OIDC/SSO later", pluggable). The
  `Smart` policy scheme now spans three auth methods (cookie / API token /
  OIDC-issued cookie), all converging on the same internal claim shape so every
  existing endpoint keeps working unchanged.
  - **Account resolution** (`IOidcUserProvisioner`, independently unit-tested):
    a returning subject signs in; a verified-email match links to an existing
    local account; an **unverified-email match is refused**: auto-linking it
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
    same account (idempotent), and, critically, an attacker registering at
    the IdP with a victim's email but *unverified* was cleanly refused with no
    session established. That run caught a real bug: `ctx.Fail()` inside
    `OnTicketReceived` didn't reliably stop sign-in from completing with the
    provider's raw, unmapped claims; fixed by writing the rejection response
    and calling `HandleResponse()` explicitly, the same pattern already used
    for provider-side failures. Migration: OidcSubjectIndex.

### Phase 4: Fast-follow (2026-07-23)

Added:
- Labels/tags: instance-wide labels (names normalized to lower case) applied to
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
    attachments, labels, export, and search: restricted content is hidden
    (404) rather than merely refused, so it is not discoverable.

- Management UI for the above: a Groups page (create/delete groups, manage
  membership from the user directory), a per-space Permissions page reached from
  the space sidebar, and a Restrictions tab on each page. Both grant flows share
  one principal picker, and each explains its current state: an open space says
  so, and warns that the first grant makes it private.

### Phase 3: Search + backup system (2026-07-23)

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

### Phase 2: Core content (2026-07-22)

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
  version model: every save appends a `PageVersion`, with version-history
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

### Phase 1: Foundation (2026-07-22)

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
