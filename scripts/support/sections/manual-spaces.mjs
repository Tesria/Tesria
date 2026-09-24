// User manual, Spaces and Pages (dev-plan 10.5).
//
// Facts from the spaces, page view, editor, tree, labels, attachments,
// history, restrictions and trash components and endpoints, 2026-09-23.

const section = (title) => `section.profile__section:has(> h2:text-is("${title}"))`

export const shots = ({ demo }) => [
  { name: 'spaces-list', url: '/spaces', settle: 1500, steps: [{ wait: 2500 }] },
  { name: 'space-home', url: '/spaces/DEMO', settle: 1500, steps: [{ wait: 2500 }] },
  { name: 'space-permissions', url: '/spaces/DEMO/settings/permissions', settle: 1200, steps: [{ wait: 2500 }], clipTo: '.page-wrap' },
  { name: 'space-icon', url: '/spaces/DEMO/settings', settle: 1200, steps: [{ wait: 2500 }], clipTo: section('Icon') },
  { name: 'space-archive', url: '/spaces/DEMO/settings', settle: 1200, steps: [{ wait: 2500 }], clipTo: [section('Archive'), section('Danger zone')] },
  { name: 'page-tree', url: demo('Launch plan'), settle: 1200, steps: [{ wait: 2500 }], clipTo: 'aside.sidebar', phone: false },
  {
    name: 'page-tree-reorder', url: demo('Launch plan'), settle: 800,
    steps: [{ wait: 2500 }, { click: 'button[title="Reorder pages"]' }, { wait: 500 }],
    clipTo: 'aside.sidebar', phone: false,
  },
  {
    name: 'page-tree-reorder-cancel', settle: 300, skipCapture: true,
    steps: [{ click: 'aside.sidebar button:has-text("Cancel")' }, { wait: 300 }], phone: false,
  },
  // The labels row is as wide as the page and mostly empty: shrunk to its
  // chips for the picture, or the chips come out a few pixels high.
  { name: 'labels', url: demo('Launch plan'), settle: 1200, steps: [{ wait: 2500 }, { css: 'article div.labels { width: fit-content }' }], clipTo: 'article div.labels', clipPad: 16 },
  {
    name: 'attachments-tab', url: demo('Image'), settle: 1200,
    steps: [{ wait: 2500 }, { click: 'article .tabs button:has-text("Attachments")' }, { wait: 800 }, { scrollTo: '.tab-panel' }],
    clipTo: ['article .tabs', '.tab-panel'], clipPad: 12,
  },
  {
    name: 'history-tab', url: demo('Launch plan'), settle: 1200,
    steps: [{ wait: 2500 }, { click: 'article .tabs button:has-text("History")' }, { wait: 800 }, { scrollTo: '.tab-panel' }],
    clipTo: ['article .tabs', '.tab-panel'], clipPad: 12,
  },
  {
    name: 'restrictions-tab', url: demo('Launch plan'), settle: 1200,
    steps: [{ wait: 2500 }, { click: 'article .tabs button:has-text("Restrictions")' }, { wait: 800 }, { scrollTo: '.tab-panel' }],
    clipTo: ['article .tabs', '.tab-panel'], clipPad: 12,
  },
  { name: 'trash', url: '/spaces/DEMO/settings/trash', settle: 1200, steps: [{ wait: 2500 }], clipTo: '.page-wrap' },
]

export async function build({ top, page, ensure, figure, doc, p, h, text, bold, code, ul, ol, li, panel, table, live }) {
  const manual = top['User manual']
  const b = (t) => text(t, bold)
  const c = (t) => text(t, code)

  // ================================================================ Spaces
  const spaces = await ensure('Spaces', manual)
  await page('Spaces', manual, doc(
    p('A space holds the pages for one team, project or subject, with its own permissions, templates and trash.'),
    live('children', { depth: '1', sort: 'position' }),
  ))

  const what = await ensure('What a space is', spaces)
  await page('What a space is', spaces, doc(
    p('Every page lives in exactly one space. A space has a short ', b('key'), ', a name, a description and an icon, and everything in it shares the space’s permissions until a page is restricted further.'),
    p(b('Spaces'), ' in the top bar lists every space you can see, alphabetically, each with its icon, key, name and description. A ', b('public'), ' badge marks a space anyone on the internet can read.'),
    ...(await figure(what, 'spaces-list', 'The list of spaces')),
  ))

  await page('Creating a space', spaces, doc(
    p('On the Spaces page, choose ', b('New space'), '. The button is there if your role may create spaces, which ordinary users can by default.'),
    table([
      ['Field', 'What to enter'],
      ['Key', 'Two to 50 letters and digits, starting with a letter, such as TEAM or ENG2. It is part of every page’s address and cannot be changed later.'],
      ['Name', 'Required. What people see, such as Engineering.'],
      ['Description', 'Optional. One line on what the space is for.'],
    ], [160, 540]),
    p('Choose ', b('Create'), '. The new space appears in the list. It starts open to every signed-in user, not public, with every export allowed and an icon made from its key.'),
    panel('info', p('A new space is ', b('open'), ': everyone signed in can read, edit and administer it. To limit it to particular people, see ', b('Who can see a space'), '.')),
  ))

  const home = await ensure('The space home and watching', spaces)
  await page('The space home and watching', spaces, doc(
    p('Opening a space shows its home: the name, the description, and the tree of pages in the sidebar. A space with no pages yet offers ', b('Create the first one'), '.'),
    ...(await figure(home, 'space-home', 'A space’s home page')),
    h(2, 'Watching a space'),
    p(b('Watch this space'), ' on the home page tells you about every new page, every update and every new comment anywhere in it, in the bell and, if you choose, by email. Choose ', b('Watching'), ' to stop. You are never told about your own changes, and nothing is watched unless you ask.'),
    p('To follow just one page, use ', b('Watch this page'), ' in that page’s ⋮ menu.'),
  ))

  const who = await ensure('Who can see a space', spaces)
  await page('Who can see a space', spaces, doc(
    p('Open ', b('Space settings → Permissions'), '.'),
    ...(await figure(who, 'space-permissions', 'Space permissions')),
    h(2, 'Open and private spaces'),
    p('A space with no grants is ', b('open'), ': every signed-in user can view, edit and administer it. Adding the first grant makes it ', b('private'), ': from then on only the people and groups listed have access, and you are added as an administrator automatically so you cannot lock yourself out.'),
    h(2, 'Giving access'),
    ol(
      li(p('Choose ', b('User'), ' or ', b('Group'), ', then the person or group.')),
      li(p('Choose the level: ', b('View'), ', ', b('Edit'), ' or ', b('Admin'), '. Each includes the ones before it.')),
      li(p('Choose ', b('Grant'), '.')),
    ),
    table([
      ['Level', 'Allows'],
      ['View', 'Reading pages, comments and attachments.'],
      ['Edit', 'Also creating, editing, moving and deleting pages.'],
      ['Admin', 'Also the space’s settings, permissions, webhooks and trash.'],
    ], [140, 560]),
    p(b('Revoke'), ' removes a grant. The last administrator of a space cannot be removed; to make a private space open to everyone again, use ', b('Make this space open again'), ', which removes every grant at once, asks for your password, and alerts every administrator.'),
    h(2, 'The built-in groups'),
    p('Three groups exist on every instance and follow each account’s role by themselves: ', b('Users'), ' (everyone with an account), ', b('Admins'), ' (administrators and the owner) and ', b('Owner'), '. Granting View to Users shares a private space with everyone signed in, and it stays right as people join and leave.'),
    panel('note', p('Administrators of the whole instance are not automatically members of every space. In ', b('Administration → Spaces'), ' they can give themselves access with ', b('Get access'), ', which is recorded in the audit log.')),
    p('To hide individual pages inside a space, see ', b('Restrictions'), ' under Pages. To let people read a space without signing in, see ', b('Public reading'), '.'),
  ))

  const icon = await ensure('Space icons', spaces)
  await page('Space icons', spaces, doc(
    p('The icon appears in the spaces list, the sidebar and the breadcrumb. Change it in ', b('Space settings → Details → Icon'), '; changes save at once.'),
    ...(await figure(icon, 'space-icon', 'Choosing a space icon')),
    ul(
      li(p(b('The default'), ' is the first letter of the key on a colored tile. ', b('Tile color'), ' changes the color.')),
      li(p(b('A picture'), ': ', b('Upload picture'), ' takes a PNG, JPEG or WebP of up to 1 MB and crops it to a square. SVG is not accepted.')),
      li(p(b('An emoji'), ': pick one of those offered, or paste any other into ', b('Any other emoji'), ' and choose ', b('Use it'), '.')),
    ),
    p(b('Use the default'), ' goes back to the letter tile.'),
  ))

  const archive = await ensure('Archiving and deleting a space', spaces)
  await page('Archiving and deleting a space', spaces, doc(
    p('Both are at the bottom of ', b('Space settings → Details'), '.'),
    ...(await figure(archive, 'space-archive', 'Archive and delete in Space settings')),
    h(2, 'Archiving'),
    p(b('Archive this space'), ' keeps everything but takes the space out of the way: it leaves the spaces list and public reading, and ', b('Administration → Spaces'), ' still lists it. ', b('Unarchive this space'), ' brings it back. Any administrator of the space can do either.'),
    h(2, 'Deleting'),
    panel('error', p(b('Deleting a space cannot be undone from inside Tesria.'), ' Every page, version, attachment, comment and label in it is destroyed, including what is in its trash. Only a backup taken beforehand still holds it.')),
    p(b('Delete this space'), ' is offered to people whose role may delete spaces, which is administrators by default. The dialog shows how many pages and attachments will go, and asks you to type the key exactly and enter your password. If the space is public, its public pages stop working immediately.'),
    p('If you only want the space out of sight, archive it instead.'),
  ))

  // ================================================================= Pages
  const pages = await ensure('Pages', manual)
  await page('Pages', manual, doc(
    p('Writing, organizing and looking after pages.'),
    live('children', { depth: '1', sort: 'position' }),
  ))

  await page('Creating a page', pages, doc(
    p('Choose ', b('+ New page'), ' in the space’s sidebar. From inside a page it makes a sub-page of that page; anywhere else it makes a page at the top of the space. On a phone, the button is ', b('+ New'), '.'),
    ol(
      li(p('Type a title. Pressing Return moves to the body.')),
      li(p('Write. Type ', c('/'), ' on a new line to add anything other than text; see ', b('The editor'), '.')),
      li(p('Choose ', b('Publish'), '.')),
    ),
    p('Nobody else sees the page until you publish it. You can add pictures and files before publishing.'),
    h(2, 'Starting from a template'),
    p('If the space has templates, ', b('Start from a template (optional)'), ' above the title offers them, including templates available across the whole instance. Choosing one replaces what you have written so far, and asks first if there is anything.'),
    p('To make a template, open a page you want to reuse and choose ', b('⋮ → Save as template'), '. Give it a name and choose ', b('This space only'), ' or ', b('Instance-wide'), '. Templates are listed, renamed and deleted in ', b('Space settings → Templates'), '.'),
  ))

  await page('Drafts, Publish and Update', pages, doc(
    p('A page you are writing for the first time is a draft that only you can see. ', b('Publish'), ' makes it version 1, tells anyone watching the space, and tells anyone you mentioned.'),
    p('Editing an existing page, the button is ', b('Update'), '. Each update adds a new version to the page’s history. ', b('What changed? (optional)'), ' under the page is saved with the version, so the history can say why.'),
    h(2, 'Closing'),
    ul(
      li(p(b('Close'), ' on a new page throws the draft away. On an existing page it returns to the page without saving.')),
      li(p('Leaving the editor any other way asks what to do: publish or update and leave, leave without saving, or stay.')),
    ),
    panel('info', p('Where several people can edit at once, unpublished edits to an existing page are kept in a shared draft and survive leaving the editor. See ', b('Editing at the same time'), '.')),
  ))

  const tree = await ensure('The page tree and reordering', pages)
  await page('The page tree and reordering', pages, doc(
    p('The sidebar shows every page you can see in the space as a tree. Pages you cannot see are left out, with everything under them.'),
    ...(await figure(tree, 'page-tree', 'The page tree')),
    h(2, 'Moving pages'),
    ol(
      li(p('Choose the pencil beside ', b('Pages'), ' (', b('Reorder pages'), ').')),
      li(p('Drag a page up or down to reorder it. Drag it right to make it a sub-page of the page above, or left to move it out a level.')),
      li(p('Choose ', b('Save'), ' to keep the changes, or ', b('Cancel'), '.')),
    ),
    ...(await figure(tree, 'page-tree-reorder', 'Reordering pages')),
    p('Moving a page needs edit rights on it and on its new parent. A page cannot be moved under one of its own sub-pages, or to another space. Moving does not create a new version.'),
    panel('note', p('On a phone, the tree is on the space’s home page, and reordering works by touch there.')),
  ))

  await page('Page actions', pages, doc(
    p('Everything you can do to a page, from the bar above it:'),
    table([
      ['Action', 'Where', 'Notes'],
      ['Edit', 'Page bar', 'Shown if you may edit the page.'],
      ['Full width', 'Page bar', 'Widens the page for everyone. Not on phones.'],
      ['Export as Markdown, HTML or PDF', '⋮ menu', 'Where the space allows it; see Exporting and publishing.'],
      ['Watch this page', '⋮ menu', 'Notifications when it changes or gets a comment.'],
      ['Save as template', '⋮ menu', 'Start new pages from this one.'],
      ['Move…', '⋮ menu', 'To another place in this space or another space, with the pages under it.'],
      ['Copy…', '⋮ menu', 'A new page, titled Copy of …, with the content, labels and attachments, and optionally the pages under it.'],
      ['Delete', '⋮ menu', 'Moves it and its sub-pages to the trash. Shown if your role allows.'],
    ], [220, 120, 360]),
    p('A copy has its own attachments, so its pictures do not depend on the original. It does not bring the history or comments. Moving needs edit rights on the page and where it goes; moving within a space can also be done by dragging in the page tree.'),
    h(2, 'Who may delete'),
    p('Deleting needs edit rights on the page, plus a right from your role: ', b('Delete pages you created'), ' (ordinary users by default) or ', b('Delete pages created by others'), ' (administrators by default).'),
  ))

  const labels = await ensure('Labels', pages)
  await page('Labels', pages, doc(
    p('Labels are short tags under a page’s title that group pages across spaces, such as ', c('release'), ' or ', c('how-to'), '.'),
    ...(await figure(labels, 'labels', 'Labels on a page')),
    ul(
      li(p(b('+ Add label'), ', type the label, then ', b('Add'), '. The × on a label removes it. Both need edit rights.')),
      li(p('Labels are lower-case letters, digits, dots, dashes and underscores, up to 50 characters, starting with a letter or digit. No spaces.')),
      li(p('Clicking a label lists every page with it that you can see. ', b('All labels'), ' there, or ', c('/labels'), ', lists every label in use.')),
    ),
    p('The ', b('Content by label'), ' and ', b('Labels list'), ' live content blocks put those lists inside a page.'),
  ))

  const attachments = await ensure('Attachments', pages)
  await page('Attachments', pages, doc(
    p('The ', b('Attachments'), ' tab under a page lists its files. Pictures you paste or drop into the editor land here too.'),
    ...(await figure(attachments, 'attachments-tab', 'The Attachments tab')),
    ul(
      li(p(b('Upload file'), ' adds one file of any type, up to 25 MB.')),
      li(p('Clicking a file’s name opens it in a new tab. Web pages, SVG images and scripts always download instead, so an uploaded file can never run as part of the site.')),
      li(p(b('Delete'), ' removes the file. Anywhere the page shows it stops showing it.')),
    ),
    p('Uploading and deleting need edit rights; opening needs view rights. To show a file inside the page, use the ', b('File or video'), ' element.'),
  ))

  const history = await ensure('History and restoring', pages)
  await page('History and restoring', pages, doc(
    p('Every publish and update keeps a version. The ', b('History'), ' tab lists them, newest first, with who made each and its comment.'),
    ...(await figure(history, 'history-tab', 'The History tab')),
    ul(
      li(p(b('Preview'), ' shows a version as it was.')),
      li(p(b('Restore'), ' puts a version’s content back by adding a new version on top, named ', b('Restored from version N'), '. Nothing in the history is lost, and watchers are told as for any update.')),
    ),
    h(2, 'Comparing versions'),
    p('Tick two versions and choose ', b('Compare'), '. The newer one’s additions are highlighted and what it removed is struck through, paragraph by paragraph.'),
    p('Versions keep the content, not the title, so restoring never changes the title back.'),
  ))

  const restrictions = await ensure('Restrictions', pages)
  await page('Restrictions', pages, doc(
    p('A restriction limits one page, and everything under it, to particular people or groups, inside a space they can otherwise use.'),
    ...(await figure(restrictions, 'restrictions-tab', 'The Restrictions tab')),
    ol(
      li(p('Open the ', b('Restrictions'), ' tab under the page.')),
      li(p('Choose ', b('User'), ' or ', b('Group'), ', the person or group, and ', b('View'), ' or ', b('Edit'), '.')),
      li(p('Choose ', b('Restrict'), '. The first restriction adds you too, so you keep access.')),
    ),
    table([
      ['Restriction', 'Effect on everyone not listed'],
      ['View', 'The page and its sub-pages disappear: from the tree, search, labels and links.'],
      ['Edit', 'They can still read, but not change the page or its sub-pages.'],
    ], [160, 540]),
    p('Space administrators can always see restricted pages. The tab lists only the restrictions set on this page; one set on a page above still applies.'),
    panel('warning', p('People who are not signed in never see a restricted page, even in a public space.')),
  ))

  const trash = await ensure('Trash', pages)
  await page('Trash', pages, doc(
    p('Deleting a page moves it and its sub-pages to the space’s trash: ', b('Space settings → Trash'), '.'),
    ...(await figure(trash, 'trash', 'The trash')),
    ul(
      li(p(b('Restore'), ' puts the page and its sub-pages back under their old parent, or at the top of the space if the parent has gone. Anyone who can edit the page may restore it.')),
      li(p(b('Delete permanently'), ' destroys the page, its history and its files. It needs administrator rights on the space and your password again, and cannot be undone.')),
    ),
    p('Pages stay in the trash until someone deletes them permanently; nothing empties it on a schedule.'),
  ))
}
