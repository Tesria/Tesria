// Getting started: from nothing to a first page (dev-plan 10.5), rewritten to
// the owner's rules of 2026-09-23 (scripts/docs/WRITING.md) for someone
// new to wikis and to running software.
//
//   What is Tesria               the idea of a wiki, and why run your own
//                                (Features, second in the tree, is the tour)
//   Prerequisites                what to have and decide before installing
//   System requirements          measured 2026-09-22 (dev-plan 10.5 step 3),
//                                with the headroom said to be headroom
//   Quick start                  README's Quick start, for a newcomer
//   First-run setup wizard       src/web/src/routes/SetupPage.tsx, in its
//                                own numbering
//   Your first space and page    a short path that links to Creating a space
//                                and Templates rather than repeating them
//
// The setup wizard's pictures come from the scratch instance, not the Demo
// space: scripts/docs/shoot-setup.sh takes them (files named setup-*), and
// publish-docs.sh finds them here by name. They are whole 1024-pixel
// windows, so only the two that show a layout are used.
//
// Run it on its own: scripts/docs/publish-docs.sh getting-started

export const shots = () => [
  // Your first space and page: writing a first page, as an animation, in a
  // narrow window so it reads on a phone. It opens a new page in Tesria
  // Demo; the harness discards the draft afterwards.
  {
    name: 'first-page', url: '/spaces/DEMO/new', phone: false,
    viewport: { width: 480, height: 400 }, record: { size: { width: 480, height: 400 } },
    waitFor: '.ProseMirror', lead: 900, tail: 1600,
    css: '.tip, .onboarding-tip { display: none !important; }',
    steps: [
      { click: 'input.title-input' }, { wait: 300 },
      { typeSlowly: 'How we work', delay: 80 }, { wait: 500 },
      { press: 'Enter', selector: 'input.title-input' }, { wait: 400 },
      { typeSlowly: 'Everything a new teammate needs in their first week.', delay: 40 }, { wait: 700 },
      { press: 'Enter', selector: '.ProseMirror' }, { wait: 300 },
      { typeSlowly: '/tip', delay: 110 }, { wait: 900 },
      { press: 'Enter', selector: '.ProseMirror' }, { wait: 300 },
      { typeSlowly: 'Stuck? Ask in the team chat.', delay: 45 }, { wait: 600 },
    ],
  },
]

export async function build({
  top, page, ensure, doc, p, h, text, bold, code, italic, ul, ol, li, panel, table, codeBlock, picture, phonePicture, animation, pageLink,
}) {
  const b = (t) => text(t, bold)
  const c = (t) => text(t, code)
  const i = (t) => text(t, italic)
  /** A numbered step: a heading that says what to do, then how. */
  const step = (n, title) => h(3, `Step ${n}: ${title}`)
  const root = top['Getting started']

  // Every page exists before any links to it, so a first run links rather
  // than printing bold titles.
  for (const title of ['What is Tesria', 'Prerequisites', 'System requirements', 'Quick start', 'First-run setup wizard', 'Your first space and page']) {
    await ensure(title, root)
  }

  // ============================================================ Getting started
  await page('Getting started', null, doc(
    p('This section takes you from “what is this?” to a Tesria of your own with its first page written. Nothing here assumes you have run a server before: each page explains what a thing is before asking you to do it.'),
    p('Read the pages in order the first time:'),
    ol(
      li(p(pageLink('What is Tesria'), ': what a wiki is, and why a team would run its own.')),
      li(p(pageLink('Prerequisites'), ': what to have ready, and the few decisions to think about.')),
      li(p(pageLink('System requirements'), ': how big a computer Tesria needs, and which browsers it works in.')),
      li(p(pageLink('Quick start'), ': installing Tesria and starting it for the first time.')),
      li(p(pageLink('First-run setup wizard'), ': the guided setup that creates your account and asks the important questions.')),
      li(p(pageLink('Your first space and page'), ': writing something, and inviting the people who will read it.')),
    ),
    panel('success', p(b('Just want to see what Tesria can do?'), ' ', pageLink('Features'), ' is a tour of everything it does, with a link to the full story of each.')),
  ))

  // ============================================================= What is Tesria
  await page('What is Tesria', root, doc(
    p('Tesria is a wiki that you run yourself. This page explains both halves of that: what a wiki is and why teams keep one, and what it means to run it on your own machine rather than paying someone else to. For a tour of what Tesria can actually do, see ', pageLink('Features'), '.'),

    h(2, 'What a wiki is'),
    p('A wiki is a website that the people who read it also write. Everyone on a team can add a page, fix a mistake they spot, or bring an old page up to date, straight from the browser. Nothing has to be emailed around or saved as a file.'),
    p('It sounds simple, and that is the point. Think of the knowledge every group of people builds up: how to set up a new laptop, who to call when the heating breaks, why the team chose one supplier over another, what the rules are for booking the meeting room. Without a wiki it ends up scattered across chat threads, email, documents on someone’s computer, and people’s heads. When someone leaves or goes on vacation, some of it goes with them.'),
    p('A wiki gives all of that one home:'),
    ul(
      li(p(b('One current version.'), ' There is one page about the heating, not five copies of a document with different dates. When something changes, whoever notices updates the page, and everyone sees the new version.')),
      li(p(b('Easy to find.'), ' Pages are organized into ', b('spaces'), ' (one for each team or project, say), nest under each other like chapters and sections, link to each other, and can all be searched at once.')),
      li(p(b('Nothing is lost.'), ' Every change is kept. You can see who changed a page and when, compare versions, and put an old one back.')),
      li(p(b('Written together.'), ' People comment on pages, mention each other, and can even edit the same page at the same time.')),
    ),
    panel('success', p(b('A good first page'), ' is the one people ask you about most often. Write down the answer once, send the link next time, and the wiki starts earning its keep on day one.')),

    h(2, 'Why run your own'),
    p('Many wikis are rented as an online service: you sign up, pay per person each month, and your pages live on the provider’s computers. Tesria is different. You install it on a computer you choose, and it runs there. That is what ', b('self-hosted'), ' means. It suits you if:'),
    ul(
      li(p(b('Your information should stay with you.'), ' Your pages, files and backups are stored on your own machine, not on another company’s. For many organizations that is a requirement, not a preference.')),
      li(p(b('You want it on your own network.'), ' Tesria can run on a home or office network and be reached only from there, by every computer, tablet and phone on it.')),
      li(p(b('You would rather not pay per person.'), ' Tesria is free and open source, under the Apache License 2.0. Adding the twentieth person costs the same as the second: nothing.')),
      li(p(b('You like to be in control.'), ' You decide when to upgrade, who can sign up, and how long backups are kept.')),
    ),

    h(2, 'What running it involves'),
    p('Running your own software means someone looks after it. Tesria is built to keep that small, but it is fair to know what it means before you start:'),
    ul(
      li(p(b('A computer that stays on.'), ' Tesria runs on it, so if it is switched off or asleep, nobody can reach the wiki. See ', pageLink('System requirements'), ' for how big it needs to be.')),
      li(p(b('An install, once.'), ' Tesria runs in Docker, a free program that runs software in self-contained packages. The ', pageLink('Quick start'), ' walks you through it, and a guided setup asks the rest in the browser.')),
      li(p(b('Backups, which are mostly automatic.'), ' Tesria backs itself up every day on its own machine. For real safety you also keep a copy somewhere else, such as a cloud bucket or a network drive. See ', pageLink('Backups and recovery'), '.')),
      li(p(b('Upgrades, now and then.'), ' Installing a new version takes two commands, and your pages are kept. See ', pageLink('Upgrading'), '.')),
      li(p(b('On a home or office network, trusting the server once per device.'), ' Browsers are wary of servers that are not on the public internet, so each device is told once to trust yours. See ', pageLink('Trusting the local certificate'), '.')),
    ),

    h(2, 'A few words you will see'),
    ul(
      li(p(b('Instance:'), ' one installed Tesria, with its own people and pages. Yours might be at ', c('wiki.example.com'), ' or ', c('studio.local'), '.')),
      li(p(b('Space:'), ' a home for a set of pages that belong together, with its own page tree and its own list of who can see it.')),
      li(p(b('Owner, administrator and user:'), ' the owner is the person who set Tesria up and holds the keys; administrators help run it; users read and write.')),
      li(p(b('Draft:'), ' a page that is still being written and that only its writer can see, until they choose ', b('Publish'), '.')),
    ),
    p('The ', pageLink('Glossary'), ' has the rest. When you are ready, go on to ', pageLink('Prerequisites'), '.'),
  ))

  // ============================================================== Prerequisites
  await page('Prerequisites', root, doc(
    p('Before you install Tesria, it helps to have a few things ready and to have thought about a few questions. None of it takes long, and knowing it up front means the install itself is just typing a few commands and waiting.'),

    h(2, 'What you need'),
    h(3, 'A computer to run it on'),
    p('Tesria runs on a computer that stays switched on, because everyone else reaches the wiki through it. It can be a Linux server, a small cloud machine, or a Mac or Windows computer that does not go to sleep. It does not need to be powerful: ', pageLink('System requirements'), ' has the figures.'),
    p('If you only want to try Tesria, your own laptop is fine. You can move it to a permanent home later.'),

    h(3, 'Docker, with Compose'),
    p(b('Docker'), ' is a free program that runs software in sealed, self-contained packages called ', b('containers'), '. Tesria is made of several parts (the wiki itself, its database, a web server, and the backup services), and each runs in its own container. ', b('Docker Compose'), ' is the part of Docker that starts them all together with one command, so you never have to install or wire up any of them yourself.'),
    ul(
      li(p(b('On a Mac or Windows,'), ' install ', b('Docker Desktop'), ' from docker.com. Compose comes with it.')),
      li(p(b('On Linux,'), ' install Docker Engine and its Compose plugin from your distribution or from docker.com.')),
    ),
    p('To check it is ready, open a terminal (Terminal on a Mac, PowerShell on Windows) and run:'),
    codeBlock('bash', 'docker compose version'),
    p('If it prints a version number, you are set. Note the space: it is ', c('docker compose'), ', not the older ', c('docker-compose'), '.'),

    h(3, 'Git, to download Tesria'),
    p('Tesria is downloaded with ', b('git'), ', the tool most software projects are shared with. Linux usually has it already; on a Mac, typing ', c('git'), ' in Terminal offers to install it; on Windows, install Git for Windows.'),

    h(3, 'Ports 80 and 443 free'),
    p('Tesria’s web server answers on ports 80 and 443, the standard ports for web pages. If another web server is already running on the same computer, one of them has to move before Tesria can start.'),

    h(3, 'A safe place for secrets'),
    p('During the install you make up a few long passwords, and the setup wizard gives you recovery codes for your account. A password manager is the ideal place for them. One matters more than the rest:'),
    panel('warning', p(b('Keep the backup encryption key somewhere safe, away from the server.'), ' Tesria encrypts its backups with ', c('BACKUP_ENCRYPTION_KEY'), ', which you set during the install. Without it, the backups cannot be restored. If the server is lost and the key with it, so are the backups.')),

    h(2, 'How people will reach it'),
    p('Every Tesria has an address that people type into their browser. There are two kinds, and it is worth deciding which you want before you start:'),
    ul(
      li(p(b('A web address you own,'), ' such as ', c('wiki.example.com'), '. Tesria gets a free certificate for it automatically, so every browser shows a padlock with no extra steps. The name has to point at your server, and the server has to be reachable from the internet while the certificate is issued. Before opening Tesria to the internet, read ', pageLink('HTTPS and domains'), '.')),
      li(p(b('Your computer’s name on your network,'), ' such as ', c('studio.local'), '. No domain and no internet exposure needed: everyone on the same network reaches it by that name. Tesria makes its own certificate, and each device is told once to trust it; see ', pageLink('Trusting the local certificate'), ' and ', pageLink('Opening Tesria by name'), '.')),
    ),
    p('If you are not sure, start with the second. You can add a web address later.'),

    h(2, 'Questions the setup wizard will ask'),
    p('The first time you open Tesria, a guided setup asks these. Every answer can be changed later, so a best guess is fine:'),
    ul(
      li(p(b('Who can join?'), ' Only people you invite, or anyone who can reach the address. For most teams, invite only is the right start.')),
      li(p(b('Can people read without signing in?'), ' Only matters if some spaces should be public, like a help site. Nothing becomes public until you mark a space.')),
      li(p(b('How much backup history to keep?'), ' More history means more disk space. The suggested setting is a sensible start.')),
    ),
    p('See ', pageLink('First-run setup wizard'), ' for every step.'),

    h(2, 'Optional, and easy to add later'),
    ul(
      li(p(b('An email server'), ' (SMTP), so Tesria can send password resets, invitations and notifications. Without one, Tesria still works: an administrator gives anyone who forgets their password a one-time reset link, and invitations are links you pass on yourself. See ', pageLink('Email (SMTP)'), '.')),
      li(p(b('Somewhere else to keep backups:'), ' a cloud storage bucket, a network drive, or a removable drive. See ', pageLink('Offsite copies'), '.')),
      li(p(b('Single sign-on'), ' (in beta), if your organization already signs people in with an OpenID Connect provider. See ', pageLink('Single sign-on (OIDC)'), '.')),
    ),
    p('Ready? Check the ', pageLink('System requirements'), ', then go to the ', pageLink('Quick start'), '.'),
  ))

  // ======================================================== System requirements
  await page('System requirements', root, doc(
    p('Tesria is light for what it does. These figures come from measuring a running Tesria, not from guesswork, and they leave plenty of room to spare. A small cloud server or a spare computer is usually enough.'),

    h(2, 'The server'),
    table([
      ['', 'At least'],
      ['Processor', '2 cores'],
      ['Memory', '2 GB (4 GB to build)'],
      ['Disk', '20 GB to start'],
      ['System', 'Linux, macOS or Windows'],
    ], [200, 400]),
    ul(
      li(p(b('Processor:'), ' x86-64 or ARM64, so Apple silicon Macs and ARM servers work as well as ordinary PCs. Most of the time Tesria is idle; exporting a page as a PDF keeps one core busy for about a second.')),
      li(p(b('Memory:'), ' 2 GB to run Tesria from its ready-made images, as ', pageLink('Quick start'), ' does. Building Tesria from source instead needs 4 GB, on the first install and each upgrade.')),
      li(p(b('Disk:'), ' about 5 GB of it is the software itself. The rest holds your pages, files and, above all, backups, which grow with the history you choose to keep. More disk is never wasted here.')),
      li(p(b('System:'), ' anything that runs Docker with Compose: Linux, or a Mac or Windows computer with Docker Desktop.')),
    ),
    panel('note', p(b('On a Mac or Windows,'), ' Docker Desktop runs Tesria inside its own small virtual machine, and the memory and disk that machine may use are set in Docker Desktop’s settings, under ', b('Resources'), '. Make sure it is given at least the figures above.')),

    h(3, 'What was measured'),
    p('For the curious, and for anyone sizing a server:'),
    ul(
      li(p(b('About 550 MB of memory at rest,'), ' for every part of Tesria together.')),
      li(p(b('665 MB at the busiest moment'), ' of a run of PDF and website exports. The 2 GB above is three times that.')),
      li(p(b('About 5.2 GB of disk'), ' for the software. The PDF renderer, which includes a web browser of its own, is 3.5 GB of that.')),
    ),

    h(2, 'Browsers'),
    p('Tesria works in current versions of every major browser, on computers, tablets and phones. The oldest versions it supports are:'),
    table([
      ['Browser', 'Version'],
      ['Chrome and Edge', '111 or later'],
      ['Firefox', '121 or later'],
      ['Safari (Mac, iPhone, iPad)', '16.2 or later'],
    ], [300, 200]),
    p('If your browser keeps itself up to date, as most do, you do not need to think about this.'),

    h(2, 'Phones and tablets'),
    p('Every page works on a phone or tablet, in portrait and landscape, and so does writing: the editor has a toolbar made for a small screen. There is nothing to install; open Tesria’s address in the browser. See ', pageLink('Tesria on phones and tablets'), ' for what changes on a small screen.'),
  ))

  // ================================================================ Quick start
  await page('Quick start', root, doc(
    p('This page takes you from a computer with Docker to your own Tesria, open in your browser and ready for its guided setup. You type a handful of commands and fill in one settings file; the rest is waiting while Tesria downloads.'),
    p('If you have not yet, read ', pageLink('Prerequisites'), ' first: it explains Docker, and what to have ready.'),
    panel('note', p(b('Throughout this page, '), c('your-server'), b(' stands for your server’s address:'), ' the name people will type to reach Tesria, such as ', c('studio.local'), ' or ', c('wiki.example.com'), '. On the computer Tesria runs on, ', c('localhost'), ' works too.')),

    step(1, 'Download Tesria'),
    p('Open a terminal (Terminal on a Mac, PowerShell on Windows) on the computer Tesria will run on, go to the folder you want Tesria in, and download the latest release. On a Mac or Linux:'),
    codeBlock('bash', 'curl -LO https://github.com/Tesria/Tesria/releases/latest/download/tesria-deploy.zip\nunzip tesria-deploy.zip -d tesria && cd tesria'),
    p('On Windows, in PowerShell:'),
    codeBlock('powershell', 'Invoke-WebRequest https://github.com/Tesria/Tesria/releases/latest/download/tesria-deploy.zip -OutFile tesria-deploy.zip\nExpand-Archive tesria-deploy.zip -DestinationPath tesria; cd tesria'),
    p('That makes a folder called ', c('tesria'), ' with Tesria’s settings and scripts, about 100 KB, and moves into it. Tesria itself comes as ready-made images, downloaded in step 3. Run the rest of the commands on this page from that folder.'),

    step(2, 'Make your settings file'),
    p('Tesria reads its settings from a plain text file called ', c('.env'), ' in that folder. It comes with an example to copy:'),
    codeBlock('bash', 'cp .env.example .env'),
    p('On Windows: ', c('Copy-Item .env.example .env'), '.'),
    p('Open ', c('.env'), ' in a plain text editor (', c('nano .env'), ' in a Mac or Linux terminal, ', c('notepad .env'), ' on Windows). Each line is a setting, in the form ', c('NAME=value'), '. Change these before the first start:'),
    ul(
      li(p(c('POSTGRES_PASSWORD'), ': the password for Tesria’s database. Any long random string.')),
      li(p(c('APP_DB_PASSWORD'), ': a second long random string, different from the first. Tesria creates a restricted database account with it and runs as that account, which cannot alter or delete the audit log.')),
      li(p(c('BACKUP_ENCRYPTION_KEY'), ': required. Another long random string, which encrypts your backups. ', b('Save a copy in your password manager now:'), ' backups cannot be restored without it.')),
      li(p(c('DOMAIN'), ': your web address, such as ', c('wiki.example.com'), ', if you have one pointed at this computer. Otherwise leave it as ', c('localhost'), '. Tesria is still reachable from other devices on your network, by the computer’s name.')),
      li(p(c('ACME_EMAIL'), ': your email address, if you set a web address above. The certificate service writes to it about your certificate.')),
      li(p(c('COLLAB_SHARED_SECRET'), ': optional, but worth setting. Any long random string turns on editing a page with several people at the same time.')),
      li(p(c('PDF_SHARED_SECRET'), ': optional, but worth setting. Any long random string turns on exporting pages as PDFs.')),
    ),
    p('On a Mac or Linux, this makes a good long random string each time you run it:'),
    codeBlock('bash', 'openssl rand -hex 32'),
    p('A password manager’s generator works just as well, on any computer.'),
    panel('warning', p(b('Do not skip a setting.'), ' The example file is filled with placeholder values rather than left blank, so a setting you forget does not stop Tesria starting: it quietly uses the placeholder, which is not a secret.')),
    panel('success', p(b('Cannot see .env in the Mac Finder?'), ' Files whose names start with a dot are hidden. Press ', b('⌘ Shift .'), ' in a Finder window to show them.')),

    step(3, 'Start Tesria'),
    codeBlock('bash', 'docker compose pull\ndocker compose up -d'),
    p('The first command downloads Tesria’s images, the ready-made software for each of its parts, for Intel, AMD or ARM computers alike. The second starts them, and ', c('-d'), ' lets them carry on in the background, so you get your terminal back. The download is several hundred megabytes the first time; later starts take seconds.'),
    p('While it starts, Tesria sets up its database and backups by itself. There is nothing else to install.'),
    panel('warning', p(b('Always start everything together.'), ' On a new install, starting only the database with ', c('docker compose up -d db'), ' makes it restart over and over, because its backups wait for the backup service that starts alongside it.')),

    step(4, 'Open Tesria in your browser'),
    p('Go to ', c('https://your-server'), ', or ', c('https://localhost'), ' on the computer Tesria runs on.'),
    ul(
      li(p(b('With a web address of your own,'), ' you see a padlock and Tesria straight away.')),
      li(p(b('With localhost or your computer’s name,'), ' the browser warns that the connection is not private the first time. Your server is fine: it has made its own certificate, which the browser does not know yet. ', pageLink('Trusting the local certificate'), ' explains why and makes the warning go away for good, one device at a time.')),
    ),
    p('To check that Tesria is up, open ', c('https://your-server/api/health'), '. It answers with a short line of text that includes ', c('"status":"ok"'), '.'),

    step(5, 'Follow the setup wizard'),
    p('Tesria opens its ', b('setup wizard'), ', which creates your account as the ', b('owner'), ' and asks a few questions about who can join and how much backup history to keep. It takes a few minutes; ', pageLink('First-run setup wizard'), ' goes through every step.'),
    panel('warning', p(b('Do the setup straight away.'), ' Until it is done, the first person to open the address and create an account becomes the owner. Finish the wizard before you share the address with anyone.')),

    h(2, 'Building from source instead'),
    p('If you want to change Tesria, or would rather build it yourself than download its images, clone the repository instead of step 1, and build in step 3:'),
    codeBlock('bash', 'git clone https://github.com/Tesria/Tesria.git\ncd Tesria'),
    codeBlock('bash', 'docker compose up -d --build'),
    p('Building takes several minutes the first time, and needs more memory than running (see ', pageLink('System requirements'), '). ', pageLink('Contributing'), ' covers working on the code.'),

    h(2, 'Next'),
    p('Once the wizard is done, ', pageLink('Your first space and page'), ' walks you through writing something. When other devices on your network need to reach Tesria, see ', pageLink('Opening Tesria by name'), '. And ', pageLink('Installing with Docker Compose'), ' explains what each part of Tesria does and where it keeps your data.'),
  ))

  // ===================================================== First-run setup wizard
  const wizard = await ensure('First-run setup wizard', root)
  await page('First-run setup wizard', root, doc(
    p('The first time anyone opens a new Tesria, it shows the ', b('setup wizard'), ': a guided tour of the settings every instance needs, one question at a time. It creates your account, names your Tesria, and asks who can join and how long to keep backups. The five required steps take about two minutes, and every answer can be changed later in Administration.'),
    p('Only the owner sees the wizard. Anyone else who signs in meanwhile carries on as normal.'),
    panel('warning', p(b('Run it as soon as Tesria starts.'), ' Whoever creates the first account becomes the owner of the whole instance. Finish the wizard before you tell anyone the address.')),

    h(2, 'Before you start'),
    ul(
      li(p(b('The email address and password'), ' you want to sign in with. The password needs at least 8 characters.')),
      li(p(b('Somewhere safe to keep recovery codes,'), ' such as a password manager. The wizard shows them once.')),
      li(p(b('Tesria’s address,'), ' the one people will type to reach it, such as ', c('https://wiki.example.com'), '.')),
      li(p(b('An authenticator app on your phone,'), ' if you want to turn on two-factor sign-in now (recommended).')),
    ),

    h(2, 'How the wizard works'),
    p('The steps are listed on the left, and the one you are on fills the rest of the screen. Each is marked ', b('Required'), ', ', b('Optional'), ' or ', b('Recommended'), '. A finished step gets a check mark, and one you skipped gets a dash; you can click either to go back to it.'),
    ...(await picture(wizard, 'setup-welcome', 'The setup wizard, with its ten steps listed on the left',
      'The setup wizard. Its ten steps are listed on the left; the one you are on fills the rest.')),
    p('The steps below have the same numbers as the list, so you can follow along.'),

    step(1, 'Welcome'),
    p('A short summary of what is ahead. Choose ', b('Start'), '.'),

    step(2, 'Your account'),
    p('This makes the first account, which becomes the ', b('owner'), ': the one account that can hand the instance over to someone else, and the one that nobody else can suspend or reset. Enter your ', b('Email'), ', ', b('Your name'), ' (as others will see it) and a ', b('Password'), ', then choose ', b('Create the owner account'), '.'),
    p('Tesria then shows your ', b('recovery codes'), '. Each one signs you in once if you ever lose your password. Save them somewhere other than this computer, tick ', b('I have saved these somewhere safe'), ', and choose ', b('Continue'), '.'),
    panel('error', p(b('Do not skip saving the codes.'), ' Nobody can reset the owner’s password, not even an administrator. Losing both the password and the recovery codes means losing the instance.')),

    step(3, 'This instance'),
    ul(
      li(p(b('What is it called:'), ' the name of your Tesria, such as ', i('Acme wiki'), '. It appears in the browser tab, in emails Tesria sends, and in your authenticator app.')),
      li(p(b('Its address:'), ' the address people reach Tesria at. It starts as the one you are using now. Links in emails use it, so if people will reach Tesria by a different name, such as ', c('https://wiki.example.com'), ' or ', c('https://studio.local'), ', put that here.')),
    ),
    p('Choose ', b('Continue'), '.'),

    step(4, 'Who can join'),
    p('Pick one of the two cards; you cannot continue until you do.'),
    ul(
      li(p(b('Invite only:'), ' nobody can sign up on their own. You create an invite link for each person and send it to them however you like. The safer choice, and right for most teams.')),
      li(p(b('Open:'), ' anyone who can reach the address can create an account. Only choose this if everyone who can reach it should be able to join, for example on a private home network.')),
    ),
    p(b('Allow anonymous reading'), ' decides whether people can read without signing in. Off, every visitor has to sign in. On, spaces you mark as public can be read by anyone, which suits a help site or public documentation. Turning it on publishes nothing by itself: nothing is public until you mark a space. See ', pageLink('Public reading'), '.'),

    step(5, 'What roles may do'),
    p('Every account has a ', b('role'), ': owner, administrator or user. This step shows what each role is allowed to do, one right per row, such as creating spaces or deleting other people’s pages, with a column for each role.'),
    ...(await picture(wizard, 'setup-permissions', 'The table of what each role may do',
      'Each row is a right, and each column a role. A tick means the role has that right.')),
    p('The defaults suit most teams, so the easiest answer is ', b('Keep these defaults'), '. If something should be different, such as users not being allowed to create spaces, untick it here first. The same table is always in Administration, under ', b('Roles'), '; see ', pageLink('Roles'), '.'),

    step(6, 'Backups'),
    p('Tesria is already backing itself up: a daily copy of the database and every uploaded file, plus a continuous backup that can rewind the database to any moment. This step decides how much of that history to keep.'),
    p('The suggestion is to keep the newest 3 backups, and everything from the last 14 days. Keeping more uses more disk space. ', b('Keep every backup forever'), ' never removes any, which is only wise with plenty of disk to spare. Choose ', b('Keep these settings'), '.'),
    panel('warning', p(b('The backups are encrypted with BACKUP_ENCRYPTION_KEY'), ' from your ', c('.env'), ' file. If you have not already, save a copy of it somewhere other than this server. Without it, the backups cannot be read.')),
    p('To keep a copy of your backups somewhere else, which is what saves you if the server itself is lost, see ', pageLink('Offsite copies'), '.'),

    step(7, 'Email'),
    p('Tesria sends password resets, invitations and notifications by email, through an email server (SMTP) such as your email provider’s. If you have its details, fill in the ', b('SMTP host'), ', ', b('Port'), ' (587 is the usual one), ', b('Username'), ' and ', b('From address'), ', and choose ', b('Continue'), '. The password and a test email are in Administration, under ', b('Settings'), '.'),
    p('No email server? Choose ', b('Skip for now'), '. Tesria works fine without one: when someone forgets their password, you give them a one-time reset link from Administration instead. See ', pageLink('Email (SMTP)'), ' when you are ready.'),

    step(8, 'Two-factor'),
    p(b('Two-factor sign-in'), ' means signing in takes your password ', i('and'), ' a six-digit code from an app on your phone, so a stolen password alone is not enough. It is recommended for the owner in particular, because nobody can reset that account.'),
    ol(
      li(p('If you signed in more than a few minutes ago, enter your ', b('Current password'), ' first. Then choose ', b('Set up two-factor'), '.')),
      li(p('Scan the code it shows with an authenticator app, such as Aegis, 1Password, Google Authenticator or Authy.')),
      li(p('Enter the six digits the app shows, and choose ', b('Turn on'), '.')),
    ),
    p('Then choose ', b('Continue'), '. Or choose ', b('Skip for now'), ' and do it later from your profile; see ', pageLink('Two-factor and recovery codes'), '.'),

    step(9, 'A first space'),
    p('A ', b('space'), ' holds the pages for one team, project or topic. Type a ', b('Name'), ', such as ', i('Team handbook'), ', and a short ', b('Key'), ', such as ', c('TEAM'), '. Tesria starts the key for you from the name; check it and make it what you want, at least two letters and digits. The key is part of the address of every page in the space and cannot be changed later. Choose ', b('Continue'), ', or ', b('Skip for now'), ' to make spaces later. ', pageLink('Creating a space'), ' helps you decide what deserves a space of its own.'),

    step(10, 'Done'),
    p('The last step says your instance is ready and lists any steps you skipped; each one is waiting in Administration. Choose ', b('Finish'), '.'),
    p('Before finishing, Tesria checks that the required steps really are done. If one is not, it takes you back to that step.'),
    p('Tesria then offers a short welcome tour of five screens, on spaces, writing, working together, finding things and your profile. Take it or leave it: you can open it again later from your profile. See ', pageLink('The tour and tips'), '.'),

    h(2, 'Stopping partway'),
    p('You do not have to finish in one go. What you have answered is saved as you go. Close the browser, and the next time you sign in as the owner the wizard opens again, with your finished steps ticked.'),

    h(2, 'On a phone'),
    p('The wizard works on a phone too. The list of steps sits above the question instead of beside it, two to a row, so scroll down past it to the step you are on.'),
    ...(await phonePicture(wizard, 'setup-welcome', 'The setup wizard on a phone, with the steps above the first question',
      'On a phone, the steps come first and the question below them.')),

    h(2, 'Next'),
    p('Your Tesria is ready. ', pageLink('Your first space and page'), ' walks you through writing something and inviting people to read it.'),
  ))

  // ================================================== Your first space and page
  const first = await ensure('Your first space and page', root)
  await page('Your first space and page', root, doc(
    p('With the setup done, your Tesria is an empty wiki waiting for its first page. This page walks you through the short path from here to a page other people can read: a space to put it in, the page itself, and the people to share it with. Each step links to the page in the ', pageLink('User manual'), ' that tells the whole story.'),

    step(1, 'Make a space'),
    p('Pages live in ', b('spaces'), ': one for each team, project or audience. If you made one in the setup wizard, it is waiting under ', b('Spaces'), ' at the top of the screen, and you can go straight to step 2.'),
    p('If not, choose ', b('Spaces'), ', then ', b('New space'), '. Give it a short key, such as ', c('TEAM'), ', and a name, such as ', i('Team handbook'), ', and choose ', b('Create'), '. ', pageLink('Creating a space'), ' walks through it with pictures and helps you decide how to divide things up.'),

    step(2, 'Start a page'),
    p('Open the space. In a new, empty space, choose ', b('Create the first one'), '. Once it has pages, choose ', b('+ New page'), ' at the top of the space’s sidebar (on a phone, ', b('+ New'), '). Choosing it while a page is open makes the new page a sub-page of that one.'),
    p('A good first page says what the space is for and who looks after it. If your space already has templates, a menu above the title offers to start from one; see ', pageLink('Templates'), '.'),

    step(3, 'Write'),
    p('Type a title, and press ', b('Enter'), ' to move down to the page itself. Then just type: it works like a word processor. To add anything other than text, such as a heading, a table, a checklist or a colored panel, type ', c('/'), ' at the start of a new line and pick from the menu.'),
    ...(await animation(first, 'first-page', 'A title, a line of text, and a Tip panel made by typing /tip.')),
    p('The editor’s toolbar has everything else. ', pageLink('The slash menu'), ' lists everything you can insert.'),

    step(4, 'Publish'),
    p('Until you publish it, the page is a ', b('draft'), ' that only you can see. When it is ready, choose ', b('Publish'), ' at the top right. The page appears in the space’s tree, and anyone watching the space hears about it.'),
    panel('warning', p(b('Close on a new page throws it away.'), ' It does not ask first. To keep what you have written, choose ', b('Publish'), '.')),
    p('To change the page later, open it and choose ', b('Edit'), '; when you are done, choose ', b('Update'), '. Every update is kept in the page’s history, so nothing is ever lost. See ', pageLink('Drafts, Publish and Update'), '.'),

    step(5, 'Invite people'),
    p('A wiki gets useful once other people read it and write in it. If you chose ', b('Invite only'), ' in the setup wizard, invite each person with a link:'),
    ol(
      li(p('Choose ', b('Admin'), ' at the top of the screen, then the ', b('Invites'), ' tab. In a narrower window, Admin is under ', b('More'), '; on a phone, it is in the menu.')),
      li(p('Optionally enter the person’s email address, so only they can use the link, and choose how many days it lasts.')),
      li(p('Choose ', b('Create invite'), ', then ', b('Copy'), ', and send the link to them however you like. It is shown only once, and works once.')),
    ),
    p('See ', pageLink('Invites'), ' for more. A new space can be read and edited by everyone who is signed in; to keep one to some people only, see ', pageLink('Who can see a space'), '.'),

    h(2, 'Where to go from here'),
    ul(
      li(p(b('Make pages look alike'), ' with ', pageLink('Templates'), ', for meeting notes, project briefs and anything else your team writes again and again.')),
      li(p(b('Make the important thing stand out'), ' with a colored ', pageLink('Panels', 'panel'), ', like the Tip in the animation above.')),
      li(p(b('Learn the rest of the editor'), ' in ', pageLink('The editor'), '.')),
      li(p(b('Get a feel for everything else'), ' in ', pageLink('Features'), '.')),
    ),
  ))
}

// Pictures these pages no longer use, taken down so they do not linger in the
// pages' attachments or in the exported pack. The first version put a phone
// copy of every picture beside it, and a whole-window picture on most steps.
const RETIRED = {
  'What is Tesria': ['what-is-tesria.png', 'what-is-tesria.phone.png'],
  'First-run setup wizard': [
    'setup-account', 'setup-instance', 'setup-registration', 'setup-backups',
    'setup-email', 'setup-two-factor', 'setup-first-space', 'setup-done',
  ].flatMap((s) => [`${s}.png`, `${s}.phone.png`]).concat(['setup-permissions.phone.png']),
  'Your first space and page': ['spaces-list', 'new-space', 'space-home', 'new-page', 'published-page']
    .flatMap((s) => [`${s}.png`, `${s}.phone.png`]),
}

export async function cleanup({ author }) {
  const space = await author.call('GET', '/api/spaces/DOCS')
  const tree = await author.call('GET', `/api/pages/tree?spaceId=${space.id}`)
  const section = tree.find((n) => n.title === 'Getting started')
  if (!section) return
  for (const [title, files] of Object.entries(RETIRED)) {
    const node = (section.children ?? []).find((n) => n.title === title)
    if (!node) continue
    for (const a of await author.call('GET', `/api/pages/${node.id}/attachments`)) {
      if (files.includes(a.filename)) {
        await author.call('DELETE', `/api/attachments/${a.id}`)
        console.log(`  - ${title}: ${a.filename}`)
      }
    }
  }
}
