# HTTPS, the certificate warning, and LAN/mobile access

Tesria is always served over HTTPS (Caddy handles this automatically),
but *how* that HTTPS is trusted depends on whether you have a real domain
pointed at the server. This doc covers both paths, and how to make the app
reachable, without browser warnings, from every device on your network,
including phones and tablets.

## Which path applies to you?

- **You have a real domain name** (e.g. `wiki.example.com`) you can point at
  this server → [Path 1](#path-1-a-real-domain-lets-encrypt). Fully trusted,
  zero warnings, zero ongoing cost.
- **You don't have a domain, or you're running this on a home/office LAN and
  want to reach it from other computers, phones, and tablets** →
  [Path 2](#path-2-no-domain-lan-and-mobile-access). Also free; takes one
  extra one-time step per device.

These aren't mutually exclusive: a server with a real `DOMAIN` set still
answers on its LAN IP too (see [How this works](#how-this-works) below), so
Path 2's trust step is worth doing either way if you access the server by IP
or hostname as well as by its real domain.

---

## Path 1: a real domain (Let's Encrypt)

This is already fully automatic. In `.env`:

```bash
DOMAIN=wiki.example.com
ACME_EMAIL=you@example.com
```

```bash
docker compose up -d --build
```

Caddy requests and auto-renews a publicly-trusted Let's Encrypt certificate
for `DOMAIN`. This needs your DNS `A`/`AAAA` record pointing at this server,
and port 80 and/or 443 reachable from the internet (Caddy uses whichever
ACME challenge type fits; by default that's HTTP-01, which needs port 80
briefly reachable from Let's Encrypt's servers).

### Advanced: a real domain without exposing anything to the internet

If you own a domain but want the server fully firewalled from the public
internet (no inbound 80/443 from the internet at all), Caddy supports the
ACME **DNS-01** challenge instead: it proves domain ownership by creating a
TXT record via your DNS provider's API, so no inbound port needs to be
reachable. This needs a Caddy build with your DNS provider's plugin (Caddy
ships dozens, e.g. Cloudflare, Route53, DigitalOcean); see [Caddy's DNS
provider list](https://caddyserver.com/docs/modules/) and [xcaddy](https://github.com/caddyserver/xcaddy)
for building a custom image. This isn't wired into this project's default
`deploy/Dockerfile`/`docker-compose.yml`: it's a DIY path for anyone who
wants it, not a turnkey option today.

---

## Path 2: no domain, LAN, and mobile access

### How this works

Caddy generates its own private certificate authority (CA) the first time it
runs, and uses it to sign a certificate for `DOMAIN` (e.g. `localhost`). Your
browser doesn't trust that CA by default, hence the warning.

As of this setup, the server *also* answers on **any other hostname** (a
`.local` mDNS name, another local DNS name, whatever a device on your network
uses to reach it) minting a certificate for that specific name on the fly
the first time it's requested (Caddy's "On-Demand TLS"), signed by that same
local CA. You don't need to configure or list anything, and it keeps working
regardless of DHCP/IP changes, since it's keyed off the name, not the address.

**Use a hostname, not a raw IP, to reach the server.** TLS's Server Name
Indication (SNI), how a server knows *which* certificate to present, only
works for hostnames; most clients (browsers included) send no SNI at all
when you connect to a literal IP address like `192.168.1.50`. Without it,
Caddy can't tell which on-demand certificate to serve and falls back to a
fixed default, so a raw IP will keep showing a mismatch/warning even after
you've trusted the CA. This isn't a bug to work around: it's inherent to
how SNI-based virtual hosting works. A `.local` name (automatic via
Bonjour/mDNS on macOS and most Linux/Android; Windows may need [Bonjour
Print Services](https://support.apple.com/kb/DL999) installed for reliable
`.local` resolution of *other* machines) is the path that actually works,
and it's what we verified above. If you'd rather use a fixed hostname than
rely on mDNS, add an entry to each device's hosts file instead, or run a
local DNS server (e.g. your router, or Pi-hole/dnsmasq) that resolves a name
of your choice to the server's IP.

That means the fix is the same regardless of which hostname you use to reach
the server: **trust the CA once, per device**, and every hostname this
server answers on becomes warning-free on that device, permanently (until
the CA itself changes: see [Troubleshooting](#troubleshooting)).

### One-time setup per device

**The easy way: open `http://<server-hostname>/trust` on the device.** It is
served over plain HTTP, so it opens with no warning. It guesses the device,
fills in the address, and gives step-by-step instructions: for a computer, a
download of the script below with the address already written in; for an
iPhone, iPad or Android phone, the certificate and the Settings path to trust
it. The sign-in page links to it. The rest of this section is the same thing
by hand.

**Windows without a script.** PowerShell's execution policy blocks
downloaded script files by default ("running scripts is disabled on this
system"), and an employer can lock it. A typed command is not affected, so
the guide gives Windows one line to paste instead, which trusts the server
for the current Windows account and needs no administrator:

```powershell
$c = "$env:TEMP\tesria-ca.crt"; Invoke-WebRequest -UseBasicParsing -Uri "http://<server-hostname>/ca.crt" -OutFile $c; Import-Certificate -FilePath $c -CertStoreLocation Cert:\CurrentUser\Root
```

`trust-ca.ps1` below is for trusting it machine-wide, as an administrator.

Each script has one line to edit if you run it from the repository without an
argument: `TESRIA_ADDRESS` in `trust-ca.sh`, `$TesriaAddress` in
`trust-ca.ps1`.

**macOS or Linux:**

```bash
./deploy/scripts/trust-ca.sh <server-hostname>
```

**Windows** (double-click, or run from PowerShell):

```powershell
.\deploy\scripts\trust-ca.ps1 <server-hostname>
```

`<server-hostname>` should be a name, e.g. `mymac.local`, not a raw IP, for
the SNI reason above. The script itself works identically either way (it's
just fetching a file over HTTP); it's the *browser's* subsequent HTTPS
requests that need a real hostname to validate cleanly.

`<server-address>` is however that device reaches the server: an IP
(`192.168.1.50`), a hostname (`mymac.local`), or just omit it to default to
`localhost` (for running the script on the same machine as the server).

Both scripts:
1. Download the CA's public root certificate from `http://<server-address>/ca.crt`
   (deliberately plain HTTP: nothing is trusted yet, so there's no TLS to
   speak of for this one bootstrap fetch; the file itself isn't secret, it's
   the public half of the CA).
2. Install it into the OS's system trust store (macOS System keychain /
   Windows machine Root store), which both scripts need administrator
   privileges for for: you'll be prompted for your password.
3. Re-check `https://<server-address>/api/health` to confirm it worked.

Re-running either script is safe: it replaces the previous copy of this
same CA instead of piling up duplicates.

**This is a one-time, per-device step.** Run it once on every computer you
want warning-free access from. Chrome, Edge, and Safari all read the system
trust store, so one run covers all three.

### Firefox

Firefox keeps its own certificate store, separate from the OS. Either:
- Import manually: Settings → Privacy & Security → Certificates → View
  Certificates → Authorities → Import, and select the `ca.crt` you
  downloaded (or fetch it yourself from `http://<server-address>/ca.crt`).
- Or set `security.enterprise_roots.enabled = true` in `about:config`, which
  makes Firefox read the OS trust store like other browsers (simplest if you
  manage several machines and don't want a per-browser step).

### Mobile (iOS / Android)

Phones and tablets can't run the trust script directly, but the process is
short:

1. On the phone's browser, visit `http://<server-address>/ca.crt` and
   download it (or AirDrop/transfer the file downloaded elsewhere).
2. **iOS:** opening the file prompts to install a configuration profile
   (Settings → General → VPN & Device Management → install it). Then go to
   Settings → General → About → Certificate Trust Settings and enable full
   trust for the new root: iOS requires this second step separately, or the
   cert is installed but not trusted for TLS.
3. **Android:** Settings → Security → Encryption & credentials → Install a
   certificate → CA certificate, and select the downloaded file. Some
   versions warn that a "network may be monitored" when a user-installed CA
   is trusted: that's standard Android messaging for any manually-installed
   CA, expected here.

### Troubleshooting

- **Still warned after running the script?** Fully quit and reopen the
  browser (not just the tab/window): certificate trust is often cached per
  process.
- **A brand-new device/IP still warns even though others don't.** Each
  hostname/IP gets its own on-demand certificate the *first* time it's
  requested: that's normal and one-time per address, separate from trusting
  the CA itself (which is per-device, not per-address).
- **Everyone needs to re-trust after a server rebuild.** If the `caddy_data`
  volume is ever removed (`docker compose down -v`, or a fresh volume from
  moving to new hardware), Caddy generates a **new** CA with a new private
  key: the old trust doesn't carry over. Re-run the trust script on every
  device. Normal `docker compose up -d --build` / container recreation does
  **not** touch this volume, so this should be rare.
- **`docker compose logs caddy` shows "YOUR SERVER MAY BE VULNERABLE TO
  ABUSE: on-demand TLS is enabled, but no protections are in place".** This
  is Caddy's generic on-demand-TLS warning, and it's expected here: the
  restriction it's referring to (an "ask" endpoint to approve/deny each
  hostname before issuing) exists to stop abuse of *public* ACME rate limits.
  It doesn't apply to this local CA: nothing here talks to the outside
  internet, so there's no external rate limit or reputation to abuse. The
  actual exposure is that **anyone who can reach this server on port 443 can
  make it mint a certificate for an arbitrary hostname**, which is a minor
  resource-usage nuisance, not a trust bypass: those certs are still signed
  by your own local CA, not a publicly-trusted one. If you're only reachable
  on your LAN, this is fine. If you ever expose port 443 directly to the
  public internet without a real `DOMAIN` configured, firewall it to your
  LAN/VPN range.

## Path 3: hosting on the public internet

The default `deploy/Caddyfile` is built for a LAN: its catch-all `:443`
block mints an internal-CA certificate for *any* name a client connects
with, so phones can reach the wiki by IP. On the internet that is a
liability, a stranger can trigger certificate minting for arbitrary
names, and the internal CA is meaningless anyway because nobody outside
your network has trusted it.

Use the public variant instead. In `.env`:

```
DOMAIN=wiki.example.com
ACME_EMAIL=you@example.com
CADDYFILE=deploy/Caddyfile.public
```

then `docker compose up -d caddy`. `deploy/Caddyfile.public` serves only
`{$DOMAIN}` with a Let's Encrypt certificate, sends HSTS (two years,
`includeSubDomains`, `preload`), and does not offer `/ca.crt`.

**HSTS is a one-way door.** Once a browser has seen it, it will refuse plain
HTTP to that host and will not let a user click past a certificate error:
for `max-age` seconds, even after you turn it off. That is the point on a
real domain and a disaster on `localhost` with an untrusted internal CA,
which is why the default file never sends it.

The app trusts `X-Forwarded-For` / `X-Forwarded-Proto` only from the
compose network's private ranges (`Proxy:TrustedNetworks`), which is safe
because the app's port 8080 is exposed only to that network. If you put your
own proxy in front instead of Caddy, set `PROXY_TRUSTED_NETWORKS` to that
proxy's address, and never publish port 8080 to the host, or any LAN
client could set those headers itself.

Before exposing anything, read the internet-readiness checklist in
[`security.md`](./security.md).

## From anywhere: Tailscale (dev-plan 19)

If your devices use Tailscale, the optional `tailscale` service puts Tesria
on your tailnet at `https://<TS_HOSTNAME>.<tailnet>.ts.net`, with a
certificate Tailscale provisions, so no device needs to trust Caddy's
internal CA for that address. It is a second way in: the LAN address and
its certificate are unchanged. Start it with
`docker compose --profile tailscale up -d` after adding `TS_AUTHKEY` to
`.env`; the Support page "Reaching Tesria from anywhere with Tailscale" has
the steps, including turning off the device's key expiry.

