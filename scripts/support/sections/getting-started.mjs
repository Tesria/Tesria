// Getting started: from nothing to a first page (dev-plan 10.5).
//
// The system requirements are the ones measured on 2026-09-22 (dev-plan 10.5
// step 3), with the headroom said to be headroom.

export const shots = ({ demo }) => [
  { name: 'what-is-tesria', url: demo('Kestrel Sync 2 launch'), settle: 2500, steps: [{ wait: 2500 }] },
  { name: 'spaces-list', url: '/spaces', settle: 2000, steps: [{ wait: 2500 }] },
  {
    name: 'new-space',
    url: '/spaces',
    settle: 800,
    steps: [
      { wait: 2500 },
      { click: 'button:has-text("New space")' },
      { wait: 400 },
      { type: 'TEAM', selector: 'form.card input[placeholder="ENG"]' },
      { type: 'Team handbook', selector: 'form.card input[placeholder="Engineering"]' },
      { type: 'How we work, in one place', selector: 'form.card input[placeholder="Optional"]' },
    ],
    clipTo: 'form.card',
    clipPad: 16,
  },
  { name: 'space-home', url: '/spaces/DEMO', settle: 2500, steps: [{ wait: 2500 }] },
  {
    name: 'new-page',
    url: '/spaces/DEMO/new',
    settle: 800,
    steps: [
      { wait: 3500 },
      { type: 'Team handbook', selector: 'input[placeholder="Page title"]' },
      { click: '.ProseMirror' },
      { keys: 'Welcome to the team. ' },
      { press: 'Enter', selector: '.ProseMirror' },
      { keys: '/' },
      { wait: 600 },
    ],
  },
  // Not a picture for the page: closes the editor so the draft above is
  // thrown away rather than left behind in the Demo space.
  { name: 'new-page-discarded', settle: 300, steps: [{ press: 'Escape', selector: '.ProseMirror' }, { click: '.page-actionbar button:has-text("Close")' }, { wait: 1200 }] },
  { name: 'published-page', url: demo('Launch plan'), settle: 2500, steps: [{ wait: 2500 }] },
]

export async function build({
  top, page, ensure, figure, doc, p, h, text, bold, code, italic, ul, ol, li, link, panel, table, codeBlock, live,
}) {
  const root = top['Getting started']
  await page('Getting started', null, doc(
    p('Everything you need to go from nothing to a working Tesria with its first page. If you only read one page, read the ', text('Quick start', bold), '.'),
    live('children', { depth: '1', sort: 'position' }),
  ))

  // ------------------------------------------------------------ What is Tesria
  const what = await ensure('What is Tesria', root)
  await page('What is Tesria', root, doc(
    p('Tesria is a wiki for teams that you host yourself. It runs on your own server with Docker, keeps its data in PostgreSQL, and is reached in a browser on a computer, tablet or phone.'),
    ...(await figure(what, 'what-is-tesria', 'A space in Tesria',
      'A space holds a tree of pages. This one is a fictional team’s product launch.')),
    h(2, 'What you get'),
    ul(
      li(p(text('Spaces and pages.', bold), ' A space holds a tree of pages for a team, a project or a topic. Pages nest, and you can drag them into a new order.')),
      li(p(text('A full editor.', bold), ' Headings, lists, tables, panels, layouts, code, diagrams, math, charts, images, video, and live content that updates itself, all from a slash menu.')),
      li(p(text('Writing together.', bold), ' Several people can edit a page at once, comment on it, mention each other and watch for changes.')),
      li(p(text('Finding things.', bold), ' Full-text search across everything you are allowed to see, and labels to group pages.')),
      li(p(text('Control over who sees what.', bold), ' Spaces are open to everyone signed in until you restrict them, and pages can be restricted further. Roles decide what each person may do across the instance.')),
      li(p(text('Getting it out.', bold), ' Export a page as Markdown, HTML or PDF, publish a whole space as a static website, or move a space between instances as a wiki pack.')),
      li(p(text('Looking after it.', bold), ' Automatic backups with point-in-time recovery, restores from the admin area, and optional copies to the cloud, a network drive or a removable drive.')),
      li(p(text('Connections.', bold), ' A REST API with API tokens, webhooks, and an MCP server so AI assistants can read and write pages.')),
    ),
    h(2, 'What it is not'),
    p('Tesria is not a hosted service: you run it, and your pages never leave your server unless you export them. It is not a file-sync tool or a chat app. It is built to be a team’s written memory.'),
    p('Next: ', text('Prerequisites', bold), '.'),
  ))

  // ------------------------------------------------------------ Prerequisites
  await page('Prerequisites', root, doc(
    p('What to have, and what to decide, before you install Tesria.'),
    h(2, 'What you need'),
    ul(
      li(p(text('A machine that runs Docker', bold), ': a Linux server, or a Mac or Windows computer with Docker Desktop. See ', text('System requirements', bold), ' for the size.')),
      li(p(text('Docker with Compose', bold), '. Tesria runs as a set of containers started together by ', text('docker compose', code), '.')),
      li(p(text('A way to reach it', bold), '. Either a domain name pointing at the machine, which gets a free HTTPS certificate automatically, or just the machine’s own address on your network, which uses a certificate Tesria makes itself.')),
    ),
    h(2, 'Optional, and easy to add later'),
    ul(
      li(p(text('An email server', bold), ' (SMTP), for password reset emails and notifications. Without one, an administrator resets a password by handing the person a one-time link.')),
      li(p(text('Somewhere else to keep backups', bold), ': an S3-compatible cloud bucket, a network drive, or a removable drive. Tesria backs itself up on the same machine regardless; these keep a copy somewhere else.')),
      li(p(text('Single sign-on', bold), ' through an OpenID Connect provider, if your organization already has one.')),
    ),
    h(2, 'Decisions the setup wizard will ask you'),
    ul(
      li(p(text('Who can join', bold), ': anyone who finds the address, or only people you invite.')),
      li(p(text('Who can read', bold), ': whether spaces can be published for people who are not signed in.')),
      li(p(text('How much backup history to keep', bold), '.')),
    ),
    p('Every one of these can be changed later in Administration.'),
    panel('warning', p(text('Keep the backup encryption key somewhere safe.', bold), ' It is set once in the ', text('.env', code), ' file, and backups made with it cannot be restored without it.')),
  ))

  // ------------------------------------------------------- System requirements
  await page('System requirements', root, doc(
    p('These figures come from measuring a running Tesria, not from guesses, and leave room to spare.'),
    h(2, 'The server'),
    table([
      ['', 'To run Tesria', 'Notes'],
      ['Processor', '2 cores', 'x86-64 or ARM64. A PDF export uses most of one core for about a second.'],
      ['Memory', '2 GB', '4 GB if the same machine also builds the images, which happens on the first install and each upgrade.'],
      ['Disk', '20 GB to start', 'About 5.2 GB is the software itself. Your pages, files and, above all, backups use the rest, and backups grow with the history you keep.'],
      ['Software', 'Docker with Compose', 'Linux, or macOS or Windows with Docker Desktop.'],
    ], [140, 180, 420]),
    h(3, 'What was measured'),
    ul(
      'About 550 MB of memory at rest for the whole of Tesria, and 665 MB at the busiest moment of a run of PDF and website exports.',
      'About 5.2 GB of disk for the software. The PDF renderer, which includes its own browser, is 3.5 GB of that.',
    ),
    h(2, 'Browsers'),
    p('Tesria works in current versions of every major browser. It needs at least:'),
    table([
      ['Browser', 'Version'],
      ['Chrome and Edge', '111 or later'],
      ['Firefox', '121 or later'],
      ['Safari, on a Mac, iPhone or iPad', '16.2 or later'],
    ], [300, 200]),
    h(2, 'Phones and tablets'),
    p('Every page works on a phone or tablet, in portrait and landscape, and so does the editor, with a toolbar made for a small screen. See ', text('Tesria on phones and tablets', bold), ' in the User manual for what changes on a small screen.'),
  ))

  // --------------------------------------------------------------- Quick start
  await page('Quick start', root, doc(
    p('From a machine with Docker to a signed-in owner account in about ten minutes.'),
    h(2, '1. Get Tesria'),
    codeBlock('bash', 'git clone https://github.com/Tesria/Tesria.git\ncd Tesria'),
    h(2, '2. Configure it'),
    p('Copy the example settings, then set the values below in ', text('.env', code), ' before the first start. The example file holds placeholders rather than blanks, so a value you skip does not fail loudly.'),
    codeBlock('bash', 'cp .env.example .env'),
    table([
      ['Setting', 'What to put there'],
      [p(text('POSTGRES_PASSWORD', code)), 'Any long random string.'],
      [p(text('APP_DB_PASSWORD', code)), 'Another long random string, different from the one above. Tesria runs with it as a database user that cannot change or delete the audit log.'],
      [p(text('BACKUP_ENCRYPTION_KEY', code)), 'Required. Encrypts your backups, which cannot be restored without it. Keep a copy somewhere safe.'],
      [p(text('DOMAIN', code), ' and ', text('ACME_EMAIL', code)), 'Your domain name and an email address for its certificate. Leave the domain as localhost to try Tesria on your own computer.'],
      [p(text('COLLAB_SHARED_SECRET', code)), 'Optional. Turns on editing a page with several people at once.'],
      [p(text('PDF_SHARED_SECRET', code)), 'Optional. Turns on PDF export.'],
    ], [240, 460]),
    p('A long random string can be made with ', text('openssl rand -hex 32', code), '.'),
    h(2, '3. Start it'),
    codeBlock('bash', 'docker compose up -d --build'),
    p('The first start builds the software and sets up the database, which takes a few minutes.'),
    panel('warning', p(text('Start everything together.', bold), ' Starting the database on its own with ', text('docker compose up -d db', code), ' on a fresh install makes it restart over and over, because its backups need the backup service that starts with it.')),
    h(2, '4. Open it'),
    p('Go to ', text('https://', code), ' and your domain, or ', text('https://localhost', code), ' on your own computer, where the browser warns once about the certificate. The ', text('setup wizard', bold), ' takes it from there: it creates your owner account, names the instance, and asks the questions in ', text('Prerequisites', bold), '.'),
    p('To check that everything is up, open ', text('/api/health', code), ' on the same address.'),
  ))

  // ---------------------------------------------------------- The setup wizard
  // Its pictures come from the scratch instance, not the Demo space: see
  // scripts/support/shoot-setup.sh.
  const wizard = await ensure('First-run setup wizard', root)
  const step = async (title, shot, alt, ...body) => [
    h(2, title), ...body, ...(await figure(wizard, shot, alt)),
  ]
  await page('First-run setup wizard', root, doc(
    p('The first time anyone opens a new Tesria, the setup wizard walks through everything an instance needs. It runs once, for the owner, and every answer can be changed later in Administration.'),
    table([
      ['Step', 'What it does'],
      ['Welcome', 'Says what the wizard covers.'],
      ['Your account', 'Required. Creates the owner account and shows its recovery codes to save.'],
      ['This instance', 'Required. Its name, and the address people use to reach it.'],
      ['Who can join', 'Required. Invitation only or open registration, and whether spaces can be read without signing in.'],
      ['What roles may do', 'Required. The rights each role holds, with the defaults to review.'],
      ['Backups', 'Required. How much backup history to keep.'],
      ['Email', 'Optional. The server Tesria sends email through.'],
      ['Two-factor', 'Recommended. A second step at sign-in for the owner account.'],
      ['A first space', 'Optional. Creates a space to start writing in.'],
      ['Done', 'Lists what you skipped, and finishes setup.'],
    ], [180, 520]),
    p('The list of steps stays on the left, or at the top on a phone. A finished step shows a check mark and a skipped one a dash, and you can go back to either. If you close the browser partway, what you finished is kept: sign in as the owner and the wizard opens again.'),
    ...(await step('Welcome', 'setup-welcome', 'The setup wizard’s welcome step',
      p('Says what the wizard covers and how long it takes. Choose ', text('Start', bold), '.'))),
    ...(await step('Your account', 'setup-account', 'Creating the owner account',
      p('The first account becomes the ', text('owner', bold), ': the one account that can transfer the instance to someone else, and the one nobody else can suspend or reset. Enter your email address, your name and a password of at least 8 characters, then choose ', text('Create the owner account', bold), '.'),
      p('Tesria then shows your ', text('recovery codes', bold), '. Each one signs you in once if you lose your password. Save them somewhere other than this computer, tick ', text('I have saved these somewhere safe', bold), ' and continue. Because nobody can reset the owner, losing both the password and the codes means losing the instance.'))),
    ...(await step('This instance', 'setup-instance', 'Naming the instance and setting its address',
      p('The name appears at the top of every page and on the sign-in page. The address is the one people type to reach Tesria; links in emails use it, so it has to be the real one, such as ', text('https://wiki.example.com', code), '.'))),
    ...(await step('Who can join', 'setup-registration', 'Choosing who can join',
      p(text('Invite only', bold), ' means nobody can sign up unless you send them an invite link. ', text('Open', bold), ' lets anyone who can reach the address create an account. You have to pick one to continue.'),
      p(text('Allow anonymous reading', bold), ' lets people read spaces you mark public without signing in. Turning it on publishes nothing by itself: every space stays private until you mark it.'))),
    ...(await step('What roles may do', 'setup-permissions', 'Reviewing what each role may do',
      p('Each column is a role and each row a right, such as creating spaces or deleting other people’s pages. The defaults suit most teams; change any box now, or choose ', text('Keep these defaults', bold), '. The same page is in Administration under ', text('Roles', bold), '.'))),
    ...(await step('Backups', 'setup-backups', 'Choosing how much backup history to keep',
      p('Backups are already running: a nightly copy of the database and files, and a continuous backup you can rewind to any moment. This step sets how many nightly copies to keep and for how many days.'),
      panel('warning', p('The backups are encrypted with ', text('BACKUP_ENCRYPTION_KEY', code), ' from your ', text('.env', code), ' file. Keep a copy of it somewhere other than the server, or the backups cannot be restored.')))),
    ...(await step('Email', 'setup-email', 'The optional email step',
      p('Tesria sends password resets, invitations and notifications by email. Enter your mail server’s details, or choose ', text('Skip for now', bold), '. Without email, you reset forgotten passwords yourself from Administration. The password and a test send are in ', text('Administration', bold), ' under ', text('Settings', bold), '.'))),
    ...(await step('Two-factor', 'setup-two-factor', 'Turning on two-factor for the owner',
      p('Recommended for the owner, since nobody can reset that account. Choose ', text('Set up two-factor', bold), ' and scan the code with an authenticator app such as 1Password, Google Authenticator or Authy. You can also do this later from your profile.'))),
    ...(await step('A first space', 'setup-first-space', 'Creating a first space',
      p('A space holds pages for a team, a project or a topic. Give it a name, and Tesria suggests a short key from it. The key is part of every page address in the space and cannot change later. Skip this if you would rather start from ', text('Spaces', bold), '.'))),
    ...(await step('Done', 'setup-done', 'The last step of the setup wizard',
      p('Lists anything you skipped, each of which is waiting in Administration. Choose ', text('Finish', bold), ' to open the wiki, or ', text('Invite people', bold), ' to send the first invite links.'))),
  ))

  // --------------------------------------------------- Your first space and page
  const first = await ensure('Your first space and page', root)
  await page('Your first space and page', root, doc(
    p('With Tesria set up, this is how you start writing.'),
    h(2, '1. Create a space'),
    p('Open ', text('Spaces', bold), ' at the top of the screen. It lists every space you can see.'),
    ...(await figure(first, 'spaces-list', 'The Spaces page')),
    p('Choose ', text('New space', bold), ', then give it a short key, a name and, if you like, a description. The key appears in the space’s address and cannot be changed later, so keep it short: ', text('TEAM', code), ' or ', text('ENG', code), '.'),
    ...(await figure(first, 'new-space', 'Creating a space')),
    p('The new space opens on its home page, with its pages listed on the left.'),
    ...(await figure(first, 'space-home', 'A space’s home page')),
    h(2, '2. Write a page'),
    p('Choose ', text('+ New page', bold), '. Give the page a title, then start typing. To add anything other than text, type ', text('/', code), ' on a new line and pick from the menu.'),
    ...(await figure(first, 'new-page', 'Writing a new page, with the slash menu open')),
    p('Nobody else sees the page until you choose ', text('Publish', bold), '. After that, ', text('Edit', bold), ' opens it again, and ', text('Update', bold), ' saves each new version.'),
    ...(await figure(first, 'published-page', 'A published page')),
    h(2, 'Next'),
    ul(
      li(p('Invite people: ', text('Administration', bold), ' then ', text('Invites', bold), '.')),
      li(p('Learn the editor: the ', text('User manual', bold), ' has a page for every element.')),
      li(p('Decide who sees the space: ', text('Space settings', bold), ' then ', text('Permissions', bold), '.')),
    ),
  ))
}
