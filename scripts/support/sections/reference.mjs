// Troubleshooting, FAQ, Glossary, Release notes, Security, License and
// credits (dev-plan 10.5). Text pages: what they describe is shown elsewhere.
//
// Facts from the runbooks (docs/tls-and-lan-access.md, backup-recovery.md,
// security.md), SECURITY.md, NOTICE, and the fixes of 2026-09-23.

export const shots = []

export async function build({ top, page, doc, p, h, text, bold, code, ul, ol, li, panel, table, codeBlock, live, expand }) {
  const b = (t) => text(t, bold)
  const c = (t) => text(t, code)

  // ======================================================== Troubleshooting
  const q = (question, ...answer) => expand(question, ...answer)
  await page('Troubleshooting', null, doc(
    p('Problems people run into, and what to do. Start with ', c('docker compose ps'), ' and ', c('/api/health'), ': most problems show in one of them.'),
    h(2, 'Installing and starting'),
    q('The database keeps restarting on a new install',
      p('It was started on its own. On a new install the database waits for the backup service to set up its repository, and restarts until it has. Start them together: ', c('docker compose up -d'), ', or at least ', c('docker compose up -d db pgbackrest'), '.')),
    q('The page does not load at all',
      p('Check that every container is running (', c('docker compose ps'), ') and read the app’s log: ', c('docker compose logs app --tail 50'), '. On the first start the build and database setup take a few minutes.')),
    q('/api/health answers but the page is blank',
      p('Reload without the cache (Shift+Reload). After an upgrade, an open tab can hold the old version of the app.')),
    h(2, 'Certificates and addresses'),
    q('The browser warns that the connection is not private',
      p('With ', c('DOMAIN=localhost'), ' or on a private network, Tesria uses its own certificate authority. Trust it once on each device: see ', b('Trusting the local certificate'), '.')),
    q('The warning is still there after trusting the certificate',
      p('Quit the browser completely and open it again. If you reach the server by a bare address such as 192.168.1.50, use a name instead: browsers cannot match a certificate to a bare address.')),
    q('Every device warns again after moving the server',
      p('The certificate authority lives in the ', c('caddy_data'), ' volume. A new volume means a new authority, which every device has to trust again.')),
    q('A real domain gets no certificate',
      p('Let’s Encrypt has to reach the server on port 80. Check that the domain points at the server and that ports 80 and 443 are open to the internet, then read ', c('docker compose logs caddy'), '.')),
    h(2, 'Signing in'),
    q('“Incorrect email or password” with the right password',
      p('After 5 wrong attempts the account is locked for a while, and even the right password is refused until it ends: from a minute up to 15. An administrator can unlock it in Administration → Users.')),
    q('“Too many attempts. Wait a minute and try again.”',
      p('Too many sign-in attempts from your address in a minute. Wait a minute. If many people share one address, an administrator can raise the limit in Administration → Security.')),
    q('Lost the phone with the authenticator app',
      p('Sign in with one of your recovery codes in place of the six-digit code, then set up two-factor again on your profile. Without a recovery code, ask an administrator to turn your two-factor off (Administration → Users → Turn off two-factor), then sign in with your password and set it up again. Nobody can do this for the owner, so the owner’s recovery codes matter most.')),
    q('Signed out after a restore',
      p('Sessions created after the backup that was restored no longer exist. Sign in again.')),
    h(2, 'Editing'),
    q('“Offline: your changes are local until reconnected”',
      p('The editor lost its connection to the live-editing service. Keep writing; changes are sent when it reconnects. If it never reconnects, check the ', c('collab'), ' container, and that ', c('COLLAB_SHARED_SECRET'), ' is set.')),
    q('“This page changed while you were editing, so it was not published”',
      p('Someone else updated the page. Their change is highlighted in your editor: accept or reject it, then publish again.')),
    q('The Edit button is missing',
      p('You can read the page but not change it: its space or the page itself is restricted. Ask a space administrator.')),
    q('A picture will not upload',
      p('Attachments are limited to 25 MB each. A page that has never been published can take pasted pictures; other files go on its Attachments tab after publishing.')),
    q('An embed says the site is not allowed',
      p('Only sites on the instance’s allowed list can be embedded. An administrator can add one in Administration → Settings → Embeds. Meanwhile the link still works.')),
    h(2, 'Exports and backups'),
    q('PDF export says there is no renderer',
      p('PDF and HTML exports need the PDF service, which runs only when ', c('PDF_SHARED_SECRET'), ' is set. Export as Markdown meanwhile.')),
    q('An export option is missing',
      p('Either your role may not export, or the space has turned that format off (Space settings → Exports).')),
    q('A backup service shows “Not reporting” or “Overdue”',
      p('Check the container: ', c('docker compose ps backup pgbackrest'), ' and ', c('docker compose logs backup --tail 50'), '. A failed backup is retried after 15 minutes.')),
    q('The cloud backup fails with a DNS or 404 error',
      p('Try ', c('OFFSITE_CLOUD_URI_STYLE=path'), ' (MinIO and most self-hosted storage) or ', c('host'), ' (Backblaze and AWS), then ', c('docker compose up -d'), ' and Test connection.')),
    q('A network drive backup seems stuck on a Mac',
      p('Docker Desktop is waiting for permission to use the folder. Allow it in the prompt, or in Docker Desktop’s file sharing settings.')),
    q('Email never arrives',
      p('Use ', b('Send test email to me'), ' in Administration → Settings: it says what the mail server answered. Check the port and encryption pair (587 with STARTTLS, or 465 with SSL), and that the From address is one your mail service lets you send as.')),
  ))

  // =================================================================== FAQ
  await page('FAQ', null, doc(
    q('Is Tesria free?', p('Yes. It is open source under the Apache License 2.0: use it, change it and run it for anything, including commercially.')),
    q('Where is my data?', p('On your server, in Docker volumes: the database, the attachments and the backups. Nothing is sent anywhere unless you set up offsite backups, email or webhooks.')),
    q('Does Tesria need the internet?', p('No. It runs on a private network with its own certificates. The internet is needed only for a public certificate, embeds from other sites, email and cloud backups.')),
    q('How many people can use it?', p('A small server (2 cores, 2 GB of memory) is comfortable for a team. See System requirements. Search, backups and exports are the parts that grow with the size of the wiki.')),
    q('Can I import from Confluence or another wiki?', p('Not directly yet. Tesria can import its own wiki packs, and scripts can create pages through the REST API or an assistant through MCP.')),
    q('Can I move a space to another Tesria?', p('Yes: export it as a wiki pack and import the pack. See Wiki packs.')),
    q('Can people read without an account?', p('Yes, for spaces you publish. Both the instance switch and the space have to be turned on. See Public reading.')),
    q('Does it work on phones?', p('Yes, including writing. See Tesria on phones and tablets.')),
    q('Can several people edit a page at once?', p('Yes, when live editing is on (COLLAB_SHARED_SECRET). Everyone sees each other’s changes as they type.')),
    q('How do I get my pages out?', p('As Markdown, HTML or PDF page by page, as a static website for a whole space, as a wiki pack, or through the API.')),
    q('What happens if the server dies?', p('Restore from an offsite copy on a new machine: see When the machine is gone. Without an offsite copy, the backups die with the server, which is why setting one up matters.')),
    q('Can AI assistants use it?', p('Yes, through MCP, as whoever’s token they hold, and only within that person’s permissions. See MCP.')),
  ))

  // ============================================================== Glossary
  const term = (t, d) => [p(b(t)), d]
  await page('Glossary', null, doc(
    table([
      ['Term', 'Meaning'],
      term('Administrator', 'An account in the administrator tier, which runs the instance day to day.'),
      term('Attachment', 'A file stored with a page, listed on its Attachments tab.'),
      term('Audit log', 'The record of administrative changes, chained so that tampering is detected.'),
      term('Backup service', 'One of the two containers that back Tesria up: backup (dumps and files) and pgbackrest (physical backups).'),
      term('Draft', 'A page, or unpublished changes to one, that only its editors can see.'),
      term('Element', 'Anything you can put on a page: a table, a panel, a picture, a chart.'),
      term('Excerpt', 'The part of a page marked for other pages to include.'),
      term('Group', 'A named set of people, used to share spaces and pages with them together.'),
      term('Instance', 'One installation of Tesria, with its own accounts and spaces.'),
      term('Key', 'A space’s short name, such as ENG, used in its address. It cannot change.'),
      term('Label', 'A tag on a page, for finding related pages across spaces.'),
      term('Live content', 'A block that looks its content up each time the page is read, such as a list of the pages under this one.'),
      term('MCP', 'Model Context Protocol: how AI assistants connect to tools. Tesria has an MCP server.'),
      term('Offsite copy', 'A copy of the backups somewhere other than the server: the cloud, a network drive, or a removable drive.'),
      term('Open space', 'A space with no permissions set, which everyone signed in can read, edit and administer.'),
      term('Owner', 'The one account that owns the instance and cannot be reset or suspended by anyone else.'),
      term('Point-in-time recovery', 'Restoring the wiki to any moment, not only to when a backup was taken.'),
      term('Public space', 'A space published for reading without signing in.'),
      term('Recovery code', 'A single-use code that signs you in, or resets your password, when your usual way in is lost.'),
      term('Restriction', 'A limit on who may view or edit one page and the pages under it.'),
      term('Retention policy', 'How long backups are kept.'),
      term('Right', 'Permission to do one thing across the instance, such as Publish spaces. Roles hold rights.'),
      term('Role', 'A named set of rights within a tier, such as the built-in Administrator role.'),
      term('Slash menu', 'The list of elements that appears when you type / in the editor.'),
      term('Space', 'A collection of pages for one team, project or subject.'),
      term('Template', 'A page to start new pages from.'),
      term('Tier', 'User, administrator or owner: who may act on whom.'),
      term('Token', 'An API token: a secret that lets a script or assistant act as you.'),
      term('Version', 'One saved state of a page. Every publish and update adds one.'),
      term('Watch', 'Asking to be told about changes to a page or a space.'),
      term('Wiki pack', 'A zip holding one space with its history, for moving or keeping it.'),
    ], [220, 480]),
  ))

  // ========================================================== Release notes
  const notes = top['Release notes']
  await page('Release notes', null, doc(
    p('What changed in each version of Tesria. ', c('/api/health'), ' shows the version you are running.'),
    live('children', { depth: '1', sort: 'position' }),
  ))
  await page('Tesria 0.2', notes, doc(
    p('The first public release.'),
    h(2, 'Writing'),
    ul(
      'A block editor with a slash menu and more than 30 elements: panels, layouts, tables, code, Mermaid diagrams, math, charts, galleries, video, animations, embeds and more.',
      'Twelve kinds of live content, from child pages to task reports.',
      'Several people editing a page at once, with changes from scripts and assistants shown as tracked changes.',
      'Comments on a page or on a selection, mentions, watching and notifications, by email too.',
    ),
    h(2, 'Organizing and sharing'),
    ul(
      'Spaces with permissions for people and groups, page restrictions, labels, templates and a trash.',
      'Full-text search.',
      'Exports as Markdown, HTML and PDF; a whole space as a static website or a wiki pack; public reading for published spaces.',
      'A REST API with tokens and webhooks, and an MCP server for AI assistants.',
    ),
    h(2, 'Running it'),
    ul(
      'One Docker Compose file, with automatic HTTPS.',
      'Backups with point-in-time recovery, restores and undo from the browser, offsite copies to the cloud, a network drive or a removable drive, and scheduled restore drills.',
      'A setup wizard, roles with assignable rights, security alerts, rate limits, lockouts, two-factor sign-in, single sign-on, and an audit log that detects tampering.',
      'Branding: your name, logo, favicon and colors.',
      'A layout for phones and tablets throughout.',
    ),
  ))

  // =============================================================== Security
  await page('Security', null, doc(
    p('How Tesria protects a wiki, and what it expects of whoever runs it. Before putting an instance on the internet, work through ', b('Security hardening'), '.'),
    h(2, 'Who it defends against'),
    table([
      ['Threat', 'Defenses'],
      ['Scanners trying every address on the internet', 'Rate limits, a blocklist, and nothing that names the software except the health check.'],
      ['Password guessing and stolen password lists', 'Limits per address, lockouts per account, two-factor, and alerts for credential stuffing.'],
      ['A trusted editor who goes wrong', 'Uploaded files that cannot run as the site, webhooks that cannot reach the server’s network, an alert on mass deletion, and an audit log they cannot edit.'],
      ['Someone who gets the database password', 'The application runs as a database account that cannot change the audit log, the log’s chain shows tampering, and every entry is also written to the application log.'],
      ['An unattended signed-in browser', 'Destructive actions ask for the password again; sessions expire; each can be signed out.'],
    ], [240, 460]),
    p('Not in scope: a compromised server or container runtime, a malicious owner, and denial of service by sheer volume.'),
    h(2, 'Permissions'),
    ul(
      'Every request is checked against the space’s permissions and the page’s restrictions. A page you may not see answers “not found”, so its existence is not revealed.',
      'Rights decide what someone may do across the instance; roles hold rights; the owner can change what every role holds.',
      'Notifications, search, live content, exports and the dashboard only ever show what the reader may see.',
    ),
    h(2, 'Reporting a vulnerability'),
    p('Please report privately, not in a public issue: use GitHub’s private vulnerability reporting on the Tesria repository, or the contact in ', c('SECURITY.md'), ' there. Include what you found, how to reproduce it, and the version from ', c('/api/health'), '. Test only against your own instance.'),
  ))

  // ====================================================== License and credits
  await page('License and credits', null, doc(
    p('Tesria is open source, under the ', b('Apache License, Version 2.0'), '. Copyright 2026 Brian Rodriguez.'),
    p('You may use, change and redistribute it, commercially or not, provided you keep the license and the NOTICE file with it. It comes without warranty.'),
    h(2, 'Built with'),
    table([
      ['Component', 'License'],
      ['ASP.NET Core, Entity Framework Core', 'Apache-2.0, MIT'],
      ['Npgsql', 'PostgreSQL Licence'],
      ['MailKit, MimeKit', 'MIT'],
      ['Isopoh.Cryptography.Argon2', 'CC0-1.0'],
      ['Otp.NET', 'MIT'],
      ['SkiaSharp', 'MIT'],
      ['React', 'MIT'],
      ['TipTap, ProseMirror', 'MIT'],
      ['Yjs, Hocuspocus', 'MIT'],
      ['Mermaid', 'MIT'],
      ['KaTeX', 'MIT'],
      ['lowlight, highlight.js', 'BSD-3-Clause'],
      ['PostgreSQL', 'PostgreSQL Licence'],
      ['pgBackRest', 'MIT'],
      ['restic', 'BSD-2-Clause'],
      ['Caddy', 'Apache-2.0'],
      ['Playwright, Chromium', 'Apache-2.0, BSD-3-Clause'],
    ], [360, 340]),
    p('The full list, with versions, is in the repository’s lock files and project file.'),
    h(2, 'In this site'),
    ul(
      li(p('The video on the Embed page is ', text('Big Buck Bunny', { type: 'italic' }), ', © Blender Foundation, licensed under Creative Commons Attribution 3.0.')),
      li(p('Everyone and everything in Tesria Demo, the space these pages’ pictures come from, is fictional.')),
    ),
  ))
}
