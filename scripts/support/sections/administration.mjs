// Administration: every tab under /admin (dev-plan 10.5), rewritten to the
// owner's rules of 2026-09-23 (WRITING.md, the pilot pages): for someone
// who has just become the administrator of a team wiki, what each screen is
// for, when they would use it and how, with narrow pictures of the controls.
//
// Facts from src/web/src/routes/admin, GroupsPage.tsx, AuditPage.tsx,
// Layout.tsx, ReauthDialog.tsx, the admin endpoints (src/Api/Features/Admin),
// the groups and audit endpoints, and the rights catalog
// (InstancePermissions.cs), gathered 2026-09-24.
//
// Privacy: these screens list the real accounts on this instance. The
// pictures are taken as Alex Rivera, an administrator, and every table in
// them is filtered to Tesria Demo's fictional people or its one space;
// anything else personal (invite addresses, blocked addresses, the real
// instance name and address) is hidden or typed over. The Audit tab and the
// storage targets cannot be made safe and are described in words, as are the
// owner-only screens (Branding, Restore, Transfer ownership), which Alex
// cannot open.
//
// No page is added or retired: every title of the first version is kept.

// Wide enough that admin tables keep their columns (below 640 pixels they
// scroll sideways), narrow enough that their text reads at the page's size.
const MEDIUM = { width: 760, height: 900 }
// Close-ups of a form or a card, as in the pilot.
const NARROW = { width: 480, height: 900 }

const PEOPLE = ['Alex Rivera', 'Sam Okafor', 'Priya Natarajan', 'Mei Chen', 'Jordan Brooks']

// Only Tesria Demo's fictional people: this instance's owner, and any other
// account made while building it, may be a real person's name.
// The Roles tab names whoever last reviewed the roles, which on this
// instance is a real person rather than one of the example accounts.
const NO_REVIEWER = "document.querySelectorAll('p').forEach((el) => { if (el.textContent.includes('Last reviewed')) el.textContent = el.textContent.replace(/\\s*Last reviewed[^.]*\\./, '') })"
const ONLY_EXAMPLE_ACCOUNTS = "document.querySelectorAll('table.admin-table tbody tr').forEach((r) => { if (!['Alex Rivera', 'Sam Okafor', 'Priya Natarajan', 'Mei Chen', 'Jordan Brooks'].some((n) => r.textContent.includes(n))) r.remove() })"
// The Spaces tab, down to Tesria Demo's row; its creator is blanked unless
// it is one of the example accounts.
const ONLY_DEMO_SPACE = "document.querySelectorAll('table.admin-table tbody tr').forEach((r) => { const key = r.querySelector('td:first-child .badge'); if (!key || key.textContent.trim() !== 'DEMO') { r.remove(); return } const who = r.querySelector('td:nth-child(2)'); if (who && !" + JSON.stringify(PEOPLE) + ".some((n) => who.textContent.includes(n))) who.textContent = '' })"
// The Groups tab, down to the three built-in groups: any other group was
// made on this instance and is not part of the example.
const ONLY_BUILT_IN_GROUPS = "document.querySelectorAll('ul.version-list > li').forEach((li) => { if (!li.querySelector('.badge')) li.remove() })"
// Names each settings card by its heading (data-shot="kill-switches"), so a
// crop and an annotation can find it with plain CSS.
const TAG_SECTIONS = "document.querySelectorAll('section.profile__section').forEach((s) => { const h = s.querySelector(':scope > h2'); if (h) s.setAttribute('data-shot', h.textContent.trim().toLowerCase().replace(/[^a-z0-9]+/g, '-')) })"
const BLUR = 'document.activeElement && document.activeElement.blur()'

// Example security activity for the Security pictures: documentation
// addresses (RFC 5737) and Tesria Demo's people only.
const at = (minutesAgo) => new Date(Date.now() - minutesAgo * 60_000).toISOString()
const SAM = '5a3c9f10-4d2e-4b8a-9c1d-000000000002'
const EXAMPLE_ALERTS = [
  { id: 'a1', eventId: 'e1', kind: 'login.failed_burst_ip', severity: 1, key: '203.0.113.7', ip: '203.0.113.7',
    actorId: null, actorName: null, status: 0, createdAt: at(12), acknowledgedAt: null, acknowledgedByName: null,
    resolvedAt: null, resolvedByName: null, note: null, metadataJson: JSON.stringify({ Failures: 25, Minutes: 5 }) },
  { id: 'a2', eventId: 'e2', kind: 'login.admin_new_address', severity: 1, key: SAM, ip: '198.51.100.23',
    actorId: SAM, actorName: 'Sam Okafor', status: 1, createdAt: at(95), acknowledgedAt: at(80), acknowledgedByName: 'Alex Rivera',
    resolvedAt: null, resolvedByName: null, note: null, metadataJson: null },
  { id: 'a3', eventId: 'e3', kind: 'mail.signin_failed', severity: 1, key: 'Google', ip: null,
    actorId: null, actorName: null, status: 0, createdAt: at(240), acknowledgedAt: null, acknowledgedByName: null,
    resolvedAt: null, resolvedByName: null, note: null, metadataJson: JSON.stringify({ Provider: 'Google' }) },
]
const SECURITY_EXAMPLE = [
  { url: '**/api/admin/security/overview', json: {
    openAlerts: 3, criticalOpen: 0, eventsLast24h: 41, blockedNetworks: 1, blockedHits: 37,
    allowPublicSpaces: false, allowPublicRegistration: false, requireTotpForAdmins: true } },
  { url: '**/api/admin/security/alerts?*', json: EXAMPLE_ALERTS },
  { url: '**/api/admin/security/events?*', json: [] },
  { url: '**/api/admin/security/blocks', json: [] },
  { url: '**/api/notifications/unread-count', json: { count: 3 } },
  { url: '**/api/notifications', json: EXAMPLE_ALERTS.map((a, n) => ({
    id: `n${n}`, action: 'security.alert', targetType: 'security', targetId: a.id, actorId: null, actorName: null,
    metadataJson: JSON.stringify({ Kind: a.kind, Severity: 'Warning', Key: a.key }), createdAt: a.createdAt, readAt: null,
  })) },
]

export const shots = () => [
  // ---- Administration: where the Admin link is. A desktop window, where
  // the link sits in the bar itself rather than under More.
  {
    name: 'admin-link', url: '/spaces', viewport: { width: 1280, height: 720 }, phone: false, settle: 800,
    steps: [{ wait: 2500 }],
    clipTo: ['.topbar .brand', '.topbar__nav'], clipPad: 8,
    annotate: [{ type: 'box', target: '.topbar__nav a[href="/admin"]', pad: 4 }],
  },

  // ---- Dashboard: the period buttons and the People figures. The tables
  // below them name real people and pages, so they are left out.
  {
    name: 'dashboard', url: '/admin', viewport: MEDIUM, phone: false, settle: 1500,
    steps: [{ wait: 3000 }, { css: '.tabs { visibility: hidden !important; }' }],
    clipTo: ['.dash__filters', '.dash__grid'], clipPad: 12,
    annotate: [{ type: 'box', target: '.dash__filters', pad: 4 }],
  },

  // ---- Users: the fictional accounts' rows, with one row's actions marked.
  {
    name: 'users', url: '/admin/users', viewport: MEDIUM, phone: false, settle: 1200,
    steps: [{ wait: 2500 }, { eval: ONLY_EXAMPLE_ACCOUNTS }],
    clipTo: 'table.admin-table', clipPad: 8,
    annotate: [{ type: 'box', target: 'table.admin-table tbody tr:last-child .admin-table__actions', pad: 4 }],
  },

  // ---- Spaces: Tesria Demo's row, with Publish and Get access marked.
  {
    name: 'admin-spaces', url: '/admin/spaces', viewport: MEDIUM, phone: false, settle: 1200,
    steps: [{ wait: 2500 }, { eval: ONLY_DEMO_SPACE }],
    clipTo: 'table.admin-table', clipPad: 8,
    annotate: [
      { type: 'box', target: 'table.admin-table tbody tr td:nth-child(5) .link-btn', pad: 4 },
      { type: 'box', target: 'table.admin-table tbody tr td:nth-child(7) .link-btn', pad: 4 },
    ],
  },

  // ---- Invites: the form, filled in. The list below it holds real
  // people's addresses and is hidden.
  {
    name: 'invites', url: '/admin/invites', viewport: NARROW, phone: false, settle: 1200,
    steps: [
      { wait: 2500 },
      { type: 'new.colleague@example.com', selector: 'form.form-inline input[type="email"]' },
      { eval: BLUR },
      { css: '.tabs, .tab-panel > p.muted, table.admin-table { visibility: hidden !important; }' },
    ],
    clipTo: 'form.form-inline', clipPad: 12,
    annotate: [
      { type: 'box', target: 'form.form-inline label:nth-of-type(1)', pad: 5 },
      { type: 'box', target: 'form.form-inline label:nth-of-type(2)', pad: 5 },
      { type: 'box', target: 'form.form-inline .invite-email', pad: 5 },
      { type: 'box', target: 'form.form-inline button[type="submit"]', pad: 5 },
    ],
  },

  // ---- Security: the dashboard and the bell, with example alerts. The real
  // ones name real accounts and addresses, so the page's own API answers are
  // replaced by these (the harness's `mock`); the screens are the real ones.
  {
    name: 'security-dashboard', url: '/admin/security', viewport: MEDIUM, phone: false, settle: 1500,
    mock: SECURITY_EXAMPLE,
    steps: [{ wait: 3000 }, { css: '.tabs { visibility: hidden !important; }' }],
    clipTo: ['.dash__grid', `section.profile__section:has(> h2:text-is("Alerts"))`], clipPad: 10,
    annotate: [
      { type: 'box', target: '.alerts__item:first-child .alerts__actions', pad: 4 },
    ],
  },
  {
    name: 'security-bell', url: '/spaces', viewport: MEDIUM, phone: false, settle: 1000,
    mock: SECURITY_EXAMPLE,
    steps: [{ wait: 2500 }, { click: '.notif__bell' }, { wait: 800 }],
    clipTo: ['.notif__bell', '.notif__dropdown'], clipPad: 4,
    annotate: [{ type: 'box', target: '.notif__bell', pad: 4 }],
  },

  // ---- Security: the three kill switches, and the block form filled in
  // with a documentation address. Alerts, events and the blocklist name
  // real people and addresses, so they are not pictured.
  {
    name: 'security-switches', url: '/admin/security', viewport: NARROW, phone: false, settle: 1200,
    steps: [{ wait: 2500 }, { eval: TAG_SECTIONS }],
    clipTo: '[data-shot="kill-switches"]', clipPad: 6,
    annotate: [{ type: 'box', target: '[data-shot="kill-switches"] label.admin__toggle:nth-of-type(3)', pad: 4 }],
  },
  {
    name: 'security-block', url: '/admin/security', viewport: NARROW, phone: false, settle: 1200,
    steps: [
      { wait: 2500 }, { eval: TAG_SECTIONS },
      { css: '[data-shot="blocked-networks"] table, [data-shot="blocked-networks"] > p { visibility: hidden !important; }' },
      { type: '203.0.113.7', selector: 'form.block-form input[name="cidr"]' },
      { type: 'Repeated failed sign-ins', selector: 'form.block-form input[name="reason"]' },
      { type: '24', selector: 'form.block-form input[name="expiresInHours"]' },
      { eval: BLUR },
    ],
    clipTo: ['[data-shot="blocked-networks"] > h2', 'form.block-form'], clipPad: 10,
    annotate: [{ type: 'box', target: 'form.block-form button[type="submit"]', pad: 4 }],
  },

  // ---- Backups: Back up now and the first service's card. The storage
  // targets name where this instance's copies are kept, so they are left out.
  {
    name: 'backups', url: '/admin/backups', viewport: NARROW, phone: false, settle: 1500,
    steps: [{ wait: 3000 }],
    clipTo: ['.backup-actions', '.backup-actions + .backup-cards > .backup-card:first-child'], clipPad: 10,
    annotate: [
      { type: 'box', target: '.backup-actions .btn--primary', pad: 4 },
      { type: 'box', target: '.backup-actions + .backup-cards > .backup-card:first-child > button', pad: 4 },
    ],
  },

  // ---- Roles: the top of the grid, down to the first administration
  // rights, where the User column shows a dash; and the review box after
  // one box is cleared. Nothing is saved: the draft lives in the page.
  {
    name: 'roles', url: '/admin/roles', viewport: MEDIUM, phone: false, settle: 1500,
    steps: [{ wait: 3000 }, { eval: NO_REVIEWER }],
    clipTo: ['table.roles-table thead', 'table.roles-table tbody tr:nth-child(10)'], clipPad: 8,
    annotate: [{ type: 'box', target: 'table.roles-table tbody tr:nth-child(9) td span.muted', pad: 5 }],
  },
  {
    name: 'roles-review', url: '/admin/roles', viewport: MEDIUM, phone: false, settle: 1000,
    steps: [
      { wait: 3000 }, { eval: NO_REVIEWER },
      { click: 'input[aria-label="Export pages for User"]' }, { wait: 300 },
      { click: 'button:has-text("Review changes")' }, { wait: 600 },
      { scrollTo: '.backup-preview' },
    ],
    clipTo: '.backup-preview', clipPad: 10,
    annotate: [{ type: 'box', target: '.backup-preview__actions .btn--primary', pad: 4 }],
  },

  // ---- Groups: the new group form filled in, and the built-in groups.
  // Filtered after typing, so the page has re-rendered before rows go.
  {
    name: 'groups', url: '/admin/groups', viewport: NARROW, phone: false, settle: 1200,
    steps: [
      { wait: 2500 },
      { type: 'Marketing', selector: 'form.card.form-inline label:nth-of-type(1) input' },
      { type: 'The marketing team', selector: 'form.card.form-inline label:nth-of-type(2) input' },
      { eval: BLUR },
      { eval: ONLY_BUILT_IN_GROUPS },
      { css: '.tabs, .tab-panel > div > p.muted { visibility: hidden !important; }' },
    ],
    clipTo: ['form.card.form-inline', 'ul.version-list'], clipPad: 12,
    annotate: [
      { type: 'box', target: 'form.card.form-inline button[type="submit"]', pad: 4 },
      { type: 'box', target: 'ul.version-list .badge', pad: 4 },
    ],
  },

  // ---- Settings: the Instance card, with an example name and address typed
  // over the real ones and the hint (which repeats the real address) hidden.
  {
    name: 'settings-instance', url: '/admin/settings', viewport: NARROW, phone: false, settle: 1200,
    steps: [
      { wait: 2500 }, { eval: TAG_SECTIONS },
      { type: 'Team wiki', selector: '[data-shot="instance"] label:nth-of-type(1) input' },
      { type: 'https://wiki.example.com', selector: '[data-shot="instance"] label:nth-of-type(2) input' },
      { eval: BLUR },
      { css: '[data-shot="instance"] label .muted { display: none !important; }' },
    ],
    clipTo: '[data-shot="instance"]', clipPad: 6,
    annotate: [{ type: 'box', target: '[data-shot="instance"] label:nth-of-type(2)', pad: 4 }],
  },
]

export async function build({ top, page, ensure, doc, p, h, text, bold, italic, code, ul, ol, li, panel, table, picture, pageLink }) {
  const b = (t) => text(t, bold)
  const c = (t) => text(t, code)
  const i = (t) => text(t, italic)
  /** A numbered step: a heading that says what to do, then how. */
  const step = (n, title) => h(3, `Step ${n}: ${title}`)
  const root = top['Administration']

  // Every page first, so the overview can link to each by title.
  await ensure('Owner, administrators and users', root)
  const dash = await ensure('Dashboard', root)
  const users = await ensure('Users', root)
  const spaces = await ensure('Spaces (administration)', root)
  const invites = await ensure('Invites', root)
  const security = await ensure('Security (administration)', root)
  const backups = await ensure('Backups (administration)', root)
  const roles = await ensure('Roles', root)
  const groups = await ensure('Groups', root)
  await ensure('Audit', root)
  const settings = await ensure('Settings (administration)', root)
  await ensure('Branding', root)

  // ============================================================ Overview
  await page('Administration', null, doc(
    p('A wiki needs looking after as well as writing. Someone has to decide who gets an account and what they may do, check that the backups are working, help the colleague who is locked out, and notice when something looks wrong. In Tesria all of that happens in one place, the ', b('Administration'), ' area. If you have just become the administrator of your team’s wiki, this section walks you through it screen by screen: what each one is for, when you would use it, and how.'),
    p('Administration is for administrators and the owner of the instance. Anyone else who opens it is told to ask an administrator. Before you start, ', pageLink('Owner, administrators and users'), ' explains who may do what; the rest makes more sense once you know.'),

    h(2, 'Opening it'),
    p('Choose ', b('Admin'), ' in the top bar. In a narrower window it moves under ', b('More'), ', and on a phone it is in the ', b('☰'), ' menu.'),
    ...(await picture(root, 'admin-link', 'The top bar with the Admin link', 'Admin is in the top bar, beside Spaces.')),
    p('It opens on the Dashboard, with a row of tabs across the top. You only see the tabs your role allows. An administrator on a new instance sees every one except ', b('Branding'), ', which belongs to the owner. At the foot of every tab is Tesria’s version number, the first thing to quote when you ask for help.'),

    h(2, 'What each tab is for'),
    ul(
      li(p(pageLink('Dashboard'), ': the instance at a glance. How many people use it, how much has been written, and whether the backups are running.')),
      li(p(pageLink('Users'), ': every account. Help someone who forgot their password, lost their phone or is leaving the team.')),
      li(p(pageLink('Spaces (administration)', 'Spaces'), ': every space, including private ones. Publish a space to the internet, or get into one nobody can manage any more.')),
      li(p(pageLink('Invites'), ': links that let one person create an account, for when anyone-can-join is off.')),
      li(p(pageLink('Security (administration)', 'Security'), ': what Tesria has noticed, such as repeated wrong passwords, and the switches and limits that protect the instance.')),
      li(p(pageLink('Backups (administration)', 'Backups'), ': whether the backups work, and backing up or testing a restore now.')),
      li(p(pageLink('Roles'), ': what each role may do, as a grid you can change.')),
      li(p(pageLink('Groups'), ': named sets of people, so a space can be shared with a whole team at once.')),
      li(p(pageLink('Audit'), ': the record of who changed what, and when.')),
      li(p(pageLink('Settings (administration)', 'Settings'), ': the instance’s name and address, which sites may be embedded, who can join, and the email server.')),
      li(p(pageLink('Branding'), ': your organization’s name, logo and colors. The owner’s, unless they share it.')),
    ),

    h(2, 'Your first week as administrator'),
    p('Nothing here is urgent on day one: Tesria works out of the box. But these are worth doing early, roughly in this order.'),
    ol(
      li(p(b('Check the backups.'), ' Open ', b('Backups'), ' and make sure both cards say ', b('Healthy'), '. Choose ', b('Test restore of newest'), ' once, so you know a backup really comes back. Then plan an offsite copy, because every backup starts on the same machine as the wiki.')),
      li(p(b('Turn on two-factor for yourself.'), ' An administrator’s account is worth more to an attacker than anyone else’s. See ', pageLink('Two-factor and recovery codes'), '.')),
      li(p(b('Decide who can join.'), ' A new instance lets anyone who can reach it create an account. For a team wiki on the internet, turn that off in ', b('Settings → Access'), ' and add people with ', b('Invites'), ' instead.')),
      li(p(b('Set the public address and email.'), ' In ', b('Settings'), ', put the address people use to open Tesria, set up the mail server, and send yourself a test email, so alerts and password reset links can reach people.')),
      li(p(b('Look at the roles.'), ' Open ', b('Roles'), ' and either keep the defaults or change them. Most teams keep them.')),
      li(p(b('Make groups for your teams,'), ' so spaces can be shared with a team rather than person by person.')),
    ),

    h(2, 'When Tesria asks for your password again'),
    p('Some actions change who can run the instance, or cannot be undone. Before those, Tesria shows a box called ', i('Confirm it’s you'), ' and asks for your password, or a code from your authenticator app if you use two-factor. It only asks when you have not entered your password in the last few minutes, so you will not see it twice in a row.'),
    p('It guards, among other things: changing someone’s role, turning off someone’s two-factor, publishing a space, switching public spaces on or off, changing what a role may do, changing the backup retention policy, unblocking an address and changing the branding. The point is simple: if you leave your computer unlocked, nobody passing by can make you an accomplice.'),
    panel('info', p(b('Nothing drastic happens on one click.'), ' Actions that take something away from a person, such as suspending an account, signing someone out, revoking an invite or deleting a group, first open a box that says exactly what will happen. Nothing is done until you choose the button inside it. Changes made in Administration are recorded in the ', pageLink('Audit', 'audit log'), '.')),
  ))

  // ================================================== Owner, administrators and users
  await page('Owner, administrators and users', root, doc(
    p('Every account on Tesria belongs to one of three ', b('tiers'), ': owner, administrator or user. The tier decides whose account can act on whose, and it is the first thing to understand before changing anything in Administration.'),

    h(2, 'The three tiers'),
    ul(
      li(p(b('Owner.'), ' Exactly one account: whoever set Tesria up, until they hand it to someone else. The owner can do everything, always. Nobody else can suspend the owner, sign them out, reset their password or turn off their two-factor, so the instance always has a way back in.')),
      li(p(b('Administrators.'), ' The people who look after the instance day to day. What they may do is set by their role (see ', pageLink('Roles'), '). Administrators cannot demote one another; only the owner can.')),
      li(p(b('Users.'), ' Everyone else: they read and write pages, create spaces, export and use API tokens, as their role allows. They never see the Administration area.')),
    ),
    p('A ', b('role'), ' is a set of rights inside a tier. Each tier has a built-in role, called Owner, Administrator and User, and you can add your own: say, a Contractor role in the user tier that cannot export. Moving someone to another role in the same tier never changes their tier.'),
    panel('info', p(b('Administrators cannot read every space.'), ' Rights say what someone may do on the instance; each space still decides who may read it. A private space stays private to administrators too. When one has to be opened up, ', pageLink('Spaces (administration)'), ' explains how, and that it is recorded.')),

    h(2, 'What only the owner can do'),
    ul(
      li(p('Promote users to administrator, and demote administrators. The owner can give promoting (never demoting) to an administrator role.')),
      li(p('Transfer ownership to someone else.')),
      li(p('Change what administrator roles may do, and create new administrator roles.')),
      li(p('Change the branding, and restore a backup. These two are the owner’s by default, and the owner can give them to an administrator role.')),
    ),

    h(2, 'Handing the instance to someone else'),
    p('When the owner is leaving, or set Tesria up on someone else’s behalf, ownership can move to another account. Only the owner can do this, so this part is described in words rather than pictured.'),
    step(1, 'Find the new owner'),
    p('In ', b('Admin → Users'), ', find the person. Their account has to be active: a suspended account cannot own the instance.'),
    step(2, 'Choose Transfer ownership'),
    p('It is in their row, and only the owner sees it. A box explains that you become an administrator, and that only the new owner can change roles or hand ownership back.'),
    step(3, 'Confirm'),
    p('Choose ', b('Transfer ownership'), ' in the box, and enter your password if asked.'),
    p('The other account becomes the owner at once, and yours becomes an administrator. Nobody is signed out. Every administrator is alerted, including you: if it was not your doing, that is the moment to act.'),
  ))

  // =========================================================== Dashboard
  await page('Dashboard', root, doc(
    p('The dashboard answers two questions on one screen: is everything all right, and is anyone using this? It shows how many people have accounts and how many are active, how much has been written and read, and whether the backups are running. It is the first thing you see when you open Administration.'),
    p('A glance once a week is plenty. Look more closely when something changes: a sudden climb in failed sign-ins, or a backup tile in red, is your cue to open ', pageLink('Security (administration)', 'Security'), ' or ', pageLink('Backups (administration)', 'Backups'), '.'),

    h(2, 'Choosing the period'),
    p('The buttons at the top choose the last ', b('7'), ', ', b('30'), ' or ', b('90 days'), '. The small charts, new accounts and page views follow the choice; totals such as the number of pages do not. Move the pointer along a chart to see one day’s number.'),
    ...(await picture(dash, 'dashboard', 'The period buttons and the People figures', 'The period buttons, and the People figures below them.')),

    h(2, 'What each part shows'),
    ul(
      li(p(b('People:'), ' how many accounts there are and how many are administrators, how many were active in the last 7 and 30 days, new accounts in the period (and how many are suspended, if any), and charts of sign-ins and of failed sign-ins, the second in red.')),
      li(p(b('Content:'), ' spaces, pages and their saved versions, comments, attachments and the space they take, and a chart of pages created.')),
      li(p(b('Usage:'), ' page views in the period, and views per day.')),
      li(p(b('Health:'), ' one tile per backup service, ', b('Last database dump'), ' and ', b('Last physical backup'), ', saying how long ago the last backup was and whether it is OK. A problem shows as ', b('Last run failed'), ', ', b('Overdue'), ', ', b('Not reporting'), ' or ', b('Agent offline'), '. Click a tile to open Backups.')),
      li(p(b('Most viewed'), ' and ', b('Most active editors:'), ' the ten pages read most in the period, and the ten people who saved the most versions.')),
    ),
    panel('note', p(b('Private pages stay private here too.'), ' A page you are not allowed to open is still counted in Most viewed, but its title is not shown and it is not a link.')),
    panel('success', p(b('Watch the failed sign-ins chart.'), ' A spike usually means someone is guessing passwords. Tesria raises an alert on the Security tab when it sees a burst from one address, and that is where you can block it.')),
  ))

  // =============================================================== Users
  await page('Users', root, doc(
    p('The Users tab lists every account on the instance, the owner first, then administrators, then everyone else. It is where you help people: the colleague who forgot their password, the one who lost the phone with their authenticator app, the one who is locked out, and the one who is leaving.'),
    ...(await picture(users, 'users', 'The Users tab, with one account’s actions marked', 'Each row ends with what you can do to that account.')),

    h(2, 'Reading the list'),
    ul(
      li(p(b('User:'), ' the person’s name and email address.')),
      li(p(b('Role:'), ' ', b('owner'), ' or ', b('admin'), ' for those tiers, otherwise ', b('User'), '. ', b('sso'), ' marks someone who signs in through single sign-on. Once you have made roles of your own, a menu here moves someone between the roles of their tier.')),
      li(p(b('Status:'), ' ', b('Active'), ' or ', b('suspended'), ', and ', b('locked'), ' while the account is locked after wrong passwords.')),
      li(p(b('Codes:'), ' how many recovery codes the person has left. ', b('none'), ' and ', b('not saved'), ' mark people who could not get back in on their own if they lost their password or phone: worth a friendly word.')),
      li(p(b('Last seen:'), ' the day they last used Tesria.')),
      li(p(b('Actions:'), ' what you can do to the account. Only the actions that apply are shown, and the owner’s row has none.')),
    ),

    h(2, 'Someone forgot their password'),
    p('People can usually help themselves with ', b('Forgot your password?'), ' on the sign-in page, by email or with a recovery code (see ', pageLink('Resetting a password'), '). When neither works:'),
    step(1, 'Choose Reset password'),
    p('In their row. A box appears above the list with a one-time link. It works once, for one hour, and is shown only this once.'),
    step(2, 'Copy the link and hand it over'),
    p('Choose ', b('Copy'), ' and give it to them yourself, in person or through a channel you trust. They open it and choose a new password.'),
    panel('note', p(b('The link starts with the address you are using.'), ' If you opened Tesria as ', c('localhost'), ' on the server, the link will too, and it will not work on anyone else’s computer. Open Tesria by the address everyone uses first.')),
    p('Accounts that sign in through single sign-on have no password in Tesria, so they have no Reset password. A new password does not turn off two-factor; that is the next job.'),

    h(2, 'Someone lost the phone with their authenticator app'),
    p('If they still have a recovery code, they type it at the two-factor step instead of a code from the app. If they have lost those too:'),
    step(1, 'Make sure it is really them'),
    p('Talk to them in person or on a video call. Someone claiming to have lost their phone is exactly how an attacker would try to get in.'),
    step(2, 'Choose Turn off two-factor'),
    p('In their row. It only appears when the account has two-factor on.'),
    step(3, 'Confirm'),
    p('Read the box and choose ', b('Turn off two-factor'), ', then enter your password if asked. They are signed out everywhere, and can sign in with their password alone. Ask them to set two-factor up again straight away and save their new recovery codes. Every administrator is alerted.'),
    p('Nobody can turn off the owner’s two-factor from here; the owner does it from their own profile. Another administrator’s can be turned off only by the owner, and your own from your profile.'),

    h(2, 'Someone is locked out after too many wrong passwords'),
    p('After 5 wrong passwords in a row (the default) an account is locked for a minute, then for twice as long after each further failure, up to 15 minutes. It shows as ', b('locked'), ' and unlocks by itself. To let the person in now, choose ', b('Unlock'), ' in their row. The same list is on the Security tab, under ', b('Active lockouts'), '.'),

    h(2, 'Someone is leaving, or an account may be misused'),
    step(1, 'Choose Suspend'),
    p('In their row, then ', b('Suspend'), ' in the box. They are signed out everywhere, cannot sign in, and their API tokens stop working. Nothing they wrote is removed, and their name stays on their pages.'),
    step(2, 'Revoke their tokens, if they had any'),
    p('Suspending already stops the tokens. If the account might be reactivated later, choose ', b('Revoke tokens'), ' too, so old tokens do not come back with it. Revoked tokens are deleted for good; any script using them stops working.'),
    p(b('Reactivate'), ' brings a suspended account back. Accounts are never deleted in Tesria. You cannot suspend yourself, the owner, or the only active administrator.'),

    h(2, 'Signing someone out everywhere'),
    p(b('Sign out'), ' ends every session of that account: each browser signed in to it is signed out on its next click. Use it when someone left themselves signed in on a shared or lost computer. They can sign straight back in.'),

    h(2, 'Making someone an administrator'),
    p(b('Make admin'), ' moves a user into the Administrator role. By default only the owner can do it: an administrator sees it grayed out unless the owner has given their role ', b('Promote users to administrator'), '. It may ask for your password, and every administrator is alerted. ', b('Demote'), ' moves an administrator back to User, and only the owner can do that.'),

    h(2, 'Moving someone to another role'),
    p('Once you have made roles of your own (see ', pageLink('Roles'), '), the Role column shows a menu. Choose the new role there; it may ask for your password. Administrators can move users between user roles; only the owner can move administrators between administrator roles.'),
  ))

  // ==================================================== Spaces (administration)
  await page('Spaces (administration)', root, doc(
    p('This tab lists every space on the instance, including private and archived ones: who created it (the ', b('Owner'), ' column), how many pages it has, how much its attachments take, whether it is public, and when it was made. It shows facts about spaces, never what is in them. Being an administrator does not let you read a private space.'),
    p('You will come here for two things: publishing a space so people can read it without an account, and getting into a private space that nobody is left to manage.'),
    ...(await picture(spaces, 'admin-spaces', 'A space’s row, with Publish and Get access marked', 'Publish, and Get access, in a space’s row.')),

    h(2, 'Publishing a space to the internet'),
    p('A published space can be read by anyone who has the address, with no account: a public handbook, say, or help pages for your customers. First, ', b('Allow public spaces'), ' has to be on, in ', pageLink('Settings (administration)', 'Settings'), ' or on the Security tab. Until it is, ', b('Publish'), ' is grayed out.'),
    step(1, 'Choose Publish'),
    p('In the space’s row.'),
    step(2, 'Read what becomes visible, and confirm'),
    p('Tesria says exactly how many pages and attachments anyone will be able to read. Restricted pages stay hidden, and comments stay private unless you allow them. Choose ', b('Publish the space'), ', and enter your password if asked.'),
    p('Once a space is public, a ', b('comments'), ' box appears beside it: tick it to let public readers see its comments. ', b('Withdraw'), ' stops public reading; anonymous readers lose access within a minute. Publishing and withdrawing each alert every administrator. See ', pageLink('Public reading'), ' for what readers see.'),

    h(2, 'Getting into a private space nobody can manage'),
    p('Say a team kept its space private, and the people who managed it have left. Nobody can change its permissions any more, and as an administrator you cannot see inside it either. ', b('Get access'), ' is the way in.'),
    step(1, 'Choose Get access'),
    p('In the space’s row.'),
    step(2, 'Confirm'),
    p('Choose ', b('Give me access'), '. You are added to the space as its administrator, so you can read it and change its permissions. It is recorded in the audit log.'),
    step(3, 'Remove yourself when you are done'),
    p('Once the space has new people to manage it, take yourself out of its ', b('Permissions'), '. The access you were given is an ordinary entry there, like anyone else’s.'),
    p('A space that is open to everyone signed in already lets you in, and Get access leaves it exactly as it is.'),
    panel('info', p(b('Why not just let administrators see everything?'), ' Because then any one administrator could quietly read every team’s private space. This way it is possible when needed, and never unnoticed.')),

    h(2, 'Archiving and deleting'),
    p('These are in each space’s own settings, not here. See ', pageLink('Archiving and deleting a space'), '.'),
  ))

  // ============================================================= Invites
  await page('Invites', root, doc(
    p('An invite is a link that lets one person create an account. It is how you add people when anyone-can-join is off, which is what most teams want for a wiki that can be reached from the internet. The link stops working once it has been used.'),
    p('If your Tesria sends email, it can email the invite for you, with a note in your own words, so there is nothing to copy and paste into a chat. Without email, you copy the link and send it however you like. See ', pageLink('Email (SMTP)'), ' to set email up.'),

    h(2, 'Creating an invite'),
    ...(await picture(invites, 'invites', 'The invite form, filled in, with the email option showing', 'The address, how long the link lasts, the email and its message, and Create and email invite.')),
    step(1, 'Enter their email address, if you know it'),
    p('With an address, only someone registering with that address can use the link, so a forwarded link is no use to anyone else. Leave it empty for a link that works for whoever has it. Tesria refuses an address that already has an account.'),
    step(2, 'Choose how long it lasts'),
    p('From 1 to 90 days; 7 unless you change it.'),
    step(3, 'Write a note, if Tesria is emailing it'),
    p('Once you type an address, and your Tesria sends email, ', b('Email the invite to'), ' appears, already ticked, with a ', b('Message'), ' box. The message starts as a short, friendly note saying who invited them and to what. Change it to anything you like: a welcome, what the wiki is for, where to start reading.'),
    p('You do not need to add the link. Tesria puts it below your message, with the address it works for and the date it expires, so it cannot be left out or mistyped. Leave the message empty to send the usual one. The subject line names you and your Tesria, so the email is easy to recognize.'),
    panel('success', p(b('Why a note helps.'), ' An email from a wiki someone has never heard of looks like spam. A line such as ', i('“This is where we keep the onboarding checklist, start with the Welcome page”'), ' tells them it is real and what to do first.')),
    p('Untick it to make the invite without emailing it, for example to send the link in a chat instead.'),
    step(4, 'Create the invite'),
    p('Choose ', b('Create and email invite'), ' (or ', b('Create invite'), ' without the email). The link appears once, above the list, whichever you chose. If it was emailed, a green note says so. Otherwise choose ', b('Copy'), ' and send it yourself. If you lose it, revoke the invite and make a new one.'),
    panel('note', p(b('If the email could not be sent,'), ' the invite is still made, and a red note gives the mail server’s reason. Copy the link and send it another way, then see ', pageLink('Email (SMTP)'), ' to find out why.')),
    panel('note', p(b('The copied link starts with the address you are using.'), ' Make invites from Tesria opened at the address everyone uses, not at ', c('localhost'), ' on the server, or the link will not work for the person you send it to. An emailed link uses the address set in ', b('Settings'), ' instead.')),

    h(2, 'Keeping track'),
    p('The list below the form shows every invite: who it is for (or ', b('Anyone'), '), whether it is ', b('Unused'), ', ', b('used'), ' and by whom, or ', b('expired'), ', and when it expires. ', b('Revoke'), ' cancels an unused one after asking; whoever you sent it to will need a new one.'),

    h(2, 'Letting others invite'),
    p('The right to ', b('Create invite links'), ' can be given to a user role on the ', pageLink('Roles'), ' tab, for example so team leads can bring in their own people. They get ', b('Invite people'), ' in their top bar, with the same form but without the list: they cannot see or revoke anyone else’s invites.'),
  ))

  // ================================================== Security (administration)
  await page('Security (administration)', root, doc(
    p('Tesria keeps an eye out for signs of trouble: many wrong passwords from one address, a flood of requests, an administrator signing in from somewhere new, lots of pages removed at once. The Security tab shows what it has noticed, lets you act on it, and holds the switches and limits that protect the instance.'),
    p('Four figures sit at the top: open alerts (and how many are critical), events in the last 24 hours, blocked networks (and how many requests they have refused), and ', b('Exposure'), ', which says whether public spaces are allowed and whether registration is open or by invite.'),

    h(2, 'Alerts'),
    p('An ', b('event'), ' is anything Tesria noticed; most are routine and are only listed under ', b('Recent events'), '. An ', b('alert'), ' is an event that needs a person to look at it, such as twenty-five wrong passwords from one address in five minutes. Alerts wait on this tab until someone deals with them.'),
    ...(await picture(security, 'security-dashboard', 'The Security tab: four figures, then the alerts, each with its buttons',
      'The figures at the top, then each alert: what happened, when, the address or account, and what you can do about it.')),
    p('Each alert shows what happened and when, the address it came from or the account it is about, and the details Tesria recorded. Its color says how serious it is: yellow for a warning, red for critical. An alert someone has already picked up says ', b('acknowledged'), '.'),
    p('Among the things that raise one:'),
    ul(
      li(p('a burst of failed sign-ins from one address, or many different accounts tried from one address;')),
      li(p('an account locked repeatedly, or an administrator signing in from a new address;')),
      li(p('many pages removed quickly, or many API tokens made quickly;')),
      li(p('someone promoted to administrator, a role given more rights, or ownership transferred;')),
      li(p('a space published, withdrawn or deleted, or the public spaces switch changed;')),
      li(p('a backup failing or overdue, a restore test failing, or the backup disk nearly full;')),
      li(p('the audit log’s chain broken (see ', b('Audit log integrity'), ' below);')),
      li(p('email stopping because Microsoft or Google refused Tesria’s mail sign-in.')),
    ),

    h(2, 'How administrators hear about an alert'),
    p('Every administrator is told, straight away, in two places:'),
    ul(
      li(p(b('The notification bell,'), ' at the top of every page. The alert shows as ', i('Security warning'), ' or ', i('Security critical'), ' with what happened; choosing it opens this tab.')),
      li(p(b('Email,'), ' when email is set up (see ', pageLink('Email (SMTP)'), '). Security alerts are always emailed at once, whatever someone chose for their other notifications, with the subject ', i('Security alert'), '. Several at once arrive together in one email.')),
    ),
    ...(await picture(security, 'security-bell', 'The notification bell open, with three security alerts in it',
      'Alerts in the bell: each opens the Security tab.')),
    panel('note', p(b('Only administrators get them.'), ' People without administration rights never see security alerts, in the bell or by email.')),

    h(2, 'Dealing with an alert'),
    p('Most alerts are harmless once you look: someone forgot a password, or signed in from a new café. The steps make sure one person looks, and that what they found is written down.'),
    step(1, 'Acknowledge it'),
    p(b('Acknowledge'), ' tells the other administrators that someone is looking, so two people do not chase the same thing. The alert then says ', b('acknowledged'), ' and by whom.'),
    step(2, 'Find out what happened'),
    p('The alert’s details, and ', b('Recent events'), ' lower on the tab, say what was seen and from where. For an account, ask its owner: was it them? For an address, is it one of yours, such as the office?'),
    step(3, 'Act on it, if it needs it'),
    p('An alert about an address has a ', b('Block'), ' button that blocks it for 24 hours. An alert about an account offers ', b('Sign out everywhere'), ', ', b('Revoke tokens'), ' and ', b('Suspend'), '. Each asks before it does anything.'),
    step(4, 'Resolve it'),
    p(b('Resolve'), ' closes the alert, with an optional note of what you found, such as ', i('“Sam mistyped a new password; nothing to do.”'), ' Resolved alerts are hidden unless you tick ', b('Show resolved'), ', and the note stays with them for the next person who wonders.'),
    panel('success', p(b('A good habit:'), ' resolve alerts once you know they are harmless. An empty list means anything new stands out.')),

    h(2, 'Kill switches'),
    p('Three switches that change the whole instance at once. They are here so that in a hurry you do not have to go looking for them; the same switches are in ', pageLink('Settings (administration)', 'Settings'), '.'),
    ...(await picture(security, 'security-switches', 'The kill switches', 'Require two-factor for administrators is the third switch.')),
    ul(
      li(p(b('Allow public spaces:'), ' off hides every published space from people who are not signed in, straight away. Each space keeps its setting, so turning it back on publishes them again. Asks for your password, and alerts every administrator.')),
      li(p(b('Allow public registration:'), ' off means new accounts need an ', pageLink('Invites', 'invite'), '.')),
      li(p(b('Require two-factor for administrators:'), ' an administrator, the owner included, who has not set up two-factor sees a page asking them to, instead of the Administration tabs, until they do.')),
    ),
    panel('warning', p(b('Set up your own two-factor first.'), ' Otherwise, the moment you turn the requirement on, Administration sends you to your profile to set it up too.')),

    h(2, 'Blocking an address'),
    p('When an alert shows attacks coming from one address, block it. Every request from a blocked address is refused before Tesria does anything else with it.'),
    ...(await picture(security, 'security-block', 'The form for blocking an address, filled in', 'The address, a reason, how many hours, and Block.')),
    step(1, 'Enter the address'),
    p('A single address such as ', c('203.0.113.7'), ', or a range such as ', c('203.0.113.0/24'), ', which means every address from ', c('203.0.113.0'), ' to ', c('203.0.113.255'), '.'),
    step(2, 'Add a reason and a time, if you like'),
    p('The reason is a note for the other administrators. The hours can be from 1 to a year; leave it empty to block until someone removes it.'),
    step(3, 'Choose Block'),
    p('The address appears in the list below the form. Tesria will not let you block a range that includes your own address. Blocks that run out are cleared away, and ', b('Remove'), ' ends one early, after asking for your password.'),

    h(2, 'Recent events'),
    p('The last 100 things Tesria noticed, with how serious each was, the address and the account. Most need nothing from you: a single wrong password is an event, and an alert is raised when events add up.'),

    h(2, 'Brute-force protection'),
    p('The limits that make guessing passwords slow and pointless. The defaults suit most instances.'),
    table([
      ['Setting', 'Default'],
      ['Credential attempts per address per minute', '10'],
      ['Anonymous requests per address per minute', '300'],
      ['API tokens per account per hour', '20'],
      ['Failed sign-ins before lockout', '5'],
      ['First lockout (seconds)', '60'],
      ['Maximum lockout (seconds)', '900'],
    ], [440, 260]),
    ul(
      li(p(b('Credential attempts'), ' are shared by signing in, registering and account recovery. If your whole office reaches Tesria through one internet connection, everyone shares one address, and you may need to raise it.')),
      li(p(b('Anonymous requests'), ' limits visitors who are not signed in. People who are signed in are not limited this way.')),
      li(p(b('Lockouts'), ' double with each further failure, up to the maximum, and are never permanent.')),
    ),
    p('Change a number and choose ', b('Save limits'), '. Below, ', b('Active lockouts'), ' lists accounts locked right now, each with ', b('Unlock'), '.'),

    h(2, 'Audit log integrity'),
    p('Every entry in the ', pageLink('Audit', 'audit log'), ' is linked to the one before it, so an entry that is changed or removed breaks the chain. ', b('Verify now'), ' checks the whole chain and names the first entry that does not fit. Tesria also checks once a day by itself, and raises a critical alert if the chain is broken.'),
  ))

  // =================================================== Backups (administration)
  await page('Backups (administration)', root, doc(
    p('Tesria backs itself up from the moment it starts, with nothing to set up. The Backups tab is where you check that it is working: whether each backup service is healthy, when the last backup was, and whether a backup has ever been restored to prove it works. How backups work in depth is in ', pageLink('Backups and recovery'), '; this page is a tour of the tab.'),
    p('Look at it once after installing, and now and then after that. A backup that fails raises an alert, so you will usually hear about trouble. But only a test restore proves a backup comes back.'),
    ...(await picture(backups, 'backups', 'Back up now and a backup service’s card', 'Back up now, and a service’s card with Test restore of newest.')),

    h(2, 'The service cards'),
    p('One card for each of the two backup services, ', b('Database dumps and uploads'), ' and ', b('Physical backups and point-in-time recovery'), '. Each says in a word how it is (', b('Healthy'), ', ', b('No backup yet'), ', ', b('Last run failed'), ', ', b('Overdue'), ', ', b('Agent offline'), ', ', b('Disk nearly full'), '), then when the last backup was and how big, when the next one runs, how far back you can restore, how many backups are kept, the free disk space, the last restore test, and how many runs succeeded in the last 30 days.'),

    h(2, 'Backing up now'),
    p('Backups run on their own schedule, but it is worth taking one before an upgrade or a big change. Choose ', b('Back up now'), ': it asks for a database dump, an archive of the uploads and a physical backup. Each service picks its job up within a minute, and the page updates when they finish.'),

    h(2, 'Testing a restore'),
    p(b('Test restore of newest'), ' on a card, or ', b('Test restore'), ' in a backup’s row, restores that backup somewhere temporary and checks it, without touching the wiki. The result appears on the card and under ', b('Recent runs'), '. Do it once after installing, and again from time to time.'),

    h(2, 'The rest of the tab'),
    ul(
      li(p(b('Disk space:'), ' a chart for each disk of what the wiki, its backups and everything else take, and what is free. It warns when there is less room than two more backups need.')),
      li(p(b('Storage targets:'), ' the copies kept somewhere other than this machine, in the cloud, on a network drive or on a removable drive. They are set up on the server, not here; each card shows its health, its last copy and restore drill, with ', b('Test connection'), ' and, for a removable drive, ', b('Copy now'), '. See ', pageLink('Offsite copies'), '.')),
      li(p(b('Retention policy:'), ' keep every backup forever, or keep the newest few and everything from the last so many days. ', b('Review change'), ' shows what a new policy would remove before you save it. See ', pageLink('Retention'), '.')),
      li(p(b('Backups:'), ' every backup kept, with its kind, when it was taken, its size and its last test. Tick ', b('Show backups removed in the last 30 days'), ' to see what retention has cleared away.')),
      li(p(b('Recent runs:'), ' every backup, test and copy, who asked for it, how long it took and what it did, with ', b('Log'), ' for the full output.')),
    ),

    h(2, 'Restoring a backup'),
    p('Restoring replaces the whole wiki with an older copy, so by default only the owner can do it, and this part is described in words. With the right, a ', b('Restore'), ' button appears in each backup’s row. You type the backup’s name and your password to confirm; a backup of the wiki as it is now is taken first, and the wiki is read-only until the restore finishes. Afterwards, ', b('The copy kept before the last restore'), ' offers ', b('Undo the restore'), ' (type ', c('UNDO'), ') and ', b('Remove the copy'), ' (type ', c('REMOVE'), '), both with your password. The whole story is in ', pageLink('Restoring and undo'), '.'),
    panel('warning', p(b('A deleted page does not need a restore.'), ' A restore takes back everyone’s work since the backup. For one page, use its ', b('History'), ', or the space’s ', b('Trash'), '.')),

    h(2, 'Who may do what'),
    table([
      ['Right', 'Allows', 'By default'],
      ['See backups', 'This tab', 'Administrators'],
      ['Run backups', 'Back up now, Test restore, Test connection, Copy now', 'Administrators'],
      ['Change the retention policy', 'Retention policy', 'Administrators'],
      ['Restore a backup', 'Restore, Undo the restore, Remove the copy', 'The owner'],
    ], [220, 320, 160]),
  ))

  // =============================================================== Roles
  await page('Roles', root, doc(
    p('A role is a named set of rights: what someone may do on the instance at all, such as create spaces, export pages or read the audit log. Every account has exactly one role. The Roles tab shows them as a grid, with a row for each right, a column for each role, and a tick where the role holds the right.'),
    p('You might come here to stop people exporting, to let only administrators create spaces, to let team leads send invites, or to make a narrower kind of administrator, say one who looks after accounts but not backups. Many teams never change anything.'),
    ...(await picture(roles, 'roles', 'The top of the roles grid', 'A dash in a user role’s column marks a right only administrator roles can hold.')),

    h(2, 'Reading the grid'),
    ul(
      li(p(b('Columns'), ' are the roles: the built-in Owner, Administrator and User, then any you have made, with how many accounts hold each. Under a role you cannot change, it says “Only the owner edits this role”.')),
      li(p(b('Rows'), ' are grouped by area: Content, People, Spaces, Security, Backups and Instance, then ', b('Always the owner'), ', which no other role can hold.')),
      li(p(b('A dash'), ' instead of a box, in a user role’s column, marks an administration right: everything outside Content. Those belong to administrator roles. To give someone one, make them an administrator.')),
    ),

    h(2, 'Changing what a role may do'),
    step(1, 'Tick or clear boxes'),
    p('Nothing is saved yet. ', b('Discard'), ' puts every box back.'),
    step(2, 'Choose Review changes'),
    p('A box below the grid lists, for each role you changed, what it gains and loses, and how many accounts that affects.'),
    ...(await picture(roles, 'roles-review', 'Reviewing a change before saving', 'What each role gains and loses, and Save roles.')),
    step(3, 'Choose Save roles'),
    p('Enter your password if asked. The change applies at once to everyone who holds the role.'),
    p('Administrators can change user roles; only the owner can change administrator roles. When an administrator role gains a right, every administrator is alerted. ', b('Reset'), ' followed by a role’s name, under the grid, puts that role back to Tesria’s defaults after asking.'),
    p('On a new instance, a note above the grid asks you to review the defaults. Choose ', b('Keep these defaults'), ' if they suit you, or change them and save.'),

    h(2, 'Making your own role'),
    step(1, 'Choose New role'),
    p('It is above the grid.'),
    step(2, 'Describe it'),
    p('Give it a name and, if you like, a description. Choose its tier, ', b('User'), ' or ', b('Administrator'), ' (only the owner can make administrator roles), and which role to copy its rights from to start with.'),
    step(3, 'Choose Create role, then adjust it'),
    p('A new column appears. Change its boxes and save as above.'),
    step(4, 'Move people into it'),
    p('On the ', pageLink('Users'), ' tab, with the menu in the Role column.'),
    p('Your own roles have ', b('Rename'), ' and ', b('Delete'), ' in their column heading. A role can only be deleted when nobody holds it.'),

    h(2, 'The rights'),
    ul(
      li(p(b('Content:'), ' Create spaces, Delete pages you created, Delete pages created by others, Export pages, Use API tokens, Create invite links.')),
      li(p(b('People:'), ' See the user list, Manage accounts, Assign roles, Promote users to administrator, Manage invite links, Manage groups.')),
      li(p(b('Spaces:'), ' Manage spaces, Publish spaces, Delete spaces, Control a space’s exports.')),
      li(p(b('Security:'), ' Read the audit log, See security, Respond to security, Change security settings.')),
      li(p(b('Backups:'), ' See backups, Run backups, Change the retention policy, Restore a backup.')),
      li(p(b('Instance:'), ' See the dashboard, Change the instance name and address, Change the branding, Change registration, Change the email server, Change anonymous reading, See roles, Edit user roles.')),
      li(p(b('Always the owner:'), ' Promote and demote administrators, Transfer ownership, Edit administrator roles.')),
    ),
    p('Each row on the tab has a line saying what the right allows. Out of the box, users cannot delete other people’s pages or create invite links; administrators hold every right except Promote users to administrator, Restore a backup and Change the branding, which start with the owner.'),
    panel('info', p(b('People reading a public space without an account'), ' get exactly what the User role holds. Take Export pages away from User, and they lose export too.')),
    p('Rights decide what someone may do at all; each space still decides where. The right to delete other people’s pages does not reach a space you cannot edit.'),
  ))

  // ============================================================== Groups
  await page('Groups', root, doc(
    p('A group is a named set of people, such as Marketing or the Berlin office. Instead of sharing a space or restricting a page person by person, you give access to the group. When someone joins the team, you add them to the group once, and they can reach everything it opens.'),

    h(2, 'Creating a group'),
    ...(await picture(groups, 'groups', 'The new group form, and the built-in groups', 'The form for a new group, and the three built-in groups below it.')),
    step(1, 'Name it'),
    p('Something people will recognize in a list, such as ', i('Marketing'), '. The description is optional.'),
    step(2, 'Choose Create group'),
    p('It appears in the list below.'),
    step(3, 'Add people'),
    p('Choose ', b('Members'), ' in its row. Pick a person from the list and choose ', b('Add member'), '. ', b('Remove'), ' takes someone out, after asking, since they lose anything they could reach only through the group.'),
    step(4, 'Use it'),
    p('In a space’s ', b('Permissions'), ', or a page’s ', b('Restrictions'), ', choose ', b('Group'), ' and then the group. See ', pageLink('Who can see a space'), ' and ', pageLink('Restrictions'), '.'),

    h(2, 'The built-in groups'),
    p(b('Owner'), ', ', b('Admins'), ' and ', b('Users'), ' are listed first, marked ', b('built in'), '. Their members follow each account’s role, so they are always up to date: ', b('Users'), ' is everyone with an active account, ', b('Admins'), ' is the administrators and the owner, and ', b('Owner'), ' is the owner. Nobody can rename or delete them, or add and remove members by hand, and a suspended account drops out of them. They are handy for, say, letting the Admins group manage a space.'),

    h(2, 'Renaming and deleting a group'),
    p(b('Edit'), ' changes a group’s name and description. ', b('Delete'), ' removes the group, after asking, along with any access given to it in spaces and pages. Its members keep their accounts.'),
    p('If a group is the only way into a space or a page, Tesria refuses to delete it and says where: removing that access would open the space or page to everyone. Give the access to someone else first.'),
  ))

  // =============================================================== Audit
  await page('Audit', root, doc(
    p('The audit log is Tesria’s record of who changed what, and when: roles and rights, settings, invites, publishing, deleted spaces, backups and restores, sign-ins, and more. Come here to answer “who turned this on?”, or after an alert, to see what else an account did.'),
    p('It is not pictured here, because every entry names real people.'),

    h(2, 'Reading it'),
    p('The tab shows the latest 50 entries, newest first. Each one has:'),
    ul(
      li(p(b('What happened,'), ' by its internal name, such as ', c('user.role_changed'), ', ', c('user.status_changed'), ', ', c('user.totp_disabled'), ', ', c('settings.updated'), ', ', c('invite.created'), ', ', c('space.published'), ', ', c('space.access_recovered'), ' or ', c('permissions.changed'), '.')),
      li(p(b('Who did it,'), ' or ', b('system'), ' for something Tesria did by itself.')),
      li(p(b('When.'))),
      li(p(b('The details'), ' as recorded, such as which account changed and its new role.')),
    ),
    p('Entries about a space or page you are not allowed to see are left out, because their details can name the page.'),

    h(2, 'Why you can trust it'),
    p('Every entry is linked to the one before it by a hash, a kind of fingerprint of its contents, so an entry that is altered or removed breaks the chain. ', b('Verify now'), ' on the ', pageLink('Security (administration)', 'Security'), ' tab checks the whole chain, and Tesria checks it once a day by itself, raising a critical alert if it is broken.'),
    p('Every entry is also written to the application’s log (', c('docker compose logs app'), ' on the server), where someone who can change the database cannot reach it.'),
    panel('note', p(b('A restore takes the audit log back too,'), ' like everything else in the wiki. Tesria knows a restore happened, and does not treat the shorter log as tampering.')),
  ))

  // ================================================== Settings (administration)
  await page('Settings (administration)', root, doc(
    p('Settings holds the choices that apply to the whole instance: its name and address, which sites pages may embed, who can join, and the email server. Each is its own card with its own save button, and each needs its own right, so a role can be allowed to fix the email server without being able to open registration. Every change is recorded in the audit log.'),

    h(2, 'Instance'),
    ...(await picture(settings, 'settings-instance', 'The Instance settings, with an example address', 'The name, and the public address.')),
    ul(
      li(p(b('Name:'), ' shown in the browser tab and at the start of every email’s subject. It starts as Tesria. The name in the top bar is set on ', pageLink('Branding'), ' instead.')),
      li(p(b('Public address:'), ' the full address people open Tesria at, such as ', c('https://wiki.example.com'), '. Links in emails use it. On a home or office network, the ', b('Trust this device'), ' guide also names it for anyone who opens Tesria by a number such as 192.168.1.50, so they use the name the secure connection is made for. Left blank, Tesria uses the address it was installed with, shown under the field.')),
    ),
    p('Change either and choose ', b('Save'), '. Once you have settled on the address everyone uses, put it here; see ', pageLink('Opening Tesria by name'), ' and ', pageLink('Trusting the local certificate'), '.'),

    h(2, 'Embeds'),
    p('Which sites a page may show inside itself, such as a YouTube video or a Figma design. One site per line; a leading dot, as in ', c('.youtube.com'), ', also allows everything under it. An address on no line is refused when the page is saved and blocked by the browser. Leave the box empty to turn embeds off.'),
    p('It starts with YouTube, Vimeo, Loom, Figma, Miro, CodePen, Google Docs and Google Drive. Choose ', b('Save'), ' after changing it. It needs the ', b('Change security settings'), ' right.'),

    h(2, 'Access'),
    ul(
      li(p(b('Allow public registration:'), ' on for a new instance, so anyone who can reach it can make an account. Off, new people need an ', pageLink('Invites', 'invite'), '. The very first account on an empty instance can always register, so this cannot lock you out.')),
      li(p(b('Allow public spaces:'), ' off for a new instance. On, you can publish spaces from ', pageLink('Spaces (administration)', 'Spaces'), ' for anyone to read. Off again, every published space is hidden at once and keeps its setting. It asks for your password and alerts every administrator. Before turning it on for an instance on the internet, work through ', pageLink('Security hardening'), '.')),
    ),
    p('Both take effect the moment you click them, and both are also on the Security tab.'),

    h(2, 'Email'),
    p('The mail server Tesria sends through: password reset links, security alerts to administrators, and notifications for people who ask for them.'),
    step(1, 'Fill in the mail server'),
    p('The host, port, username, password, from address and encryption, as your mail provider gives them. The password is never shown again; leave the field empty to keep the stored one.'),
    step(2, 'Choose Save mail settings'),
    step(3, 'Turn on Send email'),
    p('Until it is on, Tesria does not try to send anything.'),
    step(4, 'Choose Send test email to me'),
    p('It sends a message to your own address and says straight away whether the mail server accepted it, or what went wrong.'),
    p('What to put in each field is in ', pageLink('Email (SMTP)'), '.'),
  ))

  // ============================================================= Branding
  await page('Branding', root, doc(
    p('Branding makes Tesria look like your organization’s own: your name and logo in the top bar and on the sign-in page, your icon in the browser tab, and your colors. Only the owner can change it, unless the owner gives ', b('Change the branding'), ' to an administrator role, so this page describes it in words.'),

    h(2, 'Changing it'),
    step(1, 'Open Admin → Branding'),
    step(2, 'Make your changes, watching the preview'),
    p(b('Preview'), ', at the top, shows the top bar and the sign-in page as they will look, in light and dark, before anything is saved.'),
    step(3, 'Choose Save'),
    p('Enter your password if asked. ', b('Discard changes'), ' undoes anything not yet saved.'),
    panel('note', p(b('Pictures apply as soon as you pick them.'), ' The logos and the favicon are saved the moment you choose a file; everything else waits for Save.')),

    h(2, 'What you can change'),
    ul(
      li(p(b('Brand name:'), ' up to 60 characters, shown beside the logo in the top bar and on the sign-in page. Empty, it says Tesria. The instance name in Settings, which browser tabs and emails use, is separate.')),
      li(p(b('What to show:'), ' logo and name, logo only (for a logo with the name already in it), or name only. Until a logo is uploaded, the name is shown.')),
      li(p(b('On the sign-in page:'), ' logo and name side by side, or the logo above the name. It only matters when both are shown.')),
      li(p(b('Logo'), ' and ', b('Logo for dark mode:'), ' SVG, PNG, JPEG or WebP, up to 2 MB, and SVG up to 256 KB. SVG stays sharp at every size. The dark-mode logo is optional, for a logo that disappears on a dark background.')),
      li(p(b('Favicon:'), ' the icon in the browser tab. SVG, PNG, ICO, JPEG or WebP; a square picture works best, since a wide logo makes a poor tiny icon.')),
      li(p(b('Theme:'), ' let people choose light, dark or their system’s setting, or hold everyone to light only or dark only.')),
      li(p(b('Accent color:'), ' one of the six, or ', b('Custom…'), ' with a color for light mode and one for dark mode. Tesria checks that a custom color is readable as link text and on buttons, and if it is not, suggests the nearest shade that is; the choice stays yours. ', b('Use this accent for everyone'), ' stops people picking their own; without it, the color is the starting choice for anyone who has not picked one.')),
    ),
    p('Branding also goes into exports: a page exported as HTML and a space exported as a website carry the logo, favicon and colors.'),
    panel('info', p(b('Uploaded SVG files are cleaned'), ' of anything that could run, and are only ever shown as pictures.')),

    h(2, 'Going back'),
    p(b('Reset to Tesria'), ', after asking, removes the brand name, the logos and the favicon, and puts back Tesria’s colors with nobody held to a theme or accent. The uploaded files are deleted and cannot be brought back. The instance name is not changed.'),
  ))
}

// Pictures these pages no longer use: the first version's phone views, and
// shots since renamed. Taken down so they do not linger in the attachments
// or the exported pack (the old dashboard picture named real people).
const RETIRED = {
  'Dashboard': ['dashboard.phone.png'],
  'Users': ['users.phone.png'],
  'Invites': ['invites.phone.png'],
  'Security (administration)': ['security-settings.png', 'security-settings.phone.png'],
  'Roles': ['roles.phone.png'],
  'Groups': ['groups.phone.png'],
  'Settings (administration)': ['settings.png', 'settings.phone.png'],
}

export async function cleanup({ author }) {
  const space = await author.call('GET', '/api/spaces/SUPPORT')
  const tree = await author.call('GET', `/api/pages/tree?spaceId=${space.id}`)
  const admin = tree.find((n) => n.title === 'Administration')
  if (!admin) return
  for (const [title, files] of Object.entries(RETIRED)) {
    const node = (admin.children ?? []).find((n) => n.title === title)
    if (!node) continue
    for (const a of await author.call('GET', `/api/pages/${node.id}/attachments`)) {
      if (files.includes(a.filename)) {
        await author.call('DELETE', `/api/attachments/${a.id}`)
        console.log(`  - ${title}: ${a.filename}`)
      }
    }
  }
}
