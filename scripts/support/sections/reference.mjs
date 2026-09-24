// Troubleshooting, FAQ, Glossary, Release notes, Security, License and
// credits (dev-plan 10.5), rewritten to the owner's rules of 2026-09-23
// (WRITING.md, the pilot pages). Text pages: what they describe is pictured
// on the pages they link to.
//
// Facts checked 2026-09-24 against docs/tls-and-lan-access.md,
// docs/backup-recovery.md, docs/security.md, SECURITY.md, NOTICE, LICENSE,
// deploy/Caddyfile, deploy/backup/common.sh (15 minutes, doubling, at most 6
// hours), SiteSettings (lockout 5 tries, 1 to 15 minutes; 10 sign-in
// attempts a minute), AuthEndpoints (sessions 14 days idle, 90 at most; the
// 5 minute password window), AdminEndpoints (who may turn off whose
// two-factor), ExportEndpoints (the renderer message), alertKinds.ts, the
// Backups tab's states (AdminBackupsPage), the email settings
// (AdminSettingsPage), the editor's messages (CollabStatus, PageEditor), and
// the live content kinds registered in Program.cs (twelve).
//
// Left out as unconfirmed: team sizes a server suits, and any version
// number but the one the app reports (0.2). Versioning is planned (dev-plan
// Phase 16) and not described.

export const shots = []

export async function build({ top, page, doc, p, h, text, bold, italic, code, ul, ol, li, panel, codeBlock, live, expand, toc, pageLink }) {
  const b = (t) => text(t, bold)
  const c = (t) => text(t, code)
  const i = (t) => text(t, italic)
  const q = (question, ...answer) => expand(question, ...answer)
  /** A glossary entry: the term in bold, then what it means. */
  const term = (t, ...rest) => li(p(b(t), ': ', ...rest))

  // ======================================================== Troubleshooting
  await page('Troubleshooting', null, doc(
    p('Something is not working. This page is organized by what you see, such as a warning in the address bar or an email that never comes, and for each one explains the usual causes and what to do about them, most likely first.'),
    panel('note', p(b('Throughout this page, '), c('your-server'), b(' stands for your server’s address:'), ' whatever you type into the browser to open Tesria, without ', c('https://'), '. Commands that start with ', c('docker compose'), ' are typed in a terminal on the computer that runs Tesria, in the Tesria folder.')),
    h(2, 'Two checks that answer most questions'),
    ol(
      li(p(b('Is Tesria running?'), ' Open ', c('https://your-server/api/health'), '. If it answers with a line that includes ', c('"status":"ok"'), ', Tesria itself is fine and the problem is somewhere between it and you. If nothing answers, see ', i('The page will not load at all'), ', below.')),
      li(p(b('Are all its parts running?'), ' Tesria is several programs working together, each in its own container. On the server, run ', c('docker compose ps'), '. Each should say ', c('running'), ' (or ', c('Up'), '), and those with a health check ', c('healthy'), '. One that says ', c('restarting'), ' or ', c('exited'), ' is where to look.')),
    ),
    toc(),

    // ---------------------------------------------------------- in the browser
    h(2, 'Opening Tesria'),

    h(3, 'The address bar says “Not secure”, or the browser warns before opening Tesria'),
    p('The browser does not trust the certificate your server showed it. The connection is still encrypted; the browser just cannot confirm who issued the certificate. The usual causes:'),
    ul(
      li(p(b('This device has not been told to trust your server yet.'), ' A Tesria on your own network makes its own certificate, which no browser trusts until you say so, once on each device. ', pageLink('Trusting the local certificate'), ' walks you through it in about three minutes.')),
      li(p(b('You opened Tesria by a number,'), ' such as 192.168.1.50. Certificates are issued for names, so a number keeps warning even on a device that trusts the server. Open it by its name instead, such as ', c('studio.local'), ': see ', pageLink('Opening Tesria by name'), '.')),
      li(p(b('The browser still remembers the old answer.'), ' Quit it completely and open it again. Closing the window is not always enough: in Chrome or Edge, type ', c('chrome://restart'), ' or ', c('edge://restart'), ' into the address bar; in Safari, press ', b('⌘ Q'), '.')),
      li(p(b('Firefox keeps its own list.'), ' A device can trust your server and Firefox still not. ', pageLink('Trusting the local certificate'), ' has the extra step, under ', i('Firefox'), '.')),
      li(p(b('Every device started warning at once.'), ' The server made a new certificate authority, because the place it keeps certificates was deleted, for example by ', c('docker compose down -v'), ' or a move to a new machine without it. Every device needs to trust the server again. Ordinary restarts and upgrades never do this.')),
    ),
    panel('info', p(b('On a real web address,'), ' such as ', c('wiki.example.com'), ', a warning means the server could not get its free public certificate. It needs the address to point at the server, and ports 80 and 443 open to the internet. ', c('docker compose logs caddy'), ' says what went wrong. See ', pageLink('HTTPS and domains'), '.')),

    h(3, 'Tesria’s name takes a few tries to open'),
    p('You type ', c('studio.local'), ' and the browser says it cannot find the server, then it works on the second or third try. That is how names ending in ', c('.local'), ' are found: the device calls out to the whole network, “who is studio?”, and waits for the answer. Wi-Fi sometimes drops those calls, Windows asks your router first and can give up before the answer arrives, and a sleeping server answers late.'),
    p('None of it is Tesria, and all of it can be fixed: keep the server awake and, if you can, plugged in with a cable; check two router settings; or give the server a fixed address and a name your whole network knows. ', pageLink('Opening Tesria by name'), ' explains each, from easiest to most thorough.'),

    h(3, 'The page will not load at all'),
    p('The browser says it cannot reach the site, or waits and gives up.'),
    ul(
      li(p(b('Tesria is not running.'), ' Run ', c('docker compose ps'), ' on the server. If nothing is listed, or something has exited, start everything with ', c('docker compose up -d'), '.')),
      li(p(b('It is still starting.'), ' The very first start builds Tesria and sets up its database, which takes a few minutes. After a restart, give it a minute.')),
      li(p(b('Something failed to start.'), ' Read the end of its log: ', c('docker compose logs app --tail 50'), ' for Tesria itself, or the name of whichever part is not running. The last lines usually say why.')),
      li(p(b('The device is on another network.'), ' A phone on mobile data, or a laptop on a guest Wi-Fi, cannot reach a server on your home or office network. Guest networks usually keep their devices apart on purpose.')),
      li(p(b('The address is wrong.'), ' Check the name, and that it starts with ', c('https://'), '.')),
    ),
    panel('note', p(b('The database keeps restarting on a new install?'), ' It was started on its own. On a new install the database waits for the backup service to be ready, and restarts until it is. Start them together: ', c('docker compose up -d'), '. See ', pageLink('Installing with Docker Compose'), '.')),

    h(3, 'The page is blank, or looks wrong after an upgrade'),
    p('A tab that was open during an upgrade can hold on to parts of the old version. Close the tab and open Tesria again, or reload while holding ', b('Shift'), ' (in Chrome, Edge and Firefox). If it is still blank, check ', c('https://your-server/api/health'), ' to see whether Tesria is running.'),

    h(3, 'Everything is read-only for a few minutes'),
    p('An administrator is restoring a backup. While a restore runs, you can read the wiki but not change it, and ', c('/api/health'), ' shows ', c('"maintenance"'), ' with the reason. It ends by itself when the restore is done. See ', pageLink('Restoring and undo'), '.'),

    // ------------------------------------------------------------- signing in
    h(2, 'Signing in'),

    h(3, '“Incorrect email or password”, with the right password'),
    p('After 5 wrong passwords in a row, the account is locked for a minute, and each wrong try after that doubles the wait, up to 15 minutes. While it is locked, even the right password is refused with the same message, so that nobody can use the message to find out whether they guessed right. Wait, and try once, carefully.'),
    p('An administrator can end it sooner: ', b('Unlock'), ' next to the account in ', b('Administration, Users'), '. If you have simply forgotten the password, see ', pageLink('Resetting a password'), '.'),

    h(3, '“Too many attempts. Wait a minute and try again.”'),
    p('Too many sign-in attempts came from your address in the last minute: 10, by default, counting everyone who shares that address. Wait a minute. If many people sign in from one office connection, an administrator can raise the limit in ', b('Administration, Security'), ', under ', b('Brute-force protection'), '.'),

    h(3, 'You lost the phone with your authenticator app'),
    ul(
      li(p(b('Use a recovery code.'), ' When Tesria asks for the six-digit code, enter one of the recovery codes you saved when you turned two-factor on. Each works once. Then set two-factor up again on your profile with your new phone. See ', pageLink('Two-factor and recovery codes'), '.')),
      li(p(b('No recovery codes?'), ' Ask an administrator to choose ', b('Turn off two-factor'), ' for your account in ', b('Administration, Users'), '. Sign in with your password, then set it up again. Only the owner can do this for an administrator, and nobody can do it for the owner, which is why the owner’s recovery codes matter most.')),
    ),

    h(3, 'Signed out after a restore'),
    p('Restoring a backup brings back the wiki as it was when the backup was taken, including who was signed in. Sessions started after that moment no longer exist. Sign in again.'),

    // ---------------------------------------------------------------- editing
    h(2, 'Writing and editing'),

    h(3, '“Offline: your changes are local until reconnected”'),
    p('The editor lost its connection to the part of Tesria that shares edits between people. Keep writing: your changes are kept in the browser and sent when the connection comes back. If it never does, ask whoever runs Tesria to check the ', c('collab'), ' container: ', c('docker compose ps collab'), ' and ', c('docker compose logs collab --tail 50'), '.'),

    h(3, '“This page changed while you were editing, so it was not published”'),
    p('Someone else, a script or an assistant updated the page after you started. Nothing is lost: their change is highlighted in your editor. Accept or reject it, then choose ', b('Update'), ' again. See ', pageLink('Changes from assistants and the API'), '.'),

    h(3, 'There is no Edit button'),
    p('You can read the page but not change it: the space, or this page, lets you view only. Ask one of the space’s administrators for edit access. See ', pageLink('Who can see a space'), ' and ', pageLink('Restrictions'), '.'),

    h(3, 'A file will not upload'),
    p('Each attachment can be up to 25 MB. For a larger video or file, share a link to it instead, or embed it from a site such as YouTube.'),

    h(3, 'An embed says the site is not allowed'),
    p('Only sites on your Tesria’s allowed list can be shown inside a page. An administrator can add one in ', b('Administration, Settings'), ', under ', b('Embeds'), '. Meanwhile the embed shows a link that opens it.'),

    // ----------------------------------------------------------------- email
    h(2, 'Email'),

    h(3, 'Email never arrives'),
    p('Password resets, invitations, alerts and notifications all need Tesria to be able to send email. Work down this list:'),
    ol(
      li(p(b('Is sending switched on?'), ' In ', b('Administration, Settings'), ', under ', b('Email'), ', ', b('Send email'), ' must be ticked. When it is off, no email is even attempted.')),
      li(p(b('Send a test.'), ' Choose ', b('Send test email to me'), '. It says straight away either ', i('Sent: check your inbox'), ', or ', i('Not sent'), ' with the mail server’s own reason, such as a wrong password.')),
      li(p(b('Check the port and encryption together.'), ' Port 587 goes with ', b('STARTTLS'), ', and port 465 with ', b('SSL on connect'), '. A mismatch usually fails with a timeout.')),
      li(p(b('Check the From address.'), ' Most mail services only send from an address or domain you have verified with them, and refuse or quietly drop anything else.')),
      li(p(b('Look in the spam folder.'), ' Mail from a new sender often lands there at first. Marking it as not spam helps the next one.')),
    ),
    p('See ', pageLink('Email (SMTP)'), ' for every setting.'),

    h(3, 'Notifications arrive in the app but not by email'),
    p('Everyone starts with notification emails off; each person chooses on their own profile, under ', b('Email notifications'), ': immediately or as a daily digest. See ', pageLink('Email notifications'), '.'),

    // --------------------------------------------------------------- backups
    h(2, 'Backups and exports'),

    h(3, 'A backup shows “Last run failed”, “Overdue” or “Agent offline”'),
    p(b('Administration, Backups'), ' shows a card for each of Tesria’s two backup services, with a colored dot and a word for how it is doing:'),
    ul(
      li(p(b('Last run failed:'), ' the last backup did not complete. The card shows the error. Tesria tries again after 15 minutes, then waits twice as long after each further failure, up to 6 hours.')),
      li(p(b('Overdue:'), ' no backup has succeeded for longer than the schedule allows.')),
      li(p(b('Agent offline'), ' or ', b('Not reporting yet'), ': the backup service is not checking in. It may have stopped, or, on a new install, not have started yet.')),
      li(p(b('Disk nearly full:'), ' the backups are running out of room. Free some space, or keep fewer backups (see ', pageLink('Retention'), ').')),
    ),
    p('On the server, check the two services and read the end of their logs:'),
    codeBlock('bash', 'docker compose ps backup pgbackrest\ndocker compose logs backup --tail 50\ndocker compose logs pgbackrest --tail 50'),
    p('A service that is not running comes back with ', c('docker compose up -d'), '. The log’s last lines that start with ', c('ERROR'), ' say what failed. See ', pageLink('Backups and recovery'), ' for how the two services work.'),

    h(3, 'The cloud copy fails with a “not found” or name error'),
    p('Cloud storage services address a bucket in one of two ways, and Tesria has to use the one yours expects. Set ', c('OFFSITE_CLOUD_URI_STYLE'), ' in the ', c('.env'), ' file to ', c('path'), ' (for MinIO and most storage you run yourself) or ', c('host'), ' (for Backblaze and AWS), run ', c('docker compose up -d'), ', and choose ', b('Test connection'), ' again. See ', pageLink('Offsite copies'), '.'),

    h(3, 'A network drive backup seems stuck, on a Mac'),
    p('Docker Desktop is waiting for your permission to use the folder on the network drive, and waits rather than failing. Allow it when macOS asks, or add the folder in Docker Desktop’s settings, under file sharing. See ', pageLink('Offsite copies'), '.'),

    h(3, 'PDF or HTML export says there is no export renderer'),
    p('The message ', i('This instance has no export renderer configured'), ' means the service that turns pages into PDF and HTML files is not set up on this server. It runs only when ', c('PDF_SHARED_SECRET'), ' is set in the ', c('.env'), ' file. Meanwhile, Markdown exports work. See ', pageLink('Configuration reference'), '.'),

    h(3, 'An export option is missing'),
    p('Either your role may not export (an administrator can change that in ', b('Administration, Roles'), '), or this space has turned that kind of export off. See ', pageLink('Turning exports off'), '.'),

    h(2, 'Still stuck?'),
    p('Look through the ', pageLink('FAQ'), ', and ', pageLink('Health checks and monitoring'), ' for how to see what Tesria is doing. When you ask someone for help, include what you did, what you expected, what happened instead (the exact words of any message), and the version from ', c('https://your-server/api/health'), '.'),
  ))

  // =================================================================== FAQ
  await page('FAQ', null, doc(
    p('Questions people ask when they first meet Tesria. Choose a question to see its answer.'),

    h(2, 'The basics'),
    q('What is Tesria, in one sentence?',
      p('A wiki: a website where your team writes, organizes and finds its knowledge together, which you run on your own computer or server instead of renting from someone else. See ', pageLink('What is Tesria'), '.')),
    q('Is it free?',
      p('Yes. Tesria is open source under the Apache License 2.0: you may use it, change it and run it for anything, including commercially, with no fee and no limit on people or pages. See ', pageLink('License and credits'), '.')),
    q('Do I need to know how to program to use it?',
      p('No. Writing and organizing pages is all done in the browser. Installing it needs someone comfortable typing a few commands into a terminal, once; ', pageLink('Quick start'), ' has every one of them.')),
    q('Is there an app for phones?',
      p('There is nothing to install: Tesria works in the phone’s own browser, reading and writing, with menus made for touch. See ', pageLink('Tesria on phones and tablets'), '.')),

    h(2, 'Running it'),
    q('What kind of computer do I need?',
      p('A computer or server that runs Docker, with at least 2 processor cores, 2 GB of memory and 20 GB of disk. An old laptop or a small home server is enough to start. See ', pageLink('System requirements'), '.')),
    q('Do I need a domain name, or the internet?',
      p('No to both. Tesria runs happily on a home or office network with no domain and no internet connection, reached by the server’s name, such as ', c('studio.local'), '. A domain is only needed if people will open it from outside your network. See ', pageLink('Opening Tesria by name'), '.')),
    q('Why does my browser say “Not secure”?',
      p('On your own network, Tesria makes its own certificate, which each device has to be told to trust, once. It takes about three minutes: see ', pageLink('Trusting the local certificate'), '.')),
    q('How do I update Tesria?',
      p('Two commands, with a backup first. See ', pageLink('Upgrading'), '.')),
    q('Is my wiki backed up?',
      p('Yes, from the moment Tesria first starts, with nothing to set up: daily copies of everything, and a continuous record of changes that can bring the wiki back to any moment. Those copies are on the same machine, though, so also set up an offsite copy. See ', pageLink('Backups and recovery'), '.')),
    q('What happens if the server dies?',
      p('With an offsite copy, you install Tesria on another machine and restore from it. See ', pageLink('When the machine is gone'), '. Without one, the backups are lost with the server, which is why an offsite copy matters.')),

    h(2, 'Your data'),
    q('Where are my pages kept?',
      p('On your server, in Docker’s storage: the database, the attached files and the backups each have their own. Nothing is stored anywhere else.')),
    q('Does Tesria send anything to anyone?',
      p('Only what you set up: email through your mail server, offsite backups to the place you choose, and webhooks to the addresses you enter. When someone pastes a link that shows a preview, your server fetches that page’s title and picture. A server on a real domain also asks Let’s Encrypt for its certificate. Nothing is sent to the people who make Tesria.')),
    q('How do I get my pages out?',
      p('Any page downloads as Markdown, HTML or PDF, and a whole space as a website or a wiki pack. Scripts can read everything through the REST API. See ', pageLink('Exporting and publishing'), '.')),
    q('Can I import from Confluence or another wiki?',
      p('Not directly. Tesria imports its own wiki packs, so a space can move from one Tesria to another; and a script can create pages through the ', pageLink('REST API'), ', or an AI assistant through ', pageLink('MCP'), '.')),
    q('Can I move a space to another Tesria?',
      p('Yes: export it as a wiki pack, with its history, comments and files, and import the pack on the other one. See ', pageLink('Wiki packs'), '.')),

    h(2, 'People and sharing'),
    q('Can I keep some spaces private?',
      p('Yes. A new space is open to everyone signed in, and you can limit any space to particular people or groups, and any page further still. See ', pageLink('Who can see a space'), ' and ', pageLink('Restrictions'), '.')),
    q('Can people read without an account?',
      p('Yes, for spaces you choose to publish, such as public documentation. It takes two switches, one for the whole wiki and one for the space, so nothing is public by accident. See ', pageLink('Public reading'), '.')),
    q('Can several people edit a page at the same time?',
      p('Yes, when live editing is set up: everyone sees each other’s changes as they type. See ', pageLink('Editing at the same time'), '.')),
    q('Can AI assistants use it?',
      p('Yes, through MCP, with a token you make. An assistant sees and changes only what you can, can be kept to reading only, and what it writes shows up in the page’s history. See ', pageLink('MCP'), '.')),
    q('I forgot my password. What now?',
      p('Choose ', b('Forgot your password?'), ' on the sign-in page, or ask an administrator for a reset link. See ', pageLink('Resetting a password'), '.')),
  ))

  // ============================================================== Glossary
  await page('Glossary', null, doc(
    p('The words Tesria uses, in plain English, from A to Z. Where a word has a page of its own, the entry links to it.'),

    h(2, 'A'),
    ul(
      term('Accept all, Reject all', 'the buttons above the editor when a page has changed under you, from another person, a script or an assistant. They keep or discard those highlighted changes. See ', pageLink('Changes from assistants and the API'), '.'),
      term('Administrator', 'someone in the administrator tier, who runs the wiki day to day: accounts, settings, backups and security. What each administrator may do is set by their role.'),
      term('API token', 'a long secret, made on your profile, that lets a script or an AI assistant act as you. It can be made read-only. See ', pageLink('API tokens'), '.'),
      term('Archive', 'to put a space away without deleting it: it leaves the list of spaces and public reading, and nothing in it is touched. It can be brought back at any time.'),
      term('Attachment', 'a file stored with a page, such as a picture, a PDF or a video, up to 25 MB. See ', pageLink('Attachments'), '.'),
      term('Audit log', 'the record of every administrative change: who did what, and when. It is chained, so that editing or deleting an entry is detected.'),
    ),
    h(2, 'B'),
    ul(
      term('Backup', 'a copy of the wiki that it can be brought back from. Tesria makes them by itself, from the first start. See ', pageLink('Backups and recovery'), '.'),
    ),
    h(2, 'C'),
    ul(
      term('Certificate', 'what lets a browser confirm it is talking to your server. On your own network, each device has to be told once to trust Tesria’s. See ', pageLink('Trusting the local certificate'), '.'),
      term('Comment', 'a note on a page, or on a few words of it, for discussion that should not be in the page itself. A thread can be resolved when it is settled. See ', pageLink('Comments'), '.'),
    ),
    h(2, 'D'),
    ul(
      term('Draft', 'a page, or changes to a page, not yet published. A new page is a draft only its writer can see until ', b('Publish'), '. See ', pageLink('Drafts, Publish and Update'), '.'),
    ),
    h(2, 'E'),
    ul(
      term('Element', 'anything you can put on a page besides plain text: a table, a panel, a picture, a chart, a diagram. See ', pageLink('Elements'), '.'),
      term('Excerpt', 'a part of a page marked so that other pages can include it, and show it up to date wherever it appears.'),
      term('Export', 'a download of a page (Markdown, HTML or PDF) or of a whole space (a website or a wiki pack). See ', pageLink('Exporting and publishing'), '.'),
    ),
    h(2, 'G'),
    ul(
      term('Group', 'a named set of people, so that a space or page can be shared with all of them at once. Three are built in and keep themselves up to date: ', b('Users'), ' (everyone with an account), ', b('Admins'), ' and ', b('Owner'), '. See ', pageLink('Groups'), '.'),
    ),
    h(2, 'H'),
    ul(
      term('History', 'every version a page has had, with who made it and when. Any version can be looked at or brought back. See ', pageLink('History and restoring'), '.'),
      term('Home page', 'the page a space opens on: its front door, usually saying what the space is for.'),
    ),
    h(2, 'I'),
    ul(
      term('Instance', 'one installation of Tesria, with its own accounts, spaces and settings. “This instance” means your Tesria.'),
      term('Instance-wide template', 'a template offered in every space, rather than one. See ', pageLink('Templates'), '.'),
    ),
    h(2, 'K'),
    ul(
      term('Key', 'a space’s short code, such as ENG or TEAM, used in the address of every page in it. It cannot be changed after the space is made.'),
      term('Kill switches', 'three switches in ', b('Administration, Security'), ' that take effect at once for the whole wiki: public spaces, public registration, and two-factor for administrators.'),
    ),
    h(2, 'L'),
    ul(
      term('Label', 'a one-word tag on a page, such as ', i('onboarding'), ', for finding related pages across spaces. See ', pageLink('Labels'), '.'),
      term('Live content', 'a block that works out what to show each time the page is read, such as the pages under this one, or the pages with a label. Tesria has twelve kinds. See ', pageLink('Live content'), '.'),
    ),
    h(2, 'M'),
    ul(
      term('MCP', 'the Model Context Protocol: a common way for AI assistants to use other software. Tesria has an MCP server built in. See ', pageLink('MCP'), '.'),
      term('Mention', 'a person’s name typed after ', c('@'), ' in a page or comment. They are told about it. See ', pageLink('Mentions and notifications'), '.'),
    ),
    h(2, 'N'),
    ul(
      term('Notification', 'a message in the bell at the top of the page, and by email if you ask for it, when something you watch changes or someone mentions you.'),
    ),
    h(2, 'O'),
    ul(
      term('Offsite copy', 'a copy of the backups kept somewhere other than the server: a cloud bucket, a network drive or a removable drive, so that losing the machine does not lose the wiki. See ', pageLink('Offsite copies'), '.'),
      term('Open space', 'a space that has not been limited to particular people, which everyone signed in can read, edit and manage. Every new space starts open.'),
      term('Owner', 'the one account that owns the wiki. It can do everything, and nobody else can suspend it, reset it or take its place. Ownership can be handed over.'),
    ),
    h(2, 'P'),
    ul(
      term('Page', 'one document in a space. Pages sit in a tree: any page can have pages under it.'),
      term('Page properties', 'a small table of facts at the top of a page, such as its owner and status, which a report on another page can gather from many pages at once.'),
      term('Page tree', 'the outline of a space’s pages in its sidebar, showing which page sits under which. See ', pageLink('The page tree and reordering'), '.'),
      term('Panel', 'a colored box that makes a paragraph stand out: Info, Note, Tip, Warning or Error. See ', pageLink('Panels'), '.'),
      term('Point-in-time recovery', 'bringing the wiki back to any moment you choose, to the second, not only to when a backup was taken.'),
      term('Public address', 'the address people use to open your Tesria, set in ', b('Administration, Settings'), '. Links in emails use it.'),
      term('Public reading', 'letting people without an account read chosen spaces. It needs two switches: one for the wiki and one for the space. See ', pageLink('Public reading'), '.'),
      term('Publish', 'to make a draft a real page that others can see. Later changes are published with ', b('Update'), '.'),
    ),
    h(2, 'R'),
    ul(
      term('Read-only token', 'an API token that can read and search but not change anything. The right choice for most scripts and assistants.'),
      term('Recovery code', 'one of a set of single-use codes you save when you turn on two-factor sign-in. One gets you in if you lose your phone, or resets your password. See ', pageLink('Two-factor and recovery codes'), '.'),
      term('Restore', 'to bring something back: a page from the trash, an older version of a page, or the whole wiki from a backup. See ', pageLink('Restoring and undo'), '.'),
      term('Restore drill', 'a test that restores a backup somewhere temporary and checks it, without touching the wiki. A backup that has never been restored has not been proved to work. See ', pageLink('Restore drills'), '.'),
      term('Restriction', 'a limit on who may view or edit one page, and the pages under it, narrower than its space. See ', pageLink('Restrictions'), '.'),
      term('Retention', 'how long backups are kept before the oldest are removed. See ', pageLink('Retention'), '.'),
      term('Right', 'permission to do one kind of thing across the wiki, such as ', i('Create spaces'), ' or ', i('Use API tokens'), '. Roles hold rights.'),
      term('Role', 'a named set of rights, such as the built-in User and Administrator roles. Everyone has one. See ', pageLink('Roles'), '.'),
    ),
    h(2, 'S'),
    ul(
      term('Session', 'one device where you are signed in. Your profile lists them, and any one can be signed out. See ', pageLink('Sessions'), '.'),
      term('Slash menu', 'the list of elements that appears when you type ', c('/'), ' in the editor. See ', pageLink('The slash menu'), '.'),
      term('Space', 'a home for pages that belong together, such as a team’s or a project’s, with its own page tree and its own list of who can read and edit it. See ', pageLink('What a space is'), '.'),
      term('Space permissions', 'who may View, Edit or Admin a space: people, groups, or everyone signed in. See ', pageLink('Who can see a space'), '.'),
      term('Status', 'a small colored label inside a sentence or table, such as ', i('IN PROGRESS'), ' or ', i('DONE'), '.'),
    ),
    h(2, 'T'),
    ul(
      term('Template', 'a page that new pages start from, with headings and hints already in place. See ', pageLink('Templates'), '.'),
      term('Tier', 'one of three levels, User, Administrator and Owner, that decides who may act on whom. A role is a set of rights within a tier. See ', pageLink('Owner, administrators and users'), '.'),
      term('Tracked changes', 'changes made to a page while you were editing it, by someone else, a script or an assistant, shown highlighted for you to accept or reject.'),
      term('Trash', 'where deleted pages go. They can be restored from there, or deleted permanently. See ', pageLink('Trash'), '.'),
      term('Two-factor sign-in', 'signing in with a six-digit code from an app on your phone as well as your password, so that a stolen password alone is not enough.'),
    ),
    h(2, 'U'),
    ul(
      term('Update', 'the button that publishes your changes to a page that already exists. Each update makes a new version.'),
    ),
    h(2, 'V'),
    ul(
      term('Version', 'one saved state of a page. Every publish and update adds one to its history.'),
    ),
    h(2, 'W'),
    ul(
      term('Watch', 'asking to be told when a page or a whole space changes. See ', pageLink('Watching'), '.'),
      term('Webhook', 'Tesria calling another program the moment something happens in a space. See ', pageLink('Webhooks'), '.'),
      term('Wiki pack', 'one file holding a whole space, with its history, comments and attachments, for keeping or for moving to another Tesria. See ', pageLink('Wiki packs'), '.'),
    ),
  ))

  // ========================================================== Release notes
  const notes = top['Release notes']
  await page('Release notes', null, doc(
    p('What each version of Tesria brought. Read these before you upgrade, to know what will be different afterwards; ', pageLink('Upgrading'), ' explains the upgrade itself.'),
    p('To see which version you are running, open ', c('https://your-server/api/health'), ' (', c('your-server'), ' being your Tesria’s address). The answer includes ', c('"version"'), '.'),
    live('children', { depth: '1', sort: 'position' }),
  ))
  await page('Tesria 0.2', notes, doc(
    p('Tesria 0.2 is the version this site describes, and the one ', c('/api/health'), ' reports. Here is what it includes, with where to read more.'),
    h(2, 'Writing'),
    ul(
      li(p('A block editor with a slash menu and a wide range of elements: panels, layouts, tables, code, diagrams, math, charts, galleries, video, animations, embeds and more. See ', pageLink('Elements'), '.')),
      li(p('Twelve kinds of live content, from the pages under this one to a report of open tasks. See ', pageLink('Live content'), '.')),
      li(p('Templates for pages that should start alike. See ', pageLink('Templates'), '.')),
    ),
    h(2, 'Working together'),
    ul(
      li(p('Several people editing a page at once, with changes from scripts and assistants shown as tracked changes to accept or reject.')),
      li(p('Comments on a page or on a few words of it, mentions, watching, and notifications in the app and by email.')),
      li(p('A page’s full history, with any version brought back in one click.')),
    ),
    h(2, 'Organizing and sharing'),
    ul(
      li(p('Spaces with permissions for people and groups, restrictions on single pages, labels, and a trash.')),
      li(p('Full-text search across everything you can see.')),
      li(p('Exports as Markdown, HTML and PDF, a whole space as a website or a wiki pack, and public reading for the spaces you choose.')),
      li(p('A REST API with read-only and full tokens, webhooks, and a built-in MCP server for AI assistants.')),
    ),
    h(2, 'Running it'),
    ul(
      li(p('One Docker Compose file, with HTTPS set up by itself, and a ', b('Trust this device'), ' guide for servers on your own network.')),
      li(p('A setup wizard, a guided tour, and tips for new people.')),
      li(p('Backups from the first start, with point-in-time recovery, restore and undo from the browser, offsite copies to the cloud, a network drive or a removable drive, and restore drills that prove they work.')),
      li(p('Roles with rights you can change, security alerts, rate limits and lockouts, two-factor sign-in, single sign-on, and an audit log that shows if it has been tampered with.')),
      li(p('Your own name, logo, favicon and colors.')),
      li(p('Every screen made for phones and tablets as well as computers.')),
    ),
  ))

  // =============================================================== Security
  await page('Security', null, doc(
    p('A wiki holds a lot of what a team knows: plans, decisions, how things are done, sometimes things that must stay private. This page explains how Tesria protects it, what it relies on you to do, and how to tell us if you find a weakness.'),
    p('It is written for anyone who wants to know. If you run a Tesria that people will reach from the internet, also work through ', pageLink('Security hardening'), ' before you open it up.'),

    h(2, 'Who can see what'),
    ul(
      li(p(b('Every request is checked.'), ' Whether it comes from the browser, a script or an assistant, Tesria checks the space’s permissions and the page’s restrictions before it answers.')),
      li(p(b('What you may not see does not exist.'), ' A page you are not allowed to see answers “not found”, the same as one that was never written, so nobody can find out it is there by guessing.')),
      li(p(b('Nothing leaks around the side.'), ' Search, notifications, live content, exports and the admin dashboard only ever show what the person looking may see.')),
      li(p(b('Nothing is public by accident.'), ' Reading without an account needs two switches, one for the whole wiki and one for the space, and publishing a space alerts every administrator.')),
    ),

    h(2, 'Signing in'),
    ul(
      li(p(b('Passwords are never stored,'), ' only a fingerprint made with Argon2id, a method designed to make guessing slow and expensive even for someone who steals the database.')),
      li(p(b('Guessing is slowed down.'), ' After 5 wrong passwords, an account is locked for a minute, doubling up to 15 minutes; and each address may only try 10 times a minute. Both are adjustable, and never permanent, so nobody can lock someone else out for good.')),
      li(p(b('Two-factor sign-in,'), ' with a code from a phone app and single-use recovery codes, for anyone who wants it. Administrators can be required to use it.')),
      li(p(b('Single sign-on'), ' with your organization’s own sign-in service, if you set it up.')),
    ),

    h(2, 'Staying signed in safely'),
    ul(
      li(p(b('Sessions end.'), ' After 14 days without use, and after 90 days however much it is used, a session signs out.')),
      li(p(b('You can see and end each one.'), ' Your profile lists every device where you are signed in; sign out any of them. Changing your password signs out all the others.')),
      li(p(b('An unattended browser cannot do the worst things.'), ' Administration that is hard to undo, such as changing roles, asks for your password again unless you signed in within the last 5 minutes. Deleting a space or restoring a backup asks for it every time.')),
    ),

    h(2, 'Your data on the server'),
    ul(
      li(p(b('Encrypted on the way.'), ' Everything you do in Tesria goes over HTTPS, and the sign-in cookie is marked so that a browser never sends it without HTTPS.')),
      li(p(b('Secrets are kept secret.'), ' API tokens are stored only as fingerprints and shown once; the mail server password and two-factor secrets are stored encrypted.')),
      li(p(b('Backups are encrypted.'), ' The continuous database backups always are, and every offsite copy is encrypted with its own passphrase before it leaves the server.')),
      li(p(b('Nothing is sent to us.'), ' Tesria does not report to anyone. It only sends what you set up: email, offsite backups and webhooks.')),
    ),

    h(2, 'Limits on what content can do'),
    ul(
      li(p(b('Uploaded files cannot act as the site.'), ' A file someone attaches is served so that a browser will not run it as part of Tesria.')),
      li(p(b('Your network stays yours.'), ' Webhooks and link previews cannot reach private addresses, such as the server itself or other machines on your network, so a wiki page cannot be used to probe them.')),
      li(p(b('Only allowed sites are embedded.'), ' Pages can show videos and designs only from sites on the wiki’s allowed list.')),
      li(p(b('The browser is told what to trust.'), ' Tesria sends the security headers that stop other sites from framing it and stop injected scripts from running.')),
    ),

    h(2, 'Watching for trouble'),
    ul(
      li(p(b('Alerts.'), ' Every administrator is told, in the bell and by email once email is set up, about bursts of failed sign-ins, many accounts tried from one address, many pages removed quickly, a new administrator, a failed or overdue backup, and more. See ', pageLink('Security (administration)'), '.')),
      li(p(b('Blocking.'), ' An administrator can block an address straight from an alert, or a whole range of addresses, in ', b('Administration, Security'), '.')),
      li(p(b('An audit log that cannot be quietly changed.'), ' Every administrative change is recorded, and each entry is chained to the one before, so editing or deleting one breaks the chain and is detected. Tesria itself runs with database access that cannot change the log, and every entry is also written to the server’s own log.')),
    ),

    h(2, 'What Tesria cannot protect against'),
    p('Some things are outside what any application can defend, and are the job of whoever runs the server:'),
    ul(
      li(p(b('A compromised server.'), ' Someone who controls the machine Tesria runs on controls Tesria.')),
      li(p(b('An administrator who means harm.'), ' Administrators are trusted by design. What they do is recorded in the audit log, and the owner decides what each role may do.')),
      li(p(b('A flood of traffic.'), ' Stopping an attack by sheer volume is the network’s job.')),
    ),
    p('Your part: keep Tesria up to date (', pageLink('Upgrading'), '), turn on two-factor for every administrator, keep an offsite copy of the backups, and, before letting the internet in, work through ', pageLink('Security hardening'), '.'),

    h(2, 'Reporting a vulnerability'),
    p('If you find a way around any of the above, please tell us privately first, so it can be fixed before anyone else learns of it. A weakness in Tesria is a weakness in every Tesria someone runs.'),
    ol(
      li(p(b('Report it privately,'), ' never in a public issue or post. Use GitHub’s private vulnerability reporting on the Tesria repository, or the contact in its ', c('SECURITY.md'), ' file.')),
      li(p(b('Include'), ' what you found, how to reproduce it, the version (', c('/api/health'), ' shows it), and what you think the impact is.')),
      li(p(b('Test only on your own Tesria.'), ' A proof of concept against your own instance is welcome; against anyone else’s it is not.')),
    ),
    p('You can expect an acknowledgement within a few days, a fix or a way to avoid the problem as fast as its severity warrants, and credit in the changelog unless you would rather not. Please give a fix a reasonable head start before writing about it.'),
  ))

  // ====================================================== License and credits
  await page('License and credits', null, doc(
    p('Tesria is open source: its code is public, and you are free to use it, change it and share it. This page says on what terms, and credits the open-source projects it is built on.'),

    h(2, 'The license'),
    p('Tesria is licensed under the ', b('Apache License, Version 2.0'), '. Copyright 2026 Brian Rodriguez. In plain terms:'),
    ul(
      li(p(b('You may'), ' use Tesria for anything, including in a business, run as many copies as you like, change it, and give or sell copies to others, changed or not.')),
      li(p(b('If you pass it on,'), ' include the license and the NOTICE file, and mark any files you changed.')),
      li(p(b('It comes as it is,'), ' without warranty: nobody promises it will suit your purpose, and nobody is liable if it goes wrong.')),
    ),
    p('That summary is not the license. The full text is in the ', c('LICENSE'), ' file with the source code, and at ', c('https://www.apache.org/licenses/LICENSE-2.0'), '.'),

    h(2, 'Built on'),
    p('Tesria stands on the work of many open-source projects, each under its own license. The notable ones, as listed in the NOTICE file:'),
    ul(
      li(p(b('The server:'), ' ASP.NET Core and Entity Framework Core (Apache-2.0 and MIT), Npgsql (PostgreSQL License), MailKit and MimeKit (MIT), Isopoh.Cryptography.Argon2 (CC0-1.0), Otp.NET (MIT) and SkiaSharp (MIT).')),
      li(p(b('The app in your browser:'), ' React (MIT), TipTap and ProseMirror (MIT), Yjs and Hocuspocus (MIT), Mermaid (MIT), KaTeX (MIT), lowlight and highlight.js (BSD-3-Clause), qrcode (MIT), and Vite and oxlint (MIT) to build it.')),
      li(p(b('The services around it:'), ' PostgreSQL (PostgreSQL License), pgBackRest (MIT), Caddy (Apache-2.0), restic (BSD-2-Clause), and Playwright and Chromium (Apache-2.0 and BSD-3-Clause) for PDF exports.')),
    ),
    p('The complete list, with exact versions, is in the source code: ', c('src/web/package-lock.json'), ', ', c('collab/package-lock.json'), ', and the package references in ', c('src/Api/Api.csproj'), '. Thank you to everyone who works on them.'),

    h(2, 'On this site'),
    ul(
      li(p('The video on the ', pageLink('Embed'), ' page is ', i('Big Buck Bunny'), ', © Blender Foundation, under the Creative Commons Attribution 3.0 license.')),
      li(p('Everyone and everything in Tesria Demo, the space this site’s pictures are taken in, is made up.')),
    ),
  ))
}
