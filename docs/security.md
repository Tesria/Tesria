# Security: threat model, defences, and the internet-readiness checklist

Written at the close of dev-plan Phase 3 (2026-09-09). This is the page
the "Allow public spaces" switch will link to (Phase 5). Read the
checklist at the end before exposing an instance to the internet; read
the rest to understand what you are relying on.

## Who attacks a self-hosted wiki, and why

- **Opportunistic scanners** hit every public address looking for known
  software and default credentials. They are automated, high-volume and
  indifferent to what the wiki contains. Defences: rate limits, the
  blocklist, no version-specific fingerprints beyond `/api/health`.
- **Credential attackers** try passwords — against one account they want,
  or lists of leaked email/password pairs against every account. Defences:
  per-address limits, per-account lockout, two-factor, the stuffing
  detector, notification to administrators.
- **A malicious or compromised editor** is someone with a legitimate
  account. The wiki is designed for them to write; the risk is what else
  they can reach through it. Defences: the egress guard (webhooks cannot
  reach the network the server sits on), attachment types (an uploaded
  page cannot run script on the site's origin), the mass-removal detector,
  the audit log they cannot edit.
- **Someone who obtains the database credentials** — through a backup
  left somewhere, a misconfigured volume, a compromised app. Defences: the
  app itself runs as a role that cannot alter the audit log; the hash
  chain makes alteration detectable even by the owner; every audit row is
  also written to stdout; two-factor secrets and the SMTP password are
  encrypted under keys that only a full database restore recovers.
- **Someone at the keyboard of an unlocked, signed-in browser.** Defences:
  sudo mode for destructive administration, per-session sign-out, idle and
  absolute session lifetimes, the fresh-login window for minting recovery
  codes.

Not in scope: a compromised host or container runtime, a malicious
administrator (the role is trusted by design — but audited), and denial of
service by sheer volume, which is the network's job, not the app's.

## What each layer defends, and what it does not

| Layer (plan item) | Defends against | Does not defend against |
|---|---|---|
| Proxy trust + secure cookie (3.0) | Per-address limits keying on Caddy's address; the session cookie ever travelling over plain HTTP | A proxy on a *public* address that the operator has not named in `PROXY_TRUSTED_NETWORKS`; publishing port 8080 to a LAN, where any client could then set forwarded headers |
| Security headers + CSP (3.0) | Clickjacking, MIME sniffing, injected inline script, the site being framed or embedded | Injected inline *styles* (allowed — the editor needs them); images loaded from arbitrary `https:` hosts (allowed — authors paste image URLs; a tracking pixel can learn a reader's address) |
| Least-privilege DB role (3.1) | A compromised app deleting or rewriting `AuditLogs`, `PageViews`, `SecurityEvents` | The same app writing *misleading new* audit rows; anyone holding the owner password |
| Backup contract (9.1) | A compromised app hiding a failed backup (it cannot write `Backups` or `BackupAgents`, nor alter `BackupJobs`); reaching backup files (it never mounts them); quietly destroying history through the retention policy (sudo, a Critical alert, and a 24-hour wait the sidecars enforce) | An attacker with an admin session who also suppresses the alert email for a day; anyone holding the owner password, which the sidecars have; the offsite gap (there is no offsite copy yet, dev-plan 9.2) |
| Audit hash chain + stdout copy (3.1) | Silent edits or deletions in the middle of the log, by anyone including the owner; loss of the database copy | Truncation of the tail between daily checks (the monitor catches it only while the process lives — the stdout copy is the record); a compromised app binary lying at the verify endpoint |
| Rate limits (3.2) | Online brute force from one address; scraping of public content by one address | A widely distributed attacker (see lockout); users behind one NAT sharing a budget (raise the limit) |
| Account lockout (3.2) | Distributed guessing against one account | Nothing further — it is deliberately temporary so it cannot be used to lock users out |
| Threat detection + alerts (3.3) | Attacks that continue past a threshold going unnoticed; an attacker "tidying up" `SecurityEvents` | A slow attacker under every threshold; counters lost on restart (events are not); nobody reading the alerts (email arrives in Phase 4) |
| Blocklist (3.3) | A known-bad address or range, cookie or not | Address rotation; blocking a range that contains your own address (refused) |
| Egress guard (3.4) | Webhooks (and future link previews) reaching the metadata service, the database, the collab sidecar, anything private; DNS rebinding; redirect chains | Ranges the operator has opened in `Egress:AllowedNetworks`; abuse of a *public* endpoint the server can reach that the attacker cannot (rare, but real for firewalled outbound-only hosts) |
| Attachment content types (3.4) | Uploaded HTML/SVG/XML running as the site's origin | A file the *user* downloads and opens locally — that is their machine's concern |
| CSRF header (3.4) | Cross-site pages making state changes with the victim's cookie | Nothing further; it is the third of three layers |
| Sessions (3.5) | A copied cookie outliving sign-out; forever-sessions; no way to sign out one device | Cookie theft *while* the session lives (use two-factor and short lifetimes) |
| Two-factor (3.5) | A stolen or guessed password alone | A stolen recovery code; a device with the authenticator *and* the password |
| Sudo mode (3.5) | An unattended signed-in browser being used for destructive administration | The same browser within five minutes of sign-in |
| Pinned Argon2id (3.5) | Offline cracking of a leaked hash | A weak password against a determined offline attacker with time — length still matters |
| Dependency audit (3.6) | Known vulnerabilities in what ships | Unknown ones; a compromised upstream package (Dependabot + lockfiles narrow the window) |

## Known gaps and accepted trade-offs

Recorded so nobody rediscovers them as surprises:

1. **`/api/health` reports the version.** Useful for operators and
   monitoring; a scanner learns which release you run. Acceptable while
   the audit gate keeps releases clean.
2. **`img-src https:` in the CSP.** Authors paste image URLs. A remote
   image can log the reader's address. Restricting it would break content;
   Phase 7's media work may add an image proxy.
3. **Registration is not an audit entry.** The registration-burst detector
   sees it; the audit log does not list `user.registered`. Follow-up.
4. **Threat-detection counters and cooldowns are in-process.** A restart
   resets them. Written events are never lost.
5. **`SecurityAlerts` is mutable** (it has to be — acknowledging is an
   update). An attacker with the app role could mark alerts resolved. The
   underlying `SecurityEvents` cannot be touched, and the notifications
   were already sent.
6. **The TOTP challenge is not single-use** within its five minutes. It is
   bound to the client address and needs a code, and codes are single-use;
   replaying the challenge buys nothing.
7. **Trust by private range.** The default `Proxy:TrustedNetworks` trusts
   RFC 1918 — safe only because port 8080 is never published. Fronting the
   app with a different proxy, or publishing the port, changes that.
8. **No email yet.** Alerts reach administrators through the in-app bell.
   Until Phase 4 ships, an administrator who does not sign in does not
   know. Check the Security page, or forward `docker compose logs app`
   (the `Tesria.Audit` category and any `crit:` line) to something that
   pages you.
9. **Recovery codes are a second factor's backup and a password reset.**
   One set, two roles, by design (one set is one thing to keep safe). A
   stolen set is therefore a full account takeover; treat them like a
   password.

## Internet-readiness checklist

Every item is something the software cannot do for you. Do all of them
before DNS points at the box.

**Configuration (`.env`)**
- [ ] `DOMAIN` is a real hostname you control; `ACME_EMAIL` is monitored.
- [ ] `CADDYFILE=deploy/Caddyfile.public` — HSTS on, the on-demand-TLS
      catch-all gone. (`docs/tls-and-lan-access.md`, Path 3.)
- [ ] `POSTGRES_PASSWORD`, `APP_DB_PASSWORD`, `BACKUP_ENCRYPTION_KEY`,
      `COLLAB_SHARED_SECRET` are all long, random, and different from
      each other. `APP_DB_PASSWORD` is **not** empty — an empty value runs
      the app as the database owner, and the startup log warns you.
- [ ] Port 8080 (app), 5432 (db), 8090 (collab) are **not** published to
      the host. Only Caddy's 80 and 443 are. `docker compose config` shows
      `expose`, not `ports`, for the three.
- [ ] If anything other than this stack's Caddy fronts the app,
      `PROXY_TRUSTED_NETWORKS` names exactly that proxy.
- [ ] `OIDC_REQUIRE_HTTPS_METADATA` is `true` (the default) if SSO is on.

**Accounts**
- [ ] Every administrator has two-factor on, and **Require two-factor for
      administrators** is on (Admin → Security → Kill switches).
- [ ] Every administrator has saved their recovery codes.
- [ ] Registration is closed (**Allow public registration** off) unless
      you mean to run an open community; if open, watch the registration
      alerts.
- [ ] **Allow public spaces** stays off until you have read
      `docs/architecture.md` → "Public read mode" and understand that a
      published space is readable — and its pages exportable — by anyone,
      with restricted pages, drafts, trash and history excluded. Publish
      spaces one at a time from Admin → Spaces; every publish is an alert.
- [ ] The fixture accounts from development are gone or demoted.

**Operations**
- [ ] `scripts/audit.sh` exits 0 on the release you are deploying.
- [ ] Backups run and a restore has been rehearsed
      (`docs/backup-recovery.md`); the pgBackRest repo is encrypted with a
      key you have stored somewhere that is not this host.
- [ ] `docker compose logs app` is forwarded somewhere durable — that is
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
- `docs/architecture.md` — the Phase 3 sections, especially "Threat
  detection" (what will and will not be noticed) and "Egress and input
  hardening".
- The "Known gaps" above.

## Reviewing this yourself

A review pass at the end of Phase 3 walked every registered route and
confirmed: every `/api` route requires authorization except health,
register, both sign-in steps, recovery, and OIDC status/login (all
rate-limited where they take a credential); the OIDC `returnUrl` accepts
only same-origin paths; invite and reset tokens are returned once and
never listed; the SMTP password is never returned. The findings that
survived the pass are the "Known gaps" list. If you find something not on
it, see `SECURITY.md` at the repository root.
