# Security: threat model, defenses, and the internet-readiness checklist

Written at the close of dev-plan Phase 3 (2026-09-09). This is the page
the "Allow public spaces" switch will link to (Phase 5). Read the
checklist at the end before exposing an instance to the internet; read
the rest to understand what you are relying on.

## Who attacks a self-hosted wiki, and why

- **Opportunistic scanners** hit every public address looking for known
  software and default credentials. They are automated, high-volume and
  indifferent to what the wiki contains. Defenses: rate limits, the
  blocklist, and no version number in anything a stranger can fetch.
- **Credential attackers** try passwords: against one account they want,
  or lists of leaked email/password pairs against every account. Defenses:
  per-address limits, per-account lockout, two-factor, the stuffing
  detector, notification to administrators.
- **A malicious or compromised editor** is someone with a legitimate
  account. The wiki is designed for them to write; the risk is what else
  they can reach through it. Defenses: the egress guard (webhooks cannot
  reach the network the server sits on), attachment types (an uploaded
  page cannot run script on the site's origin), the mass-removal detector,
  the audit log they cannot edit.
- **Someone who obtains the database credentials**: through a backup
  left somewhere, a misconfigured volume, a compromised app. Defenses: the
  app itself runs as a role that cannot alter the audit log; the hash
  chain makes alteration detectable even by the owner; every audit row is
  also written to stdout; two-factor secrets, the SMTP password, and the
  mail providers' client secrets and stored sign-in (dev-plan Phase 18)
  are encrypted under keys that only a full database restore recovers.
- **Someone at the keyboard of an unlocked, signed-in browser.** Defenses:
  sudo mode for destructive administration, per-session sign-out, idle and
  absolute session lifetimes, the fresh-login window for minting recovery
  codes.

Not in scope: a compromised host or container runtime, a malicious
administrator (the role is trusted by design, but audited), and denial of
service by sheer volume, which is the network's job, not the app's.

## What each layer defends, and what it does not

| Layer (plan item) | Defends against | Does not defend against |
|---|---|---|
| Proxy trust + secure cookie (3.0) | Per-address limits keying on Caddy's address; the session cookie ever traveling over plain HTTP | A proxy on a *public* address that the operator has not named in `PROXY_TRUSTED_NETWORKS`; publishing port 8080 to a LAN, where any client could then set forwarded headers |
| Security headers + CSP (3.0) | Clickjacking, MIME sniffing, injected inline script, the site being framed or embedded | Injected inline *styles* (allowed, the editor needs them); images loaded from arbitrary `https:` hosts (allowed, authors paste image URLs; a tracking pixel can learn a reader's address) |
| The owner role (10.1) | An administrator, or a stolen admin session, promoting itself or anyone else, unseating the owner, or getting at the owner's account through a password reset or a session revoke | An attacker who takes the *owner's* session within the sudo window; the owner's own mistakes, which is why the transfer is confirmed, audited and alerted |
| Instance rights (11.1) | An administrator doing something this instance has decided administrators should not do (changing retention, opening registration, reading the audit log); a user deleting other people's pages; automation through a token whose owner has lost the right | An administrator with `permissions.edit_user_tier` widening *user* roles, which is theirs to do; the owner, who holds everything; anything the space permissions allow (rights are additive over them, never a bypass) |
| Least-privilege DB role (3.1) | A compromised app deleting or rewriting `AuditLogs`, `PageViews`, `SecurityEvents` | The same app writing *misleading new* audit rows; anyone holding the owner password |
| Backup contract (9.1) | A compromised app hiding a failed backup (it cannot write `Backups` or `BackupAgents`, nor alter `BackupJobs`); reaching backup files (it never mounts them); quietly destroying history through the retention policy (sudo, a Critical alert, and a 24-hour wait the sidecars enforce) | An attacker with an admin session who also suppresses the alert email for a day; anyone holding the owner password, which the sidecars have. Offsite copies exist since 9.2, and 9.4 lets the page *spend* a backup as well as take one, under its own right |
| Audit hash chain + stdout copy (3.1) | Silent edits or deletions in the middle of the log, by anyone including the owner; loss of the database copy | Truncation of the tail between daily checks (the monitor catches it only while the process lives, the stdout copy is the record); a compromised app binary lying at the verify endpoint |
| Rate limits (3.2) | Online brute force from one address; scraping of public content by one address | A widely distributed attacker (see lockout); users behind one NAT sharing a budget (raise the limit) |
| Account lockout (3.2) | Distributed guessing against one account | Nothing further, it is deliberately temporary so it cannot be used to lock users out |
| Threat detection + alerts (3.3) | Attacks that continue past a threshold going unnoticed; an attacker "tidying up" `SecurityEvents` | A slow attacker under every threshold; counters lost on restart (events are not); nobody reading the alerts (email arrives in Phase 4) |
| Blocklist (3.3) | A known-bad address or range, cookie or not | Address rotation; blocking a range that contains your own address (refused) |
| Egress guard (3.4) | Webhooks (and future link previews) reaching the metadata service, the database, the collab sidecar, anything private; DNS rebinding; redirect chains | Ranges the operator has opened in `Egress:AllowedNetworks`; abuse of a *public* endpoint the server can reach that the attacker cannot (rare, but real for firewalled outbound-only hosts) |
| Attachment content types (3.4) | Uploaded HTML/SVG/XML running as the site's origin | A file the *user* downloads and opens locally, that is their machine's concern |
| CSRF header (3.4) | Cross-site pages making state changes with the victim's cookie | Nothing further; it is the third of three layers |
| Sessions (3.5) | A copied cookie outliving sign-out; forever-sessions; no way to sign out one device | Cookie theft *while* the session lives (use two-factor and short lifetimes) |
| Two-factor (3.5) | A stolen or guessed password alone | A stolen recovery code; a device with the authenticator *and* the password |
| Sudo mode (3.5) | An unattended signed-in browser being used for destructive administration | The same browser within five minutes of sign-in |
| Anonymous reading opt-in twice (5.5) | An instance-wide switch left on with nothing published still presenting a public face; a deep link to a page on an instance that publishes nothing rendering the public shell | Anything about a space that *is* published: that is the point of publishing it. `/api/instance` is anonymous by design and says the instance name, whether it needs an owner, whether anything is public, and whether sign-up is open |
| Password inside the request (11.3, 9.4) | An unattended browser deleting a space or restoring a backup, which the sudo window alone would allow; acting on the wrong one, which the key or label typed back catches | Someone who knows the password and means it. The point is deliberateness, not a second factor (an account without a password answers with a one-time code instead) |
| Instance branding (13.1) | An administrator changing what every visitor sees first (`settings.branding` is the owner's by default); script in an uploaded SVG, which is rebuilt from an allowlist, only ever shown through `<img>`, and served sandboxed; CSS injection through a color, which is stored and emitted only as `#rrggbb`; the CSP being weakened to fit the branding, which it is not, since the inline script's text is identical on every instance | Someone holding the right making the sign-in page look like another organization's, which the audit log records but nothing prevents; a color chosen to be unreadable, which the owner may keep on purpose |
| Restore from the admin page (9.4) | An administrator replacing the wiki with an older copy at all (`backups.restore` is the owner's by default and has to be granted); doing it to the wrong backup (the label is typed back) or by accident (the password is in the request); losing what was there (a safety backup is taken first and cannot be skipped, and the replaced copy is kept as the undo); doing it unnoticed (`backup.restored` is a Critical alert with no cooldown to every administrator, written *after* the restore so it lands in the restored database's own chain) | The owner, who can grant themselves the right and holds the password: this is deliberateness and a record, not a barrier. An owner-level account restoring to before something it wants hidden still leaves the safety backup, the kept copy and the alert, which is what makes it visible rather than impossible |
| Pinned Argon2id (3.5) | Offline cracking of a leaked hash | A weak password against a determined offline attacker with time: length still matters |
| Dependency audit (3.6) | Known vulnerabilities in what ships | Unknown ones; a compromised upstream package (Dependabot + lockfiles narrow the window) |

## Known gaps and accepted trade-offs

Recorded so nobody rediscovers them as surprises. Each ends with a verdict
from the enterprise-readiness review of 2026-09-24: **Acceptable** (a
reasoned trade-off, documented), **Should fix** (worth doing before a 1.0),
or **Must fix** (before Tesria is offered for enterprise use). Every Must
fix and Should fix was done in dev-plan 14.3 (2026-09-24, reviewed by
Fable 5.1 before it was built); each says how. The numbers are kept so
older references still point at the right item.

1. ~~`/api/health` reports the version.~~ **Fixed.** The version is given
   only to signed-in callers: `/api/health`, `/api/instance` and the
   OpenAPI document all leave it out for anyone else. Monitoring needs
   liveness, not the release number. Administrators find the version in
   Administration, About.
2. ~~`img-src https:` in the CSP.~~ **Fixed, as a choice.** A remote image
   can log each reader's address. Administration, Settings, **Images**
   (off by default) limits pictures to this instance and a list of hosts:
   the CSP's `img-src` follows it, exported pages carry it as a
   `<meta>` CSP, and the editor tells an author when a picture's host is
   not allowed. Off, any https picture shows, as before.
3. ~~Registration is not an audit entry.~~ **Fixed.** Every new account is
   recorded as `user.registered`, with how it was made (first account,
   invite, open registration or single sign-on) and the address it came
   from.
4. **Threat-detection counters and cooldowns are in-process.** A restart
   resets them. Written events are never lost.
   *Acceptable* for a single server, the supported topology.
5. **`SecurityAlerts` is mutable** (it has to be: acknowledging is an
   update). An attacker with the app role could mark alerts resolved. The
   underlying `SecurityEvents` cannot be touched, and the notifications
   were already sent.
   *Acceptable:* the evidence is immutable; the alert list is a to-do list.
6. ~~The TOTP challenge is not single-use.~~ **Fixed.** Each challenge
   carries a nonce also stored on the account; signing in clears it, and a
   newer challenge replaces it, so a challenge works once.
7. ~~Trust by private range.~~ **Fixed.** The Compose network has a fixed
   subnet (`TESRIA_SUBNET`, default `10.203.0.0/24`), and under Compose the
   app trusts forwarded headers only from loopback and that subnet. An
   explicit `Proxy:TrustedNetworks` still wins.
8. **Alerts need email set up to reach anyone who is not signed in.**
   Without an email server, alerts reach administrators only through the
   in-app bell. Set up email (Administration, Settings), or forward
   `docker compose logs app` (the `Tesria.Audit` category and any `crit:`
   line) to something that pages you.
   *Acceptable:* documented, and the setup wizard offers email.
9. **Recovery codes are a second factor's backup and a password reset.**
   One set, two roles, by design (one set is one thing to keep safe). A
   stolen set is therefore a full account takeover; treat them like a
   password.
   *Acceptable*, and common (GitHub works the same way). An organization
   with single sign-on can have its own provider handle recovery instead.
10. ~~The database owner's password is in the app's environment.~~
    **Fixed.** A `migrate` service holds the owner credentials: it applies
    migrations, fills in the audit chain and provisions the least-privilege
    role. The app and the collaboration service are given only the app
    role (`APP_DB_PASSWORD`, now required), and in production the app
    refuses to start with migrations pending rather than applying them.
    Since 0.7.3 `migrate` stays running: a restore asks it (through a
    comment on the restored copy, which only the owner can set) to bring
    that copy up to date and grant the app its access, and every thirty
    seconds it checks the live database needs neither. It listens on no
    port and takes no request from the app. The two backup services also
    hold the owner password: a backup has to read everything, and
    pgBackRest needs the owner. None of the three runs code that answers
    the network.
11. ~~An open live-editing connection outlives a change of permission.~~
    **Fixed in 0.7.3.** 0.6.0 called this fixed and was wrong: it closed
    the connections when access changed, but the collaboration service
    checked only a token's signature and expiry, so a client that kept its
    token could reconnect with it for up to ten minutes (an outside review
    found it, 2026-09-24; advisory to follow). Now:
    - Every connection is authorized by the app, not only the token that
      opened it. The token names the browser session or API token it was
      issued under; at every connection the collaboration service asks the
      app (on the compose network, with the shared secret) whether the
      account is active, that session or token still valid, and the page
      still editable by them. Caddy refuses the internal route from
      outside. An app that cannot be asked admits nobody, after waiting
      fifteen seconds for one that is restarting.
    - Once a minute it asks again for every open connection, in one
      request, and closes those the app no longer allows, so access that
      has gone ends within a minute even if the notice below never arrives.
    - As before, when a save changes something that can take editing away
      (a suspension, a sign-out or password change, a role, a group
      membership, a space permission or a page restriction), the app asks
      the service to close the connections it touches at once, and tokens
      last ten minutes. A change to a page restriction closes the
      connections to every page in that space, since restrictions are
      inherited; the reconnect costs the others a moment.
12. ~~Asking for a password reset takes longer for an address that has an
    account.~~ **Fixed.** The email is queued and sent in the background,
    so both answers take the same order of time. Not exactly the same: a
    database lookup still differs slightly, which is far below what can be
    measured over a network.
13. **Some state assumes a single app instance:** render tokens are signed
    with a key the app makes when it starts, and export progress, rate
    limits and settings are cached in memory. Running more than one app
    container is not supported.
    *Acceptable* for now, as a stated limit: one app container per
    instance. Enterprise high availability (several app containers behind
    a load balancer) would need shared state (a distributed cache and a
    shared token key) and is a feature, not a fix.
14. ~~IPv6 NAT64 addresses are not in the egress guard's private list.~~
    **Fixed.** The guard reads the IPv4 address inside NAT64
    (`64:ff9b::/96`) and 6to4 (`2002::/16`) addresses and judges that, and
    also refuses `192.0.0.0/24`, `198.18.0.0/15` and the IPv6 discard
    range.

## Internet-readiness checklist

Every item is something the software cannot do for you. Do all of them
before DNS points at the box.

**Configuration (`.env`)**
- [ ] `DOMAIN` is a real hostname you control; `ACME_EMAIL` is monitored.
- [ ] `CADDYFILE=deploy/Caddyfile.public`: HSTS on, the on-demand-TLS
      catch-all gone. (`docs/tls-and-lan-access.md`, Path 3.)
- [ ] `POSTGRES_PASSWORD`, `APP_DB_PASSWORD`, `BACKUP_ENCRYPTION_KEY`,
      `COLLAB_SHARED_SECRET` are all long, random, and different from
      each other. `APP_DB_PASSWORD` is required: Compose refuses to start
      without it, and the app refuses to run in production as the owner.
- [ ] Port 8080 (app), 5432 (db), 8090 (collab) are **not** published to
      the host. Only Caddy's 80 and 443 are. `docker compose config` shows
      `expose`, not `ports`, for the three.
- [ ] If anything other than this stack's Caddy fronts the app,
      `PROXY_TRUSTED_NETWORKS` names exactly that proxy.
- [ ] Decide whether pages may show pictures from anywhere
      (Administration, Settings, Images). If readers' privacy matters,
      limit pictures to this wiki and the hosts you trust.
- [ ] `OIDC_REQUIRE_HTTPS_METADATA` is `true` (the default) if SSO is on.

**Accounts**
- [ ] Administration -> Roles has been reviewed, and each role holds only
      what that role needs on this instance.
- [ ] The owner account has two-factor on and its recovery codes saved:
      it is the one account that cannot be suspended or reset by anyone else.
- [ ] Every administrator has two-factor on, and **Require two-factor for
      administrators** is on (Admin → Security → Kill switches).
- [ ] Every administrator has saved their recovery codes.
- [ ] Registration is closed (**Allow public registration** off) unless
      you mean to run an open community; if open, watch the registration
      alerts.
- [ ] **Allow public spaces** stays off until you have read
      `docs/architecture.md` → "Public read mode" and understand that a
      published space is readable, and its pages exportable, by anyone,
      with restricted pages, drafts, trash and history excluded. Publish
      spaces one at a time from Admin → Spaces; every publish is an alert.
      Anonymous reading is **opt-in twice** (5.5): this switch *and* a space
      marked public. With either missing there is no anonymous reading at
      all, and a visitor with no session gets the sign-in page rather than
      an empty public shell, so leaving the switch on with nothing published
      does not quietly expose a surface.
- [ ] The fixture accounts from development are gone or demoted.

**Operations**
- [ ] `scripts/audit.sh` exits 0 on the release you are deploying.
- [ ] Backups run and a restore has been rehearsed
      (`docs/backup-recovery.md`); the pgBackRest repo is encrypted with a
      key you have stored somewhere that is not this host.
- [ ] `docker compose logs app` is forwarded somewhere durable: that is
      the copy of the audit log an attacker with the database cannot
      reach.
- [ ] `scripts/verify-audit-chain.sh` runs from cron with an admin token
      and its exit status is monitored.
- [ ] Someone reads the bell, or the Security page, daily until email
      alerting (Phase 4) is configured; then the SMTP settings are filled
      in and **Send test email** works.
- [ ] Dependabot (or a person) watches for updates; `scripts/audit.sh`
      runs after each one.

**Reading list before you flip Phase 5's switch**
- `docs/architecture.md`: the Phase 3 sections, especially "Threat
  detection" (what will and will not be noticed) and "Egress and input
  hardening".
- The "Known gaps" above.

## Reviewing this yourself

A review pass at the end of Phase 3 walked every registered route and
confirmed: every `/api` route requires authorization except health,
register, both sign-in steps, recovery, and OIDC status/login (all
rate-limited where they take a credential); the OIDC `returnUrl` accepts
only same-origin paths; invite and reset tokens are returned once and
never listed; the SMTP password is never returned. (Since 2026-09-24 one
more route is anonymous: the mail sign-in callback,
`/api/email/oauth/callback`, because Microsoft's or
Google's redirect may arrive without the session cookie. It acts only on a
`state` that is random, single-use, ten minutes long and bound to the
administrator who started the sign-in, and it checks that account still
holds the email right before storing anything. Starting a sign-in needs
sudo; the stored refresh token can send mail as that mailbox until
revoked, and is never returned.) The findings that
survived the pass are the "Known gaps" list. If you find something not on
it, see `SECURITY.md` at the repository root.
