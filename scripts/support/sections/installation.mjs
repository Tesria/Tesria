// Installation and operations: running Tesria, for whoever looks after the
// server (dev-plan 10.5), rewritten to the owner's rules of 2026-09-23
// (scripts/support/WRITING.md). Trusting the local certificate and Opening
// Tesria by name are the approved pilot pages, moved here from pilot.mjs
// unchanged.
//
// The facts come from the code and the runbooks it ships with, checked on
// 2026-09-24: docker-compose.yml and .env.example for every setting,
// deploy/ (the Caddyfiles, the backup and pgBackRest scripts),
// docs/tls-and-lan-access.md, docs/backup-recovery.md, docs/security.md, and
// the admin screens in src/web/src/routes/admin. Those stay the engineers'
// copies; these pages are the operator's.
//
// Pictures only where an admin screen is the subject: where Back up now and
// Test restore are, the retention policy, and the email settings. None of a
// restore: only the owner may restore, and the pictures are taken as an
// administrator. None of Storage targets either: the instance the pictures
// come from has no offsite target, so the section is a paragraph of text.
//
// For the owner to consider (not removed here):
//   Testing a target and the cloud budget   short; could be two sections of
//                                           Offsite copies instead

import { readFileSync } from 'node:fs'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'

const ROOT = join(dirname(fileURLToPath(import.meta.url)), '..', '..', '..')

// Pictures only where they show something words cannot, such as where a
// control is, and taken in a narrow window: at about the width they are
// shown, their text is the size of the page's on a desktop and still
// readable on a phone (the owner, 2026-09-23).
const NARROW = { width: 480, height: 900 }
const ADDRESS = [{ wait: 1500 }, { type: 'wiki-server.local', selector: '#trust-address' }, { click: '[data-device="mac"]' }, { wait: 300 }]

/** An admin page's section by its heading (a Playwright selector, for clipTo only). */
const section = (title) => `section.profile__section:has(> h2:text-is("${title}"))`

// The Email section's controls, tagged so the picture can box them: the
// Settings page has several sections with toggles and buttons alike.
const TAG_EMAIL = `(() => {
  const s = [...document.querySelectorAll('section.profile__section')].find((x) => x.querySelector(':scope > h2')?.textContent.trim() === 'Email')
  if (!s) return
  s.querySelector('label.admin__toggle')?.setAttribute('data-shot', 'send-email')
  for (const b of s.querySelectorAll('button')) {
    if (b.textContent.trim() === 'Save mail settings') b.setAttribute('data-shot', 'save-mail')
    if (b.textContent.trim() === 'Send test email to me') b.setAttribute('data-shot', 'send-test')
  }
})()`

// "Last changed ... by" names a real account on the instance the pictures
// come from, so it is hidden.
const HIDE_POLICY_AUTHOR = "[...document.querySelectorAll('.backup-policy p')].filter((p) => p.textContent.trim().startsWith('Last changed')).forEach((p) => { p.style.display = 'none' })"

export const shots = () => [
  // ---- Trusting the local certificate: the two controls in the /trust
  // guide a reader has to find. Its written steps are not pictured: the
  // page says the same in words, which read at any size (the owner,
  // 2026-09-23).
  {
    name: 'trust-device', url: '/trust', viewport: NARROW, phone: false, steps: ADDRESS,
    clipTo: '[data-step="1"]', clipPad: 12,
    annotate: [{ type: 'box', target: '.trust-device.is-active', pad: 5 }],
  },
  {
    name: 'trust-address', url: '/trust', viewport: NARROW, phone: false, steps: ADDRESS,
    clipTo: '[data-step="2"]', clipPad: 12,
    annotate: [{ type: 'box', target: '.trust-address', pad: 5 }],
  },

  // ---- Backups and recovery: where Back up now and Test restore are, above
  // the first backup service's card.
  {
    name: 'backup-now', url: '/admin/backups', viewport: NARROW, phone: false, settle: 800, steps: [{ wait: 3000 }],
    clipTo: ['.backup-actions', '.backup-cards > .backup-card:first-child'], clipPad: 12,
    annotate: [
      { type: 'box', target: '.backup-actions .btn--primary', pad: 5 },
      { type: 'box', target: '.backup-cards > .backup-card:first-child > .btn', pad: 5 },
    ],
  },

  // ---- Retention: the policy's two choices, and Review change.
  {
    name: 'retention-review', url: '/admin/backups', viewport: NARROW, phone: false, settle: 800,
    steps: [{ wait: 3000 }, { eval: HIDE_POLICY_AUTHOR }],
    clipTo: section('Retention policy'), clipPad: 0,
    annotate: [
      { type: 'box', target: '.backup-policy__rule', pad: 5 },
      { type: 'box', target: '.backup-policy button[type="submit"]', pad: 5 },
    ],
  },

  // ---- Email (SMTP): the form filled in with example values (which also
  // covers whatever the instance really uses), and the three controls the
  // steps name. Nothing is saved: the form is never submitted.
  {
    name: 'email-steps', url: '/admin/settings', viewport: NARROW, phone: false, settle: 800,
    steps: [
      { wait: 2500 },
      { type: 'smtp.example.com', selector: 'input[name="smtpHost"]' },
      { type: 'wiki@example.com', selector: 'input[name="smtpUsername"]' },
      { type: 'wiki@example.com', selector: 'input[name="smtpFromAddress"]' },
      { eval: 'document.activeElement && document.activeElement.blur()' },
      { eval: TAG_EMAIL },
    ],
    clipTo: section('Email'), clipPad: 0,
    annotate: [
      { type: 'box', target: '[data-shot="send-email"]', pad: 4 },
      { type: 'box', target: '[data-shot="save-mail"]', pad: 4 },
      { type: 'box', target: '[data-shot="send-test"]', pad: 4 },
    ],
  },
]

export async function build({
  top, page, ensure, attachCurrent, doc, p, h, text, bold, italic, code, ul, ol, li, panel, table, codeBlock,
  fileBlock, live, tasks, task, picture, pageLink,
}) {
  const root = top['Installation and operations']
  const b = (t) => text(t, bold)
  const c = (t) => text(t, code)
  const i = (t) => text(t, italic)
  /** A numbered step: a heading that says what to do, then how. */
  const step = (n, title) => h(3, `Step ${n}: ${title}`)
  /** The note, once per page, that your-server stands for the reader's own address. */
  const yourServer = () => panel('note',
    p(b('On this page, '), c('your-server'), b(' stands for your server’s address:'), ' whatever you type into the browser to open Tesria, without ', c('https://'), '. For example, if you open Tesria at ', c('https://wiki-server.local'), ', then ', c('https://your-server/api/health'), ' means ', c('https://wiki-server.local/api/health'), '.'))

  // ============================================================ The section
  await page('Installation and operations', null, doc(
    p('These pages are for whoever looks after the computer Tesria runs on: installing it, choosing its settings, keeping it safe and backed up, and getting it back if something goes wrong. If you only use the wiki, the ', pageLink('User manual'), ' is where you want to be.'),
    p('Never run a server before? Start with ', pageLink('Quick start'), ', which takes you from nothing to a working wiki in about ten minutes, and come back here when you want to know what is going on underneath.'),
    live('children', { depth: '2', sort: 'position' }),
  ))

  // ============================================================ Installing
  await page('Installing with Docker Compose', root, doc(
    p('Tesria is not one program but several that work together: the wiki itself, a database, a web server, and a few helpers. ', b('Docker'), ' runs each of them in its own ', b('container'), ', a sealed box holding everything that program needs, so nothing else on the computer can clash with it. ', b('Docker Compose'), ' starts the whole set together, from one file called ', c('docker-compose.yml'), ' in the Tesria folder.'),
    p('This page explains what each part does and where your data is kept, then walks through the first start. ', pageLink('Quick start'), ' is the short version of the same steps.'),

    h(2, 'What runs'),
    p('When Tesria is running, ', c('docker compose ps'), ' lists these services:'),
    ul(
      li(p(c('app'), ': Tesria itself, the pages you see and the API behind them.')),
      li(p(c('db'), ': PostgreSQL 18, the database. It holds pages, comments, accounts and settings: everything except uploaded files.')),
      li(p(c('caddy'), ': the web server in front. It looks after HTTPS (the padlock in the address bar) and is the only part other computers can reach, on ports 80 and 443.')),
      li(p(c('collab'), ': lets several people edit the same page at once. It does nothing until ', c('COLLAB_SHARED_SECRET'), ' is set.')),
      li(p(c('pdf'), ': turns pages into PDF files. PDF export stays off until ', c('PDF_SHARED_SECRET'), ' is set.')),
      li(p(c('backup'), ': copies the database and every attachment on a schedule, and sends copies offsite if you set that up.')),
      li(p(c('pgbackrest'), ': keeps physical backups of the database and a running record of every change, which is what lets you restore to any moment.')),
    ),

    h(2, 'Where your data lives'),
    p('Containers are thrown away and made again on every upgrade, so your data is not kept in them. It is kept in Docker ', b('volumes'), ', storage areas Docker looks after separately, which survive stopping, restarting and rebuilding:'),
    ul(
      li(p(c('pgdata'), ': the database.')),
      li(p(c('uploads'), ': attachments, meaning the images, files and videos added to pages.')),
      li(p(c('backups'), ': the database dumps and attachment archives the ', c('backup'), ' service takes.')),
      li(p(c('pgbackrest'), ': the physical backups and the record of changes, encrypted.')),
      li(p(c('caddy_data'), ': the HTTPS certificates, including the certificate authority behind the local certificate.')),
    ),
    p('On disk each name starts with ', c('tesria_'), ', so the database’s volume is ', c('tesria_pgdata'), '.'),
    panel('warning', p(b('One command deletes all of them:'), ' ', c('docker compose down -v'), '. The ', c('-v'), ' means “and the volumes”, which is the wiki and every backup on this machine. Plain ', c('docker compose down'), ' is safe. See ', pageLink('Uninstalling and moving'), '.')),

    h(2, 'Before you start'),
    ul(
      li(p(b('Docker with Compose'), ' on the computer that will run Tesria. See ', pageLink('Prerequisites'), ' and ', pageLink('System requirements'), '.')),
      li(p(b('The Tesria folder'), ', with ', c('docker-compose.yml'), ' in it. ', pageLink('Quick start'), ' shows how to get it.')),
      li(p(b('A terminal open in that folder.'), ' Every command on these pages is run from there.')),
    ),

    step(1, 'Make your settings file'),
    p('Tesria reads its settings from a file called ', c('.env'), ' in the Tesria folder. Start from the example that comes with it:'),
    codeBlock('bash', 'cp .env.example .env'),
    p('Open ', c('.env'), ' in a text editor and set at least these. The example holds placeholders rather than blanks, so a value you forget does not stop Tesria starting: it just stays insecure.'),
    ul(
      li(p(c('POSTGRES_PASSWORD'), ' and ', c('APP_DB_PASSWORD'), ': two different long random passwords for the database.')),
      li(p(c('BACKUP_ENCRYPTION_KEY'), ': a long random passphrase that encrypts the physical backups.')),
      li(p(c('DOMAIN'), ' and ', c('ACME_EMAIL'), ': the address people will open Tesria at, and an email address. ', c('localhost'), ' is fine to try it on one computer.')),
    ),
    p('This makes a good random value; run it once for each:'),
    codeBlock('bash', 'openssl rand -hex 32'),
    p('Every setting is explained in the ', pageLink('Configuration reference'), '.'),
    panel('warning', p(b('Keep a copy of BACKUP_ENCRYPTION_KEY somewhere other than this computer,'), ' such as a password manager. The backups it encrypts cannot be restored without it, by anyone.')),

    step(2, 'Start everything'),
    codeBlock('bash', 'docker compose up -d --build'),
    p(c('--build'), ' makes Tesria’s containers from the files in the folder, which takes a few minutes the first time. ', c('-d'), ' runs them in the background, so you get the terminal back.'),
    p('While it starts, a few things happen on their own: the database starts, the ', c('pgbackrest'), ' service prepares its backup store, Tesria creates its tables and a restricted database account for itself, and Caddy makes a certificate for your address.'),
    panel('note', p(b('Start the whole set, not only the database.'), ' On a new install, ', c('docker compose up -d db'), ' on its own makes the database restart every few seconds, because it waits for the ', c('pgbackrest'), ' service to prepare the backup store. If you ever need the database without the rest, start the two together: ', c('docker compose up -d db pgbackrest'), '.')),

    step(3, 'Check it is running'),
    codeBlock('bash', 'docker compose ps'),
    p('Every line should say ', c('Up'), '. The ', c('db'), ', ', c('app'), ', ', c('backup'), ' and ', c('pgbackrest'), ' lines add ', c('(healthy)'), ' once their own checks pass, which can take a couple of minutes for the backup services. ', pageLink('Health checks and monitoring'), ' has more ways to check.'),

    step(4, 'Open it and create the owner'),
    yourServer(),
    p('Open ', c('https://your-server'), ' in a browser. A new Tesria has no accounts yet, so it opens the ', b('setup wizard'), ': it creates the ', b('owner'), ', the one account that owns this Tesria, then asks for its name, who can join, and how long to keep backups. See ', pageLink('First-run setup wizard'), '.'),
    p('On your own network, the browser may warn that the connection is not private. Nothing is wrong: ', pageLink('Trusting the local certificate'), ' explains why, and makes the warning go away.'),

    h(2, 'Everyday commands'),
    p('Run from the Tesria folder:'),
    table([
      ['Command', 'What it does'],
      [p(c('docker compose ps')), 'Lists the services and whether they are healthy'],
      [p(c('docker compose logs app')), 'Shows Tesria’s log; add -f to follow it'],
      [p(c('docker compose restart app')), 'Restarts Tesria'],
      [p(c('docker compose stop')), 'Stops everything and keeps your data'],
      [p(c('docker compose up -d')), 'Starts everything, with any change to .env'],
    ], [300, 400]),
  ))

  // ============================================================ Configuration
  /** A setting and what it does, as a list item. */
  const setting = (name, ...what) => li(p(c(name), ': ', ...what))
  await page('Configuration reference', root, doc(
    p('Tesria has two kinds of settings. The ones on this page live in the ', c('.env'), ' file in the Tesria folder: passwords, the address, backups. They are read when the containers start, and most people set them once and forget them.'),
    p('Everything else, such as the wiki’s name, who can sign up and the email server, is changed in the browser, under ', b('Admin'), '. See ', pageLink('Settings (administration)'), '.'),

    h(2, 'Changing a setting'),
    ol(
      li(p('Open ', c('.env'), ' in a plain text editor.')),
      li(p('Change the line. Each is ', c('NAME=value'), ', with no spaces around the ', c('='), '. A line that starts with ', c('#'), ' is a comment and is ignored, so to turn on a setting the example has commented out, delete the ', c('#'), '.')),
      li(p('Save the file, and run ', c('docker compose up -d'), '. It restarts the services whose settings changed and leaves the rest alone.')),
    ),
    codeBlock('bash', '# Off: the # at the start makes the line a comment\n# OFFSITE_RETRY_MINUTES=15\n\n# On\nOFFSITE_RETRY_MINUTES=30'),
    p('A long random value, for a password or a passphrase, is made with:'),
    codeBlock('bash', 'openssl rand -hex 32'),
    panel('warning', p(b('.env holds every secret the server has.'), ' Never share it or put it in version control. Keep a copy of the backup passphrases somewhere that is not this machine: without them, the backups cannot be read.')),

    h(2, 'Database'),
    ul(
      setting('POSTGRES_PASSWORD', b('Required.'), ' The password of the database’s owner account. Tesria uses that account only to set the database up, and the backup services use it for backups and restores.'),
      setting('POSTGRES_USER', 'the owner account’s name, and ', c('POSTGRES_DB'), ', the database’s name. The example’s values work. The database is created with them on the first start; changing them later renames nothing.'),
      setting('APP_DB_PASSWORD', 'the password of the restricted account Tesria runs as day to day, which cannot change or delete the audit log. Tesria creates that account itself. Left empty, Tesria runs as the database owner, which is acceptable on a private network and not on the internet. To change it later, change the value and run ', c('docker compose up -d app collab'), '.'),
      setting('APP_DB_USER', 'optional. That account’s name, ', c('tesria_app'), ' unless you set another.'),
    ),

    h(2, 'Address and HTTPS'),
    p('See ', pageLink('HTTPS and domains'), ' for which to choose.'),
    ul(
      setting('DOMAIN', 'the name people reach Tesria by, such as ', c('wiki.example.com'), ', or ', c('localhost'), ' to try it on one computer. A real domain gets a free certificate from Let’s Encrypt; anything else uses a certificate Tesria makes itself. Links in emails use this address too, unless you set ', b('Public address'), ' in ', b('Admin'), ', ', b('Settings'), '.'),
      setting('ACME_EMAIL', 'an email address Let’s Encrypt can write to about your certificate.'),
      setting('CADDYFILE', 'which web server configuration to use. Leave it out on a private network. Set it to ', c('deploy/Caddyfile.public'), ' when the server can be reached from the internet.'),
      setting('PROXY_TRUSTED_NETWORKS', 'only if you put a proxy of your own in front of Tesria: that proxy’s address, such as ', c('10.0.0.5/32'), '. Tesria then believes the visitor addresses it passes on.'),
    ),

    h(2, 'Optional features'),
    ul(
      setting('COLLAB_SHARED_SECRET', 'turns on editing a page with several people at once. Any long random value. Empty, one person edits a page at a time.'),
      setting('PDF_SHARED_SECRET', 'turns on PDF export. Any long random value. Without it, exporting as HTML still works, and asking for a PDF says to print the HTML export instead.'),
    ),

    h(2, 'Single sign-on'),
    p('All optional, and off while ', c('OIDC_AUTHORITY'), ' is empty. ', pageLink('Single sign-on (OIDC)'), ' walks through them.'),
    ul(
      setting('OIDC_AUTHORITY', 'your identity provider’s address.'),
      setting('OIDC_CLIENT_ID', 'the client ID you registered there.'),
      setting('OIDC_CLIENT_SECRET', 'its secret.'),
      setting('OIDC_DISPLAY_NAME', 'the words on the sign-in button, such as ', i('Company SSO'), '.'),
      setting('OIDC_REQUIRE_HTTPS_METADATA', 'leave it out. Set it to ', c('false'), ' only for a test provider on plain HTTP on the same machine.'),
    ),

    h(2, 'Backups on this machine'),
    p('See ', pageLink('How backups work'), '.'),
    ul(
      setting('BACKUP_ENCRYPTION_KEY', b('Required.'), ' Encrypts the physical backups. They cannot be restored without it.'),
      setting('BACKUP_INTERVAL_HOURS', 'how often backups run: 24 in the example.'),
      setting('BACKUP_FULL_EVERY_DAYS', 'a new full physical backup once the newest is this many days old, with smaller ones in between. 7 unless set.'),
      setting('BACKUP_RETENTION_DAYS', 'only the starting point for how long backups are kept, read on the first start. After that, retention is set in the browser (see ', pageLink('Retention'), ') and this is ignored.'),
    ),

    h(2, 'Offsite backups'),
    p('Three places to keep a copy, each optional, each with its own passphrase. ', pageLink('Offsite copies'), ' explains how to set each one up.'),
    p(b('Cloud storage')),
    ul(
      setting('OFFSITE_CLOUD_TYPE', c('b2'), ' for Backblaze B2, or ', c('s3'), ' for any other storage that speaks the S3 protocol. Empty turns the cloud copy off.'),
      setting('OFFSITE_CLOUD_ENDPOINT', 'the storage service’s address, such as ', c('s3.us-west-000.backblazeb2.com'), '.'),
      setting('OFFSITE_CLOUD_BUCKET', 'the bucket to use.'),
      setting('OFFSITE_CLOUD_REGION', 'the bucket’s region.'),
      setting('OFFSITE_CLOUD_PATH', 'a folder inside the bucket, such as ', c('/tesria'), '.'),
      setting('OFFSITE_CLOUD_URI_STYLE', c('host'), ' for Backblaze and AWS, ', c('path'), ' for MinIO and most self-hosted storage. The first thing to change if backups fail with a DNS or 404 error.'),
      setting('OFFSITE_CLOUD_KEY', 'and ', c('OFFSITE_CLOUD_SECRET'), ': the access key and its secret.'),
      setting('OFFSITE_CLOUD_PASSPHRASE', 'encrypts the cloud copy. Different from every other passphrase here.'),
      setting('OFFSITE_CLOUD_RETENTION_FULL', 'how many full backups the cloud keeps. 4 unless set.'),
      setting('OFFSITE_CLOUD_BACKUP_EVERY_DAYS', 'how often a full backup goes to the cloud. 7 unless set. Changes stream there continuously in between.'),
      setting('OFFSITE_CLOUD_BUDGET_GB', 'optional. How much you mean the cloud copy to hold. See ', pageLink('Testing a target and the cloud budget'), '.'),
      setting('OFFSITE_CLOUD_VERIFY_TLS', 'only for testing against storage on this computer. Never set it for a real provider.'),
      setting('OFFSITE_ARCHIVE_QUEUE_MAX', 'how much unsent change history may pile up while the cloud cannot be reached, before it is dropped. 16GiB unless set. Keep it well under the free disk space.'),
    ),
    p(b('Network drive')),
    ul(
      setting('OFFSITE_NAS_PATH', 'a network share, already connected to this computer, to copy to.'),
      setting('OFFSITE_NAS_PASSPHRASE', 'encrypts the copy on the share.'),
    ),
    p(b('Removable drive')),
    ul(
      setting('OFFSITE_REMOVABLE_PATH', 'where the drive appears when it is plugged in.'),
      setting('OFFSITE_REMOVABLE_PASSPHRASE', 'encrypts the copy on the drive.'),
    ),
    p(b('All offsite copies')),
    ul(
      setting('OFFSITE_RETRY_MINUTES', 'how long to wait before trying again when a copy could not reach its target. 15 unless set.'),
      setting('OFFSITE_DRILL_DAYS', 'how often each offsite copy is restored for real, as a test. 30 unless set. See ', pageLink('Restore drills'), '.'),
    ),

    h(2, 'Not in .env'),
    p('The largest upload is set in the web server’s configuration file (', c('deploy/Caddyfile'), ') rather than here: 100 MB for a file added to a page, and 500 MB for importing a wiki pack.'),
  ))

  // ============================================================ HTTPS
  await page('HTTPS and domains', root, doc(
    p(b('HTTPS'), ' is what puts the padlock in the browser’s address bar. Everything between the browser and Tesria is encrypted, so nobody else on the network can read pages or passwords as they pass. Tesria always uses HTTPS; you never have to turn it on.'),
    p('What differs is who vouches for Tesria’s ', b('certificate'), ', the ID card a server shows to prove who it is. That depends on how people reach your Tesria, and this page helps you choose.'),

    h(2, 'Which one are you?'),
    ul(
      li(p(b('Only on your own network.'), ' Tesria runs on a computer at home or in the office, and people open it by that computer’s name, such as ', c('studio.local'), '. Tesria makes its own certificate. It works straight away, and each device warns once until you tell it to trust Tesria. This is how Tesria starts out. See ', b('On your own network'), ' below.')),
      li(p(b('On a domain name you own.'), ' People open it at an address such as ', c('wiki.example.com'), ', from anywhere. The certificate comes free from Let’s Encrypt, is trusted by every browser, and renews itself. See ', b('A real domain'), ' and ', b('On the public internet'), ' below.')),
    ),
    p('With the standard configuration the two go together: a Tesria with a domain name also answers by its own name on the local network. The stricter configuration for the internet, below, serves only the domain.'),

    h(2, 'On your own network'),
    p('Nothing to set: this is what ', c('DOMAIN=localhost'), ' in ', c('.env'), ' does, and it is the default.'),
    p('Tesria answers to whatever name a device uses to reach it. The first time a name is used, it makes a certificate for that name, signed by its own ', b('certificate authority'), '. So phones and other computers can open it with no setup, and it keeps working if the computer’s network address changes.'),
    p('Browsers do not know Tesria’s certificate authority, so each device warns until you tell it to trust it. That takes a few minutes, once per device, and then covers every name: ', pageLink('Trusting the local certificate'), '.'),
    p('Open Tesria by a name rather than a number such as 192.168.1.50: a certificate is issued for a name, so a number keeps warning even on a device that trusts Tesria. ', pageLink('Opening Tesria by name'), ' shows how to find the name and make it reliable.'),

    h(2, 'A real domain'),
    p('You need a domain name you control, and a server the internet can reach.'),
    step(1, 'Point the domain at the server'),
    p('Where you manage the domain, add a DNS record (an A record) for the name, such as ', c('wiki'), ' in ', c('example.com'), ', with the server’s public address.'),
    step(2, 'Open ports 80 and 443'),
    p('The server must be reachable from the internet on both. On a home or office network, that usually means forwarding them to the server in the router’s settings. Let’s Encrypt checks port 80 when it issues the certificate.'),
    step(3, 'Set the address in .env'),
    codeBlock('bash', 'DOMAIN=wiki.example.com\nACME_EMAIL=you@example.com\nCADDYFILE=deploy/Caddyfile.public'),
    p('The third line is the stricter configuration for the internet, explained below. Use your own domain and email address.'),
    step(4, 'Restart and open it'),
    codeBlock('bash', 'docker compose up -d'),
    p('Tesria asks Let’s Encrypt for a certificate, and renews it by itself from then on. Open ', c('https://wiki.example.com'), ': a padlock, and no warning on any device.'),

    h(2, 'On the public internet'),
    p('The standard configuration is made for a private network, where it helps that Tesria makes a certificate for any name. On the internet that becomes a weakness: anyone who connects with a made-up name gets a certificate made for it. So a server the internet can reach uses the stricter configuration, with this line in ', c('.env'), ':'),
    codeBlock('bash', 'CADDYFILE=deploy/Caddyfile.public'),
    p('Then run ', c('docker compose up -d caddy'), '. It differs from the standard one in three ways:'),
    ul(
      li(p('It serves only your domain.')),
      li(p('It no longer offers the local certificate, which nobody outside your network would trust anyway.')),
      li(p('It tells browsers to always use HTTPS for your domain from then on. This is called ', b('HSTS'), '.')),
    ),
    panel('warning', p(b('HSTS cannot be taken back quickly.'), ' Once a browser has seen it, for two years it refuses plain HTTP to your domain and will not let anyone click past a certificate warning, even if you switch the configuration back. Use it only with a real domain and a real certificate, never with ', c('localhost'), '.')),
    p('Before you let the internet in, work through ', pageLink('Security hardening'), '.'),

    h(2, 'Your own proxy in front'),
    p('If Tesria sits behind a reverse proxy of your own rather than directly on the internet, set ', c('PROXY_TRUSTED_NETWORKS'), ' in ', c('.env'), ' to that proxy’s address. Tesria then believes the visitor addresses the proxy passes on, which its sign-in limits depend on. Never publish Tesria’s own port, 8080, to the network: anyone who could reach it could claim to be any address.'),
  ))

  // ====================================================== Trusting the certificate
  const install = top['Installation and operations']
  const trust = await ensure('Trusting the local certificate', install)
  const sh = await attachCurrent(trust, 'trust-ca.sh', readFileSync(join(ROOT, 'deploy/scripts/trust-ca.sh')), 'text/plain')
  const ps1 = await attachCurrent(trust, 'trust-ca.ps1', readFileSync(join(ROOT, 'deploy/scripts/trust-ca.ps1')), 'text/plain')

  await page('Trusting the local certificate', install, doc(
    p('The first time you open Tesria, your browser may stop you with a warning such as ', i('Your connection is not private'), ' or ', i('This Connection Is Not Private'), '. Nothing is wrong with your server. This page explains why it happens and walks you through making it go away for good, one device at a time.'),

    h(2, 'Why the warning appears'),
    p('Every time your browser opens a secure web page, the server shows it a ', b('certificate'), ': a kind of ID card that proves the server is who it says it is. The browser only accepts ID cards issued by a short list of organizations it already trusts, called certificate authorities. Public websites get their certificates from them.'),
    p('A Tesria server on your own network cannot get one of those, because those organizations only issue certificates for addresses on the public internet. So Tesria makes its own. The connection is still fully encrypted; your browser just does not recognize who issued the ID card, so it warns you.'),
    p('The fix is to tell each device, once, that certificates from your Tesria server are to be trusted. After that the warning is gone on that device for good, for every address the server answers on.'),
    panel('info',
      p(b('Does this apply to you?'), ' Only if you open Tesria at a local address, such as ', c('wiki-server.local'), ' or ', c('localhost'), '. If you open it at a real web address, like ', c('wiki.example.com'), ', and see a padlock, your server already has a public certificate and you can skip this page.')),

    h(2, 'Before you start'),
    panel('note',
      p(b('Throughout this page, '), c('your-server'), b(' stands for your server’s address:'), ' whatever you type into the browser to open Tesria, without ', c('https://'), '. For example, if you open Tesria at ', c('https://wiki-server.local'), ', then ', c('http://your-server/trust'), ' means ', c('http://wiki-server.local/trust'), '.')),
    ul(
      li(p(b('About three minutes'), ' per device.')),
      li(p(b('Permission to change the device’s settings.'), ' On a computer that means an administrator account, because the certificate is added for everyone who uses it.')),
      li(p(b('The device on the same network as the server'), ', the same as when you use Tesria.')),
    ),

    h(2, 'The easy way: the Trust this device guide'),
    p('Tesria has a guide built in that asks which device you are on, fills your server’s address into a small script, and tells you exactly what to click. There are three ways to open it:'),
    ul(
      li(p(b('From your profile.'), ' Once you are signed in, open your profile (your initials at the top right) and choose ', b('Set up this device'), ' under ', b('Trust this device'), '.')),
      li(p(b('From the sign-in page.'), ' Choose ', b('Trust this device'), ' under ', i('Did your browser warn that this site is not secure?'), '.')),
      li(p(b('By its address.'), ' Type ', c('http://your-server/trust'), ' into the browser. Note ', c('http'), ', not ', c('https'), ': the guide is served without encryption on purpose, so a device that does not trust the server yet can open it with no warning. This is the easiest way on a phone.')),
    ),
    p('If ', c('http://your-server/trust'), ' does not open, your network or security software may block plain ', c('http'), ' addresses. Open ', c('https://your-server/trust'), ' instead and click past the warning once, as described under ', b('Getting past the warning the first time'), '.'),

    h(3, 'First, open the guide'),
    p('Open it any of the three ways above. It starts by explaining the warning, then has four numbered steps. The steps below have the same numbers, so you can follow along.'),

    step(1, 'Which device are you on?'),
    p('The guide guesses your device from your browser. If it guessed wrong, choose the right one. Everything below changes to match.'),
    ...(await picture(trust, 'trust-device', 'Choosing the device in the guide', 'The highlighted button is the device the steps are for.')),

    step(2, 'What address do you open Tesria at?'),
    p('The guide fills in the address you used to reach it. It has to be the address you normally open Tesria at, because that is the address the script will trust. If you type a numeric address such as 192.168.1.50, the guide explains how to find your computer’s name instead, since certificates are issued for names.'),
    ...(await picture(trust, 'trust-address', 'The address box in the guide', 'Your server’s address, without https://.')),

    step(3, 'Trust the certificate'),
    p('This is the part that does the work, and it differs a little by device. On a phone the guide takes you through the Settings app; see ', b('On a phone'), ' below.'),
    p(b('On a Mac or Linux,'), ' you download a small script with your address already in it and run it with one line, which the guide gives you to copy. It asks for your password, because adding a trusted certificate changes a setting for the whole computer.'),
    p(b('On Windows,'), ' there is no file to download. You copy one line into PowerShell and press Enter. Windows then asks whether to install a certificate from ', i('Caddy Local Authority'), ': that is your server, so choose ', b('Yes'), '.'),
    panel('info', p(b('Why a line and not a script on Windows?'), ' Windows refuses to run script files downloaded from the internet unless you change a security setting, and on a work computer that setting is often locked. A line you paste in yourself is not affected, and it needs no administrator, because it trusts the server for your own Windows account.')),
    panel('success', p(b('Typing your password shows nothing.'), ' In Terminal, and in most command windows, the password you type is hidden completely, not even as dots. That is normal: type it and press Return.')),

    step(4, 'Check it worked'),
    p('Quit your browser completely and open it again. Closing its windows is not always enough, because some browsers keep running in the background: in Chrome or Edge, type ', c('chrome://restart'), ' or ', c('edge://restart'), ' into the address bar; in Safari, press ', b('⌘ Q'), '. Then choose ', b('Open Tesria securely'), ' at the end of the guide. If Tesria opens with no warning and the address bar shows a padlock, you are done on this device.'),
    p('Still says “Not secure”? Check that you opened Tesria by its name, such as ', c('wiki-server.local'), ', and not a number such as 192.168.1.50. A number never matches the certificate, however it is trusted. The guide’s last step lists the other causes.'),

    h(2, 'Getting past the warning the first time'),
    p('To sign in and reach your profile before the device trusts the server, you can tell the browser to go ahead this once. It is safe here because it is your own server on your own network. ', b('Do not do this for websites you do not recognize.')),
    table([
      ['Browser', 'What to choose on the warning page'],
      ['Chrome, Edge, Brave', 'Advanced, then Proceed to your-server (unsafe)'],
      ['Safari', 'Show Details, then visit this website, then Visit Website'],
      ['Firefox', 'Advanced…, then Accept the Risk and Continue'],
    ], [200, 500]),
    p('Some browsers offer no way past, for example on a work computer your IT department manages. Use ', c('http://your-server/trust'), ' instead.'),

    h(2, 'On a phone'),
    p('On an iPhone, iPad or Android phone there is no script: you install the certificate and then switch trust on in the Settings app. Open ', c('http://your-server/trust'), ' on the phone and the guide shows the steps for it. On an iPhone or iPad, use ', b('Safari'), '; other browsers there cannot install certificates.'),
    h(3, 'iPhone and iPad'),
    ol(
      li(p('In Safari, open ', c('http://your-server/trust'), ', choose ', b('iPhone or iPad'), ', and tap ', b('Download the certificate'), '. Tap ', b('Allow'), '. It says a configuration profile was downloaded; that is the certificate.')),
      li(p('Open the ', b('Settings'), ' app. Tap ', b('Profile Downloaded'), ' near the top, then ', b('Install'), ', enter your passcode, and tap ', b('Install'), ' twice more.')),
      li(p('Go to ', b('Settings, General, About, Certificate Trust Settings'), '. It is at the very bottom of About.')),
      li(p('Switch on the certificate named ', i('Caddy Local Authority'), ' and tap ', b('Continue'), '. Without this step the certificate is installed but not trusted, and the warning stays.')),
    ),
    h(3, 'Android'),
    ol(
      li(p('Open ', c('http://your-server/trust'), ', choose ', b('Android'), ', and tap ', b('Download the certificate'), '. It is saved as ', c('tesria-ca.crt'), '.')),
      li(p('Open ', b('Settings'), ' and search for ', b('CA certificate'), '. The menus vary between phone makers; it is usually under Security and privacy, More security settings, Encryption and credentials, Install a certificate.')),
      li(p('Choose ', b('CA certificate'), ', then ', b('Install anyway'), ', and confirm with your screen lock.')),
      li(p('Pick ', c('tesria-ca.crt'), '. Android then says your network may be monitored, as it does for any certificate you add yourself.')),
    ),

    h(2, 'Doing it by hand'),
    p('If you would rather not use the guide, or you are setting up many computers, the same scripts are here. They are the ones the guide gives out, before your address is filled in.'),
    fileBlock(sh, 'player'),
    fileBlock(ps1, 'player'),
    p(c('trust-ca.sh'), ' is for Mac and Linux, and ', c('trust-ca.ps1'), ' for Windows. Each needs your server’s address, and you can give it one of two ways:'),
    ul(
      li(p(b('Edit the script.'), ' Open it in a plain text editor (TextEdit, Notepad or any code editor) and change the one marked line near the top:')),
    ),
    codeBlock('bash', '# Mac and Linux: trust-ca.sh\nTESRIA_ADDRESS="localhost"          # before\nTESRIA_ADDRESS="wiki-server.local"  # after: your address'),
    codeBlock('powershell', '# Windows: trust-ca.ps1\n$TesriaAddress = "localhost"          # before\n$TesriaAddress = "wiki-server.local"  # after: your address'),
    ul(
      li(p(b('Or give the address when you run it'), ', and leave the file alone:')),
    ),
    codeBlock('bash', '# Mac or Linux, in Terminal\nbash ~/Downloads/trust-ca.sh wiki-server.local'),
    codeBlock('powershell', '# Windows, in PowerShell\npowershell -ExecutionPolicy Bypass -File "$env:USERPROFILE\\Downloads\\trust-ca.ps1" wiki-server.local'),
    p('On Windows, ', c('-ExecutionPolicy Bypass'), ' is what lets the script run: Windows blocks script files by default, with the message ', i('running scripts is disabled on this system'), '. This allows the one script, once, and changes no setting. If your employer has locked it, scripts cannot run at all; use the single line instead, which is what the guide gives you:'),
    codeBlock('powershell', '$c = "$env:TEMP\\tesria-ca.crt"; Invoke-WebRequest -UseBasicParsing -Uri "http://wiki-server.local/ca.crt" -OutFile $c; Import-Certificate -FilePath $c -CertStoreLocation Cert:\\CurrentUser\\Root'),
    p('That trusts the server for your Windows account. The script trusts it for everyone who uses the computer, which is why the script needs an administrator and the line does not.'),
    p('Either way, the script downloads the certificate from ', c('http://your-server/ca.crt'), ', shows its fingerprint, adds it to the device’s trusted list, and checks that ', c('https://your-server'), ' now opens without a warning.'),
    h(3, 'Just the certificate'),
    p('Some tools and devices want the certificate file itself. It is always at ', c('http://your-server/ca.crt'), ' and downloads as ', c('tesria-ca.crt'), '.'),

    h(2, 'Firefox'),
    p('Firefox keeps its own list of trusted certificates, separate from your computer’s, so it needs one more step after the others:'),
    ol(
      li(p('Download the certificate from ', c('http://your-server/ca.crt'), '.')),
      li(p('In Firefox, open ', b('Settings'), ', then ', b('Privacy & Security'), ', scroll down to ', b('Certificates'), ', and choose ', b('View Certificates…'), '.')),
      li(p('On the ', b('Authorities'), ' tab, choose ', b('Import…'), ', pick ', c('tesria-ca.crt'), ', tick ', b('Trust this CA to identify websites'), ', and choose ', b('OK'), '.')),
    ),

    h(2, 'If the warning comes back'),
    ul(
      li(p(b('Quit the browser completely'), ' and open it again. Browsers remember the old answer until they restart.')),
      li(p(b('Check the address.'), ' A device trusts the server, but a browser only stops warning when it reaches it by a name. Opening it by number, such as 192.168.1.50, can still warn.')),
      li(p(b('Was the server reinstalled or moved?'), ' If its certificate storage was deleted, for example with ', c('docker compose down -v'), ' or a move to a new machine without it, the server made a new certificate. Every device needs these steps again. Ordinary restarts and upgrades never do this.')),
    ),
  ))

  // ============================================== Opening Tesria by name
  // The owner's home network, 2026-09-23: a .local name took several tries
  // to open. Words only: routers and system files cannot be photographed
  // usefully, and every router's screens differ.
  await ensure('Opening Tesria by name', install)
  await page('Opening Tesria by name', install, doc(
    p('Once Tesria is running on a computer in your home or office, other devices reach it by that computer’s ', b('name'), ', such as ', c('studio.local'), ', or by its ', b('address'), ', a number such as 192.168.1.50. This page explains why the name is the one to use, how to find it, and what to do if the name sometimes takes a few tries to open.'),

    h(2, 'Why a name and not a number'),
    ul(
      li(p(b('The secure connection needs it.'), ' Tesria’s certificate is issued for a name. Opened by its number, the browser keeps saying “Not secure”, even on a device that trusts the server. See ', pageLink('Trusting the local certificate'), '.')),
      li(p(b('Numbers change.'), ' Your router hands out addresses and can give the computer a different one after a restart. The name stays the same.')),
    ),

    h(2, 'Finding the server’s name'),
    p('On the computer that runs Tesria:'),
    ul(
      li(p(b('Mac:'), ' System Settings, General, Sharing. The name is under ', b('Local hostname'), ', for example ', c('studio.local'), '.')),
      li(p(b('Windows:'), ' Settings, System, About. Take the ', b('Device name'), ' and add ', c('.local'), ', for example ', c('office-pc.local'), '.')),
      li(p(b('Linux:'), ' run ', c('hostname'), ' in a terminal and add ', c('.local'), '.')),
    ),
    p('Then open ', c('https://that-name'), ' on another device. Tesria makes a certificate for whichever name it is opened by, so a device that already trusts the server needs nothing more.'),

    h(2, 'Why it sometimes takes a few tries'),
    p('A name ending in ', c('.local'), ' is not looked up in your router’s list of names. Instead the device calls out to the whole network, “who is studio?”, and waits for that computer to answer. It works without any setup, which is why every Mac and most other computers use it, but it is not always quick:'),
    ul(
      li(p(b('Wi-Fi drops some of these calls.'), ' Many routers treat messages sent to everyone as low priority, and some filter them.')),
      li(p(b('Windows asks your router first.'), ' It waits for the router to say it has never heard of the name, and only then calls out on the network, so the first try can time out.')),
      li(p(b('A sleeping computer answers late.'), ' A laptop that has dimmed its Wi-Fi to save power may miss the call entirely.')),
    ),
    p('So the name might fail once or twice and then work. None of this is Tesria: it is how the name is found. The fixes below make it instant, from the easiest to the most thorough.'),

    h(2, 'Making it reliable'),
    h(3, 'Keep the server awake, and wired if you can'),
    p('Stop the server computer from sleeping (on a Mac, System Settings, Energy, ', b('Prevent automatic sleeping when the display is off'), '), and plug it into the router with a network cable if you can. A wired, awake computer answers every call at once.'),

    h(3, 'Check your router’s settings'),
    p('Two settings on most routers affect those network-wide calls. Their names and places differ from router to router; on TP-Link Archer routers they are under ', b('Advanced'), '.'),
    ul(
      li(p(b('AP isolation'), ' (sometimes “client isolation”) stops devices on Wi-Fi from talking to each other at all. Make sure it is ', b('off'), ' for the network your devices use. It is often on for guest networks, which is one reason Tesria cannot be reached from a guest network.')),
      li(p(b('IGMP snooping'), ' (usually under Network, IPTV or Multicast) decides which devices hear messages sent to everyone. If names are unreliable, try switching it the other way, test for a day, and keep whichever works better.')),
    ),

    h(3, 'Give the server a fixed address'),
    p('Ask the router to always give the server computer the same number. The setting is usually called ', b('Address Reservation'), ' or ', b('DHCP reservation'), '; on a TP-Link Archer it is under Advanced, Network, DHCP Server. Choose the server computer from the list of connected devices and save. This does not change how the name is found, but it is needed for the next two fixes, which point a name at a number.'),

    h(3, 'Write the name into a computer’s own list'),
    p('Every computer keeps a small list of names and numbers, called the ', b('hosts file'), ', which it checks before anything else. A line in it makes the name open instantly on that computer. Phones and tablets do not let you change theirs.'),
    p(b('On Windows:')),
    ol(
      li(p('Press the Windows key, type ', i('Notepad'), ', right-click it and choose ', b('Run as administrator'), '.')),
      li(p('Choose ', b('File, Open'), ', go to ', c('C:\\Windows\\System32\\drivers\\etc'), ', change the file type from Text Documents to ', b('All Files'), ', and open ', c('hosts'), '.')),
      li(p('Add a line at the end with the server’s fixed address and its name, then save:')),
    ),
    codeBlock('text', '192.168.1.50   studio.local'),
    p(b('On a Mac or Linux:'), ' open Terminal and run ', c('sudo nano /etc/hosts'), '. Type your password, add the same line at the end, then press Control-O, Return and Control-X to save and close.'),
    panel('warning', p(b('Keep it up to date.'), ' If the server’s address ever changes, this line points at the wrong computer and the name stops working on that device. That is why the fixed address above comes first.')),

    h(3, 'Run a name server for your whole network'),
    p('The most thorough fix, and the one that also works on phones: a small program on an always-on device, such as a Raspberry Pi, that answers name lookups for everyone. ', b('Pi-hole'), ' and ', b('AdGuard Home'), ' are free and popular, and both block ads as well.'),
    ol(
      li(p('Install one of them and give its device a fixed address.')),
      li(p('Add a local name for the server, pointing at the server’s fixed address. Use a name ending in ', c('.home.arpa'), ', such as ', c('wiki.home.arpa'), ': that ending is set aside for home networks. Avoid ', c('.local'), ', which belongs to the network-wide calls above and confuses both methods.')),
      li(p('In the router’s DHCP settings, set the DNS server to the address of the Pi-hole or AdGuard device, so every device on the network uses it.')),
      li(p('Open ', c('https://wiki.home.arpa'), '. Tesria makes a certificate for the new name, and devices that trust the server trust it for this name too.')),
    ),

    h(2, 'Once you have settled on a name'),
    ul(
      li(p('Put it in ', b('Admin, Settings, Public address'), ', such as ', c('https://wiki.home.arpa'), '. Links in emails use it, and the ', b('Trust this device'), ' guide suggests it to anyone who opens Tesria by number.')),
      li(p('Tell everyone the new address, and bookmark it on each device.')),
    ),
  ))

  // ============================================================ Single sign-on
  await page('Single sign-on (OIDC)', root, doc(
    p(b('Single sign-on'), ' (SSO) lets people sign in to Tesria with an account they already have somewhere else, such as their work account, instead of a separate Tesria password. There is one password less to remember, and when someone’s work account is closed, they can no longer sign in with it here either.'),
    p('Tesria works with any provider that speaks ', b('OpenID Connect'), ' (OIDC), the standard most of them use: Keycloak, Authentik, Okta, Microsoft Entra ID and Google among them. It sits alongside ordinary Tesria accounts, so nobody has to switch.'),

    h(2, 'Before you start'),
    ul(
      li(p(b('Administrator access to your provider,'), ' or someone who has it, to register Tesria there.')),
      li(p(b('The address people open Tesria at,'), ' because the provider sends people back to it after they sign in.')),
      li(p(b('Access to the server,'), ' to add the settings to ', c('.env'), '.')),
    ),
    yourServer(),

    step(1, 'Register Tesria with your provider'),
    p('In your provider’s admin console, add a new application (some call it a client). Choose the ', b('web'), ' or ', b('confidential'), ' type, and give it this redirect address:'),
    codeBlock('text', 'https://your-server/signin-oidc'),
    p('The provider then gives you three things to copy: its own address (called the authority or issuer), a ', b('client ID'), ' and a ', b('client secret'), '.'),

    step(2, 'Add them to .env'),
    codeBlock('bash', 'OIDC_AUTHORITY=https://idp.example.com/realms/company\nOIDC_CLIENT_ID=tesria\nOIDC_CLIENT_SECRET=the-secret-from-the-provider\nOIDC_DISPLAY_NAME=Company SSO'),
    p(c('OIDC_DISPLAY_NAME'), ' is what the sign-in button says, here ', i('Sign in with Company SSO'), '. Without it, the button says ', i('Sign in with Single sign-on'), '.'),

    step(3, 'Restart Tesria'),
    codeBlock('bash', 'docker compose up -d app'),

    step(4, 'Try it'),
    p('Sign out and open the sign-in page: it now has the ', b('Sign in with'), ' button. Choose it, sign in at your provider, and you come back to Tesria signed in. If something is wrong, you land back on the sign-in page with the reason.'),

    h(2, 'Which account someone gets'),
    ul(
      li(p(b('Someone who has signed in with SSO before'), ' gets the same account every time, even if their email address changes at the provider.')),
      li(p(b('Someone who already has a Tesria account'), ' with the same email address is connected to it, but only if the provider says it has confirmed that address. Otherwise anyone who could claim the address at the provider could take over the account.')),
      li(p(b('Someone new'), ' gets a new account with no Tesria password, as long as ', b('Allow public registration'), ' is on (', b('Admin'), ', ', b('Settings'), ', ', b('Access'), '). When it is off, they are turned away: send them an invite, let them create their account from it, and from then on SSO signs them in to that account. See ', pageLink('Invites'), '.')),
    ),
    panel('note', p(b('Two-factor sign-in is the provider’s job'), ' for people who use SSO. Tesria’s own two-factor applies when someone signs in with a Tesria password.')),
    p('Testing with a provider on plain HTTP on the same machine? Add ', c('OIDC_REQUIRE_HTTPS_METADATA=false'), '. Never leave it that way for a real provider.'),
  ))

  // ============================================================ Email
  const email = await ensure('Email (SMTP)', root)

  // Providers for people with no mail server of their own (the owner,
  // 2026-09-24). Checked against the providers' own pages that day: Google's
  // app passwords (support.google.com/mail/answer/185833), Microsoft's
  // Outlook.com modern-authentication notice, and the Exchange Online SMTP
  // AUTH timeline (off by default at the end of December 2026). Tesria signs
  // in with a username and password only, so an OAuth-only provider cannot
  // be used; say so rather than leave someone stuck.
  await page('Sending with Gmail', email, doc(
    p('No mail server of your own? A Gmail account can send Tesria’s email. It suits a small team: a handful of password resets, invitations and notifications a day. Setting it up takes about five minutes.'),
    p('Gmail will not let a program like Tesria sign in with your everyday password. Instead you make an ', b('app password'), ': a separate, 16-letter password that works only for the program you give it to. If it ever leaks, you delete it, and your real password and the rest of your account stay safe.'),
    panel('success', p(b('Use an account made for the wiki.'), ' A new Gmail address such as ', c('ourteam.wiki@gmail.com'), ' keeps the wiki’s email out of your own inbox and sent folder, and means nobody’s personal account is tied to it. The emails people receive come from this address.')),

    h(2, 'Before you start'),
    ul(
      li(p(b('A Gmail account'), ' you can sign in to.')),
      li(p(b('2-Step Verification turned on'), ' for that account. Google only offers app passwords when it is. If it is off, turn it on at ', c('myaccount.google.com/security'), ', under ', b('How you sign in to Google'), '.')),
    ),
    panel('note', p(b('A work or school Google account'), ' (Google Workspace) may not offer app passwords, because the organization’s administrator decides. If you cannot find the option, ask them, or use a personal Gmail account made for the wiki.')),

    step(1, 'Make an app password'),
    ol(
      li(p('Go to ', c('myaccount.google.com/apppasswords'), ' and sign in to the Gmail account the wiki will send from.')),
      li(p('Type a name you will recognize later, such as ', c('Tesria'), ', and choose ', b('Create'), '.')),
      li(p('Google shows the password once, as four groups of four letters. Copy it now; you cannot see it again. If you lose it, delete it and make another.')),
    ),

    step(2, 'Fill in Tesria’s email settings'),
    p('Choose ', b('Admin'), ', then ', b('Settings'), ', and fill in the ', b('Email'), ' section with these values:'),
    table([
      ['Setting', 'Value'],
      ['SMTP host', p(c('smtp.gmail.com'))],
      ['Port', p(c('587'))],
      ['Username', 'The full Gmail address'],
      ['Password', 'The app password, without the spaces'],
      ['From address', 'The same Gmail address'],
      ['Encryption', 'STARTTLS'],
    ], [140, 300]),
    p('Then choose ', b('Save mail settings'), '.'),

    step(3, 'Turn it on and send a test'),
    p('Turn on ', b('Send email'), ' at the top of the section, then choose ', b('Send test email to me'), '. The test goes to the email address on your own Tesria account. Check that it arrived, including in the spam folder.'),
    p('The full walk-through of these settings, with a picture, is on ', pageLink('Email (SMTP)'), '.'),

    h(2, 'If the test fails'),
    ul(
      li(p(b('“Username and Password not accepted”'), ' means Gmail refused the sign-in. Check that the username is the whole address, and paste the app password again. Your everyday Gmail password does not work here.')),
      li(p(b('The email arrives from a different address'), ' than the one you typed as the From address. Gmail sends only as the account you signed in with, so use that address.')),
      li(p(b('It worked, then stopped.'), ' Changing the Gmail account’s password deletes all of its app passwords. Make a new one and enter it in the ', b('Password'), ' box.')),
    ),

    h(2, 'Good to know'),
    ul(
      li(p(b('Gmail limits how much one account sends in a day.'), ' A small team never reaches it. If your wiki has hundreds of people who all want email notifications, use a sending service instead, such as Amazon SES, Postmark or Mailgun.')),
      li(p(b('You can switch off the app password at any time'), ' from the same page where you made it. Tesria stops sending until you give it a new one.')),
    ),
  ))

  await page('Sending with Outlook or Microsoft 365', email, doc(
    p('Whether Microsoft can send Tesria’s email depends on which kind of Microsoft account you have. Microsoft is moving every program that sends email away from signing in with a password, toward a sign-in method called OAuth, and Tesria signs in to a mail server with a username and password. This page tells you where that leaves each kind of account, so you do not spend an evening on settings that cannot work.'),

    h(2, 'Which account do you have?'),
    ul(
      li(p(b('A personal account'), ': an address ending in ', c('@outlook.com'), ', ', c('@hotmail.com'), ', ', c('@live.com'), ' or ', c('@msn.com'), ', which you signed up for yourself.')),
      li(p(b('A work or school account'), ' (Microsoft 365): an address at your organization’s own domain, such as ', c('you@example.com'), ', which your IT department gave you.')),
    ),

    h(2, 'A personal Outlook.com account'),
    p(b('This does not work with Tesria.'), ' Since September 2024, Microsoft only lets programs send through personal Outlook.com, Hotmail and Live accounts after an OAuth sign-in, and no longer accepts a password, not even an app password. Tesria cannot do an OAuth sign-in to a mail server.'),
    p('What to do instead:'),
    ul(
      li(p(b('Use a Gmail account made for the wiki.'), ' It takes about five minutes. See ', pageLink('Sending with Gmail'), '.')),
      li(p(b('Use a sending service'), ' such as Amazon SES, Postmark or Mailgun. They are made for programs that send email and have free or low-cost plans for a small team.')),
    ),

    h(2, 'A Microsoft 365 work or school account'),
    p('This works today, as long as your organization’s Microsoft 365 administrator allows it. The setting they need is called ', b('Authenticated SMTP'), ' (SMTP AUTH), and it is turned off in many organizations. Ask your IT department to turn it on for the mailbox Tesria will send from, and tell them it is for an internal wiki that sends password resets and notifications.'),
    panel('warning', p(b('Microsoft is phasing this out.'), ' At the end of December 2026, Microsoft turns password sign-in for sending off by default in existing organizations. Your administrator can turn it back on for now. Organizations created from 2027 cannot use it at all, and Microsoft plans to remove it completely later. For a setup that keeps working, use a sending service or Gmail.')),

    step(1, 'Ask for a mailbox to send from'),
    p('A mailbox made for the wiki, such as ', c('wiki@example.com'), ', is best, so nobody’s personal mailbox is tied to it. It needs to be an ordinary mailbox that can sign in with a password; a Microsoft 365 “shared mailbox” has no password of its own. You need its address and password, and Authenticated SMTP turned on for it.'),

    step(2, 'Fill in Tesria’s email settings'),
    p('Choose ', b('Admin'), ', then ', b('Settings'), ', and fill in the ', b('Email'), ' section with these values:'),
    table([
      ['Setting', 'Value'],
      ['SMTP host', p(c('smtp.office365.com'))],
      ['Port', p(c('587'))],
      ['Username', 'The mailbox’s full address'],
      ['Password', 'The mailbox’s password'],
      ['From address', 'The same address'],
      ['Encryption', 'STARTTLS'],
    ], [140, 300]),
    p('Then choose ', b('Save mail settings'), '.'),

    step(3, 'Turn it on and send a test'),
    p('Turn on ', b('Send email'), ' at the top of the section, then choose ', b('Send test email to me'), '. Check that the email arrived, including in the junk folder. The full walk-through is on ', pageLink('Email (SMTP)'), '.'),

    h(2, 'If the test fails'),
    ul(
      li(p(b('“Authentication unsuccessful”'), ' that mentions ', c('SmtpClientAuthentication'), ' means Authenticated SMTP is still off, for the mailbox or for the whole organization. Your administrator needs to turn it on.')),
      li(p(b('“Authentication unsuccessful”'), ' on its own usually means a wrong password, or that the mailbox needs a second sign-in step (such as a code on a phone). Tesria cannot answer that step. Ask your administrator whether a mailbox without it can be used for sending, or use a sending service instead.')),
    ),
  ))
  await page('Email (SMTP)', root, doc(
    p('Tesria sends a few kinds of email: password reset links, invitations, security alerts to administrators, and notifications to people who ask for them. To send them it needs an ', b('SMTP server'), ', the kind of mail server programs send through. Your organization’s mail server works, and so do sending services such as Amazon SES, Postmark or Mailgun.'),
    p('Email is optional. Without it, an administrator resets a forgotten password by giving the person a one-time link (see ', pageLink('Resetting a password'), '), and alerts wait in the bell for an administrator to see.'),
    panel('info', p(b('No mail server of your own?'), ' A Gmail account made for the wiki works well for a small team: see ', pageLink('Sending with Gmail'), '. For Microsoft accounts, see ', pageLink('Sending with Outlook or Microsoft 365'), ' first, because a personal Outlook.com account cannot be used.')),

    h(2, 'Before you start'),
    p('Have these from your email provider or IT department. They are usually on a help page about “SMTP” or “sending from an app”:'),
    ul(
      li(p(b('The SMTP host and port,'), ' such as ', c('smtp.example.com'), ' and 587.')),
      li(p(b('A username and password'), ' for sending.')),
      li(p(b('The address to send from,'), ' such as ', c('wiki@example.com'), '. Most services only allow an address on a domain you have verified with them.')),
    ),

    step(1, 'Open the email settings'),
    p('Choose ', b('Admin'), ' at the top of any page, then the ', b('Settings'), ' tab. The ', b('Email'), ' section has everything, and the steps below use the boxed controls in order.'),
    ...(await picture(email, 'email-steps', 'The Email settings, filled in with example values', 'Send email, Save mail settings and Send test email to me are boxed.')),

    step(2, 'Fill in the mail server'),
    ul(
      li(p(b('SMTP host'), ' and ', b('Port'), ': your mail server. Port 587 with STARTTLS is the usual choice; port 465 goes with SSL on connect.')),
      li(p(b('Username'), ' and ', b('Password'), ': the account Tesria signs in to the mail server with. The password is stored encrypted and never shown again; to keep it when you change something else, leave the field empty.')),
      li(p(b('From address'), ': who the email appears to come from.')),
      li(p(b('Encryption'), ': STARTTLS, SSL on connect, or None. Use None only for a mail server on the same private network.')),
    ),

    step(3, 'Choose Save mail settings'),

    step(4, 'Turn on Send email'),
    p('The switch at the top of the section. It takes effect straight away. While it is off, Tesria does not try to send anything.'),

    step(5, 'Send yourself a test'),
    p('Choose ', b('Send test email to me'), '. Tesria answers straight away: ', i('Sent: check your inbox'), ', or ', i('Not sent'), ' with the mail server’s reason. Then check that the message arrived, including in your spam folder.'),

    h(2, 'If the test fails'),
    ul(
      li(p(b('Check the port and encryption together.'), ' 587 goes with STARTTLS and 465 with SSL on connect; a mismatch usually fails without a clear reason.')),
      li(p(b('Check the username and password.'), ' Many providers want a password made for apps rather than your everyday one.')),
      li(p(b('Check the from address.'), ' A sending service refuses an address on a domain you have not verified with it.')),
    ),
    p('The setup wizard’s Email step fills in the same settings. Changing them needs the ', b('Change the email server'), ' right, which administrators have unless the owner takes it away (see ', pageLink('Roles'), ').'),
  ))

  // ============================================================ Upgrading
  await page('Upgrading', root, doc(
    p('An ', b('upgrade'), ' swaps Tesria’s software for a newer version and keeps everything you have written. Tesria updates its own database when the new version starts, so there is nothing to convert by hand. It takes a few minutes, most of them while the old version keeps running.'),

    h(2, 'Before you start'),
    ul(
      li(p(b('Read the '), pageLink('Release notes'), b(' for every version between yours and the new one.'), ' They say what changed and anything you need to do.')),
      li(p(b('Pick a quiet time.'), ' Tesria is unavailable for about a minute, and people with a page open may need to reload it.')),
    ),

    step(1, 'Take a backup'),
    p('Choose ', b('Admin'), ', then ', b('Backups'), ', then ', b('Back up now'), ', and wait until the page says the backups have finished. If the upgrade goes wrong, this is what you go back to. See ', pageLink('Backups and recovery'), '.'),

    step(2, 'Get the new version'),
    p('If you installed Tesria with ', c('git clone'), ', as ', pageLink('Quick start'), ' does, run this in the Tesria folder:'),
    codeBlock('bash', 'git pull'),

    step(3, 'Rebuild and restart'),
    codeBlock('bash', 'docker compose up -d --build'),
    p('Building takes a few minutes while the old version keeps running. Then each container is replaced, Tesria updates its database, and it is back.'),

    step(4, 'Check it'),
    p('Open ', c('/api/health'), ' on your Tesria’s address. It names the version now running. ', c('docker compose ps'), ' should show every service ', c('Up'), '.'),

    h(2, 'If something goes wrong'),
    p('Tesria’s log usually says why:'),
    codeBlock('bash', 'docker compose logs app'),
    p('Going back means restoring the backup you took in step 1 with the version you had before. A backup made by a newer Tesria cannot be restored into an older one: it is refused, because the older version would not understand the updated database. See ', pageLink('Restoring and undo'), '.'),
  ))

  // ============================================================ Health
  await page('Health checks and monitoring', root, doc(
    p('A wiki nobody can open on Monday morning is a bad surprise. This page covers the quick ways to check that Tesria is well, and how to hear about it automatically when it is not, so you find out before the people who use it do.'),
    yourServer(),

    h(2, 'Is it running?'),
    p('Open ', c('https://your-server/api/health'), ' in a browser. It needs no sign-in, and answers like this:'),
    codeBlock('json', '{"status":"ok","service":"tesria-api","version":"0.2.0","utc":"2026-09-23T05:26:02Z","maintenance":null}'),
    ul(
      li(p(c('status'), ': an answer at all means Tesria is running and taking requests.')),
      li(p(c('version'), ': which version of Tesria is running.')),
      li(p(c('maintenance'), ': empty, except while a backup is being restored, when the wiki can be read but not changed.')),
    ),
    p('On the server itself, the same check from a terminal:'),
    codeBlock('bash', 'curl -k https://localhost/api/health'),
    panel('success', p(b('Let a monitor do the checking.'), ' Point any uptime monitoring service at ', c('https://your-server/api/health'), ' and it tells you by email or phone when Tesria stops answering.')),

    h(2, 'The containers'),
    codeBlock('bash', 'docker compose ps'),
    p('Every service should say ', c('Up'), '. Four of them also check themselves, and add ', c('(healthy)'), ' or ', c('(unhealthy)'), ':'),
    ul(
      li(p(c('db'), ': the database answers.')),
      li(p(c('app'), ': Tesria answers.')),
      li(p(c('backup'), ' and ', c('pgbackrest'), ': the backup services have checked in within the last five minutes. They check in every minute, even in the middle of a long backup.')),
    ),
    p('To see why a service is unhappy, read its log, for example ', c('docker compose logs backup'), '.'),

    h(2, 'Inside Tesria'),
    p('Under ', b('Admin'), ':'),
    ul(
      li(p(b('Dashboard'), ': people, content, usage and health at a glance.')),
      li(p(b('Backups'), ': whether each backup is working, when it last ran, and how much disk is left.')),
      li(p(b('Security'), ': alerts, such as repeated failed sign-ins, a failed backup or a disk running low. Every administrator gets them in the bell, and by email once ', pageLink('Email (SMTP)', 'email'), ' is set up.')),
    ),

    h(2, 'The audit log'),
    p('Every change an administrator makes is recorded in the ', b('audit log'), '. Each entry is chained to the one before it, so an entry changed or deleted afterwards is detected. Tesria checks the chain every day, and ', b('Verify now'), ' under ', b('Admin'), ', ', b('Security'), ', ', b('Audit log integrity'), ' checks it on demand. See ', pageLink('Audit'), '.'),
    p('To check it from outside Tesria, for example on a schedule with cron, use the script in the Tesria folder with an administrator’s API token (see ', pageLink('API tokens'), '):'),
    codeBlock('bash', 'scripts/verify-audit-chain.sh https://your-server "$TESRIA_ADMIN_TOKEN"'),
    p('It ends with exit status 0 when the chain holds and 1 when it is broken, which is what a scheduler or monitor looks at. Every audit entry is also written to Tesria’s log (', c('docker compose logs app'), '); sending that log to another machine keeps a copy that even someone with the database cannot touch.'),
  ))

  // ============================================================ Backups
  const backups = await ensure('Backups and recovery', root)
  await page('Backups and recovery', root, doc(
    p('A ', b('backup'), ' is a copy of the wiki from which it can be rebuilt: if a disk fails, a change goes badly wrong, or the computer is lost, the backup is how you get your pages back. Tesria starts backing itself up the moment it is installed, with nothing to set up. Every day it copies the database and every attachment, and it keeps a running record of every change in between, so you can go back to last night, or to the minute before something went wrong.'),

    h(2, 'Where to see them'),
    p('Choose ', b('Admin'), ' at the top of any page, then the ', b('Backups'), ' tab. Each backup service has a card saying whether it is healthy and when it last worked. Above them, ', b('Back up now'), ' takes a backup straight away, and each card’s ', b('Test restore of newest'), ' proves its newest backup can be restored, without touching the wiki.'),
    ...(await picture(backups, 'backup-now', 'The top of the Backups tab', 'Back up now, and a backup service’s card with Test restore of newest.')),

    h(2, 'The one thing to do yourself'),
    p('Every backup Tesria takes on its own is on the same computer as the wiki, so a dead disk, a fire or a theft takes both. Two things protect against that, and only you can do them:'),
    ul(
      li(p(b('Set up an offsite copy,'), ' in the cloud, on a network drive, or on a removable drive. See ', pageLink('Offsite copies'), '.')),
      li(p(b('Keep the passphrases from .env somewhere else,'), ' such as a password manager. A copy whose passphrase is lost cannot be read by anyone.')),
    ),

    h(2, 'You may not need a backup at all'),
    p('For everyday mistakes, Tesria has safety nets of its own that put things right in seconds, without touching anyone else’s work: every page keeps its ', pageLink('History and restoring', 'history'), ', and deleted pages go to the space’s ', pageLink('Trash', 'trash'), '. A backup is for when the whole wiki needs to go back.'),

    h(2, 'In this section'),
    live('children', { depth: '1', sort: 'position' }),
  ))

  await page('How backups work', backups, doc(
    p('Two backup services run beside Tesria. Each keeps its own kind of copy, for a different kind of trouble, and both report to the ', b('Backups'), ' tab under ', b('Admin'), '.'),

    h(2, 'Database dumps and uploads'),
    p('The ', c('backup'), ' service. Once every 24 hours (', c('BACKUP_INTERVAL_HOURS'), '), it takes a ', b('dump'), ' of the database, a complete copy in one file, and an archive of every attachment, both at the same moment.'),
    ul(
      li(p(b('What it gets back:'), ' the wiki as it was when that copy was taken.')),
      li(p(b('Where:'), ' the ', c('backups'), ' volume, as ', c('db-<time>.dump'), ' and ', c('uploads-<time>.tar.gz'), '.')),
      li(p(b('Good for:'), ' going back to a known day, moving Tesria to a new machine, and offsite copies. A dump can be read on any computer.')),
    ),

    h(2, 'Physical backups and point-in-time recovery'),
    p('The ', c('pgbackrest'), ' service. It takes a full copy of the database’s files once a week (', c('BACKUP_FULL_EVERY_DAYS'), ') and a smaller one holding only the changes on the other days, and in between it records every change the database makes, as it happens.'),
    ul(
      li(p(b('What it gets back:'), ' the database as it was at any moment you choose, to the second.')),
      li(p(b('Where:'), ' the ', c('pgbackrest'), ' volume, always encrypted with ', c('BACKUP_ENCRYPTION_KEY'), '.')),
      li(p(b('Good for:'), ' undoing something that happened at a known time, such as a mass deletion at 2:32 this afternoon.')),
    ),
    p('It holds the database only. Attachments come from the dumps and uploads backups.'),

    h(2, 'The Backups tab'),
    ul(
      li(p(b('A card for each service:'), ' whether it is healthy, when it last worked and next runs, how far back you can restore, the disk space left, and when a restore was last tested.')),
      li(p(b('Back up now'), ' asks both services for a backup. Each picks the request up within a minute.')),
      li(p(b('Test restore'), ' restores a backup somewhere temporary and checks it, without touching the wiki. A backup that has never been restored has not been proved to work.')),
      li(p(b('Disk space'), ' shows what the wiki, its backups and everything else take on this computer.')),
      li(p(b('Storage targets'), ' lists the offsite copies. See ', pageLink('Offsite copies'), '.')),
      li(p(b('Retention policy'), ' decides how long backups are kept. See ', pageLink('Retention'), '.')),
      li(p(b('Backups'), ' lists every backup kept, each with ', b('Test restore'), ' and, for the owner, ', b('Restore'), '. See ', pageLink('Restoring and undo'), '.')),
      li(p(b('Recent runs'), ' lists what the services did, each with its ', b('Log'), '.')),
    ),
    p('What each person may do on the tab depends on their role: see ', pageLink('Backups (administration)'), '.'),

    h(2, 'When something goes wrong'),
    p('A backup that fails, a service that stops checking in, a failed restore test or a disk running low raises an alert for every administrator: in the bell, under ', b('Admin'), ', ', b('Security'), ', and by email once email is set up. A failed backup is tried again after 15 minutes, then at longer and longer gaps up to six hours, rather than waiting a whole day.'),

    h(2, 'From the command line'),
    p('Everything the tab does can also be done on the server, from the Tesria folder, which helps when the wiki itself will not start:'),
    table([
      ['Command', 'What it does'],
      [p(c('docker compose exec backup /scripts/backup.sh')), 'Takes a database dump now'],
      [p(c('docker compose exec backup /scripts/backup-files.sh')), 'Takes an attachments archive now'],
      [p(c('docker compose exec backup /scripts/verify-backup.sh')), 'Test-restores the newest dump'],
      [p(c('docker compose exec backup ls /backups')), 'Lists the dumps and archives'],
    ], [420, 280]),
    panel('warning', p(b('Every backup on this page is on the same computer as the wiki.'), ' Set up at least one ', pageLink('Offsite copies', 'offsite copy'), '.')),
  ))

  const retention = await ensure('Retention', backups)
  await page('Retention', backups, doc(
    p('Backups take disk space, and a disk that fills up stops the backups. The ', b('retention policy'), ' decides how long backups are kept, so old ones are removed before that happens. It applies to both kinds of backup, and to nothing else: pages, history and the trash are never touched by it.'),

    h(2, 'The two choices'),
    ul(
      li(p(b('Keep every backup forever.'), ' Nothing is ever removed. Keep an eye on the disk space on the Backups tab.')),
      li(p(b('Prune old backups:'), ' keep the newest ', i('N'), ' backups and everything from the last ', i('D'), ' days.')),
    ),
    p('With pruning, a backup is removed only when it is outside ', b('both'), ': older than ', i('D'), ' days, and not among the newest ', i('N'), '. For example, with 7 backups and 14 days, a backup from a month ago is removed, unless it is one of the last 7. So even if backups stopped for a while, the last 7 are always there, however old.'),

    h(2, 'Changing it'),
    step(1, 'Open the retention policy'),
    p('Choose ', b('Admin'), ', then ', b('Backups'), ', and scroll to ', b('Retention policy'), '.'),
    ...(await picture(retention, 'retention-review', 'The retention policy', 'The rule, and Review change.')),
    step(2, 'Choose the policy'),
    p('Pick one of the two choices, and with ', b('Prune old backups'), ', the number of backups and of days.'),
    step(3, 'Review the change'),
    p('Choose ', b('Review change'), '. Before anything happens, Tesria lists exactly which backups the new policy would remove today, and how far back you could still restore. If it looks right, choose ', b('Save policy'), '. Unless you signed in in the last few minutes, Tesria asks for your password first.'),

    h(2, 'Why some changes wait a day'),
    ul(
      li(p(b('A change that keeps more'), ' takes effect at once.')),
      li(p(b('A change that could remove more waits 24 hours'), ', and every administrator is alerted when it is saved. That is time for someone to notice if it was a mistake, or was not made by you. The service cards say when it will take effect, and nothing is removed before then.')),
    ),
    p('Administrators can change the policy unless the owner takes that right away from a role on the ', pageLink('Roles'), ' tab.'),

    h(2, 'Offsite copies'),
    ul(
      li(p(b('The cloud’s database copy'), ' keeps its own count of full backups (', c('OFFSITE_CLOUD_RETENTION_FULL'), ', 4 unless set), because an offsite copy is usually kept longer.')),
      li(p(b('The dumps and attachments'), ' copied offsite follow this policy, so they expire together with the ones on this computer.')),
      li(p(b('A removable drive'), ' keeps a count with no time limit, so a drive left in a drawer for months is not emptied when it comes back.')),
    ),
  ))

  await page('Restoring and undo', backups, doc(
    p('A ', b('restore'), ' replaces the whole wiki with a backup: every page, comment, attachment and account goes back to how it was when the backup was taken. It is the one thing on the Backups tab that uses a backup rather than making one, so Tesria asks you to be sure, keeps what it replaces, and lets you undo it.'),

    h(2, 'Before you restore'),
    ul(
      li(p(b('A bad edit or a deleted page does not need a restore.'), ' Put it right from the page’s ', pageLink('History and restoring', 'history'), ', or the space’s ', pageLink('Trash', 'trash'), '. A restore also takes back everyone else’s work since the backup.')),
      li(p(b('Only the owner can restore,'), ' unless the owner has given the ', b('Restore a backup'), ' right to a role. Others do not see the ', b('Restore'), ' button.')),
      li(p(b('Tell people.'), ' The wiki is read-only while it runs, and people who signed in after the backup was taken have to sign in again.')),
    ),

    h(2, 'Restoring a backup'),
    step(1, 'Find the backup'),
    p('Choose ', b('Admin'), ', then ', b('Backups'), ', and scroll to the ', b('Backups'), ' list. Each row has ', b('Test restore'), ' and, in red, ', b('Restore'), '. Choose ', b('Restore'), ' on the one you want.'),
    step(2, 'Read what it will do'),
    p('Tesria says when the backup was taken and what has been written since, which the restore takes back.'),
    step(3, 'Confirm'),
    p('Type the backup’s name where it asks, and your password. Someone who signs in with SSO and has no Tesria password gives a code from their authenticator app instead. Then choose ', b('Restore now'), '.'),

    h(2, 'What happens next'),
    ol(
      li(p(b('A backup is taken first'), ', of the wiki as it is now. It cannot be skipped, and nothing is restored if it fails.')),
      li(p('The wiki becomes ', b('read-only'), ' for everyone. Reading works; saving waits, and open editors are closed so they cannot write old pages back.')),
      li(p('The backup is restored beside the live wiki and checked. A backup from a newer version of Tesria is refused.')),
      li(p('The restored copy takes the live wiki’s place, in a moment. The wiki it replaces is ', b('kept'), ', not deleted.')),
      li(p('Tesria restarts, and your page comes back by itself.')),
    ),
    p('While it runs, the Backups tab shows ', b('A restore is in progress'), ' with ', b('Stop the restore'), ', which works until step 4. Afterwards, the restore is recorded in the audit log and raised as a critical alert for every administrator.'),

    h(2, 'To a moment in time'),
    p('A physical backup’s row restores to any second it covers: its ', b('Restore'), ' also asks for the time to roll forward to, between the earliest and latest it can reach. Use it when you know when things went wrong, for example to just before a mass deletion. It keeps no copy of what it replaces; its undo is another restore, to the moment the first one began, which the page offers.'),

    h(2, 'Undo'),
    p('After a restore from a dump, the Backups tab shows ', b('The copy kept before the last restore'), ', with two buttons:'),
    ul(
      li(p(b('Undo the restore'), ' puts the kept copy back, taking a backup of the wiki as it is now first. Type ', c('UNDO'), ' and your password to confirm.')),
      li(p(b('Remove the copy'), ' deletes it to free the disk. Type ', c('REMOVE'), ' and your password. After that, the restore cannot be undone.')),
    ),
    p('The kept copy is also removed on its own when the ', pageLink('Retention', 'retention policy'), ' would remove a backup taken at the time of the restore. While retention is off, it stays until someone removes it.'),

    h(2, 'From the command line'),
    p('When the wiki itself will not start, the same restore runs on the server, from the Tesria folder. List the dumps first:'),
    codeBlock('bash', 'docker compose exec backup ls /backups'),
    p('To see every step without changing anything, add ', c('-e RESTORE_DRY_RUN=1'), ':'),
    codeBlock('bash', 'docker compose exec -e RESTORE_DRY_RUN=1 backup /scripts/restore.sh db-20260920T030000Z.dump'),
    p('Then run it for real, with the name of the dump you want:'),
    codeBlock('bash', 'docker compose exec backup /scripts/restore.sh db-20260920T030000Z.dump'),
    p('It takes the same safety backup first, restores beside the live wiki, and puts back the attachments archived with the dump. Then restart Tesria, and the live editing service after it, so they pick up the restored database:'),
    codeBlock('bash', 'docker compose restart app\ndocker compose restart collab'),
    p('A restore run this way is not recorded as one, so the next daily audit check warns once that the audit log is shorter than before. That is expected: the log went back with everything else.'),
  ))

  await page('Offsite copies', backups, doc(
    p('An ', b('offsite copy'), ' is a copy of your backups somewhere other than the computer Tesria runs on, so that losing that computer, to a dead disk, a fire or a theft, does not lose the wiki. It is the most important thing to add after installing, because it is the one thing Tesria cannot do without you.'),

    h(2, 'The three kinds'),
    p('Use any of them, or all three together.'),
    ul(
      li(p(b('Cloud storage.'), ' Any storage that speaks the S3 protocol; Backblaze B2 is the documented choice. Copied to after every backup, and database changes stream there continuously in between. It holds everything, including restoring to any moment. The best single choice.')),
      li(p(b('A network drive.'), ' A shared folder on a NAS on your network. Copied to after every backup. It holds the dumps and the attachments.')),
      li(p(b('A removable drive.'), ' A USB disk you plug in, copy to, and take away. Copied to only when someone chooses ', b('Copy now'), '. It holds the dumps and the attachments.')),
    ),
    p('Every copy is encrypted on the server before it leaves, each with its own passphrase. The settings go in ', c('.env'), ' rather than on the admin page, so that the keys never enter the database and never travel inside a backup. The Backups tab shows fingerprints of them instead of the keys.'),
    panel('warning', p(b('Keep the passphrases somewhere other than the server,'), ' such as a password manager. A copy whose passphrase is lost cannot be read by anyone, including you.')),

    h(2, 'Cloud storage'),
    step(1, 'Make a bucket and a key'),
    p('At your storage provider, create a bucket (a named storage area) and an access key allowed to use it. The provider shows the key’s ID and secret once; copy both.'),
    step(2, 'Add the settings to .env'),
    p('For Backblaze B2, with your own bucket, region and key. Make the passphrase with ', c('openssl rand -hex 32'), ':'),
    codeBlock('bash', 'OFFSITE_CLOUD_TYPE=b2\nOFFSITE_CLOUD_ENDPOINT=s3.us-west-000.backblazeb2.com\nOFFSITE_CLOUD_BUCKET=tesria-backups\nOFFSITE_CLOUD_REGION=us-west-000\nOFFSITE_CLOUD_PATH=/tesria\nOFFSITE_CLOUD_URI_STYLE=host\nOFFSITE_CLOUD_KEY=your-key-id\nOFFSITE_CLOUD_SECRET=your-key-secret\nOFFSITE_CLOUD_PASSPHRASE=a-new-long-random-passphrase'),
    p('For other providers, ', c('OFFSITE_CLOUD_TYPE'), ' is ', c('s3'), '. Every setting is in the ', pageLink('Configuration reference'), '.'),
    step(3, 'Restart the services'),
    codeBlock('bash', 'docker compose up -d'),
    step(4, 'Test it'),
    p('On the Backups tab, a ', b('Cloud'), ' card appears under ', b('Storage targets'), '. Choose ', b('Test connection'), ' on it. See ', pageLink('Testing a target and the cloud budget'), '.'),
    panel('warning', p(b('If the cloud stays unreachable, act on the alert.'), ' Database changes wait on the server until the cloud accepts them. Past a limit (', c('OFFSITE_ARCHIVE_QUEUE_MAX'), ') they are dropped, and then restoring to a moment before the outage stops working on this computer too. Tesria alerts long before that. Fix the connection, or remove the cloud settings and restart, and then take a new backup.')),

    h(2, 'A network drive'),
    step(1, 'Connect the share to the server'),
    p('Tesria never connects to a network share itself; the computer it runs on does, and handles the password and reconnecting.'),
    ul(
      li(p(b('On a Mac,'), ' connect to the share in Finder. It then appears under ', c('/Volumes'), '. The first time Tesria uses it, Docker Desktop asks to allow access to the folder: allow it, or the backup hangs until you do.')),
      li(p(b('On Linux,'), ' add the share to ', c('/etc/fstab'), ' with the options ', c('_netdev,nofail'), ', so a missing share does not hold up starting the computer.')),
    ),
    step(2, 'Add the settings to .env'),
    codeBlock('bash', 'OFFSITE_NAS_PATH=/Volumes/my-nas/tesria-backups\nOFFSITE_NAS_PASSPHRASE=a-new-long-random-passphrase'),
    p('Then run ', c('docker compose up -d'), '.'),
    step(3, 'Mark the share as Tesria’s'),
    codeBlock('bash', 'docker compose exec backup /scripts/claim-target.sh nas'),
    p('This leaves a small marker file on the share, and Tesria only copies to a folder that has it. Here is why: when a share is not connected, its folder is still there on the server’s own disk, empty. Without the marker, backups would quietly fill the server’s disk instead of reaching the NAS. If the command warns that the folder is empty, the share is probably not connected.'),
    panel('success', p(b('Turn on your NAS’s own snapshots'), ' for that folder too. A snapshot the Tesria server cannot delete protects the copies even if the server itself is broken into.')),

    h(2, 'A removable drive'),
    step(1, 'Add the settings to .env'),
    codeBlock('bash', 'OFFSITE_REMOVABLE_PATH=/Volumes/my-backup-drive\nOFFSITE_REMOVABLE_PASSPHRASE=a-new-long-random-passphrase'),
    p('Plug the drive in, then run ', c('docker compose up -d'), '.'),
    step(2, 'Mark the drive as Tesria’s, once'),
    codeBlock('bash', 'docker compose exec backup /scripts/claim-target.sh removable'),
    step(3, 'Copy to it'),
    p('Whenever the drive is plugged in, choose ', b('Copy now'), ' on its card under ', b('Storage targets'), '. When the card says it is safe to remove, every byte is on the drive.'),
    p('The computer may still refuse to eject the drive, because the backup service is using it. To eject it cleanly, stop the service, eject, and start it again:'),
    codeBlock('bash', 'docker compose stop backup\n# eject the drive\ndocker compose up -d backup'),
    ul(
      li(p(b('Format the drive as exFAT,'), ' not FAT32, which cannot hold files over 4 GB.')),
      li(p(b('A removable drive alone is not an offsite backup.'), ' Between the times someone plugs it in, there is no copy anywhere else, and the Backups tab says so.')),
    ),
  ))

  await page('Testing a target and the cloud budget', backups, doc(
    p('Two things on the ', b('Storage targets'), ' cards on the Backups tab: a way to check that an offsite copy can be reached, and a way to keep an eye on what the cloud copy costs.'),

    h(2, 'Test connection'),
    p('Each card has ', b('Test connection'), '. It asks the backup services to reach that target and open it with its passphrase, and changes nothing. Use it whenever you have set up or changed a target.'),
    ol(
      li(p('After editing ', c('.env'), ', run ', c('docker compose up -d'), ' first: the test uses the settings the services are running with, not the ones in the file.')),
      li(p('Choose ', b('Test connection'), '. The card says ', i('Testing the connection…'), '.')),
      li(p('The answer usually comes within a minute, because the services look for work once a minute. If a backup is running, the test waits for it to finish.')),
    ),
    p('The cloud card holds two copies, the database and the dumps with attachments, so it gives an answer for each.'),

    h(2, 'The cloud budget'),
    p('Cloud storage has no free space to measure, so on its own the cloud card shows only how much is stored. Set ', c('OFFSITE_CLOUD_BUDGET_GB'), ' in ', c('.env'), ' to the amount you mean to pay for, such as ', c('100'), ', and the card’s chart also shows what is left of it, and says when it has been passed.'),
    p('It is a number to watch, not a limit: nothing is refused or removed for going over.'),
  ))

  await page('Restore drills', backups, doc(
    p('A backup nobody has restored has not been proved to work. It can look complete and still fail to turn back into a wiki, and the day you find out should not be the day you need it. So Tesria proves its offsite copies on a schedule, by itself.'),
    ul(
      li(p(b('After every copy,'), ' each offsite target checks that the copy is complete and undamaged.')),
      li(p(b('Every 30 days'), ' (', c('OFFSITE_DRILL_DAYS'), '), each offsite copy is restored for real: the newest dump is taken out of it, loaded into a temporary database, counted and thrown away. Nothing live is touched.')),
      li(p(b('Test restore'), ' on the Backups tab does the same for the backups on this computer, whenever you ask.')),
    ),
    p('The first asks whether the copy is intact; the drill asks whether it still turns back into a wiki. A copy can pass the first and fail the second, which is why a failed drill is a critical alert for every administrator. Each target’s card shows ', b('Last restore drill'), '.'),
    panel('success', p(b('Rehearse it yourself, once or twice a year.'), ' Follow ', pageLink('When the machine is gone'), ' on a spare computer. It is the one procedure you should not be reading for the first time when you need it.')),
  ))

  await page('When the machine is gone', backups, doc(
    p('The server is broken, stolen or will not start, and all you have is an offsite copy. This page takes you from there back to a working wiki on another computer. Read it through once before you start, and keep going step by step: nothing here can make things worse, because the offsite copy is only ever read.'),

    h(2, 'What you need'),
    ul(
      li(p(b('The passphrase of the copy you have:'), ' ', c('OFFSITE_CLOUD_PASSPHRASE'), ', ', c('OFFSITE_NAS_PASSPHRASE'), ' or ', c('OFFSITE_REMOVABLE_PASSPHRASE'), ', from wherever you kept it. Without it the copy cannot be read, and there is no way around that.')),
      li(p(b('Access to the copy:'), ' the cloud account’s key and secret, or the drive or share itself.')),
      li(p(b('A computer with Docker'), ' and room for the wiki. See ', pageLink('Prerequisites'), '.')),
    ),
    p('You get back every page, every attachment and every account. You do not get back ', c('.env'), ', which is in no backup because it holds the keys: you write a new one.'),

    step(1, 'Look at what the copy holds'),
    p('The copies are standard ', b('restic'), ' repositories: restic and the passphrase read them on any computer, with no Tesria involved. Docker runs restic for you. For a drive or share, use the path it is at on this computer:'),
    codeBlock('bash', 'docker run --rm -e RESTIC_PASSWORD=your-passphrase -v /Volumes/your-drive:/t restic/restic -r /t/restic snapshots'),
    p('For the cloud copy, with your own endpoint, bucket and path:'),
    codeBlock('bash', 'docker run --rm -e RESTIC_PASSWORD=your-passphrase -e AWS_ACCESS_KEY_ID=key -e AWS_SECRET_ACCESS_KEY=secret restic/restic -r s3:https://endpoint/bucket/tesria/files snapshots'),
    p('Each line it lists is a copy, with its date. The newest is the one to restore.'),

    step(2, 'Install an empty Tesria'),
    p('Get Tesria as in ', pageLink('Quick start'), ', and write a new ', c('.env'), ' with new passwords and a new ', c('BACKUP_ENCRYPTION_KEY'), '. Keep the same ', c('DOMAIN'), ' if you want the same address. Then start it:'),
    codeBlock('bash', 'docker compose up -d --build'),
    p('Wait until ', c('docker compose ps'), ' shows ', c('app'), ' as healthy. Do not go through the setup wizard: the restore replaces everything anyway.'),

    step(3, 'Take the newest backup out of the copy'),
    p('This writes it into a folder called ', c('restored'), ' inside the Tesria folder. For a drive or share:'),
    codeBlock('bash', 'docker run --rm -e RESTIC_PASSWORD=your-passphrase -v "$PWD/restored:/out" -v /Volumes/your-drive:/t restic/restic -r /t/restic restore latest --target /out'),
    p('For the cloud copy:'),
    codeBlock('bash', 'docker run --rm -e RESTIC_PASSWORD=your-passphrase -e AWS_ACCESS_KEY_ID=key -e AWS_SECRET_ACCESS_KEY=secret -v "$PWD/restored:/out" restic/restic -r s3:https://endpoint/bucket/tesria/files restore latest --target /out'),
    p('Either way, ', c('restored/backups/'), ' now holds the dumps and their attachment archives.'),

    step(4, 'Restore it'),
    p('Copy them into the new Tesria’s backup service, and list them:'),
    codeBlock('bash', 'docker compose cp restored/backups/. backup:/backups/\ndocker compose exec backup ls /backups'),
    p('Restore the newest dump by its name, then restart Tesria so it picks it up:'),
    codeBlock('bash', 'docker compose exec backup /scripts/restore.sh db-<time>.dump\ndocker compose restart app\ndocker compose restart collab'),
    p('Use the dump’s real name in place of ', c('db-<time>.dump'), '. The script also puts back the attachments archived with it.'),

    step(5, 'Check it'),
    p('In this order, because each proves something different:'),
    ol(
      li(p('Sign in with your old account.')),
      li(p('Open a page with a picture on it. That proves the attachments came back, not only the database.')),
      li(p('Under ', b('Admin'), ', ', b('Backups'), ', check that both backup services are healthy.')),
    ),

    step(6, 'Before calling it done'),
    ul(
      li(p(b('Take a new backup'), ' straight away, and check the ', pageLink('Retention', 'retention policy'), ', which came back with the wiki.')),
      li(p(b('Set up an offsite copy'), ' for the new computer, as in ', pageLink('Offsite copies'), '. The copy you restored from belonged to the old one.')),
      li(p(b('A week later, check that the restore drill has run'), ' on the new computer. Until it has, nothing has proved the new copies can be restored.')),
      li(p(b('If people used the local certificate,'), ' each device trusts the new server again: see ', pageLink('Trusting the local certificate'), '.')),
    ),

    h(2, 'To a moment in time, from the cloud'),
    p('The steps above bring the wiki back as it was at its newest nightly backup. The cloud copy can also go back to a chosen minute, because it holds the database’s record of changes. That needs the original ', c('BACKUP_ENCRYPTION_KEY'), ' as well as the cloud passphrase, and is a job for someone comfortable with PostgreSQL: the steps are in ', c('docs/backup-recovery.md'), ' in the Tesria folder.'),
  ))

  // ============================================================ Hardening
  await page('Security hardening', root, doc(
    p('Tesria arrives set up for a private network. Before people can reach it from the internet, a few things need doing that the software cannot do for you: strong passwords, the stricter web server configuration, two-factor sign-in for the people who run it. This page is that list, with why each item matters.'),
    p('On a private network, the accounts and operations items still apply.'),

    h(2, 'Configuration'),
    tasks(
      task(false, c('DOMAIN'), ' is a real name you control, and ', c('ACME_EMAIL'), ' is an address someone reads. See ', pageLink('HTTPS and domains'), '.'),
      task(false, c('CADDYFILE=deploy/Caddyfile.public'), ' is set. The standard configuration makes a certificate for any name a stranger connects with.'),
      task(false, c('POSTGRES_PASSWORD'), ', ', c('APP_DB_PASSWORD'), ', ', c('BACKUP_ENCRYPTION_KEY'), ' and ', c('COLLAB_SHARED_SECRET'), ' are long, random and all different. ', c('APP_DB_PASSWORD'), ' is not empty: empty, Tesria runs as the database owner.'),
      task(false, 'Only ports 80 and 443 are open to the outside. Nothing publishes Tesria’s own port (8080), the database (5432) or the live editing service (8090).'),
      task(false, 'If a proxy of your own sits in front, ', c('PROXY_TRUSTED_NETWORKS'), ' names exactly that proxy.'),
      task(false, 'If single sign-on is on, ', c('OIDC_REQUIRE_HTTPS_METADATA'), ' is left out or true.'),
    ),

    h(2, 'Accounts'),
    tasks(
      task(false, 'The owner has two-factor sign-in on and has saved their recovery codes. Nobody else can reset the owner, so losing both locks the owner out. See ', pageLink('Two-factor and recovery codes'), '.'),
      task(false, 'Every administrator has two-factor on, and ', b('Require two-factor for administrators'), ' is on (', b('Admin'), ', ', b('Security'), ', ', b('Kill switches'), ').'),
      task(false, 'Each role has only what it needs: review ', b('Admin'), ', ', pageLink('Roles'), '.'),
      task(false, b('Allow public registration'), ' is off, so new accounts need an invite, unless you mean to run an open community.'),
      task(false, b('Allow public spaces'), ' stays off until you mean to publish a space to people who are not signed in.'),
    ),

    h(2, 'Operations'),
    tasks(
      task(false, 'Backups run, a restore has been rehearsed, and there is at least one ', pageLink('Offsite copies', 'offsite copy'), '.'),
      task(false, 'The passphrases in ', c('.env'), ' are kept somewhere other than the server.'),
      task(false, 'Email works, so alerts reach administrators: ', b('Send test email to me'), ' succeeds. See ', pageLink('Email (SMTP)'), '.'),
      task(false, 'Tesria’s log, which carries a copy of the audit log, is sent somewhere durable.'),
      task(false, c('scripts/verify-audit-chain.sh'), ' runs on a schedule and someone watches its result. See ', pageLink('Health checks and monitoring'), '.'),
      task(false, 'Someone watches for new releases and upgrades promptly. See ', pageLink('Upgrading'), '.'),
    ),

    h(2, 'What Tesria does for you'),
    p('Once those are done, Tesria covers a good deal by itself: limits on sign-in attempts from one address and lockouts for one account; alerts for patterns such as password spraying or mass deletion; a blocklist for addresses; strict browser security headers; attachments that cannot run scripts as the site; webhooks that cannot reach the server’s own network; and an audit log the running application cannot alter. The ', pageLink('Security'), ' section explains each, and what it does not cover.'),
  ))

  // ============================================================ Uninstalling
  await page('Uninstalling and moving', root, doc(
    p('How to stop Tesria, move it, or remove it. Stopping keeps everything; moving takes the wiki with it; removing deletes it. Each is below, in that order, from the gentlest to the one that cannot be undone.'),

    h(2, 'Stopping'),
    codeBlock('bash', 'docker compose down'),
    p('Stops and removes the containers. Your data stays in its volumes, and ', c('docker compose up -d'), ' brings everything back as it was.'),

    h(2, 'Moving one space'),
    p('A ', b('wiki pack'), ' carries one space, with its pages, history, comments and attachments, to another Tesria. Export it with ', b('Export as a pack'), ' in the space’s settings, then import it on the other Tesria with ', b('Import a pack'), ' on the Spaces page. See ', pageLink('Wiki packs'), '.'),

    h(2, 'Moving the whole Tesria'),
    p('To a new computer, taking everything: every space, account and setting. It moves the newest backup across and restores it.'),
    step(1, 'Take a backup on the old computer'),
    p('Choose ', b('Admin'), ', ', b('Backups'), ', ', b('Back up now'), ', and wait until it finishes.'),
    step(2, 'Pack up the backups'),
    p('In the Tesria folder on the old computer:'),
    codeBlock('bash', 'docker run --rm -v tesria_backups:/b -v "$PWD":/out alpine tar czf /out/tesria-backups.tgz -C /b .'),
    p('That makes ', c('tesria-backups.tgz'), '. Copy it to the new computer, into its Tesria folder.'),
    step(3, 'Install Tesria on the new computer'),
    p('As in ', pageLink('Quick start'), ', with a new ', c('.env'), ', and the same ', c('DOMAIN'), ' to keep the same address. Start it with ', c('docker compose up -d --build'), ', wait until ', c('app'), ' is healthy, and skip the setup wizard.'),
    step(4, 'Restore the backup'),
    codeBlock('bash', 'docker compose cp tesria-backups.tgz backup:/tmp/\ndocker compose exec backup tar xzf /tmp/tesria-backups.tgz -C /backups\ndocker compose exec backup ls /backups'),
    p('Then restore the newest dump by its name, and restart Tesria:'),
    codeBlock('bash', 'docker compose exec backup /scripts/restore.sh db-<time>.dump\ndocker compose restart app\ndocker compose restart collab'),
    step(5, 'Point people at the new computer'),
    p('If you use a domain, point it at the new server. If people use the local certificate, each device trusts the new one: see ', pageLink('Trusting the local certificate'), '. Then set up the offsite copies again, as in ', pageLink('Offsite copies'), '.'),

    h(2, 'Removing Tesria completely'),
    panel('error', p(b('This deletes the wiki and every backup on this computer.'), ' It cannot be undone. First take a copy you can restore from somewhere else, or be sure you never want it again.')),
    codeBlock('bash', 'docker compose down -v --rmi local'),
    p(c('-v'), ' deletes the volumes, which hold the database, the attachments and the backups. ', c('--rmi local'), ' deletes the images built for Tesria. Offsite copies are not touched: delete those from your cloud storage, network drive or removable drive yourself. Then you can delete the Tesria folder.'),
  ))
}

// Pictures these pages no longer use, taken down so they do not linger in the
// page's attachments or in the exported pack.
const RETIRED = {
  'Trusting the local certificate': ['trust-top.png', 'trust-steps.png', 'trust-steps-windows.png', 'trust-check.png', 'trust-iphone.phone.png'],
  'Backups and recovery': ['backups-page.png', 'backups-page.phone.png'],
  'Retention': ['retention-policy.png', 'retention-policy.phone.png'],
  'Offsite copies': ['storage-targets.png', 'storage-targets.phone.png'],
  'Email (SMTP)': ['email-settings.png', 'email-settings.phone.png'],
  'Security hardening': ['kill-switches.png', 'kill-switches.phone.png'],
}

export async function cleanup({ author }) {
  const space = await author.call('GET', '/api/spaces/SUPPORT')
  const tree = await author.call('GET', `/api/pages/tree?spaceId=${space.id}`)
  const find = (nodes, title) => {
    for (const n of nodes) {
      if (n.title === title) return n
      const hit = find(n.children ?? [], title)
      if (hit) return hit
    }
    return null
  }
  // Opening Tesria by name goes straight after Trusting the local
  // certificate, which it follows on from; a new page lands at the end.
  const install = find(tree, 'Installation and operations')
  if (install) {
    const kids = install.children ?? []
    const trustAt = kids.findIndex((n) => n.title === 'Trusting the local certificate')
    const byName = kids.findIndex((n) => n.title === 'Opening Tesria by name')
    if (trustAt >= 0 && byName >= 0 && byName !== trustAt + 1) {
      const index = byName > trustAt ? trustAt + 1 : trustAt
      await author.call('PUT', `/api/pages/${kids[byName].id}/move`, { parentPageId: install.id, index })
      console.log('  moved Opening Tesria by name after Trusting the local certificate')
    }
  }

  for (const [title, files] of Object.entries(RETIRED)) {
    const node = find(tree, title)
    if (!node) continue
    for (const a of await author.call('GET', `/api/pages/${node.id}/attachments`)) {
      if (files.includes(a.filename)) {
        await author.call('DELETE', `/api/attachments/${a.id}`)
        console.log(`  - ${title}: ${a.filename}`)
      }
    }
  }
}
