# Tesria

**The self-hosted wiki built for people and AI agents.**

Write, keep and share your team's knowledge on your own server. Connect any
AI assistant through MCP, and see every change it makes.

**Latest release:** [0.8.1](https://github.com/Tesria/Tesria/releases/latest).
**Website and docs:** [tesria.com](https://tesria.com). Free and open source
under the [Apache License 2.0](./LICENSE).

## What Tesria does

### Write: writing together, fast

- Edit the same page at the same time, with everyone's cursor and changes
  live.
- A block editor with a slash menu for tables, panels, charts, Mermaid
  diagrams, math, task lists, layouts, page properties, embeds and live
  lists of pages and tasks, and Markdown shortcuts as you type.
- Drafts, version history with restore, templates, labels, threaded and
  inline comments, mentions, watches and notifications, and search across
  every space.
- Two looks, **Minimal** and **Glass**, each in light and dark with your
  choice of accent color. Reading, editing and the page tree all work on a
  phone.

### Keep: yours, safe and recoverable

- Ready-made Docker images for Intel, AMD and ARM. One command installs it,
  with no settings file to write: Tesria makes its own passwords and keys,
  and asks you to save the backup key.
- Backups that are tested, restore **and undo** from the admin page, and
  point-in-time recovery: roll the whole wiki back to the minute before
  something went wrong.
- Encrypted offsite copies to S3-compatible cloud storage, a network drive
  or a removable drive.
- No telemetry: Tesria sends nothing about your instance or its people
  anywhere. Once installed it runs without the internet: sign-in, password
  resets and backups to a network drive or removable drive all work offline.
- A tamper-evident audit log, security alerts, two-factor sign-in, rate
  limits, and an app that runs as a database role unable to alter its own
  audit log.
- Reach it privately from anywhere through the optional Tailscale
  integration, with no port opened to the internet.

### Share: the right people, the right pages

- Roles, groups, space permissions and page restrictions; open sign-up,
  invitations, or single sign-on through OpenID Connect (in beta).
- Public spaces anyone can read, without an account.
- Export a page as Markdown, HTML or PDF, and a whole space as a static
  website or as a wiki pack that imports into another Tesria. The docs on
  tesria.com are a Tesria export.
- Your own name, logo and colors on your instance.
- Email through any SMTP server, with step-by-step guides for Gmail,
  Outlook and Microsoft 365, Apple iCloud Mail, Zoho, Fastmail and Proton
  Mail, and **Sign in with Microsoft** or **Sign in with Google** in place
  of an app password.

### Automate: agents that work with you

- **A built-in MCP server:** Claude, Cursor or any MCP client can read,
  search, create and update pages, limited to what the token's owner may
  see. A read-only token cannot change anything.
- **Agents never silently overwrite you:** every change an agent makes is a
  new version in the page's history, which anyone can restore, and
  administrators see what each token did. When someone edits the page, the
  change is highlighted, with **Accept All** and **Reject All**.
- A REST API with an OpenAPI reference, and webhooks when pages change.

Approving an agent's changes before they go live (review mode) is planned.
What is planned is on the [public roadmap](./docs/roadmap-public.md); what
changed in each version is in [`docs/CHANGELOG.md`](./docs/CHANGELOG.md).
The working plans are [`docs/roadmap.md`](./docs/roadmap.md) and
[`docs/dev-plan.md`](./docs/dev-plan.md), and [`PLAN.md`](./PLAN.md) is the
original design the project started from.

## Documentation

- **Using and running Tesria:** the docs at
  [tesria.com/docs](https://tesria.com/docs). Each release also carries
  them as downloads: `docs-pack.zip` to import into your own Tesria (Spaces,
  then **Import a Pack**), and `docs-site.zip` to read offline.
- **How it is built:** [`docs/architecture.md`](./docs/architecture.md),
  and the threat model and known gaps in [`docs/security.md`](./docs/security.md).
- **Backups and disaster recovery:** [`docs/backup-recovery.md`](./docs/backup-recovery.md).

## Tech stack

- **Backend:** ASP.NET Core (C#), .NET 10 (LTS); EF Core 10
- **Database:** PostgreSQL 18 (Npgsql); page content stored as ProseMirror JSON
- **Frontend:** React 19 + TypeScript + Vite; TipTap v3 block editor
- **Auth:** local accounts (Argon2id), API tokens, and optional OIDC/SSO (beta),
  cookie sessions, all converging on the same permission model
- **Collaboration:** Node + Hocuspocus/Yjs sidecar for simultaneous editing
- **PDF export:** Node + Playwright sidecar rendering the print-ready HTML export
- **Deploy:** Docker Compose, Caddy reverse proxy with automatic HTTPS
- **Backups:** pgBackRest continuous WAL archiving + point-in-time recovery,
  scheduled `pg_dump` + `uploads` archives, and in-app version history / trash

## Quick start (Docker)

From the ready-made images (Docker Hub `brianintheloop/tesria-*`, or
`ghcr.io/tesria/tesria-*`), with only the files needed to run them:

```bash
curl -LO https://github.com/Tesria/Tesria/releases/latest/download/tesria-deploy.zip
unzip tesria-deploy.zip -d tesria && cd tesria
docker compose up -d
```

Or from source, in a clone of this repository:

```bash
docker compose up -d --build
```

There is no `.env` to write. On the first start the one-shot `init` service
generates every secret (the two database passwords, the backup encryption
key, and the shared secrets for live editing and PDF export), keeps each in
its own Docker volume, and mounts it only into the services that need it:
the app never sees the owner password or the backup key. Later starts reuse
what it stored; it never replaces a stored secret with a new one.

**Save the backup key.** Tesria writes it to `backup-key.txt` in the folder
you started it from, and the setup wizard asks you to save it somewhere that
is not this machine. Without it no backup can be restored, and a key that
lives only on the server is lost with it. `docker compose run --rm init
show-backup-key` prints it again at any time.

A `.env` is for choosing things yourself; every line of `.env.example` is
optional:

| Variable | |
|---|---|
| `DOMAIN` | A real hostname (`wiki.example.com`) gets a Let's Encrypt certificate. Default `localhost`. |
| `ACME_EMAIL` | Optional contact address for Let's Encrypt. |
| `POSTGRES_PASSWORD`, `APP_DB_PASSWORD`, `BACKUP_ENCRYPTION_KEY`, `COLLAB_SHARED_SECRET`, `PDF_SHARED_SECRET` | Generated unless set. A value set here always wins over the stored one, which is how an install from before 0.8.0 keeps its own, and how a new machine restores backups made with an old key. |

`init` refuses to start a **new** install on one of the `change-me-...`
values that `.env.example` shipped with before 0.8.0, because those are
public. An existing install that still has one starts, logs a warning at
every start and raises a critical security alert; the docs page *Security
hardening* explains how to change it.

Everything else, schema included, sets itself up: the `migrate` service
updates the database before the app starts (and stays up to do the same for
a restore), and the pgBackRest
sidecar creates its stanza on first boot. Then open `https://<domain>/` and the setup wizard takes it from
there: it creates the owner account, names the instance, and walks you
through who can join, what each role may do, and how much backup history to
keep. A fresh database has no users, so the first account to be created owns
the instance.

> **Never run `docker compose down -v`.** The `-v` deletes the volumes: the
> wiki, its backups, and the generated secrets that open them.

> **One Tesria per Compose project.** Every Tesria folder is the project
> `tesria` unless its `.env` says otherwise, so a second copy in another
> folder would be the first Tesria again. Since 0.8.2 `init` refuses to
> start a Tesria from a folder other than the one it was installed from.
> For a second, separate Tesria on the same computer, set
> `COMPOSE_PROJECT_NAME`, `TESRIA_SUBNET`, `TESRIA_HTTP_PORT` and
> `TESRIA_HTTPS_PORT` in its folder's `.env` before its first start (see
> `.env.example`).

> **Bring the whole stack up together** (`docker compose up -d`), not
> `docker compose up -d db` on its own. On a fresh volume the database
> crash-loops every ~10s if started alone, because WAL archiving fails
> until the `pgbackrest` sidecar has created the stanza. If you do need the
> database by itself, start `db pgbackrest` together.

- **A real domain:** set `DOMAIN=wiki.example.com` in a `.env`, and Caddy
  gets a Let's Encrypt certificate for it automatically. Visit
  `https://wiki.example.com`.
- **No domain:** leave `DOMAIN` unset (it defaults to `localhost`) and visit
  `https://localhost`. Tesria makes its own certificate, so a browser warns
  until that device trusts it.
- **Other devices on your network,** phones included, reach Tesria by the
  server's IP address or name with nothing to set up. To trust its
  certificate on a device, open `http://<server>/trust` there for a guided
  setup, or run `deploy/scripts/trust-ca.sh` (macOS and Linux) or
  `trust-ca.ps1` (Windows). Checking the fingerprint is optional:
  `--fingerprint` with the value from `docker compose logs app | grep -i
  fingerprint` on the server. See
  [`docs/tls-and-lan-access.md`](./docs/tls-and-lan-access.md) for details,
  and for using a real domain without exposing Tesria to the internet.

Check health directly: `curl -k https://localhost/api/health` (it gives the
version only to a signed-in caller).

## Development

Tesria is developed the way it runs, with Docker: change the code, rebuild
the part you changed, and look at it in the browser.

```bash
docker compose up -d --build app   # after changing the API or the web app
docker compose up -d --build collab   # or pdf, after changing those services
```

See [`CONTRIBUTING.md`](./CONTRIBUTING.md) for the conventions and how a
change is proposed, and the **Developers** section of the docs for the
architecture.

## Tests

```bash
dotnet test tests/Api.Tests                       # the server
cd src/web && npm ci && npm run build && npm run lint && npm test   # the web app
```

The API test suite boots the app in-process against SQLite in-memory, so it runs
without Docker or a live PostgreSQL. GitHub Actions runs both on every push and
pull request, and runs the API tests against PostgreSQL too.

## Backups

Two sidecars back the instance up every `BACKUP_INTERVAL_HOURS`: `backup`
(a `pg_dump` plus an archive of uploads, on the `backups` volume) and
`pgbackrest` (physical backups and continuous WAL archiving for point-in-time
recovery). **Administration → Backups** shows both, runs a backup or a restore
test on demand, and sets the retention policy (keep the newest *N* and the last
*D* days, or keep everything). `BACKUP_RETENTION_DAYS` only seeds that policy
on the first start after upgrading. The same page restores a backup (with an
undo), and can send copies offsite to cloud storage, a network drive or a
removable drive.

```bash
docker compose exec backup /scripts/backup.sh          # backup now
docker compose exec backup /scripts/verify-backup.sh   # restore-test newest
docker compose exec backup /scripts/restore.sh         # restore newest (destructive)
```

Full details and disaster-recovery runbook: [`docs/backup-recovery.md`](./docs/backup-recovery.md).

## Roles

Three roles ship: **User**, **Administrator** and **Owner**. The owner is the
one account that owns the instance; only it changes roles or hands the
instance on. **Administration → Roles** is the matrix of what each role may
do, from creating spaces to changing the backup retention policy.
Administrators may shape user roles; only the owner may change what
administrators can do.

You can add your own roles alongside the three, in either the user or the
administrator tier: **New Role** copies an existing one and you edit the
copy in the matrix. A role is a set of rights, not a rank, so moving
someone between roles of the same tier never promotes them.

Administrators and the owner can also **delete a space**, from its settings
page. That destroys every page in it and cannot be undone from inside Tesria,
so it asks for the space key typed back and your password in the same step.
Archiving is the reversible alternative, and a space's own administrator can
do that without holding the right.

## Repository layout

```
src/Api/        ASP.NET Core API (Domain / Infrastructure / Features slices)
src/web/        React + Vite + TypeScript SPA (TipTap editor)
collab/         Real-time collaboration sidecar (Node + Hocuspocus/Yjs)
pdf/            PDF rendering sidecar (Node + Playwright/Chromium)
tests/          API integration tests (in-process, SQLite in-memory)
deploy/         Dockerfile, Caddyfile, the init service, backup scripts, pgBackRest
docs/           architecture, security, backup-recovery runbook, CHANGELOG, plans
scripts/        the docs' publisher (scripts/docs), demo data, screenshots,
                the dependency manifest, the audit gate
docker-compose.yml   full stack
```

## Project

Tesria is free and open source under the [Apache License 2.0](./LICENSE)
([`NOTICE`](./NOTICE) has the attributions). How to take part:
[`CONTRIBUTING.md`](./CONTRIBUTING.md), [`CODE_OF_CONDUCT.md`](./CODE_OF_CONDUCT.md)
and [`GOVERNANCE.md`](./GOVERNANCE.md). Getting help: [`SUPPORT.md`](./SUPPORT.md).
Reporting a vulnerability: [`SECURITY.md`](./SECURITY.md).
