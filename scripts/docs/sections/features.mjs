// Features: everything Tesria does, grouped by what you are trying to do,
// each with what it is, why you would want it, a picture or a short
// animation of it, and a link to the page that explains it (the owner,
// 2026-09-24: "we want it to look nice and entice people to use the
// product"). Second at the top of the tree, after Welcome, so someone
// deciding whether to use Tesria finds it first. No other products named,
// and nothing that is not built yet.
//
// Pictures follow scripts/docs/WRITING.md: narrow windows so they read on
// a phone, only the Tesria Demo space, the Docs space's own tree, and the
// example accounts. A few features have no picture because none shows
// anything words do not (sessions) or because the screen holds details a
// public page must not (backups, which name the owner's storage).

const NARROW = { width: 480, height: 900 }
const section = (title) => `section.profile__section:has(> h2:text-is("${title}"))`
const tag = (selector, text, name) =>
  `[...document.querySelectorAll('${selector}')].find((b) => b.textContent.trim() === '${text}')?.setAttribute('data-shot', '${name}')`
const closeNewPage = (name) => ({ name, skipCapture: true, phone: false, steps: [{ click: '.page-actionbar button:has-text("Close")' }, { wait: 1200 }] })

export const shots = ({ demo }) => [
  // ---- Writing pages
  {
    name: 'feat-editor', url: '/spaces/DEMO/new', phone: false,
    viewport: { width: 480, height: 400 }, record: { size: { width: 480, height: 400 } },
    waitFor: '.ProseMirror', lead: 800, tail: 1500,
    css: '.tip, .onboarding-tip { display: none !important; }',
    steps: [
      { click: 'input.title-input' }, { typeSlowly: 'Weekly update', delay: 60 }, { wait: 300 },
      { click: '.ProseMirror' }, { typeSlowly: '/h2', delay: 110 }, { wait: 600 }, { press: 'Enter', selector: '.ProseMirror' },
      { typeSlowly: 'Wins this week', delay: 45 }, { press: 'Enter', selector: '.ProseMirror' },
      { typeSlowly: '/info', delay: 110 }, { wait: 600 }, { press: 'Enter', selector: '.ProseMirror' },
      { typeSlowly: 'Launch moved to October 14.', delay: 45 }, { wait: 900 },
    ],
  },
  { name: 'feat-chart', url: demo('Chart'), viewport: NARROW, phone: false, steps: [{ wait: 3000 }], clipTo: '.page-body .chart', clipPad: 10 },
  { name: 'feat-gallery', url: demo('Gallery'), viewport: NARROW, phone: false, steps: [{ wait: 3000 }], clipTo: '.page-body .gallery', clipPad: 10 },
  // The Demo's embedded film shows what an embed is; its smart link is only example.com.
  { name: 'feat-embed', url: demo('Embed'), viewport: NARROW, phone: false, steps: [{ wait: 5000 }], clipTo: '.page-body .embed', clipPad: 10 },
  { name: 'feat-layout', url: demo('Layout'), viewport: { width: 760, height: 900 }, phone: false, steps: [{ wait: 3000 }], clipTo: '.page-body [data-type="layout-section"]', clipPad: 10 },
  // A task report is a table, and a wide one: a narrow window cut it off.
  { name: 'feat-live', url: demo('Reports'), viewport: { width: 760, height: 900 }, phone: false, steps: [{ wait: 3500 }], clipTo: '.page-body .dynamic-block', clipPad: 10 },
  {
    name: 'feat-draft', url: '/spaces/DEMO/new', viewport: NARROW, phone: false, waitFor: '.ProseMirror',
    steps: [{ wait: 1500 }, { eval: tag('.page-actionbar button', 'Publish', 'publish') }],
    clipTo: '.page-actionbar', clipPad: 6,
    annotate: [{ type: 'box', target: '[data-shot="publish"]', pad: 4 }],
  },
  closeNewPage('feat-draft-closed'),
  {
    name: 'feat-template', url: '/spaces/DEMO/new', viewport: NARROW, phone: false, waitFor: '.editor-form select',
    steps: [{ wait: 1500 }], clipTo: '.editor-form label.change-comment', clipPad: 12,
    annotate: [{ type: 'box', target: '.editor-form label.change-comment select', pad: 4 }],
  },
  closeNewPage('feat-template-closed'),

  // ---- Keeping it organized
  { name: 'feat-spaces', url: '/spaces', viewport: NARROW, phone: false, steps: [{ wait: 2500 }], clipTo: '.space-grid', clipPad: 10 },
  // The Docs space's own tree, numbered: the Demo space's is plain.
  { name: 'feat-tree', url: '/spaces/DOCS', viewport: { width: 900, height: 640 }, phone: false, steps: [{ wait: 3000 }], clipTo: '.sidebar .tree-section', clipPad: 6 },
  {
    name: 'feat-filter', url: '/spaces/DOCS', phone: false,
    viewport: { width: 420, height: 560 }, record: { size: { width: 420, height: 560 } },
    waitFor: '.space-home-tree .tree-filter__input', lead: 900, tail: 1800,
    css: '.tip, .onboarding-tip { display: none !important; }',
    steps: [
      { eval: "sessionStorage.clear(); document.querySelector('.space-home-tree').scrollIntoView()" }, { wait: 400 },
      { click: '.space-home-tree .tree-filter__input' }, { typeSlowly: 'backup', delay: 140 }, { wait: 1600 },
    ],
  },
  { name: 'feat-labels', url: '/labels', viewport: NARROW, phone: false, steps: [{ wait: 2500 }], clipTo: '.page-wrap', clipPad: 0 },
  { name: 'feat-search', url: '/search?q=launch', viewport: NARROW, phone: false, steps: [{ wait: 3000 }], clip: { x: 0, y: 52, width: 480, height: 620 } },
  {
    name: 'feat-history', url: demo('Launch plan'), viewport: NARROW, phone: false,
    steps: [{ wait: 2500 }, { click: 'article .tabs button:has-text("History")' }, { wait: 800 }, { scrollTo: '.tab-panel' }],
    clipTo: ['article .tabs', '.tab-panel'], clipPad: 10,
  },

  // ---- Working together
  {
    name: 'feat-comments', url: demo('Launch plan'), viewport: NARROW, phone: false,
    steps: [{ wait: 2500 }, { click: 'article .tabs button:has-text("Comments")' }, { wait: 800 }, { scrollTo: '.tab-panel' }],
    clipTo: ['article .tabs', '.tab-panel'], clipPad: 10,
  },
  {
    name: 'feat-bell', url: demo('Launch plan'), viewport: NARROW, phone: false,
    steps: [{ wait: 2500 }, { click: '.notif__bell' }, { wait: 800 },
      { eval: "document.querySelectorAll('.notif__dropdown .notif__item').forEach((e) => { if (e.textContent.startsWith('Security')) e.remove() })" }],
    clipTo: '.notif__dropdown', clipPad: 8,
  },
  { name: 'feat-permissions', url: '/spaces/DEMO/settings/permissions', viewport: NARROW, phone: false, steps: [{ wait: 2500 }], clip: { x: 0, y: 52, width: 480, height: 640 } },

  // ---- Sharing and publishing
  {
    name: 'feat-export', url: demo('Launch plan'), viewport: NARROW, phone: false,
    steps: [{ wait: 2500 }, { click: 'button[title="More actions"]' }, { wait: 400 }],
    clipTo: ['.page-actionbar', '.overflow-menu__dropdown'], clipPad: 8,
    annotate: [{ type: 'box', target: '.overflow-menu__dropdown > a:first-of-type', pad: 3 }],
  },
  { name: 'feat-site', url: '/spaces/DEMO/settings', viewport: NARROW, phone: false, steps: [{ wait: 2500 }], clipTo: '#export', clipPad: 8 },
  { name: 'feat-pack', url: '/spaces/DEMO/settings', viewport: NARROW, phone: false, steps: [{ wait: 2500 }], clipTo: '#pack', clipPad: 8 },
  { name: 'feat-public', url: '/admin/settings', viewport: NARROW, phone: false, steps: [{ wait: 2500 }], clipTo: section('Access'), clipPad: 8 },

  // ---- Accounts and security
  { name: 'feat-invites', url: '/admin/invites', viewport: NARROW, phone: false, steps: [{ wait: 2500 }], clipTo: 'form.form-inline', clipPad: 10 },
  { name: 'feat-two-factor', url: '/profile', viewport: NARROW, phone: false, steps: [{ wait: 2500 }], clipTo: section('Two-factor sign-in'), clipPad: 8 },
  { name: 'feat-groups', url: '/admin/groups', viewport: NARROW, phone: false, steps: [{ wait: 2500 }], clipTo: 'ul.version-list', clipPad: 10 },
  { name: 'feat-protection', url: '/admin/security', viewport: NARROW, phone: false, steps: [{ wait: 2500 }], clipTo: section('Brute-force protection'), clipPad: 8 },

  // ---- Running it
  {
    name: 'feat-trust', url: '/trust', viewport: NARROW, phone: false,
    steps: [{ wait: 1500 }, { type: 'wiki-server.local', selector: '#trust-address' }, { click: '[data-device="mac"]' }, { wait: 300 }],
    clipTo: ['.trust h1', '[data-step="1"]'], clipPad: 12,
  },
  // The two activity charts, which say more than the counts above them.
  { name: 'feat-dashboard', url: '/admin', viewport: NARROW, phone: false, steps: [{ wait: 3000 }], clipTo: ['.stat--wide', ':nth-match(.stat--wide, 2)'], clipPad: 10 },

  // ---- For developers
  // Where a token is made, rather than the reference, which is a page of text.
  // Only this section is shown: it is the last on the profile, and when the
  // Sessions list above it listed every ended session (fixed 2026-09-24) it sat
  // past the 16,000 pixels Chromium can capture, and came out blank.
  { name: 'feat-api', url: '/profile', viewport: NARROW, phone: false, steps: [{ wait: 2500 }, { css: '.profile__section:not(#api-tokens) { display: none !important; }' }, { wait: 500 }], clipTo: section('API tokens'), clipPad: 8 },
  { name: 'feat-webhooks', url: '/spaces/DEMO/settings/webhooks', viewport: NARROW, phone: false, steps: [{ wait: 2500 }], clip: { x: 0, y: 52, width: 480, height: 560 } },

  // ---- On a phone: the one picture taken on a phone.
  { name: 'feat-phone', url: demo('Launch plan'), desktop: false, steps: [{ wait: 3000 }] },
]

export async function build({ page, ensure, doc, p, h, text, bold, italic, panel, picture, phonePicture, animation, pageLink }) {
  const b = (t) => text(t, bold)
  const i = (t) => text(t, italic)

  const features = await ensure('Features', null)
  /**
   * One feature: its name as a heading, what it does and why that matters,
   * the page that explains it, and its picture or animation.
   */
  const feature = async (name, what, see, shot) => [
    h(3, name),
    p(...[].concat(what), ...(see ? [' See ', pageLink(see), '.'] : [])),
    ...(shot?.kind === 'animation' ? await animation(features, shot.name)
      : shot?.kind === 'phone' ? await phonePicture(features, shot.name, shot.alt)
      : shot ? await picture(features, shot.name, shot.alt) : []),
  ]
  const pic = (name, alt) => ({ name, alt })
  const anim = (name) => ({ kind: 'animation', name })

  await page('Features', null, doc(
    p('Tesria is a wiki for teams: a shared place to write things down, keep them organized, and find them again. You run it on your own computer or server, so your pages stay with you. This page is a tour of what it can do, grouped by what you are trying to get done, with a link to the page that explains each part in full.'),

    h(2, 'Writing pages'),
    p('Pages are written in an editor that works like a word processor: type, and format as you go. There is no special syntax to learn, and nothing to save by hand while you work.'),
    ...(await feature('A rich editor', 'Headings, lists, tables, links, quotes, text colors and highlights, all from a toolbar or by typing / to open a menu of everything you can insert. People who like keyboard shortcuts get Markdown-style ones too, such as # for a heading.', 'Editor tour', anim('feat-editor'))),
    ...(await feature('More than text', 'Colored panels for notes and warnings, expandable sections, decisions, task lists with the people doing each task, status labels, dates, mentions, code with highlighting, math, diagrams drawn from text, and charts: bar, column, line and pie, drawn from a table on the page.', 'Elements', pic('feat-chart', 'A chart drawn from a table on a page'))),
    ...(await feature('Pictures, files and video', 'Drop in pictures and resize, align and caption them, lay several out as a gallery, attach any file, or play a short video as a silent looping animation that shows a process without anyone pressing play.', 'Image', pic('feat-gallery', 'Pictures laid out as a gallery'))),
    ...(await feature('Embeds and smart links', 'Paste a link to a video or a design and it plays or shows right on the page; any other link can show as a card with the site’s title, so readers see where it goes.', 'Embed', pic('feat-embed', 'A video playing on a page'))),
    ...(await feature('Layouts', 'Put content side by side in two or three columns, and let a page use the full width of the screen when a wide table needs it.', 'Layout', pic('feat-layout', 'Content in columns'))),
    ...(await feature('Live content', 'Blocks that fill themselves in and stay current: the pages under this one, recently updated pages, pages with a label, open tasks across a space, attachments, contributors, a page’s history, and a section reused from another page so it is written once and kept in step everywhere.', 'Live content', pic('feat-live', 'A live block that keeps itself up to date'))),
    ...(await feature('Drafts, then publish', 'A new page is a private draft until you publish it, so half-finished work never shows up in anyone’s search or tree.', 'Drafts, Publish and Update', pic('feat-draft', 'The Publish button of a new page'))),
    ...(await feature('Templates', 'Save a page as a starting point, such as meeting notes or an incident report, and start new pages from it so pages that should look alike do.', 'Templates', pic('feat-template', 'Choosing a template for a new page'))),

    h(2, 'Keeping it organized'),
    ...(await feature('Spaces', 'A space holds the pages for one team, project or audience, with its own home page, page tree, icon and permissions.', 'Creating a space', pic('feat-spaces', 'The list of spaces'))),
    ...(await feature('A page tree you arrange yourself', 'Pages nest under other pages. Drag them into order, move them to another space with everything under them, or copy them. The tree can number its pages automatically (1, 1.1, 1.2) or mark them with bullets, and renumbers itself as pages move.', 'The page tree and reordering', pic('feat-tree', 'A numbered page tree'))),
    ...(await feature('Filter the tree as you type', 'Type a few letters at the top of the tree to see just the pages that match, with their parents and everything under them.', 'Finding your way around', anim('feat-filter'))),
    ...(await feature('Labels', ['Tag pages with labels, such as ', i('meeting-notes'), ', and see every page with a label in one list, across spaces.'], 'Labels', pic('feat-labels', 'Every label, with how many pages carry it'))),
    ...(await feature('Search', 'Search every page you can see, with the matching words shown in context and the space each result is in.', 'Search', pic('feat-search', 'Search results with the matching words'))),
    ...(await feature('History and the trash', 'Every change is kept. See who changed what, compare any two versions, and put an old version back. Deleted pages go to the trash and can be restored.', 'History and restoring', pic('feat-history', 'A page’s history'))),

    h(2, 'Working together'),
    ...(await feature('Editing at the same time', 'Several people can edit the same page at once and see each other’s changes as they are typed, without overwriting anyone.', 'Editor tour')),
    ...(await feature('Comments', 'Comment on a page, or on the exact words you mean, reply in threads, mention people, and resolve a thread when it is settled.', 'Comments', pic('feat-comments', 'A comment thread with a reply'))),
    ...(await feature('Notifications you control', 'Watch a page or a whole space to hear about changes and comments, in the app’s bell and, if you choose, by email: straight away or as a daily digest.', 'Mentions and notifications', pic('feat-bell', 'The notifications list'))),
    ...(await feature('Who can see what', 'A space can be open to everyone signed in, or limited to particular people and groups; a single page can be restricted further.', 'Who can see a space', pic('feat-permissions', 'A space’s permissions'))),

    h(2, 'Sharing and publishing'),
    ...(await feature('Export a page', 'Download a page as a PDF, a Markdown file or a single HTML file that looks like the page and works on its own.', 'Exporting and publishing', pic('feat-export', 'The export choices in a page’s menu'))),
    ...(await feature('Publish a space as a website', 'Export a whole space as a static website, with its sidebar, a filter that narrows the pages as you type, and your branding, ready to host anywhere. These docs are one.', 'Exporting and publishing', pic('feat-site', 'Exporting a space as a website'))),
    ...(await feature('Wiki packs', 'Package a whole space, with its history, comments and files, as one file you can keep as a backup or import into another Tesria.', 'Wiki packs', pic('feat-pack', 'Exporting a space as a wiki pack'))),
    ...(await feature('Public reading', 'Let people without an account read chosen spaces, such as public documentation, while everything else stays private. It is off until an administrator turns it on and a space is chosen.', 'Public reading', pic('feat-public', 'The switches that allow public reading'))),

    h(2, 'Accounts and security'),
    ...(await feature('Accounts your way', 'Open sign-up, invite links only, or sign-in with your organization’s single sign-on (OpenID Connect, in beta).', 'Accounts and invites', pic('feat-invites', 'Making an invite link'))),
    ...(await feature('Two-factor sign-in and recovery', 'Add a code from an authenticator app to sign-in, keep recovery codes for when you lose it, and reset a forgotten password by email.', 'Two-factor and recovery codes', pic('feat-two-factor', 'Turning on two-factor sign-in'))),
    ...(await feature('Roles and groups', 'Owner, administrator and user roles, with each right a role holds shown and changeable in one table, plus your own roles and groups of people to share with at once.', 'Roles', pic('feat-groups', 'Groups, including the three built in'))),
    ...(await feature('Protection built in', 'Passwords are stored with a modern slow hash, repeated wrong guesses are slowed and locked out, suspicious activity alerts administrators, and every administrative change is written to an audit log that shows if anyone tampers with it.', 'Security hardening', pic('feat-protection', 'Limits on repeated sign-in attempts'))),
    ...(await feature('Sessions', 'See every device signed in to your account and sign out any of them.', 'Sessions')),

    h(2, 'Running it'),
    ...(await feature('Installs with one command', 'Tesria runs in Docker. A guided setup on first start creates the owner’s account and walks through the important settings.', 'Installing with Docker Compose')),
    ...(await feature('Secure connections without the fuss', 'HTTPS is automatic: a free certificate for a real web address, or one of its own on a home or office network, with a built-in guide that sets each device up to trust it.', 'Trusting the local certificate', pic('feat-trust', 'The Trust this device guide'))),
    ...(await feature('Backups you can count on', 'Continuous backups of the database, daily copies of everything, encrypted copies to a cloud bucket, a network drive or a removable drive, and a restore from the admin pages that can itself be undone.', 'Backups and recovery')),
    ...(await feature('An admin area', 'A dashboard of activity, and screens for people, spaces, invites, security, backups, roles, groups, settings and the audit log.', 'Administration', pic('feat-dashboard', 'Sign-in activity on the administration dashboard'))),
    ...(await feature('Your name and colors', 'Replace the Tesria name and logo with your own, set the accent color, and choose light or dark for everyone.', 'Administration')),

    h(2, 'For developers and assistants'),
    ...(await feature('A REST API', 'Everything the app does is available to scripts, with personal API tokens, scopes that limit what each token may do, and an interactive reference.', 'REST API', pic('feat-api', 'Making an API token'))),
    ...(await feature('Webhooks', 'Tell another system when pages are created, updated or commented on.', 'REST API', pic('feat-webhooks', 'A space’s webhooks'))),
    ...(await feature('AI assistants, safely', 'Connect an AI assistant through MCP to search, read and write pages. What it writes arrives as tracked changes that a person accepts or rejects, like a colleague’s suggestions.', 'MCP')),

    h(2, 'On phones and tablets'),
    ...(await feature('Everything, on the go', 'Reading, editing, tables and comments all work on a phone or tablet, with menus and toolbars made for touch.', 'Tesria on phones and tablets', { kind: 'phone', name: 'feat-phone', alt: 'A page on a phone' })),

    panel('info', p(b('Your wiki, on your server.'), ' Tesria is open source under the Apache License 2.0: free to use, change and run for anything, including commercially. Your pages, files and backups stay on machines you control.')),
  ))
}

/** Features goes second at the top of the tree, after Welcome to Tesria. */
export async function cleanup({ author }) {
  const space = await author.call('GET', '/api/spaces/DOCS')
  const tree = await author.call('GET', `/api/pages/tree?spaceId=${space.id}`)
  const at = tree.findIndex((n) => n.title === 'Features')
  const welcome = tree.findIndex((n) => n.title === 'Welcome to Tesria')
  if (at >= 0 && welcome >= 0 && at !== welcome + 1) {
    await author.call('PUT', `/api/pages/${tree[at].id}/move`, { parentPageId: null, index: at > welcome ? welcome + 1 : welcome })
    console.log('  moved Features after Welcome to Tesria')
  }
}
