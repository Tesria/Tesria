// Administration: every tab under /admin (dev-plan 10.5).
//
// Facts from src/web/src/routes/admin, the admin endpoints and the rights
// catalog (InstancePermissions.cs), gathered 2026-09-23 after that day's
// admin fixes (group deletion, expired blocks, alert names, the dashboard's
// restricted titles). The pictures are taken as Alex, an administrator: the
// owner-only screens (Branding, Restore, Transfer ownership) are described
// in words. Rows that would show a real person's account or address are left
// out of the pictures.

const section = (title) => `section.profile__section:has(> h2:text-is("${title}"))`
// Only Tesria Demo's fictional people: this instance's owner, and any other
// account made while building it, may be a real person's name.
// The Roles tab names whoever last reviewed the roles, which on this
// instance is a real person rather than one of the example accounts.
const NO_REVIEWER = "document.querySelectorAll('p').forEach((el) => { if (el.textContent.includes('Last reviewed')) el.textContent = el.textContent.replace(/\\s*Last reviewed[^.]*\\./, '') })"
const ONLY_EXAMPLE_ACCOUNTS = "document.querySelectorAll('table.admin-table tbody tr').forEach((r) => { if (!['Alex Rivera', 'Sam Okafor', 'Priya Natarajan', 'Mei Chen', 'Jordan Brooks'].some((n) => r.textContent.includes(n))) r.remove() })"

export const shots = () => [
  { name: 'dashboard', url: '/admin', settle: 1500, steps: [{ wait: 3000 }] },
  { name: 'users', url: '/admin/users', settle: 1200, steps: [{ wait: 2500 }, { eval: ONLY_EXAMPLE_ACCOUNTS }], clipTo: 'table.admin-table' },
  { name: 'invites', url: '/admin/invites', settle: 1200, steps: [{ wait: 2500 }], clipTo: 'form.form-inline', clipPad: 12 },
  { name: 'security-settings', url: '/admin/security', settle: 1200, steps: [{ wait: 2500 }], clipTo: section('Brute-force protection') },
  { name: 'roles', url: '/admin/roles', settle: 1500, steps: [{ wait: 3000 }, { eval: NO_REVIEWER }] },
  { name: 'groups', url: '/admin/groups', settle: 1200, steps: [{ wait: 2500 }], clipTo: ['form.card.form-inline', 'ul.version-list'], clipPad: 12 },
  {
    name: 'settings', url: '/admin/settings', settle: 1200,
    steps: [{ wait: 2500 },
      { type: 'smtp.example.com', selector: 'input[name="smtpHost"]' },
      { type: 'wiki@example.com', selector: 'input[name="smtpUsername"]' },
      { type: 'wiki@example.com', selector: 'input[name="smtpFromAddress"]' }],
    clipTo: '.admin-settings',
    // One column on a phone: the whole grid would be a picture 5,000 pixels tall.
    phone: { clipTo: section('Instance') },
  },
]

export async function build({ top, page, ensure, figure, doc, p, h, text, bold, code, ul, ol, li, panel, table, live }) {
  const b = (t) => text(t, bold)
  const c = (t) => text(t, code)
  const root = top['Administration']

  await page('Administration', null, doc(
    p('Running the instance from the browser: people, spaces, security, backups, roles and settings. Choose ', b('Admin'), ' in the top bar.'),
    p('Each tab appears only to people whose role holds its right. By default administrators hold almost all of them, and the owner holds everything; ', b('Roles'), ' explains how to change that.'),
    live('children', { depth: '1', sort: 'position' }),
  ))

  // ------------------------------------------------------------ Roles first
  await page('Owner, administrators and users', root, doc(
    p('Every account is in one of three tiers.'),
    table([
      ['Tier', 'Who', 'Can'],
      ['Owner', 'Exactly one account: whoever set the instance up, until they hand it on.', 'Everything, always. Cannot be suspended, demoted or reset by anyone else.'],
      ['Administrator', 'People who run the instance day to day.', 'What the Administrator role allows. Administrators cannot promote or demote each other: only the owner can.'],
      ['User', 'Everyone else.', 'Read and write content, create spaces, export, and use API tokens, as the User role allows.'],
    ], [140, 260, 300]),
    p('A ', b('role'), ' is a set of rights within a tier. Each tier has a built-in role, and you can add your own; moving someone between roles of the same tier never changes their tier.'),
    h(2, 'Rights only the owner has'),
    ul(
      li(p('Promoting and demoting administrators.')),
      li(p('Transferring ownership.')),
      li(p('Editing what administrator roles may do.')),
      li(p('By default, also: changing the branding and restoring a backup. The owner can give these two to a role.')),
    ),
    h(2, 'Transferring ownership'),
    p('The owner finds the new owner in ', b('Users'), ' and chooses ', b('Transfer ownership'), '. After confirming, and entering the password, the other account becomes the owner and the former owner becomes an administrator. Every administrator is alerted. Only the new owner can hand it back.'),
  ))

  const dash = await ensure('Dashboard', root)
  await page('Dashboard', root, doc(
    p('The instance at a glance, over the last 7, 30 or 90 days.'),
    ...(await figure(dash, 'dashboard', 'The dashboard')),
    table([
      ['Section', 'Shows'],
      ['People', 'Accounts, how many were active in the last 7 and 30 days, new accounts, and sign-ins and failed sign-ins over time.'],
      ['Content', 'Spaces, pages and their versions, comments, attachments and their size, and pages created over time.'],
      ['Usage', 'Page views, and views per day.'],
      ['Health', 'Whether each backup service ran on time; click through to Backups.'],
      ['Most viewed, Most active editors', 'The top ten of each in the range.'],
    ], [220, 480]),
    p('A page you cannot open is counted in Most viewed but not named.'),
  ))

  const users = await ensure('Users', root)
  await page('Users', root, doc(
    p('Every account, with its role, status, recovery codes and when it was last seen. The owner is listed first, then administrators.'),
    ...(await figure(users, 'users', 'The Users tab')),
    table([
      ['Action', 'What it does', 'Who may'],
      ['Make admin', 'Moves a user into the Administrator role.', 'The owner, or a role with Promote users to administrator'],
      ['Demote', 'Moves an administrator back to User.', 'The owner'],
      ['Role picker', 'Moves someone between roles in the same tier.', 'Assign roles (users); the owner (administrators)'],
      ['Suspend, Reactivate', 'A suspended account is signed out and cannot sign in or use its tokens.', 'Manage accounts'],
      ['Sign out', 'Ends every session of that account.', 'Manage accounts'],
      ['Revoke tokens', 'Deletes all of that account’s API tokens.', 'Manage accounts'],
      ['Reset password', 'Makes a one-time reset link, valid for an hour. Give it to the person yourself.', 'Manage accounts'],
      ['Unlock', 'Clears a lockout after too many wrong passwords.', 'Manage accounts'],
      ['Turn off two-factor', 'For someone who has lost the phone with their authenticator app and every recovery code. Signs them out everywhere and alerts every administrator. Never the owner; another administrator only by the owner.', 'Manage accounts'],
      ['Transfer ownership', 'See Owner, administrators and users.', 'The owner'],
    ], [170, 330, 200]),
    p('Changing anyone’s role, and turning off two-factor, ask for your password again. Suspend, Sign out and Revoke tokens ask you to confirm first. Promoting someone to administrator alerts every administrator.'),
    ul(
      li(p(b('Codes'), ' shows how many recovery codes are left; ', b('none'), ' and ', b('not saved'), ' mark accounts that could not get back in without help.')),
      li(p(b('locked'), ' in the Status column marks an account locked after wrong passwords.')),
      li(p('Nobody can suspend, reset or sign out the owner. Accounts are never deleted: suspend one instead.')),
    ),
  ))

  const spaces = await ensure('Spaces (administration)', root)
  await page('Spaces (administration)', root, doc(
    p('Every space, including archived and private ones, with its creator, page count, storage and whether it is public. It shows facts about spaces, never their content.'),
    ul(
      li(p(b('Publish'), ' and ', b('Withdraw'), ' make a space readable without signing in, or stop it. See ', b('Public reading'), '. The ', b('comments'), ' box lets public readers see its comments.')),
      li(p(b('Get access'), ' gives you administrator rights on a private space you are not in, for when its own administrators have left. It is recorded in the audit log; remove yourself from the space’s Permissions when you are done.')),
    ),
    p('Archiving and deleting are in each space’s own settings.'),
  ))

  const invites = await ensure('Invites', root)
  await page('Invites', root, doc(
    p('Single-use links that let someone create an account, whatever ', b('Who can join'), ' is set to.'),
    ...(await figure(invites, 'invites', 'Creating an invite')),
    ol(
      li(p('Optionally enter the person’s email address, so only they can use it.')),
      li(p('Choose how long it lasts: 1 to 90 days, 7 by default.')),
      li(p(b('Create invite'), ', then copy the link. It is shown once.')),
    ),
    p('The list shows each invite as ', b('Unused'), ', ', b('used'), ' by whom, or ', b('expired'), '. ', b('Revoke'), ' cancels an unused one.'),
    p('A role with ', b('Create invite links'), ' but not ', b('Manage invite links'), ' can make invites without seeing the list. People outside administration who may invite have ', b('Invite people'), ' in their top bar.'),
  ))

  const security = await ensure('Security (administration)', root)
  await page('Security (administration)', root, doc(
    p('What Tesria has noticed, and what to do about it.'),
    h(2, 'Alerts'),
    p('Alerts are raised for things that need a person: repeated failed sign-ins, credential stuffing, an administrator signing in from a new address, many pages removed quickly, a backup failing, a space published or deleted, and more. Every administrator gets them in the bell, and by email when email is set up.'),
    ul(
      li(p(b('Acknowledge'), ' says someone is looking; ', b('Resolve'), ' closes it, with an optional note.')),
      li(p('Alerts about an address offer ', b('Block'), ' for 24 hours. Alerts about an account offer ', b('Sign out everywhere'), ', ', b('Revoke tokens'), ' and ', b('Suspend'), '.')),
    ),
    h(2, 'Kill switches'),
    p(b('Allow public spaces'), ', ', b('Allow public registration'), ' and ', b('Require two-factor for administrators'), ', each taking effect at once. With the last one on, an administrator without two-factor sees a page asking them to set it up instead of the admin tabs.'),
    h(2, 'Blocked networks'),
    p('Block an address such as ', c('203.0.113.7'), ' or a range such as ', c('203.0.113.0/24'), ', for a number of hours or until removed. A blocked address is refused before anything else. You cannot block a range containing your own address. Blocks that have run out are cleared away.'),
    h(2, 'Brute-force protection'),
    ...(await figure(security, 'security-settings', 'Brute-force protection settings')),
    table([
      ['Setting', 'Default'],
      ['Credential attempts per address per minute', '10 (sign-in, registration and recovery share it)'],
      ['Anonymous requests per address per minute', '300'],
      ['API tokens per account per hour', '20'],
      ['Failed sign-ins before lockout', '5'],
      ['First lockout (seconds)', '60, doubling each time'],
      ['Maximum lockout (seconds)', '900'],
    ], [360, 340]),
    h(2, 'Audit log integrity'),
    p(b('Verify now'), ' checks that no entry in the audit log has been changed or removed. Tesria also checks every day and raises a critical alert if the chain is broken.'),
  ))

  await page('Backups (administration)', root, doc(
    p('Status, retention, backing up now, testing a restore, restoring, and offsite copies. Everything on this tab is explained under ', b('Installation and operations → Backups and recovery'), '.'),
    table([
      ['Right', 'Allows', 'Held by default by'],
      ['See backups', 'The tab itself', 'Administrators'],
      ['Run backups', 'Back up now, Test restore, Copy now, Test connection', 'Administrators'],
      ['Change the retention policy', 'Retention', 'Administrators'],
      ['Restore a backup', 'Restore, undo, and removing the kept copy', 'The owner only'],
    ], [220, 300, 180]),
  ))

  const roles = await ensure('Roles', root)
  await page('Roles', root, doc(
    p('What each role may do, as a table: a row for each right, a column for each role.'),
    ...(await figure(roles, 'roles', 'The Roles tab')),
    h(2, 'Changing a role'),
    ol(
      li(p('Tick or clear boxes. Nothing is saved yet.')),
      li(p(b('Review changes'), ' lists what each role gains and loses.')),
      li(p(b('Save roles'), ' asks for your password and applies at once.')),
    ),
    p('Administrators may change user roles. Only the owner may change administrator roles. A role that gains rights is announced to every administrator. ', b('Reset'), ' puts a role back to its defaults.'),
    p('Rights of the administration area belong to administrator roles, and show as a dash in user roles’ columns. To give someone administration rights, promote them to an administrator role.'),
    h(2, 'Your own roles'),
    p(b('New role'), ' copies an existing role into a new column. Give it a name, choose its tier (only the owner can make administrator roles), and adjust its boxes. ', b('Rename'), ' and ', b('Delete'), ' work on your own roles; a role can be deleted only when nobody holds it. Move people into it on the Users tab.'),
    h(2, 'The rights'),
    table([
      ['Area', 'Rights'],
      ['Content', 'Create spaces · Delete pages you created · Delete pages created by others · Export pages · Use API tokens · Create invite links'],
      ['People', 'See the user list · Manage accounts · Assign roles · Promote users to administrator · Manage invite links · Manage groups'],
      ['Spaces', 'Manage spaces · Publish spaces · Delete spaces · Control a space’s exports'],
      ['Security', 'Read the audit log · See security · Respond to security · Change security settings'],
      ['Backups', 'See backups · Run backups · Change the retention policy · Restore a backup'],
      ['Instance', 'See the dashboard · Change the instance name and address · Change the branding · Change registration · Change the email server · Change anonymous reading · See roles · Edit user roles'],
      ['Always the owner', 'Promote and demote administrators · Transfer ownership · Edit administrator roles'],
    ], [160, 540]),
    panel('info', p('People reading public spaces without signing in get exactly what the User role holds: take ', b('Export pages'), ' away from User and they lose export too.')),
    p('Rights decide what someone may do at all. Where they may do it is still up to each space’s permissions: the right to delete other people’s pages does not reach a space you cannot edit.'),
  ))

  const groups = await ensure('Groups', root)
  await page('Groups', root, doc(
    p('A group is a named set of people, so a space or a page can be shared with a whole team at once.'),
    ...(await figure(groups, 'groups', 'The Groups tab, with the three built-in groups')),
    ul(
      li(p(b('Create group'), ' with a name and an optional description.')),
      li(p(b('Members'), ' opens the list: choose a person and ', b('Add member'), ', or ', b('Remove'), '.')),
      li(p('Groups then appear as ', b('Group'), ' in space permissions and page restrictions.')),
    ),
    p(b('Delete'), ' removes the group and the permissions and restrictions given to it; its members keep their accounts. If the group is the only way into a space or page, deleting it is refused, because removing that access would open it to everyone. Give the access to someone else first.'),
    h(2, 'Built-in groups'),
    p(b('Owner'), ', ', b('Admins'), ' and ', b('Users'), ' are listed first and marked built in. Their members follow each account’s role: Users is everyone with an account, Admins is the administrators and the owner, Owner is the owner. They cannot be renamed, deleted or have members added by hand.'),
  ))

  await page('Audit', root, doc(
    p('The latest 50 changes across the spaces you can see: who did what, and when. Every entry is chained to the one before, so an entry that is changed or removed is detected; ', b('Security → Verify now'), ' checks the chain.'),
    p('Entries include role and permission changes, settings, invites, publishing, space deletions, backup policy changes and restores, sign-in security events and more. Each is shown with its internal name, such as ', c('user.role_changed'), ', and its details.'),
    p('Every entry is also written to the application log (', c('docker compose logs app'), '), where someone who can change the database cannot reach it.'),
  ))

  const settings = await ensure('Settings (administration)', root)
  await page('Settings (administration)', root, doc(
    ...(await figure(settings, 'settings', 'Instance settings')),
    table([
      ['Section', 'Setting', 'Default'],
      ['Instance', 'Name: shown in the browser tab and email subjects.', 'Tesria'],
      ['Instance', 'Public address: where links in email point. Blank uses the address Tesria was installed with.', 'Blank'],
      ['Embeds', 'Allowed embed hosts, one per line. A leading dot also allows subdomains; empty turns embeds off.', 'YouTube, Vimeo, Loom, Figma, Miro, CodePen, Google Docs and Drive'],
      ['Access', 'Allow public registration.', 'On'],
      ['Access', 'Allow public spaces. Asks for your password and alerts every administrator.', 'Off'],
      ['Email', 'The mail server; see Email (SMTP).', 'Off'],
    ], [110, 420, 170]),
    p('Each section needs its own right, so a role can be allowed to change the email server without being able to open registration. Every change is recorded in the audit log.'),
  ))

  await page('Branding', root, doc(
    p('Makes Tesria look like your organization’s: its name and logo in the top bar and on the sign-in page, its favicon, and its colors. Only the owner can change it, unless the owner gives the right to a role.'),
    table([
      ['Option', 'Choices'],
      ['Brand name', 'Up to 60 characters. Replaces “Tesria” in the top bar.'],
      ['What to show', 'Logo and name, logo only (for a logo with the name in it), or name only.'],
      ['On the sign-in page', 'Logo and name side by side, or the logo above the name.'],
      ['Logo, and Logo for dark mode', 'SVG, PNG, JPEG or WebP. Pictures up to 2 MB, SVG up to 256 KB.'],
      ['Favicon', 'The browser tab icon: SVG, PNG, ICO, JPEG or WebP. A square works best.'],
      ['Theme', 'Let people choose, Light only, or Dark only.'],
      ['Accent color', 'One of the six, or a custom color for light mode and dark mode, with a contrast check. Use this accent for everyone stops people picking their own.'],
    ], [220, 480]),
    p('Logos and the favicon apply as soon as they are chosen; everything else waits for ', b('Save'), '. ', b('Reset to Tesria'), ' goes back to the original look. Branding also appears in HTML exports and exported websites.'),
    panel('note', p('SVG logos are cleaned of anything that could run, and are only ever shown as pictures.')),
  ))
}
