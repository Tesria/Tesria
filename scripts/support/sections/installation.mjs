// Installation and operations: running Tesria, for whoever looks after the
// server (dev-plan 10.5).
//
// The facts come from the code and the runbooks it ships with, checked on
// 2026-09-23: .env.example and docker-compose.yml for every setting,
// docs/tls-and-lan-access.md, docs/backup-recovery.md and docs/security.md.
// Those stay the engineers' copies; these pages are the operator's.
//
// No picture shows a restore: only the owner may restore, and the pictures
// are taken as an administrator. The restore screens are described in words
// until the owner-only shots are taken (dev-plan 10.5 step 6).

const clipSection = (title) => `section.profile__section:has(> h2:text-is("${title}"))`

export const shots = () => [
  { name: 'backups-page', url: '/admin/backups', settle: 1500, steps: [{ wait: 3000 }] },
  { name: 'retention-policy', url: '/admin/backups', settle: 800, steps: [{ wait: 3000 }], clipTo: clipSection('Retention policy'), clipPad: 0 },
  { name: 'storage-targets', url: '/admin/backups', settle: 800, steps: [{ wait: 3000 }], clipTo: '#storage-targets' },
  { name: 'email-settings', url: '/admin/settings', settle: 800, steps: [{ wait: 2500 }], clipTo: clipSection('Email') },
  { name: 'kill-switches', url: '/admin/security', settle: 800, steps: [{ wait: 2500 }], clipTo: clipSection('Kill switches') },
]

export async function build({
  top, page, ensure, figure, doc, p, h, text, bold, code, ul, ol, li, panel, table, codeBlock, live, tasks, task,
}) {
  const root = top['Installation and operations']
  const c = (t) => text(t, code)
  const b = (t) => text(t, bold)

  await page('Installation and operations', null, doc(
    p('For whoever runs the server: installing Tesria, configuring it, keeping it healthy and backed up, and getting it back when something goes wrong. People who only use the wiki want the ', b('User manual'), ' instead.'),
    live('children', { depth: '2', sort: 'position' }),
  ))

  // ------------------------------------------------------------- Installing
  await page('Installing with Docker Compose', root, doc(
    p('Tesria runs as a set of containers that Docker Compose starts together. ', b('Quick start'), ' in Getting started is the short version; this page says what each part is and what the first start does.'),
    h(2, 'What runs'),
    table([
      ['Service', 'What it does'],
      [p(c('app')), 'Tesria itself: the web application and its API.'],
      [p(c('db')), 'PostgreSQL 18, which holds everything except uploaded files.'],
      [p(c('caddy')), 'The web server in front. It handles HTTPS and is the only service reachable from outside, on ports 80 and 443.'],
      [p(c('collab')), 'Lets several people edit one page at once. Off unless ', c('COLLAB_SHARED_SECRET'), ' is set.'],
      [p(c('pdf')), 'Turns pages into PDFs. Off unless ', c('PDF_SHARED_SECRET'), ' is set.'],
      [p(c('backup')), 'Takes the nightly database dump and uploads archive, and copies them offsite.'],
      [p(c('pgbackrest')), 'Takes physical backups and keeps the continuous record that lets you restore to any moment.'],
    ], [160, 540]),
    h(2, 'Where your data lives'),
    p('In Docker volumes, which survive stopping, restarting and rebuilding the containers:'),
    table([
      ['Volume', 'Holds'],
      [p(c('pgdata')), 'The database.'],
      [p(c('uploads')), 'Attachments: images, files and videos added to pages.'],
      [p(c('backups')), 'The nightly dumps and uploads archives.'],
      [p(c('pgbackrest')), 'The physical backups and the continuous record, encrypted.'],
      [p(c('caddy_data')), 'HTTPS certificates, including the local certificate authority.'],
    ], [160, 540]),
    p('Compose prefixes each name with the project, so on disk they are ', c('tesria_pgdata'), ', ', c('tesria_uploads'), ' and so on.'),
    h(2, 'The first start'),
    ol(
      li(p('Copy ', c('.env.example'), ' to ', c('.env'), ' and set the required values. The ', b('Configuration reference'), ' lists every one.')),
      li(p('Run ', c('docker compose up -d --build'), '. The first build takes a few minutes.')),
      li(p('The database starts, and the backup service sets up its repository.')),
      li(p('Tesria creates its tables and a restricted database account for itself, then starts.')),
      li(p('Open the address in a browser. The ', b('First-run setup wizard'), ' creates the owner and asks the rest.')),
    ),
    panel('warning', p(b('Start everything together.'), ' On a new install, starting only the database makes it restart every few seconds, because it waits for the backup service to set up the repository. If you ever need the database on its own, start it with its backup service: ', c('docker compose up -d db pgbackrest'), '.')),
    h(2, 'Checking it worked'),
    codeBlock('bash', 'docker compose ps'),
    p('Every service should say ', c('running'), ', and those with a health check ', c('healthy'), '. Then open ', c('/api/health'), ' on your address; ', b('Health checks and monitoring'), ' explains the answer.'),
  ))

  // ------------------------------------------------------ Configuration
  const setting = (name, what) => [p(c(name)), what]
  await page('Configuration reference', root, doc(
    p('Every setting lives in the ', c('.env'), ' file next to ', c('docker-compose.yml'), '. Change one, then run ', c('docker compose up -d'), ' so the containers pick it up. Everything else is set in the browser, under ', b('Administration'), '.'),
    panel('warning', p(c('.env'), ' holds every secret the server has. Keep it out of version control, and keep a copy of the backup passphrases somewhere that is not this machine: without them the backups cannot be read.')),
    p('A long random value can be made with ', c('openssl rand -hex 32'), '.'),
    h(2, 'Database'),
    table([
      ['Setting', 'What it does'],
      setting('POSTGRES_USER', 'The database owner account. Used only for setting up the database and for backups and restores.'),
      setting('POSTGRES_PASSWORD', 'Required. That account’s password.'),
      setting('POSTGRES_DB', 'The database name.'),
      setting('APP_DB_PASSWORD', 'The password of the restricted account Tesria runs as, which cannot change or delete the audit log. Tesria creates the account itself. Left empty, Tesria runs as the database owner, which is acceptable on a private network and not on the internet.'),
      setting('APP_DB_USER', 'Optional. That account’s name, tesria_app unless set.'),
    ], [240, 460]),
    h(2, 'Address and HTTPS'),
    table([
      ['Setting', 'What it does'],
      setting('DOMAIN', 'The name people reach Tesria by, such as wiki.example.com. A real domain gets a free certificate from Let’s Encrypt; localhost uses a certificate Tesria makes itself. Also the address used in links in email, unless changed in Administration → Settings.'),
      setting('ACME_EMAIL', 'An address Let’s Encrypt can write to about your certificate.'),
      setting('CADDYFILE', 'Which web server configuration to run. Set it to deploy/Caddyfile.public on the internet; see HTTPS and domains.'),
      setting('PROXY_TRUSTED_NETWORKS', 'Only if you put your own proxy in front of Tesria: that proxy’s address, whose forwarded headers Tesria then believes.'),
    ], [240, 460]),
    h(2, 'Optional features'),
    table([
      ['Setting', 'What it does'],
      setting('COLLAB_SHARED_SECRET', 'Turns on editing a page with several people at once. Any long random value.'),
      setting('PDF_SHARED_SECRET', 'Turns on PDF export. Any long random value. Without it, the HTML export still works.'),
    ], [240, 460]),
    h(2, 'Single sign-on'),
    p('All optional, and off while ', c('OIDC_AUTHORITY'), ' is empty. See ', b('Single sign-on (OIDC)'), '.'),
    table([
      ['Setting', 'What it does'],
      setting('OIDC_AUTHORITY', 'Your identity provider’s address.'),
      setting('OIDC_CLIENT_ID', 'The client ID you registered there.'),
      setting('OIDC_CLIENT_SECRET', 'Its secret.'),
      setting('OIDC_DISPLAY_NAME', 'The words on the sign-in button, such as Company SSO.'),
      setting('OIDC_REQUIRE_HTTPS_METADATA', 'Leave it out (true). False only for a test provider on plain HTTP on the same machine.'),
    ], [240, 460]),
    h(2, 'Backups on this machine'),
    table([
      ['Setting', 'What it does'],
      setting('BACKUP_ENCRYPTION_KEY', 'Required. Encrypts the physical backups. They cannot be restored without it.'),
      setting('BACKUP_INTERVAL_HOURS', 'How often backups run. 24 by default.'),
      setting('BACKUP_FULL_EVERY_DAYS', 'A new full physical backup once the newest is this many days old, with smaller incremental ones in between. 7 by default.'),
      setting('BACKUP_RETENTION_DAYS', 'Read once, on the first start, as the starting retention policy. After that, retention is set in Administration → Backups and this is ignored.'),
    ], [240, 460]),
    h(2, 'Offsite backups'),
    p('Three places to keep a copy, each optional and each with its own passphrase. See ', b('Offsite copies'), '.'),
    table([
      ['Setting', 'What it does'],
      setting('OFFSITE_CLOUD_TYPE', 'The kind of cloud storage, such as b2 for Backblaze B2. Anything with an S3 API works.'),
      setting('OFFSITE_CLOUD_ENDPOINT', 'The storage service’s address.'),
      setting('OFFSITE_CLOUD_BUCKET', 'The bucket to use.'),
      setting('OFFSITE_CLOUD_REGION', 'The bucket’s region.'),
      setting('OFFSITE_CLOUD_PATH', 'A folder inside the bucket, such as /tesria.'),
      setting('OFFSITE_CLOUD_URI_STYLE', 'host for Backblaze and AWS; path for MinIO and most self-hosted storage. The first thing to change if backups fail with a DNS or 404 error.'),
      setting('OFFSITE_CLOUD_KEY', 'The access key.'),
      setting('OFFSITE_CLOUD_SECRET', 'The secret key.'),
      setting('OFFSITE_CLOUD_PASSPHRASE', 'Encrypts the cloud copy. Different from every other passphrase here.'),
      setting('OFFSITE_CLOUD_RETENTION_FULL', 'How many full backups the cloud keeps. 4 by default.'),
      setting('OFFSITE_CLOUD_BACKUP_EVERY_DAYS', 'How often a full backup goes to the cloud. 7 by default. Changes stream there continuously in between.'),
      setting('OFFSITE_CLOUD_BUDGET_GB', 'Optional. How much you mean the cloud copy to hold; the Backups page then shows what is left. Nothing is refused or removed for going over.'),
      setting('OFFSITE_CLOUD_VERIFY_TLS', 'Only for testing against local storage on plain HTTP. Never set it for a real provider.'),
      setting('OFFSITE_ARCHIVE_QUEUE_MAX', 'How much unsent change history may pile up while the cloud is unreachable before it is dropped. 16GiB by default. Keep it well under the free disk space.'),
      setting('OFFSITE_NAS_PATH', 'A network share, already mounted on this machine, to copy to.'),
      setting('OFFSITE_NAS_PASSPHRASE', 'Encrypts the copy on the network share.'),
      setting('OFFSITE_REMOVABLE_PATH', 'Where a removable drive appears when plugged in.'),
      setting('OFFSITE_REMOVABLE_PASSPHRASE', 'Encrypts the copy on that drive.'),
      setting('OFFSITE_RETRY_MINUTES', 'How long to wait before trying a target again after it could not be reached. 15 by default.'),
      setting('OFFSITE_DRILL_DAYS', 'How often each offsite copy is restored for real, as a test. 30 by default.'),
    ], [240, 460]),
  ))

  // ----------------------------------------------------- HTTPS and domains
  await page('HTTPS and domains', root, doc(
    p('Tesria is always served over HTTPS. Which certificate it uses depends on how people reach it.'),
    table([
      ['How it is reached', 'Certificate', 'What to do'],
      ['A domain name that points at the server', 'Let’s Encrypt, trusted everywhere, renewed automatically', 'Set DOMAIN and ACME_EMAIL.'],
      ['Only on your own network, by name or address', 'Made by Tesria’s own certificate authority', 'Trust that authority once on each device: see Trusting the local certificate.'],
      ['On the public internet', 'Let’s Encrypt, with the stricter configuration', 'Also set CADDYFILE, below.'],
    ], [220, 240, 240]),
    h(2, 'A real domain'),
    codeBlock('bash', 'DOMAIN=wiki.example.com\nACME_EMAIL=you@example.com'),
    p('Point the domain’s DNS record at the server and make sure ports 80 and 443 reach it; Let’s Encrypt checks port 80 when it issues the certificate. Then run ', c('docker compose up -d'), '.'),
    h(2, 'On your own network'),
    p('With ', c('DOMAIN=localhost'), ', or when people use a name other than the domain, Tesria answers on any name it is reached by and makes a certificate for that name the first time it is asked. Browsers warn about these certificates until the device trusts Tesria’s certificate authority.'),
    p('Reach it by a name, such as ', c('wiki-server.local'), ', rather than an address like 192.168.1.50: browsers do not say which name they want when they connect to a bare address, so the warning stays even after trusting.'),
    h(2, 'On the public internet'),
    p('The default configuration is made for a private network, and makes a certificate for any name anyone connects with. On the internet, use the stricter one instead:'),
    codeBlock('bash', 'CADDYFILE=deploy/Caddyfile.public'),
    p('Then run ', c('docker compose up -d caddy'), '. It serves only your domain, stops offering the local certificate, and tells browsers to use HTTPS for your domain from then on (HSTS).'),
    panel('warning', p(b('HSTS cannot be taken back quickly.'), ' Once a browser has seen it, it refuses plain HTTP to your domain for up to two years, even if you turn it off. Only use the public configuration on a real domain with a real certificate.')),
    p('Read ', b('Security hardening'), ' before letting the internet in.'),
  ))

  // ------------------------------------------------ Trusting the certificate
  await page('Trusting the local certificate', root, doc(
    p('When Tesria runs without a public domain, it signs its own certificates, and each device warns about them once until you tell it to trust Tesria’s certificate authority. Do this once per device; after that every name the server answers on is trusted on that device.'),
    h(2, 'Mac or Linux'),
    p('From a copy of the Tesria folder on that computer:'),
    codeBlock('bash', './deploy/scripts/trust-ca.sh wiki-server.local'),
    p('Use the name the computer reaches the server by, or leave it out on the server itself. It asks for your password, because it adds the certificate to the system’s trusted list. Chrome, Edge and Safari all use that list.'),
    h(2, 'Windows'),
    codeBlock('powershell', '.\\deploy\\scripts\\trust-ca.ps1 wiki-server.local'),
    h(2, 'Firefox'),
    p('Firefox keeps its own list. Either import the certificate in ', b('Settings → Privacy & Security → Certificates → View Certificates → Authorities → Import'), ', or set ', c('security.enterprise_roots.enabled'), ' to true in ', c('about:config'), ' so Firefox uses the system’s list.'),
    h(2, 'iPhone and iPad'),
    ol(
      li(p('In Safari, open ', c('http://wiki-server.local/ca.crt'), ' (plain http, on purpose) and allow the download.')),
      li(p('Open ', b('Settings'), ', tap the downloaded profile near the top, and install it.')),
      li(p('Go to ', b('Settings → General → About → Certificate Trust Settings'), ' and turn on full trust for the Tesria certificate. Without this step it is installed but not trusted.')),
    ),
    h(2, 'Android'),
    p('Download ', c('http://wiki-server.local/ca.crt'), ', then install it from ', b('Settings → Security → Encryption & credentials → Install a certificate → CA certificate'), '. Android then says the network may be monitored, as it does for any certificate you add yourself.'),
    h(2, 'If a warning comes back'),
    ul(
      li(p('Quit the browser completely and open it again: browsers remember the old answer.')),
      li(p('If the server’s ', c('caddy_data'), ' volume was deleted, for example by ', c('docker compose down -v'), ' or a move to a new machine, Tesria made a new certificate authority. Trust the new one on every device. Ordinary restarts and upgrades never do this.')),
    ),
  ))

  // --------------------------------------------------------- Single sign-on
  await page('Single sign-on (OIDC)', root, doc(
    p('People can sign in with an account they already have at an OpenID Connect provider, such as Keycloak, Authentik, Okta, Microsoft Entra ID or Google, alongside ordinary Tesria accounts.'),
    h(2, 'Setting it up'),
    ol(
      li(p('At your provider, register an application of the web or confidential type, with the redirect address ', c('https://wiki.example.com/signin-oidc'), ' (your own domain).')),
      li(p('Put what the provider gives you into ', c('.env'), ':')),
    ),
    codeBlock('bash', 'OIDC_AUTHORITY=https://idp.example.com/realms/company\nOIDC_CLIENT_ID=tesria\nOIDC_CLIENT_SECRET=the-secret-from-the-provider\nOIDC_DISPLAY_NAME=Company SSO'),
    ol(
      li(p('Run ', c('docker compose up -d app'), '. The sign-in page now has a button with that name.')),
    ),
    h(2, 'Which account someone gets'),
    ul(
      li(p(b('Someone who has signed in with SSO before'), ' gets the same account every time, even if their email address changes at the provider.')),
      li(p(b('Someone who already has a Tesria account'), ' with the same email address is connected to it, but only if the provider says it has confirmed that address. Otherwise anyone who could claim the address at the provider could take over the account.')),
      li(p(b('Someone new'), ' gets a new account with no Tesria password, if ', b('Who can join'), ' is set to Open. When it is set to Invite only, they are turned away: send them an invite, let them create their account from it, and from then on SSO signs them in to that account.')),
    ),
    panel('note', p('Two-factor at sign-in is your provider’s job for people who use SSO. Tesria’s own two-factor applies to signing in with a Tesria password.')),
  ))

  // ------------------------------------------------------------------ Email
  const email = await ensure('Email (SMTP)', root)
  await page('Email (SMTP)', root, doc(
    p('Tesria sends password reset links, invitations, security alerts to administrators, and notifications to people who ask for them. It needs a mail server to send through: your organization’s, or a sending service such as Amazon SES, Postmark or Mailgun.'),
    p('Without email, Tesria still works: an administrator resets a forgotten password by giving the person a one-time link, and alerts wait in the bell.'),
    h(2, 'Setting it up'),
    p('In ', b('Administration → Settings'), ', under ', b('Email'), ':'),
    ...(await figure(email, 'email-settings', 'The email settings')),
    table([
      ['Field', 'What to enter'],
      ['SMTP host and Port', 'Your mail server. Port 587 with STARTTLS is the usual choice; 465 uses SSL on connect.'],
      ['Username and Password', 'The account Tesria signs in to the mail server with. The password is stored encrypted and never shown again; leave the field empty to keep it.'],
      ['From address', 'Who the email appears to come from. Most services require an address on a domain you have verified with them.'],
      ['Encryption', 'STARTTLS, SSL on connect, or None. Use None only for a mail server on the same private network.'],
    ], [200, 500]),
    p('Choose ', b('Save mail settings'), ', turn on ', b('Send email'), ', then ', b('Send test email to me'), '. It says straight away whether the mail server accepted the message.'),
    p('The setup wizard’s Email step fills in the same settings. Changing them needs the Email settings right, which administrators have by default.'),
  ))

  // -------------------------------------------------------------- Upgrading
  await page('Upgrading', root, doc(
    p('An upgrade replaces the software and keeps your data. Tesria updates its own database when it starts.'),
    h(2, 'Before you start'),
    ol(
      li(p('Read the ', b('Release notes'), ' for every version between yours and the new one.')),
      li(p('Take a backup: ', b('Administration → Backups → Back up now'), '. An upgrade that goes wrong is undone by restoring it.')),
    ),
    h(2, 'Upgrading'),
    p('From the Tesria folder:'),
    codeBlock('bash', 'git pull\ndocker compose up -d --build'),
    p('Building takes a few minutes, while the old version keeps running. Then each container is replaced, the database is updated, and Tesria is back, usually within a minute. People with a page open may need to reload it.'),
    p('To check, open ', c('/api/health'), ': it names the version now running.'),
    h(2, 'Going back'),
    p('Restoring a backup taken by a newer version into an older one is refused, because the older version would not understand the updated database. To go back, check out the version you had before, rebuild, and restore the backup you took before upgrading.'),
  ))

  // ------------------------------------------------------ Health and monitoring
  await page('Health checks and monitoring', root, doc(
    p('How to tell Tesria is well, and how to hear about it when it is not.'),
    h(2, 'The health address'),
    p(c('/api/health'), ' answers without signing in:'),
    codeBlock('json', '{"status":"ok","service":"tesria-api","version":"0.2.0","utc":"2026-09-23T05:26:02Z","maintenance":null}'),
    p('An answer means Tesria is running. ', c('maintenance'), ' is empty except while a backup is being restored, when the wiki can be read but not changed. Point an uptime monitor at this address.'),
    h(2, 'The containers'),
    codeBlock('bash', 'docker compose ps'),
    p('The database and both backup services report ', c('healthy'), ' or ', c('unhealthy'), '. The backup services count as unhealthy when they have not checked in for five minutes.'),
    h(2, 'Inside Tesria'),
    ul(
      li(p(b('Administration → Dashboard'), ': people, content and sign-in activity at a glance.')),
      li(p(b('Administration → Backups'), ': whether each backup is running, when it last worked, and the disk space left.')),
      li(p(b('Administration → Security'), ': alerts, such as repeated failed sign-ins, a failed backup or a disk running low. Every administrator gets them in the bell, and by email once email is set up.')),
    ),
    h(2, 'The audit log'),
    p('Every change an administrator makes is in the audit log, chained so that an edit or deletion inside it can be detected. Tesria checks the chain daily, and ', b('Verify now'), ' in ', b('Administration → Security'), ' checks it on demand. To check it from outside, with an administrator’s API token:'),
    codeBlock('bash', 'scripts/verify-audit-chain.sh https://wiki.example.com "$TESRIA_ADMIN_TOKEN"'),
    p('It exits 0 when the chain holds, so it can run from cron. Every audit entry is also written to the application log (', c('docker compose logs app'), '); sending that log somewhere else keeps a copy that someone with the database cannot touch.'),
  ))

  // ------------------------------------------------------ Backups and recovery
  const backups = await ensure('Backups and recovery', root)
  await page('Backups and recovery', root, doc(
    p('Tesria backs itself up from the moment it starts, with nothing to set up. These pages explain what it keeps, how to get it back, and how to keep a copy somewhere safer than the same machine.'),
    ...(await figure(backups, 'backups-page', 'Administration → Backups')),
    live('children', { depth: '1', sort: 'position' }),
  ))

  await page('How backups work', backups, doc(
    p('Two backup services run beside Tesria and each keeps its own kind of copy.'),
    table([
      ['', 'Database dumps and uploads', 'Physical backups and point-in-time recovery'],
      ['What it keeps', 'A copy of the database and an archive of every attachment, taken together', 'A copy of the whole database, plus a continuous record of every change since'],
      ['How often', 'Every 24 hours (BACKUP_INTERVAL_HOURS)', 'A full copy weekly and smaller ones daily, and the change record all the time'],
      ['What it gets back', 'The wiki as it was when that copy was taken', 'The wiki as it was at any moment you choose, to the second'],
      ['Encrypted', 'Only when copied offsite', 'Always, with BACKUP_ENCRYPTION_KEY'],
    ], [160, 270, 270]),
    p('Tesria keeps a third safety net of its own, separate from backups: every page keeps its ', b('history'), ', and deleted pages go to the ', b('trash'), '. A bad edit or a deleted page is put right from the page itself, with no backup involved.'),
    h(2, 'Administration → Backups'),
    ul(
      li(p('A card for each service: when it last worked, when it next runs, how far back you can restore, the disk space left, and the result of the last restore test.')),
      li(p(b('Back up now'), ' asks both services for a backup. Each starts within a minute.')),
      li(p(b('Test restore'), ' restores a backup somewhere temporary and checks it, without touching the wiki. A backup that has never been restored has not been proved to work.')),
      li(p(b('Disk space'), ' shows what the wiki, its backups and everything else take on this machine.')),
    ),
    p('A backup that fails, a service that stops checking in, a failed test or a disk running low raises an alert for every administrator.'),
    panel('warning', p(b('Every backup here is on the same machine as the wiki.'), ' A dead disk takes both. Set up at least one ', b('offsite copy'), '.')),
  ))

  const retention = await ensure('Retention', backups)
  await page('Retention', backups, doc(
    p('The retention policy decides how long backups are kept. It is under ', b('Administration → Backups'), ', and it applies to both kinds of backup and nothing else.'),
    ...(await figure(retention, 'retention-policy', 'The retention policy')),
    p('Either keep every backup forever, or keep the newest ', b('N'), ' backups and everything from the last ', b('D'), ' days. A backup is removed only when it is outside both, so there are always at least N, however old.'),
    h(2, 'Changing it'),
    ul(
      li(p(b('Review change'), ' shows what the new policy would remove before anything happens, and asks for your password.')),
      li(p('A change that keeps more takes effect at once.')),
      li(p('A change that could remove more ', b('waits 24 hours'), ' before it takes effect, and every administrator is alerted when it is saved. That is time for someone to notice if it was not meant, or was not you.')),
    ),
    p('Administrators can change it by default; the owner can take that right away from a role on the ', b('Roles'), ' tab.'),
    p('Offsite copies in the cloud keep their own count of full backups (', c('OFFSITE_CLOUD_RETENTION_FULL'), '), because an offsite copy is usually kept longer. A removable drive keeps a count with no time limit, so a drive left in a drawer for months is not emptied when it comes back.'),
  ))

  await page('Restoring and undo', backups, doc(
    p('A restore replaces the whole wiki with a backup. It is the one thing on the Backups page that uses a backup rather than making one, so it is guarded.'),
    h(2, 'Before restoring'),
    ul(
      li(p(b('A bad edit or a deleted page does not need a restore.'), ' Use the page’s ', b('History'), ', or the space’s ', b('Trash'), '. A restore takes back everyone’s work since the backup.')),
      li(p('Only the ', b('owner'), ' can restore, unless the owner grants the right to a role.')),
    ),
    h(2, 'Restoring'),
    ol(
      li(p('In ', b('Administration → Backups'), ', find the backup and choose ', b('Restore'), '.')),
      li(p('Type the backup’s name back, and your password. Someone who signs in with SSO and has no password gets a one-time code instead.')),
    ),
    p('Then, in order:'),
    ol(
      li(p(b('A backup is taken first'), '. It cannot be skipped, and nothing is restored if it fails.')),
      li(p('The wiki becomes ', b('read-only'), ' for everyone. Reading works; saving waits, and open editors are closed so they cannot write old pages back.')),
      li(p('The backup is restored beside the live wiki and checked. A backup from a newer version of Tesria is refused.')),
      li(p('The restored copy takes the live wiki’s place, in moments. The replaced wiki is ', b('kept'), ', not deleted.')),
      li(p('Tesria restarts, and your page reloads by itself.')),
    ),
    p('You can cancel until the switch in step 4. People who signed in after the backup was taken will need to sign in again, possibly including you. The restore is recorded in the audit log and raised as a critical alert for every administrator.'),
    h(2, 'To a moment in time'),
    p('A physical backup row restores to any second it covers: choose the time as well. Its undo is another restore to the moment the first one began, which the page offers.'),
    h(2, 'Undo'),
    p('After a restore, the Backups page shows ', b('The copy kept before the last restore'), ', with two choices:'),
    ul(
      li(p(b('Undo the restore'), ' puts the kept copy back, taking a backup of the current state first.')),
      li(p(b('Remove the copy'), ' deletes it to free the disk, and asks for your password. After that there is no undo.')),
    ),
    p('The kept copy is removed on its own when the retention policy would remove a backup taken at the time of the restore.'),
    h(2, 'From the command line'),
    p('When the wiki itself will not start, the same restore runs from the server:'),
    codeBlock('bash', 'docker compose exec backup /scripts/restore.sh db-20260920T030000Z.dump'),
    p('Add ', c('RESTORE_DRY_RUN=1'), ' in front to see every step without changing anything.'),
  ))

  const offsite = await ensure('Offsite copies', backups)
  await page('Offsite copies', backups, doc(
    p('A copy of your backups somewhere other than the server, so that losing the machine does not lose the wiki. There are three kinds, and you can use any of them together.'),
    ...(await figure(offsite, 'storage-targets', 'Storage targets, before any is set up')),
    table([
      ['', 'Cloud storage', 'Network drive', 'Removable drive'],
      ['What', 'Any S3-compatible storage; Backblaze B2 is the documented choice', 'A share on a NAS, mounted on the server', 'A USB disk you plug in and take away'],
      ['When', 'After every backup, and changes stream there continuously', 'After every backup', 'Only when someone presses Copy now'],
      ['What it holds', 'Everything, including restoring to any moment', 'The dumps and the attachments', 'The dumps and the attachments'],
    ], [140, 190, 190, 180]),
    p('Every copy is encrypted on the server before it leaves, each with its own passphrase. The settings go in ', c('.env'), ' rather than on the admin page, so that the keys never enter the database and never travel inside a backup. The page shows fingerprints instead of the keys themselves.'),
    panel('warning', p(b('Keep the passphrases somewhere other than the server.'), ' A copy whose passphrase is lost cannot be read by anyone.')),
    h(2, 'Cloud storage'),
    ol(
      li(p('Create a bucket and an access key with your provider.')),
      li(p('Fill in the ', c('OFFSITE_CLOUD_'), ' settings in ', c('.env'), ' (see the ', b('Configuration reference'), '), with a new passphrase from ', c('openssl rand -hex 32'), '.')),
      li(p('Run ', c('docker compose up -d'), '.')),
      li(p('Press ', b('Test connection'), ' on the new card.')),
    ),
    panel('warning', p(b('If the cloud stays unreachable, act on the alert.'), ' Changes wait on the server until the cloud accepts them. After a limit (', c('OFFSITE_ARCHIVE_QUEUE_MAX'), ') they are dropped, and then restoring to a moment before the outage no longer works on this machine either. Tesria alerts long before that: fix the connection, or remove the cloud settings and restart, then take a new backup.')),
    h(2, 'A network drive'),
    ol(
      li(p('Mount the share on the server first; Tesria never mounts anything itself. On a Mac, connect to it in Finder and allow Docker Desktop to use the folder when it asks. On Linux, add it to ', c('/etc/fstab'), ' with ', c('_netdev,nofail'), '.')),
      li(p('Set ', c('OFFSITE_NAS_PATH'), ' and ', c('OFFSITE_NAS_PASSPHRASE'), ', and run ', c('docker compose up -d'), '.')),
      li(p('Mark the share as Tesria’s, once:')),
    ),
    codeBlock('bash', 'docker compose exec backup /scripts/claim-target.sh nas'),
    p('That leaves a small marker file on the share, and Tesria only copies to a folder that has it. When a share is not mounted its folder is still there, empty, on the server’s own disk; without the marker, backups would quietly fill that disk instead of reaching the NAS.'),
    p('Turn on your NAS’s own snapshots for that folder too. A snapshot the Tesria server cannot delete protects the copies even if the server is compromised.'),
    h(2, 'A removable drive'),
    p('Set ', c('OFFSITE_REMOVABLE_PATH'), ' and ', c('OFFSITE_REMOVABLE_PASSPHRASE'), ', plug the drive in, and claim it once with ', c('claim-target.sh removable'), '. Then, whenever the drive is plugged in, press ', b('Copy now'), '. When the card says it is safe to remove, every byte is on the drive.'),
    p('To eject it cleanly first, stop the backup service, eject, and start it again:'),
    codeBlock('bash', 'docker compose stop backup\n# eject the drive\ndocker compose up -d backup'),
    p('Format the drive as exFAT rather than FAT32, which cannot hold files over 4 GB. And a removable drive alone is not an offsite backup: between the times someone plugs it in, there is no copy anywhere else, and the Backups page says so.'),
  ))

  await page('Testing a target and the cloud budget', backups, doc(
    h(2, 'Test connection'),
    p('Each card under ', b('Storage targets'), ' has ', b('Test connection'), '. It asks the backup services to reach that target and open it with its passphrase, and changes nothing. Use it after editing ', c('.env'), ', and run ', c('docker compose up -d'), ' first: the test uses the settings the services are running with, not the file.'),
    p('The answer usually comes within a minute. If a backup is running, the test waits for it to finish.'),
    h(2, 'The cloud budget'),
    p('Cloud storage has no free space to measure, so on its own the cloud card shows only how much is stored. Set ', c('OFFSITE_CLOUD_BUDGET_GB'), ' to the amount you mean to pay for, and the card shows what is left of it and says when it has been passed. It is a number to watch: nothing is refused or removed for going over.'),
  ))

  await page('Restore drills', backups, doc(
    p('A backup nobody has restored has not been proved to work. Tesria proves its copies on a schedule, so you do not find out on the day you need one.'),
    ul(
      li(p(b('After every backup'), ', each offsite copy checks that it is complete and undamaged.')),
      li(p(b('Every 30 days'), ' (', c('OFFSITE_DRILL_DAYS'), '), each offsite copy is restored for real: the newest dump is taken out of it, loaded into a temporary database, counted and thrown away. Nothing live is touched.')),
      li(p(b('Test restore'), ' on the Backups page does the same for the copies on this machine, whenever you ask.')),
    ),
    p('The first check asks whether the copy is intact; the drill asks whether it still turns back into a wiki. A copy can pass the first and fail the second, which is why a failed drill is a critical alert. Each card shows ', b('Last restore drill'), '.'),
    p('Once or twice a year, also rehearse ', b('When the machine is gone'), ' on a spare computer. It is the one procedure you should not be reading for the first time when you need it.'),
  ))

  await page('When the machine is gone', backups, doc(
    p('The server is destroyed, stolen, or will not start, and all you have is an offsite copy. This is how to come back.'),
    h(2, 'What you need'),
    ol(
      li(p(b('The passphrases'), ', from wherever you kept them: ', c('OFFSITE_CLOUD_PASSPHRASE'), ', ', c('OFFSITE_NAS_PASSPHRASE'), ' or ', c('OFFSITE_REMOVABLE_PASSPHRASE'), ' for the copy you have, and ', c('BACKUP_ENCRYPTION_KEY'), ' to restore to a moment in time from the cloud.')),
      li(p(b('Access to the copy'), ': the cloud account’s keys, or the drive or share itself.')),
      li(p(b('A computer with Docker'), ' and the Tesria files.')),
    ),
    p('You get back every page and every attachment. You do not get back ', c('.env'), ', which is in no backup because it holds the keys: you write a new one.'),
    h(2, '1. Look at what the copy holds'),
    p('The copies are standard restic repositories, readable with restic and the passphrase on any computer, with no Tesria involved. For a drive:'),
    codeBlock('bash', 'docker run --rm -e RESTIC_PASSWORD=your-passphrase -v /Volumes/your-drive:/t restic/restic -r /t/restic snapshots'),
    p('For the cloud copy:'),
    codeBlock('bash', 'docker run --rm -e RESTIC_PASSWORD=your-passphrase -e AWS_ACCESS_KEY_ID=key -e AWS_SECRET_ACCESS_KEY=secret restic/restic -r s3:https://endpoint/bucket/tesria/files snapshots'),
    h(2, '2. Start an empty Tesria'),
    p('Get the Tesria files, write a new ', c('.env'), ' (new database passwords; the same ', c('DOMAIN'), ' to keep the same address), and start only the database:'),
    codeBlock('bash', 'docker compose up -d db pgbackrest'),
    p('Do not start the whole of Tesria yet.'),
    h(2, '3. Restore the dump and the attachments'),
    codeBlock('bash', 'docker run --rm -e RESTIC_PASSWORD=your-passphrase -v "$PWD/restored:/out" -v /Volumes/your-drive:/t restic/restic -r /t/restic restore latest --target /out'),
    p('That leaves the newest dump in ', c('restored/backups/'), ' and the attachments beside it. Load them:'),
    codeBlock('bash', 'docker compose up -d backup\ndocker compose cp restored/backups/ backup:/backups/\ndocker compose exec backup /scripts/restore.sh db-<stamp>.dump'),
    p('Use the dump’s real name in place of ', c('db-<stamp>.dump'), '. The script also puts back the attachments archived with it.'),
    h(2, 'Or: to a moment in time, from the cloud'),
    p('To get back to a minute rather than to last night, restore the cloud’s physical backup instead. Put the ', c('OFFSITE_CLOUD_'), ' settings and the original ', c('BACKUP_ENCRYPTION_KEY'), ' in the new ', c('.env'), ' first, then:'),
    codeBlock('bash', 'docker compose exec pgbackrest gosu postgres pgbackrest --stanza=main --repo=2 --type=time --target="2026-09-22 14:30:00+00" restore'),
    h(2, '4. Start it and check it'),
    codeBlock('bash', 'docker compose up -d'),
    p('Then, in this order: sign in; open a page with an image on it, which proves the attachments came back; check that ', b('Administration → Backups'), ' shows both services healthy; and check that your offsite copy is listed under Storage targets.'),
    h(2, '5. Before calling it done'),
    p('Take a new backup straight away and set the retention policy again. A week later, check that the restore drill has run on the new machine: the copy you just used was the old machine’s, and the new one has not proved anything yet.'),
  ))

  // ------------------------------------------------------- Security hardening
  const hardening = await ensure('Security hardening', root)
  await page('Security hardening', root, doc(
    p('What to do before a Tesria can be reached from the internet. None of it is something the software can do for you. On a private network, the account items still apply.'),
    h(2, 'Configuration'),
    tasks(
      task(false, c('DOMAIN'), ' is a real name you control, and ', c('ACME_EMAIL'), ' is read by someone.'),
      task(false, c('CADDYFILE=deploy/Caddyfile.public'), ' is set (see ', b('HTTPS and domains'), ').'),
      task(false, c('POSTGRES_PASSWORD'), ', ', c('APP_DB_PASSWORD'), ', ', c('BACKUP_ENCRYPTION_KEY'), ' and ', c('COLLAB_SHARED_SECRET'), ' are long, random and all different. ', c('APP_DB_PASSWORD'), ' is not empty.'),
      task(false, 'Only ports 80 and 443 are open to the outside. Nothing publishes the app (8080), the database (5432) or the collaboration service (8090).'),
      task(false, 'If you put your own proxy in front, ', c('PROXY_TRUSTED_NETWORKS'), ' names exactly that proxy.'),
    ),
    h(2, 'Accounts'),
    tasks(
      task(false, 'The owner has two-factor on and has saved their recovery codes. Nobody can reset the owner.'),
      task(false, 'Every administrator has two-factor on, and ', b('Require two-factor for administrators'), ' is on.'),
      task(false, b('Administration → Roles'), ' has been reviewed, and each role has only what it needs.'),
      task(false, b('Who can join'), ' is Invite only, unless you mean to run an open community.'),
      task(false, b('Allow public spaces'), ' stays off until you mean to publish a space to people who are not signed in.'),
    ),
    ...(await figure(hardening, 'kill-switches', 'Kill switches in Administration → Security')),
    h(2, 'Operations'),
    tasks(
      task(false, 'Backups run, a restore has been rehearsed, and there is at least one offsite copy.'),
      task(false, 'The passphrases in ', c('.env'), ' are kept somewhere other than the server.'),
      task(false, 'Email works, so alerts reach administrators (', b('Send test email to me'), ').'),
      task(false, 'The application log, which carries a copy of the audit log, is sent somewhere durable.'),
      task(false, c('scripts/verify-audit-chain.sh'), ' runs from cron and someone watches its result.'),
      task(false, 'Someone watches for new releases and upgrades promptly.'),
    ),
    h(2, 'What Tesria does for you'),
    p('Limits on sign-in attempts from one address and lockouts for one account; alerts for patterns such as password spraying or mass deletion; a blocklist for addresses; strict browser security headers; attachments that cannot run script as the site; outgoing webhooks that cannot reach the server’s own network; and an audit log that the running application cannot alter. The ', b('Security'), ' section explains each, and what it does not cover.'),
  ))

  // ------------------------------------------------- Uninstalling and moving
  await page('Uninstalling and moving', root, doc(
    h(2, 'Stopping'),
    codeBlock('bash', 'docker compose down'),
    p('Stops and removes the containers. The data stays in its volumes, and ', c('docker compose up -d'), ' brings everything back as it was.'),
    h(2, 'Moving one space'),
    p('A ', b('wiki pack'), ' carries one space, with its pages, history, comments and attachments, to another Tesria. Export it with ', b('Export as a pack'), ' on the Details tab of the space’s ', b('Space settings'), ', then import it on the other instance from ', b('Spaces → Import a pack'), '. See ', b('Wiki packs'), ' in the User manual.'),
    h(2, 'Moving the whole instance'),
    ol(
      li(p('On the old server, take a backup: ', b('Administration → Backups → Back up now'), '.')),
      li(p('Copy the ', c('backups'), ' volume to the new server:')),
    ),
    codeBlock('bash', 'docker run --rm -v tesria_backups:/b -v "$PWD":/out alpine tar czf /out/tesria-backups.tgz -C /b .'),
    ol(
      li(p('On the new server, install Tesria with a new ', c('.env'), ', start ', c('db pgbackrest backup'), ', copy the dump and its uploads archive into the backup service, and restore the dump, as in ', b('When the machine is gone'), '.')),
      li(p('Point your domain at the new server. If you use the local certificate, every device trusts the new one again.')),
    ),
    h(2, 'Removing Tesria completely'),
    panel('error', p(b('This deletes the wiki and every backup on this machine.'), ' It cannot be undone. Take a copy you can restore from somewhere else first, or be sure you never want it.')),
    codeBlock('bash', 'docker compose down -v --rmi local'),
    p(c('-v'), ' deletes the volumes, which hold the database, the attachments and the backups. ', c('--rmi local'), ' deletes the images built for Tesria. Offsite copies are not touched: delete those from your cloud storage, NAS or drive yourself.'),
  ))
}
