// User manual, Spaces and Pages (dev-plan 10.5, rewritten for 15.6).
//
// Written to scripts/docs/WRITING.md. "Creating a space" and "Templates"
// are the approved pilot pages, moved here from sections/pilot.mjs as they
// were, with their pictures, the Meeting notes template they need, and the
// cleanup that places Templates after Creating a page.
//
// Facts checked against the code, 2026-09-24: SpacesPage, SpaceHome,
// SpacePage, SpaceSettingsPage, DeleteSpaceDialog, SpaceIconPicker,
// SpaceAccessEditor, TrashPage, PageView, PageEditor, LeaveEditorDialog,
// PageTree, treeMarkers, PageEmoji, MoveCopyDialog, HistoryPanel,
// RestrictionsPanel, AttachmentsPanel, PageLabels, LabelsIndexPage,
// LabelPage; and in src/Api, SpaceEndpoints, SpaceIcons, PermissionEndpoints,
// PermissionService, PageEndpoints, PageCopy, LabelEndpoints,
// AttachmentEndpoints, NotificationService.
//
// New pages (not in the first version), for the owner to confirm:
//   Pages → Page emoji                  the emoji above a page's title (15.7)
//   Pages → Moving and copying pages    Move… and Copy… in the ⋮ menu (15.3)
// No page is proposed for removal.

// Pictures only where they show something words cannot, such as where a
// control is, and taken in a narrow window: at about the width they are
// shown, their text is the size of the page's on a desktop and still
// readable on a phone (2026-09-23).
const NARROW = { width: 480, height: 900 }
const SPACE_FORM = [
  { wait: 2000 }, { click: '.row-gap .btn--primary' }, { wait: 400 },
  { type: 'TEAM', selector: 'form.card.form-inline label:nth-of-type(1) input' },
  { type: 'Team handbook', selector: 'form.card.form-inline label:nth-of-type(2) input' },
  { type: 'How we work, in one place', selector: 'form.card.form-inline label:nth-of-type(3) input' },
  { eval: 'document.activeElement && document.activeElement.blur()' },
  // The crop leaves room around the form for its labels; what is behind
  // that room (the heading, the space cards) is hidden rather than cut in half.
  { css: '.row-between, .space-grid { visibility: hidden !important; }' },
]

// Marks the menu's Save as template button, so a picture can box it.
const TAG_SAVE_TEMPLATE = "[...document.querySelectorAll('.overflow-menu__dropdown button')].find((b) => b.textContent.trim() === 'Save as template')?.setAttribute('data-shot', 'save-template')"

// Marks the menu's Move… and Copy… buttons, for the same reason.
const TAG_MOVE_COPY = "document.querySelectorAll('.overflow-menu__dropdown button').forEach((b) => { const t = b.textContent.trim(); if (t === 'Move…') b.setAttribute('data-shot', 'move'); if (t === 'Copy…') b.setAttribute('data-shot', 'copy') })"

/** A settings section by its heading (a Playwright selector, for clipTo only). */
const section = (title) => `section.profile__section:has(> h2:text-is("${title}"))`

// The desktop sidebar: taken in a desktop window, because a narrow one has
// no sidebar, and cropped to it, so it is still shown near its own size.
const HIDE_CONTENT = { css: '.space-content { visibility: hidden !important; }' }
// The first six rows of the tree are enough to show what it is; the rest,
// and the + New page button above it, are hidden so the crop cuts nothing
// in half.
const TREE_ROWS = (row) => ({ css: `aside.sidebar .tree > ${row}:nth-of-type(n+7), aside.sidebar .sidebar__top { visibility: hidden !important; }` })
// Opens a page's ⋮ menu, in the narrow window.
const OPEN_MENU = [{ wait: 2500 }, { click: 'button[title="More actions"]' }, { wait: 400 }]
// Opens one of the tabs under a page and brings it into view.
const OPEN_TAB = (tab) => [{ wait: 2500 }, { click: `article .tabs button:has-text("${tab}")` }, { wait: 800 }, { scrollTo: '.tab-panel' }]

/**
 * Tesria Demo needs a template for the "Start from a template" picture: the
 * menu only appears when there is one. Made once, if it is missing.
 *
 * And the Launch plan must have no emoji, or the "Add emoji" picture has no
 * Add emoji to show.
 */
export async function prepare({ lib, author, demoId }) {
  const launchPlan = demoId('Launch plan')
  const plan = await author.call('GET', `/api/pages/${launchPlan}`)
  if (plan.emoji) {
    await author.call('PUT', `/api/pages/${launchPlan}/emoji`, { emoji: null })
    console.log('  took the emoji off Launch plan in Tesria Demo')
  }

  const space = await author.call('GET', '/api/spaces/DEMO')
  const have = await author.call('GET', `/api/templates?spaceId=${space.id}`)
  if (have.some((t) => t.name === 'Meeting notes' && t.spaceId === space.id)) return {}
  const { doc, h, p, text, bold, panel, ul, li, tasks, task } = lib
  const content = doc(
    panel('info', p(text('Replace the hints in each section, then delete this box.', bold))),
    h(2, 'Attendees'), ul(li(p('Who was there?'))),
    h(2, 'Agenda'), ul(li(p('What will be discussed?'))),
    h(2, 'Decisions'), ul(li(p('What was agreed, and by whom?'))),
    h(2, 'Action items'), tasks(task(false, 'Who does what, by when?')),
  )
  await author.call('POST', '/api/templates', { spaceId: space.id, name: 'Meeting notes', description: 'Attendees, agenda, decisions and action items.', contentJson: JSON.stringify(content) })
  console.log('  made the Meeting notes template in Tesria Demo')
  return {}
}

export const shots = ({ demo }) => [
  // ---- What a space is: the sidebar, with the four things in it.
  {
    name: 'space-sidebar', url: '/spaces/DEMO', phone: false, steps: [{ wait: 2500 }, HIDE_CONTENT],
    clipTo: 'aside.sidebar', clipPad: 6,
    annotate: [
      { type: 'box', target: 'aside.sidebar .sidebar__head', pad: 3 },
      { type: 'box', target: 'aside.sidebar .sidebar__top .btn--block', pad: 3 },
      { type: 'box', target: 'aside.sidebar .tree-section', pad: 0 },
      { type: 'box', target: 'aside.sidebar .sidebar__foot a', pad: 3 },
    ],
  },

  // ---- Creating a space: where the button is, and the form.
  {
    name: 'space-new', url: '/spaces', viewport: NARROW, phone: false, settle: 800, steps: [{ wait: 2000 }],
    clipTo: ['.topbar', '.row-between'], clipPad: 0,
    annotate: [{ type: 'box', target: '.row-gap .btn--primary', pad: 5 }],
  },
  {
    name: 'space-form', url: '/spaces', viewport: NARROW, phone: false, steps: SPACE_FORM,
    clipTo: 'form.card.form-inline', clipPad: 40,
    annotate: [
      { type: 'box', target: 'form.card.form-inline label:nth-of-type(1)', pad: 5 },
      { type: 'box', target: 'form.card.form-inline label:nth-of-type(2)', pad: 5 },
      { type: 'box', target: 'form.card.form-inline button[type="submit"]', pad: 5 },
      { type: 'note', target: 'form.card.form-inline label:nth-of-type(1)', label: 'Cannot be changed later', dy: -30 },
    ],
  },

  // ---- The space home: where Watch this space is.
  {
    name: 'space-watch', url: '/spaces/DEMO', viewport: NARROW, phone: false,
    steps: [{ wait: 2500 }, { css: '.space-content .page-wrap > p, .space-home-tree { visibility: hidden !important; }' }],
    clipTo: '.space-content .page-wrap > .row-between', clipPad: 12,
    annotate: [{ type: 'box', target: '.space-content .page-wrap > .row-between .btn', pad: 4 }],
  },

  // ---- Who can see a space: the form that grants access. Only the form:
  // the list below it names whoever holds access to Tesria Demo.
  {
    name: 'space-permissions', url: '/spaces/DEMO/settings/permissions', viewport: NARROW, phone: false,
    steps: [{ wait: 2500 }, { css: '.tab-panel > p, .tab-panel .version-list { visibility: hidden !important; }' }],
    clipTo: '.tab-panel .card', clipPad: 12,
    annotate: [{ type: 'box', target: '.tab-panel .principal-picker', pad: 4 }],
  },

  // ---- Space icons: the Icon section of Space settings.
  {
    name: 'space-icon', url: '/spaces/DEMO/settings', viewport: NARROW, phone: false, steps: [{ wait: 2500 }],
    clipTo: section('Icon'), clipPad: 8,
    annotate: [
      { type: 'box', target: '.icon-picker__current .row-gap .btn', pad: 3 },
      { type: 'box', target: '.icon-picker__emoji', pad: 3 },
    ],
  },

  // ---- Archiving and deleting: the two buttons at the end of Details.
  {
    name: 'space-archive', url: '/spaces/DEMO/settings', viewport: NARROW, phone: false,
    steps: [{ wait: 2500 }, { scrollTo: '#archive' }],
    clipTo: ['#archive', '.danger-zone'], clipPad: 8,
    annotate: [
      { type: 'box', target: '#archive .btn', pad: 4 },
      { type: 'box', target: '.danger-zone .btn--danger', pad: 4 },
    ],
  },

  // ---- Creating a page: + New page in the sidebar, and Publish.
  {
    name: 'page-new-button', url: '/spaces/DEMO', phone: false,
    steps: [{ wait: 2500 }, { css: '.space-content, aside.sidebar .tree-section { visibility: hidden !important; }' }],
    clipTo: 'aside.sidebar .sidebar__top', clipPad: 10,
    annotate: [{ type: 'box', target: 'aside.sidebar .sidebar__top .btn--block', pad: 4 }],
  },
  {
    name: 'page-publish', url: '/spaces/DEMO/new', phone: false,
    waitFor: '.ProseMirror', steps: [{ wait: 2500 }],
    clipTo: '.page-actionbar--editor .page-actionbar__secondary', clipPad: 8,
    annotate: [{ type: 'box', target: 'button[form="page-editor-form"]', pad: 4 }],
  },
  // Closes that new page without saving, which discards its draft.
  { name: 'page-publish-closed', settle: 300, skipCapture: true, phone: false, steps: [{ click: '.page-actionbar button:has-text("Close")' }, { wait: 1200 }] },

  // ---- Templates: where Save as template is, its form, and where a new
  // page offers one. From a meeting page in Tesria Demo, whose space has a
  // Meeting notes template (prepare, above).
  {
    name: 'template-menu', url: demo('Kickoff, September 2'), viewport: NARROW, phone: false,
    steps: [{ wait: 2500 }, { click: 'button[title="More actions"]' }, { wait: 400 }, { eval: TAG_SAVE_TEMPLATE }],
    clipTo: ['.page-actionbar', '.overflow-menu__dropdown'], clipPad: 8,
    annotate: [{ type: 'box', target: '[data-shot="save-template"]', pad: 4 }],
  },
  {
    name: 'template-form', url: demo('Kickoff, September 2'), viewport: NARROW, phone: false,
    steps: [
      { wait: 2500 }, { click: 'button[title="More actions"]' }, { wait: 400 }, { eval: TAG_SAVE_TEMPLATE },
      { click: '[data-shot="save-template"]' }, { wait: 300 },
      { type: 'Meeting notes', selector: '.template-form input' },
      { eval: 'document.activeElement && document.activeElement.blur()' },
    ],
    clipTo: '.overflow-menu__dropdown', clipPad: 8,
    annotate: [{ type: 'box', target: '.template-form', pad: 4 }],
  },
  {
    name: 'template-pick', url: '/spaces/DEMO/new', viewport: NARROW, phone: false,
    waitFor: '.editor-form select', steps: [{ wait: 1500 }],
    clipTo: ['.editor-form label.change-comment'], clipPad: 12,
    annotate: [{ type: 'box', target: '.editor-form label.change-comment select', pad: 4 }],
  },
  // Closes that new page without saving, which discards its draft.
  { name: 'template-pick-closed', settle: 300, skipCapture: true, phone: false, steps: [{ click: '.page-actionbar button:has-text("Close")' }, { wait: 1200 }] },

  // ---- The page tree: the filter and the pencil, reorder mode, and the
  // tree style setting.
  {
    name: 'tree-tools', url: demo('Launch plan'), phone: false, steps: [{ wait: 2500 }, TREE_ROWS('a')],
    clipTo: ['aside.sidebar .tree-section__heading', 'aside.sidebar .tree > a:nth-of-type(6)'], clipPad: 10,
    annotate: [
      { type: 'box', target: 'aside.sidebar .tree-section__reorder', pad: 3 },
      { type: 'box', target: 'aside.sidebar .tree-filter__input', pad: 3 },
    ],
  },
  {
    name: 'tree-reorder', url: demo('Launch plan'), phone: false,
    steps: [{ wait: 2500 }, { click: 'aside.sidebar button[title="Reorder pages"]' }, { wait: 500 }, TREE_ROWS('div')],
    clipTo: ['aside.sidebar .tree-section__heading', 'aside.sidebar .tree > div:nth-of-type(6)'], clipPad: 10,
    annotate: [{ type: 'box', target: 'aside.sidebar .tree-section__actions', pad: 3 }],
  },
  {
    name: 'tree-style', url: '/spaces/DEMO/settings', viewport: NARROW, phone: false,
    steps: [{ wait: 2500 }, { scrollTo: '#page-tree' }],
    clipTo: '#page-tree', clipPad: 8,
    annotate: [{ type: 'box', target: '#page-tree .tree-style', pad: 4 }],
  },

  // ---- Page actions, and Moving and copying: the ⋮ menu, and the Move dialog.
  {
    name: 'page-menu', url: demo('Launch plan'), viewport: NARROW, phone: false, steps: OPEN_MENU,
    clipTo: ['.page-actionbar', '.overflow-menu__dropdown'], clipPad: 8,
    annotate: [{ type: 'box', target: 'button[title="More actions"]', pad: 3 }],
  },
  {
    name: 'page-move-menu', url: demo('Launch plan'), viewport: NARROW, phone: false,
    steps: [...OPEN_MENU, { eval: TAG_MOVE_COPY }],
    clipTo: ['.page-actionbar', '.overflow-menu__dropdown'], clipPad: 8,
    annotate: [
      { type: 'box', target: '[data-shot="move"]', pad: 3 },
      { type: 'box', target: '[data-shot="copy"]', pad: 3 },
    ],
  },
  {
    name: 'page-move-dialog', url: demo('Launch plan'), viewport: NARROW, phone: false,
    steps: [...OPEN_MENU, { eval: TAG_MOVE_COPY }, { click: '[data-shot="move"]' }, { wait: 1200 }],
    clipTo: '.move-copy .recovery-prompt__card', clipPad: 12,
    annotate: [
      { type: 'box', target: '.move-copy label', nth: 0, pad: 4 },
      { type: 'box', target: '.move-copy label', nth: 1, pad: 4 },
    ],
  },

  // ---- Page emoji: Add emoji on the title, and the picker it opens.
  // Everything below the title is hidden: the crop's margin would cut it.
  {
    name: 'emoji-add', url: demo('Launch plan'), viewport: NARROW, phone: false,
    steps: [{ wait: 2500 }, { css: '.paper > .page-head ~ * { visibility: hidden !important; }' }, { hover: '.paper > .page-head h1' }, { wait: 300 }],
    clipTo: '.paper > .page-head', clipPad: 16,
    annotate: [{ type: 'box', target: '.page-emoji__add', pad: 4 }],
  },
  {
    name: 'emoji-picker', url: demo('Launch plan'), viewport: NARROW, phone: false,
    steps: [
      { wait: 2500 }, { css: '.paper > .page-head ~ * { visibility: hidden !important; }' },
      { hover: '.paper > .page-head h1' }, { wait: 300 }, { click: '.page-emoji__add' }, { wait: 400 },
      { eval: 'document.activeElement && document.activeElement.blur()' },
    ],
    clipTo: ['.paper > .page-head', '.page-emoji__panel'], clipPad: 12,
  },

  // ---- Labels: adding one. The labels row is as wide as the page and
  // mostly empty, so it is shrunk to its chips for the picture.
  {
    name: 'labels-add', url: demo('Launch plan'), viewport: NARROW, phone: false,
    steps: [
      { wait: 2500 },
      { css: 'article div.labels { width: fit-content } .paper > p.muted, .page-body { visibility: hidden !important; }' },
      { click: 'article .labels > button.link-btn' }, { wait: 300 },
      { type: 'how-to', selector: 'article .label-add input' }, { wait: 200 },
    ],
    clipTo: 'article div.labels', clipPad: 16,
    annotate: [{ type: 'box', target: 'article .label-add', pad: 3 }],
  },

  // ---- Attachments: the tab, and Upload file.
  {
    name: 'attachments-upload', url: demo('Image'), viewport: NARROW, phone: false, steps: OPEN_TAB('Attachments'),
    clipTo: ['article .tabs', '.tab-panel'], clipPad: 12,
    annotate: [{ type: 'box', target: '.tab-panel .upload-btn', pad: 3 }],
  },

  // ---- History: two versions ticked, and what Compare shows. The Launch
  // plan has two versions, by two people (seed-demo.mjs).
  {
    name: 'history-compare', url: demo('Launch plan'), viewport: NARROW, phone: false,
    steps: [...OPEN_TAB('History'), { click: '.version__pick >> nth=0' }, { click: '.version__pick >> nth=1' }, { wait: 300 }],
    clipTo: ['article .tabs', '.tab-panel'], clipPad: 12,
    annotate: [
      { type: 'box', target: '.version__pick', nth: 0, pad: 3 },
      { type: 'box', target: '.version__pick', nth: 1, pad: 3 },
      { type: 'box', target: '.history > p .btn', pad: 3 },
    ],
  },
  {
    name: 'history-diff', url: demo('Launch plan'), viewport: NARROW, phone: false,
    steps: [
      ...OPEN_TAB('History'), { click: '.version__pick >> nth=0' }, { click: '.version__pick >> nth=1' }, { wait: 300 },
      { click: '.history > p .btn' }, { wait: 1000 }, { scrollTo: '.version-preview' },
    ],
    clipTo: '.version-preview', clipPad: 8,
  },

  // ---- Restrictions: the tab and its form.
  {
    name: 'restrictions-form', url: demo('Launch plan'), viewport: NARROW, phone: false, steps: OPEN_TAB('Restrictions'),
    clipTo: ['article .tabs', '.tab-panel'], clipPad: 12,
    annotate: [{ type: 'box', target: '.tab-panel .principal-picker', pad: 3 }],
  },

  // ---- Trash: where it is in Space settings.
  {
    name: 'trash-tab', url: '/spaces/DEMO/settings/trash', viewport: NARROW, phone: false, steps: [{ wait: 2500 }],
    clipTo: ['.space-content .page-wrap > h1', '.space-content .tab-panel'], clipPad: 12,
    annotate: [{ type: 'box', target: '.space-content .tabs a[href$="/trash"]', pad: 3 }],
  },
]

export async function build({ top, page, ensure, doc, p, h, text, bold, italic, code, ul, ol, li, panel, live, picture, pageLink, adminAt }) {
  const manual = top['User manual']
  const b = (t) => text(t, bold)
  const c = (t) => text(t, code)
  const i = (t) => text(t, italic)
  /** A numbered step: a heading that says what to do, then how. */
  const step = (n, title) => h(3, `Step ${n}: ${title}`)

  // Every page first, so a link from one to another works on the first run,
  // whichever is written first. A new page lands at the end; cleanup() puts
  // it in its place.
  const spaces = await ensure('Spaces', manual)
  const what = await ensure('What a space is', spaces)
  const creating = await ensure('Creating a space', spaces)
  const home = await ensure('The space home and watching', spaces)
  const who = await ensure('Who can see a space', spaces)
  const icons = await ensure('Space icons', spaces)
  const archive = await ensure('Archiving and deleting a space', spaces)
  const pagesSection = await ensure('Pages', manual)
  const newPage = await ensure('Creating a page', pagesSection)
  const templates = await ensure('Templates', pagesSection)
  await ensure('Drafts, Publish and Update', pagesSection)
  const tree = await ensure('The page tree and reordering', pagesSection)
  const actions = await ensure('Page actions', pagesSection)
  const moving = await ensure('Moving and copying pages', pagesSection)
  const emoji = await ensure('Page emoji', pagesSection)
  const labels = await ensure('Labels', pagesSection)
  const attachments = await ensure('Attachments', pagesSection)
  const history = await ensure('History and restoring', pagesSection)
  const restrictions = await ensure('Restrictions', pagesSection)
  const trash = await ensure('Trash', pagesSection)

  // ================================================================ Spaces
  await page('Spaces', manual, doc(
    p('A space is where a set of pages lives: one team’s notes, one project’s plans, one handbook. Each has its own page tree, its own people and its own settings. These pages cover making a space, deciding who can see it, and looking after it as it grows.'),
    live('children', { depth: '1', sort: 'position' }),
  ))

  // ------------------------------------------------------- What a space is
  await page('What a space is', spaces, doc(
    p('Every page in Tesria lives in exactly one ', b('space'), '. If Tesria is a library, a space is one of its sections: the pages in it belong together, the same people read and write them, and they are looked after as a whole. Who can see a space, which templates it offers and what is in its trash are all set for the space, so you decide them once rather than page by page.'),

    h(2, 'What every space has'),
    ul(
      li(p(b('A key:'), ' a short code such as ', c('DEMO'), ' or ', c('TEAM'), '. It is part of the address of every page in the space, as in ', c('/spaces/TEAM/…'), ', so it never changes.')),
      li(p(b('A name and a description:'), ' what everyone sees in lists, such as ', i('Team handbook'), ', and a line on what the space is for. Both can be changed at any time.')),
      li(p(b('An icon,'), ' so people recognize the space at a glance. See ', pageLink('Space icons'), '.')),
      li(p(b('A page tree:'), ' the pages, arranged under one another like the chapters and sections of a book. See ', pageLink('The page tree and reordering'), '.')),
      li(p(b('A home page:'), ' what you see when you open the space without choosing a page. See ', pageLink('The space home and watching'), '.')),
      li(p(b('Its own settings:'), ' who can see it, its templates, its webhooks and its trash, all under ', b('Space settings'), '.')),
    ),

    h(2, 'Finding your way around a space'),
    p('Open a space from ', b('Spaces'), ' at the top of any page. On a computer, the space’s sidebar runs down the left of every page in it. From the top:'),
    ...(await picture(what, 'space-sidebar', 'The sidebar of a space, with its four parts boxed', 'A space’s sidebar: its name, + New page, the page tree and Space settings.')),
    ol(
      li(p(b('The space itself:'), ' its icon, key and name. A ', b('public'), ' badge beside the key means anyone on the internet can read it; see ', pageLink('Public reading'), '.')),
      li(p(b('+ New page,'), ' which starts a new page. See ', pageLink('Creating a page'), '.')),
      li(p(b('The page tree,'), ' under ', b('Pages'), ', with every page you can see.')),
      li(p(b('Space settings,'), ' at the bottom: details, icon, permissions, templates, webhooks and trash.')),
    ),
    p('The button beside the space’s name hides the sidebar when you want the whole window for reading; the button left in the narrow strip down the side brings it back. Drag the sidebar’s right edge to make it wider or narrower, and double-click the edge to return it to its usual width.'),
    panel('note', p(b('On a phone there is no sidebar.'), ' The space’s home page lists its pages instead, and the bar at the top of the home page has ', b('+ New'), ' and a menu with ', b('Space settings'), '.')),

    h(2, 'The Spaces page'),
    p(b('Spaces'), ' at the top of every page lists every space you can see, in alphabetical order, each with its icon, key, name and description. Spaces you have no access to are not listed at all, and neither are archived ones; see ', pageLink('Archiving and deleting a space'), '. To make a new space, see ', pageLink('Creating a space'), '.'),
  ))

  // ---------------------------------------------- Creating a space (approved)
  await page('Creating a space', spaces, doc(
    p('A ', b('space'), ' is a home for a set of pages that belong together, like a binder on a shelf. Each space has its own page tree, its own home page, and its own list of who can read and edit it. This page helps you decide what deserves a space of its own, then walks you through making one.'),

    h(2, 'What a space is for'),
    p('Give something its own space when it has its own audience: a group of people who read and write it together, and who you might want to give different access from everyone else. Some common ways teams divide things up:'),
    ul(
      li(p(b('By team,'), ' such as Engineering, Marketing or People: each team owns its own processes and notes.')),
      li(p(b('By project,'), ' such as Kestrel launch or Office move: the work has a start and an end, and people from several teams join in.')),
      li(p(b('By audience,'), ' such as Team handbook or Customer help: the same material is read by people who should not see everything else.')),
    ),
    panel('success', p(b('Start with fewer, larger spaces.'), ' It is easier to find things in three well-organized spaces than in twenty small ones, and a page can be moved to another space later with everything under it.')),

    h(2, 'Before you start'),
    p('You need the right to create spaces. Everyone has it by default; if you do not see a ', b('New space'), ' button in step 1, an administrator has turned it off for your role, and they can create the space for you.'),

    step(1, 'Open Spaces and choose New space'),
    p('Choose ', b('Spaces'), ' at the top of any page, then ', b('New space'), ' at the top right.'),
    ...(await picture(creating, 'space-new', 'The Spaces page with the New space button', 'The Spaces page. New space is at the top right.')),

    step(2, 'Give it a key and a name'),
    p('A short form opens above the list.'),
    ...(await picture(creating, 'space-form', 'The new space form, filled in', 'Key and name are required; the description is optional. Create is at the end.')),
    ul(
      li(p(b('Key:'), ' a short code for the space, such as TEAM or ENG2: two to 50 letters and digits, starting with a letter. It becomes part of every page’s address, as in /spaces/TEAM, so it cannot be changed later. Tesria makes it capitals as you type.')),
      li(p(b('Name:'), ' what everyone sees in lists and at the top of the space, such as Team handbook. You can change it later in the space’s settings.')),
      li(p(b('Description:'), ' optional. One line on what the space is for, shown on the Spaces page to help people pick the right one.')),
    ),

    step(3, 'Choose Create'),
    p('It is the button at the end of the form, boxed in the picture above. The space appears in the list. Open it to find an empty home page with a ', b('Create the first one'), ' button.'),

    h(2, 'What a new space starts with'),
    ul(
      li(p(b('Open to everyone signed in.'), ' Anyone with an account can read it, edit it and change its settings.')),
      li(p(b('Not public.'), ' People who are not signed in cannot see it.')),
      li(p(b('An icon made from its key,'), ' which you can change to an emoji or a picture.')),
      li(p(b('Every export allowed:'), ' PDF, Markdown, HTML, a static site and a wiki pack.')),
    ),
    panel('warning', p(b('Want it private?'), ' A new space is open to everyone signed in, which is right for most team spaces. For anything that should be seen by only some people, such as salaries or a confidential project, limit it straight away, before you write anything in it. See ', pageLink('Who can see a space'), '.')),

    h(2, 'Next steps'),
    ul(
      li(p(b('Write the first page.'), ' A good first page says what the space is for and who looks after it. See ', pageLink('Creating a page'), '.')),
      li(p(b('Give it an icon'), ' so people recognize it at a glance. See ', pageLink('Space icons'), '.')),
      li(p(b('Decide who can see it.'), ' See ', pageLink('Who can see a space'), '.')),
    ),
  ))

  // ------------------------------------------ The space home and watching
  await page('The space home and watching', spaces, doc(
    p('Every space has a home: the page you land on when you open the space without choosing a page in it. It shows the space’s name, its description and its contents, and it is where you choose to be told about everything that happens in the space.'),

    h(2, 'The home page'),
    ul(
      li(p(b('The name and description'), ' at the top. Change them in ', b('Space settings'), ', on the ', b('Details'), ' tab.')),
      li(p(b('Contents,'), ' on a computer: each top-level page and the pages directly under it, numbered the way the sidebar numbers them. Choose one to open it. An exported website’s front page shows the same list.')),
      li(p(b('On a phone,'), ' the whole page tree instead, because a phone has no sidebar to show it in. You can reorder pages there too.')),
      li(p(b('A new space'), ' has no pages yet, and says so, with a ', b('Create the first one'), ' link.')),
    ),
    p('To get back to the home page from anywhere in the space, choose the space’s name at the start of the breadcrumb above the page.'),

    h(2, 'Watching a space'),
    p('Watching a space is for when you want to keep up with it without checking it every day: a team lead following the team’s space, say, or someone who looks after a handbook and wants to see every change to it.'),
    p('While you watch a space, you are told about:'),
    ul(
      li(p(b('every new page'), ' in it,')),
      li(p(b('every update'), ' to a page in it, and')),
      li(p(b('every new comment'), ' on a page in it.')),
    ),
    p('The news arrives in the bell at the top of Tesria, and by email too if you have turned that on (see ', pageLink('Email notifications'), '). You are never told about your own changes, and never about a page you cannot see.'),

    step(1, 'Open the space’s home'),
    p('Choose the space on the ', b('Spaces'), ' page, or its name at the start of the breadcrumb.'),
    step(2, 'Choose Watch this space'),
    p('The button is at the top right, beside the space’s name.'),
    ...(await picture(home, 'space-watch', 'The Watch button on a space’s home page', 'Watch this space is beside the space’s name.')),
    p('The button then reads ', b('Watching'), '. Choose it again to stop.'),

    panel('success', p(b('Only need one page?'), ' Watch just that page instead, from ', b('Watch this page'), ' in its ⋮ menu. See ', pageLink('Watching'), '.')),
  ))

  // -------------------------------------------------- Who can see a space
  await page('Who can see a space', spaces, doc(
    p('Most spaces are for everyone in your organization, and a new space starts that way. Some should be seen by only a few people: salaries, a confidential project, a team’s private planning. This page explains how a space decides who gets in, and how to give or take away access.'),
    p('Everything here is done in ', b('Space settings'), ', on the ', b('Permissions'), ' tab, and needs administrator access to the space.'),

    h(2, 'Open and private spaces'),
    ul(
      li(p(b('Open'), ' is how every space starts. Nobody is listed, and everyone signed in can read it, edit it and change its settings.')),
      li(p(b('Private'), ' is a space with anyone at all listed on its Permissions tab. From then on, only the people and groups listed can get in.')),
    ),
    panel('warning', p(b('Adding the first person makes the space private.'), ' The moment you grant anyone access, everyone who is not listed loses it. Tesria adds you as an administrator at the same time, so you cannot lock yourself out. If you want to keep everyone reading while you limit who edits, grant ', b('View'), ' to the ', b('Users'), ' group as well (see below).')),
    p('Whether a space is open or private, people who are not signed in never see it, unless an administrator publishes it for public reading. See ', pageLink('Public reading'), '.'),

    h(2, 'The three levels of access'),
    p('Each level includes everything the ones before it allow.'),
    ul(
      li(p(b('View:'), ' read the pages and their files, and join in the comments.')),
      li(p(b('Edit:'), ' also create, change, move and delete pages, add labels and files, and manage the space’s templates.')),
      li(p(b('Admin:'), ' also change the space’s settings (its name, icon and page tree), its permissions and its webhooks, archive it, and delete pages from its trash for good.')),
    ),

    h(2, 'Giving someone access'),
    step(1, 'Open the Permissions tab'),
    p('Choose ', b('Space settings'), ' at the bottom of the space’s sidebar, then the ', b('Permissions'), ' tab.'),
    step(2, 'Choose who'),
    p('In the box, choose ', b('User'), ' or ', b('Group'), ', then the person or group from the list next to it.'),
    ...(await picture(who, 'space-permissions', 'The form for granting access to a space', 'Choose a user or group, the person or group, and the level, then Grant.')),
    step(3, 'Choose the level, then Grant'),
    p('Choose ', b('View'), ', ', b('Edit'), ' or ', b('Admin'), ', and then ', b('Grant'), '. They appear in the list below the box, and have access straight away.'),

    h(2, 'Sharing with everyone, or with a team'),
    p('Rather than adding people one at a time, grant access to a group. Three groups exist on every Tesria and keep themselves up to date as people join, leave and change roles:'),
    ul(
      li(p(b('Users:'), ' everyone with an account. Granting ', b('View'), ' to Users lets everyone signed in read a private space.')),
      li(p(b('Admins:'), ' Tesria’s administrators, and its owner.')),
      li(p(b('Owner:'), ' the person who owns this Tesria.')),
    ),
    p('An administrator can also make groups of their own, such as ', i('Finance'), ' or ', i('Launch team'), '. See ', pageLink('Groups'), '.'),

    h(2, 'Taking access away'),
    p('Choose ', b('Revoke'), ' beside anyone in the list. The last administrator of a space cannot be removed, because nobody would be left to manage it.'),

    h(2, 'Making a private space open again'),
    p('Because the last administrator always stays, a private space cannot be emptied one person at a time. To go back to open, use the link made for it:'),
    ol(
      li(p('On the Permissions tab, choose ', b('Make this space open again'), '. It is in the line above the box, and only there while the space is private.')),
      li(p('Read what will happen and choose ', b('Make it open'), '. If you have not entered your password in the last few minutes, Tesria asks for it again.')),
    ),
    p('Every grant is removed at once, and everyone signed in can view, edit and administer the space again. Restrictions on single pages stay as they are. Because this opens everything in the space to everyone, Tesria’s administrators are alerted each time it is done.'),

    h(2, 'Administrators and private spaces'),
    p('Being an administrator of Tesria does not let you into every private space. If a private space has lost all its administrators, a Tesria administrator can give themselves access with ', b('Get access'), ' in ', ...adminAt('Spaces'), ', and that is recorded in the audit log. See ', pageLink('Spaces (administration)'), '.'),
    p('To keep one page, rather than a whole space, to a few people, see ', pageLink('Restrictions'), '.'),
  ))

  // --------------------------------------------------------- Space icons
  await page('Space icons', spaces, doc(
    p('A space’s icon appears wherever the space does: on the Spaces page, at the top of its sidebar and in the breadcrumb above its pages. A good icon lets people tell spaces apart at a glance, which matters more as their number grows. Every space starts with the first letter of its key on a colored tile; you can change it to an emoji or a picture of your own.'),
    p('Changing the icon needs administrator access to the space. In an open space that is everyone signed in.'),

    step(1, 'Open Space settings'),
    p('Choose ', b('Space settings'), ' at the bottom of the space’s sidebar. The ', b('Icon'), ' section is the first thing on the ', b('Details'), ' tab, with the current icon beside it.'),
    ...(await picture(icons, 'space-icon', 'The Icon section of Space settings', 'Upload picture, and the emoji to choose from, are boxed.')),

    step(2, 'Choose the new icon'),
    p('Whatever you choose is saved straight away; there is no Save button. You have three kinds to choose from:'),
    ul(
      li(p(b('A picture of your own:'), ' choose ', b('Upload picture'), ' and pick a PNG, JPEG or WebP file. Tesria trims it to a square from the middle, so a logo with space around it works best. Once there is one, the button reads ', b('Replace picture'), '.')),
      li(p(b('An emoji:'), ' choose one under ', b('Or pick an emoji'), '. For one that is not offered, paste it into ', b('Any other emoji'), ' and choose ', b('Use it'), '. It has to be a single emoji, not letters.')),
      li(p(b('A different tile color:'), ' choose a color under ', b('Tile color'), '. It changes the tile behind the letter or the emoji. A picture has no tile, so remove the picture first to choose one.')),
    ),

    h(2, 'Going back to the letter'),
    p(b('Use the default'), ', beside the upload button, takes away the emoji or the picture and puts the letter tile back.'),
  ))

  // ---------------------------------------- Archiving and deleting a space
  await page('Archiving and deleting a space', spaces, doc(
    p('When a project ends or a team moves on, its space can go one of two ways. ', b('Archiving'), ' puts it out of the way and keeps everything, and can be undone at any time. ', b('Deleting'), ' destroys it and everything in it, for good. If you are not sure, archive.'),
    p('Both are at the end of ', b('Space settings'), ', on the ', b('Details'), ' tab.'),
    ...(await picture(archive, 'space-archive', 'Archive this space and Delete this space in Space settings', 'Archive this space, and Delete this space in the Danger zone below it.')),

    h(2, 'Archiving a space'),
    p('An archived space is hidden from the Spaces page, and from public reading if it was published. Nothing in it is changed or removed: people with access can still open it at its address or from a bookmark, and ', ...adminAt('Spaces'), ' lists it with an ', i('archived'), ' badge.'),
    step(1, 'Open Space settings'),
    p('Choose ', b('Space settings'), ' at the bottom of the space’s sidebar, and scroll to ', b('Archive'), '.'),
    step(2, 'Choose Archive this space'),
    p('It happens at once, with no questions: it is easy to undo. To bring the space back, open its settings the same way and choose ', b('Unarchive this space'), '.'),
    p('Any administrator of the space can archive it or bring it back.'),

    h(2, 'Deleting a space'),
    panel('error', p(b('Deleting a space cannot be undone from inside Tesria.'), ' Every page in it goes, with every version, comment, attachment and restriction, and so does everything in its trash. Only a backup taken beforehand still holds any of it.')),
    p('Deleting is only offered to people whose role may delete spaces, which is Tesria’s administrators unless your roles were changed. For everyone else there is no Danger zone.'),
    step(1, 'Choose Delete this space'),
    p('It is in the ', b('Danger zone'), ', the last section of the Details tab.'),
    step(2, 'Read what will go'),
    p('The dialog counts the pages and attachments that will be destroyed, including the ones already in the trash. If the space is published for public reading, it warns that its public pages stop working at once.'),
    step(3, 'Type the key and your password'),
    p('Type the space’s key exactly as shown, in capitals, to show you have the right space. Then enter your password. If you sign in through your organization’s single sign-on and have no Tesria password, enter a code from your authenticator app instead. A wrong password counts toward locking your account, as it does at sign-in.'),
    step(4, 'Choose Delete'),
    p('The button names the key, such as ', b('Delete TEAM'), '. Tesria takes you to the Spaces page and confirms the space was deleted.'),
    panel('success', p(b('Keep a copy first.'), ' Before deleting a space you might ever want again, export it as a wiki pack, which holds its pages, history and files and can be imported into any Tesria. See ', pageLink('Wiki packs'), '.')),
  ))

  // ================================================================= Pages
  await page('Pages', manual, doc(
    p('Pages are what a space is made of. These pages cover writing them, arranging them, finding them again, and keeping them safe: every change is kept, and a deleted page waits in the trash until someone removes it for good.'),
    live('children', { depth: '1', sort: 'position' }),
  ))

  // ------------------------------------------------------ Creating a page
  await page('Creating a page', pagesSection, doc(
    p('A page is one document in a space: a meeting’s notes, a how-to, a project plan. Pages can sit under other pages, so a space can be organized like a book, with chapters and the sections inside them. This page walks you through making one.'),

    step(1, 'Go to where the page belongs'),
    p('Where the new page goes depends on where you are when you start it:'),
    ul(
      li(p(b('On a page:'), ' the new page goes under it, as a sub-page. To add a meeting to a “Meeting notes” page, open “Meeting notes” first.')),
      li(p(b('On the space’s home, or its settings:'), ' the new page goes at the top level of the space.')),
    ),
    p('You can always move it later; see ', pageLink('Moving and copying pages'), '.'),

    step(2, 'Choose + New page'),
    p('On a computer, it is near the top of the space’s sidebar. On a phone, it is ', b('+ New'), ', in the bar above the page.'),
    ...(await picture(newPage, 'page-new-button', 'The + New page button in the sidebar', '+ New page, under the space’s name.')),

    step(3, 'Give it a title'),
    p('Type the title in the ', i('Page title'), ' box at the top. Press ', b('Enter'), ' to move down to the body. If the space has templates, you can start from one instead of a blank page; see ', pageLink('Templates'), '.'),

    step(4, 'Write'),
    p('Type as you would anywhere else. To add something other than text, such as a table, a picture or a colored panel, type ', c('/'), ' on a new line and choose from the list. See ', pageLink('The slash menu'), ' and ', pageLink('The editor'), '.'),

    step(5, 'Choose Publish'),
    p(b('Publish'), ' is at the top right, above the page.'),
    ...(await picture(newPage, 'page-publish', 'The Publish button above a new page', 'Publish, beside Close.')),
    p('Until you publish, the page is a draft that only you can see. A page needs a title before it can be published. After that, anyone who can see the space can read it, and anyone watching the space is told about it.'),

    h(2, 'Changing your mind'),
    p(b('Close'), ', beside Publish, throws the new page away. For what happens to your work when you leave the editor, and when you change a page later, see ', pageLink('Drafts, Publish and Update'), '.'),
  ))

  // ------------------------------------------------- Templates (approved)
  // How to make a template was hard to find (2026-09-23): it was one
  // paragraph inside Creating a page.
  await page('Templates', pagesSection, doc(
    p('A ', b('template'), ' is a page that new pages start from. Instead of a blank page, whoever creates one gets your headings, your tables and your hints already in place, and only has to fill them in. Templates keep pages that should look alike looking alike, and save everyone from copying the last one and deleting its contents.'),
    p('Good candidates are pages your team writes again and again:'),
    ul(
      li(p(b('Meeting notes:'), ' attendees, agenda, decisions, action items.')),
      li(p(b('Project brief:'), ' goal, people, timeline, open questions.')),
      li(p(b('Incident report:'), ' what happened, impact, cause, what changes now.')),
      li(p(b('How-to guide:'), ' what you will need, the steps, and what to do if it goes wrong.')),
    ),

    h(2, 'Making a template'),
    p('A template is made from a page, so start by writing one.'),
    step(1, 'Write the page new ones should start as'),
    p('Put in the headings, tables and lists every page of this kind needs, and write hints where the details go, such as “Owner: who?” or “What was agreed, and by whom?”. An ', b('Info panel'), ' at the top is a good place for instructions the writer should delete once they have filled the page in. You can save the page as usual, or keep it as a draft.'),
    step(2, 'Choose Save as template'),
    p('On that page, open the ', b('⋮'), ' menu at the top right and choose ', b('Save as template'), '.'),
    ...(await picture(templates, 'template-menu', 'The page menu with Save as template', 'Save as template is in the page’s ⋮ menu.')),
    step(3, 'Name it and choose where it is offered'),
    p('Give the template a name people will recognize when they create a page, such as ', i('Meeting notes'), '. Then choose where it is offered, and choose ', b('Save'), '.'),
    ...(await picture(templates, 'template-form', 'Naming a template', 'The name, and where the template is offered.')),
    ul(
      li(p(b('This space only'), ' offers it when someone creates a page in this space. Most templates belong here.')),
      li(p(b('Instance-wide'), ' offers it in every space. Use it for something the whole organization shares, such as an incident report. This choice appears only for people with the right ', b('Manage instance-wide templates'), ', which administrators have; everyone else saves templates for their space.')),
    ),
    p('The template is a copy of the page as it is now. Changing the page later does not change the template; save it as a template again if you want the new version.'),

    h(2, 'Starting a page from a template'),
    p('Create a page as usual, with ', b('+ New page'), '. Above the title, ', b('Start from a template (optional)'), ' lists the space’s templates and the instance-wide ones. Choose one and the page fills in with it; then give it a title and write.'),
    ...(await picture(templates, 'template-pick', 'Choosing a template for a new page', 'Start from a template appears above the title of a new page.')),
    p('This menu only appears when there is at least one template to offer. If you have already written something, Tesria asks first, because the template replaces everything on the page so far.'),

    h(2, 'Renaming and deleting templates'),
    p('Every template offered in a space is listed in ', b('Space settings, Templates'), ', the space’s own first, then the instance-wide ones. ', b('Rename'), ' changes its name and description; ', b('Delete'), ' stops it being offered. Pages already made from it are not changed either way.'),
    p('Who may rename or delete one:'),
    ul(
      li(p(b('A space’s template:'), ' anyone who can edit that space.')),
      li(p(b('An instance-wide template:'), ' anyone with ', b('Manage instance-wide templates'), ', which administrators have.')),
    ),
  ))

  // ---------------------------------------------- Drafts, Publish and Update
  await page('Drafts, Publish and Update', pagesSection, doc(
    p('Nothing you write reaches anyone else until you say so. A new page stays a private draft until you publish it, and changes to an existing page only appear when you update it. That way you can write, rethink and tidy up in peace, and readers only ever see finished work.'),

    h(2, 'A new page: Publish'),
    p('While you write a new page, it is a draft that only you can see; it is not in the page tree and it does not turn up in search. ', b('Publish'), ', at the top right, makes it a real page:'),
    ul(
      li(p('It becomes ', b('version 1'), ' in the page’s history.')),
      li(p('Anyone watching the space is told about it.')),
      li(p('Anyone you mentioned with ', c('@'), ' is told they were mentioned.')),
    ),

    h(2, 'Changing a page: Edit and Update'),
    p('To change a published page, choose ', b('Edit'), ' above it. When you are done, the button at the top right says ', b('Update'), ' instead of Publish. Each update adds a new version to the page’s history, so nothing that was there before is lost; see ', pageLink('History and restoring'), '.'),
    p('Below the page, ', b('What changed? (optional)'), ' takes a few words about the change, such as ', i('fixed the dates'), ' or ', i('added the budget'), '. They are shown beside the version in the history, which makes it much easier to find a change later.'),

    h(2, 'Leaving the editor'),
    ul(
      li(p(b('Close,'), ' beside Publish or Update, leaves without saving. On a new page it throws the draft away. On a page you are changing, it goes back to the page as it was.')),
      li(p(b('Leaving any other way,'), ' such as a link, the back button or another page in the tree, stops to ask what you want: ', b('Publish and leave'), ' (or ', b('Update and leave'), '), ', b('Discard page'), ' (or ', b('Leave unpublished'), '), or ', b('Stay in the editor'), '.')),
      li(p(b('Closing the tab or the browser'), ' makes the browser ask whether you really want to leave.')),
    ),
    panel('info', p(b('Changes kept for later.'), ' Where several people can edit a page at once, changes you have not published yet are kept in a shared draft when you leave, and are waiting for you the next time you edit. See ', pageLink('Editing at the same time'), '.')),

    h(2, 'If the page changed while you were editing'),
    p('If someone else updated the page after you started, Tesria does not overwrite their work. It stops, highlights the difference in the editor, and asks you to accept or reject it; then choose ', b('Update'), ' again.'),
  ))

  // ------------------------------------------ The page tree and reordering
  await page('The page tree and reordering', pagesSection, doc(
    p('The page tree, in the space’s sidebar, is the space’s table of contents. Pages can sit under other pages, as sections sit inside chapters, and the tree shows them that way, indented under the page they belong to. It lists every page in the space that you can see; a page you have no access to is left out, with everything under it.'),
    p('Choose any page in the tree to open it. On a phone, the tree is on the space’s home page.'),

    h(2, 'Finding a page in a long tree'),
    p('Type in ', b('Filter pages'), ', just above the tree, and the tree shows only the pages whose titles match, with the pages above them (faded) so you can see where they sit.'),
    ...(await picture(tree, 'tree-tools', 'The Filter pages box and the reorder pencil above the tree', 'Filter pages, and the pencil that starts reordering.')),
    ul(
      li(p(b('Enter'), ' opens the first match, and ', b('Esc'), ' clears the filter.')),
      li(p('The button beside the box also shows the pages under each match. It starts on; choose it to see only the matches themselves.')),
      li(p('The filter stays while you move from page to page, so the next result is one click away.')),
    ),

    h(2, 'Rearranging pages'),
    p('As a space grows, pages end up in the wrong order, or belong under a different page. Reorder mode lets you drag them where they should be and save the result in one go.'),
    step(1, 'Choose the pencil beside Pages'),
    p('It is at the top of the tree, boxed in the picture above. The pages stop being links while you reorder, so a stray click cannot take you away from unsaved changes.'),
    step(2, 'Drag pages where they belong'),
    ul(
      li(p(b('Up or down'), ' changes a page’s place among its neighbors.')),
      li(p(b('To the right'), ' puts it under the page above it, as a sub-page.')),
      li(p(b('To the left'), ' moves it out a level.')),
    ),
    p('A line shows where the page will land. Its sub-pages always go with it. You can make as many moves as you like before saving.'),
    step(3, 'Choose Save, or Cancel'),
    p(b('Save'), ' keeps every move; it shows how many you have made. ', b('Cancel'), ' puts everything back as it was.'),
    ...(await picture(tree, 'tree-reorder', 'The tree in reorder mode, with Save and Cancel', 'While reordering, Cancel and Save replace the pencil.')),
    p('Moving a page needs edit rights on it and on the page it goes under. A page cannot go under one of its own sub-pages. Moving does not add a version to the page’s history, and nobody is notified.'),
    p('To move a page to another space, or to copy one, see ', pageLink('Moving and copying pages'), '.'),

    h(2, 'Numbers or bullets beside each page'),
    p('A space whose pages are read in order, such as a manual or a course, is easier to follow when its tree is numbered: 1, 1.1, 1.2, 2, like the contents of a book. The tree of these docs is numbered that way. A space can have numbers, bullets, or neither.'),
    step(1, 'Open Space settings'),
    p('Choose ', b('Space settings'), ' at the bottom of the space’s sidebar, and scroll to ', b('Page tree'), ' on the ', b('Details'), ' tab.'),
    step(2, 'Choose a style'),
    p('Each choice shows a small preview, and is saved as soon as you choose it.'),
    ...(await picture(tree, 'tree-style', 'The three page tree styles in Space settings', 'Plain, Numbered or Bulleted.')),
    ul(
      li(p(b('Plain:'), ' titles only. Every space starts this way.')),
      li(p(b('Numbered:'), ' outline numbers, such as 1, 1.1, 1.2 and 2.')),
      li(p(b('Bulleted:'), ' a bullet that changes with the level: •, then ◦, then ▪.')),
    ),
    p('The numbers and bullets are drawn beside the titles, never written into them. They are not part of any title or address, so links and search are unaffected, and they follow the tree by themselves: add, move or reorder a page and every number after it adjusts. While you reorder, they follow each drag before you save. A space exported as a website is numbered the same way.'),
    p('Changing the style needs administrator access to the space.'),
    panel('success', p(b('Numbers that never move?'), ' To mark a few pages with a fixed number instead, give each one a number emoji. See ', pageLink('Page emoji'), '.')),
  ))

  // --------------------------------------------- Moving and copying pages
  await page('Moving and copying pages', pagesSection, doc(
    p('Pages do not always stay where they started. A page written in the wrong place, or one that now belongs to another team, can be ', b('moved'), ' anywhere in the space or to another space, with everything under it. And when a new page should start as a copy of an existing one, such as last year’s event plan, you can ', b('copy'), ' it, on its own or with its sub-pages.'),
    p('Both are in the page’s ', b('⋮'), ' menu, at the top right.'),
    ...(await picture(moving, 'page-move-menu', 'Move… and Copy… in a page’s menu', 'Move… and Copy… in the ⋮ menu.')),

    h(2, 'Moving a page'),
    step(1, 'Choose ⋮, then Move…'),
    p('Open the page you want to move first. ', b('Move…'), ' is only in the menu if you can edit the page.'),
    step(2, 'Choose where it goes'),
    p('Choose the ', b('Space'), ', then the page to ', b('Put it under'), ', or ', b('The top of the space'), '. The page itself and its sub-pages are not offered, since a page cannot go under itself.'),
    ...(await picture(moving, 'page-move-dialog', 'The Move dialog', 'Choose the space, and the page to put it under.')),
    step(3, 'Choose Move'),
    p('The page and everything under it move together, and Tesria opens the page in its new place. It goes after the pages already there; drag it in the tree if it should be higher up.'),
    p('A moved page is the same page: its history, comments, labels and files go with it, and no new version is made. Moving needs edit rights on the page and on wherever it goes.'),
    panel('warning', p(b('Moving to another space changes who can see it.'), ' A page follows the permissions of the space it is in, so people who could not see it before may be able to now, and some who could may not. Restrictions set on the page itself go with it; any it had from the pages above it in its old place do not. See ', pageLink('Who can see a space'), ' and ', pageLink('Restrictions'), '.')),
    panel('success', p(b('Moving within the same space?'), ' Dragging it in the page tree is quicker, and lets you choose its exact place. See ', pageLink('The page tree and reordering'), '.')),

    h(2, 'Copying a page'),
    step(1, 'Choose ⋮, then Copy…'),
    p('Open the page you want to copy first.'),
    step(2, 'Choose where the copy goes'),
    p('Choose the ', b('Space'), ' and the page to ', b('Put it under'), ', as for moving. It can go anywhere you can edit, even beside the original.'),
    step(3, 'Choose whether to copy the pages under it'),
    p(b('Copy the pages under it too'), ' is ticked to begin with, which copies the whole branch. Untick it to copy just this one page.'),
    step(4, 'Choose Copy'),
    p('Tesria opens the copy. It is titled ', i('Copy of'), ' and the original’s title, so the two cannot be mixed up; rename it by editing it. Its sub-pages keep their own titles.'),
    p('What a copy brings, and what it does not:'),
    ul(
      li(p(b('Comes with it:'), ' the content, the labels, the emoji, and the attachments. The copy gets its own copies of the files, so its pictures keep working even if the original is deleted.')),
      li(p(b('Stays behind:'), ' the history (the copy starts at version 1), the comments, and any restrictions.')),
      li(p(b('Left out:'), ' pages under it that you cannot read, with everything under them.')),
    ),
    p('Copying needs read access to the page and edit rights where the copy goes. Anyone watching that space is told about the new pages, as for any new page.'),
    panel('info', p(b('Copy or template?'), ' Copy a page when you want this one page, once. When pages of the same kind are made again and again, a template is better: it is offered every time someone creates a page. See ', pageLink('Templates'), '.')),
  ))

  // ----------------------------------------------------------- Page actions
  await page('Page actions', pagesSection, doc(
    p('Everything you can do to a page is in the bar above it, with the things used less often tucked into its ', b('⋮'), ' menu. What you see depends on what you may do: a button you have no right to use is not shown at all.'),
    ...(await picture(actions, 'page-menu', 'The bar above a page, with its menu open', 'The bar above a page, and its ⋮ menu.')),

    h(2, 'On the bar'),
    ul(
      li(p(b('Edit'), ' opens the page in the editor. See ', pageLink('Drafts, Publish and Update'), '.')),
      li(p(b('Full width'), ' (or ', b('Normal width'), ') widens the page to fill the window, for wide tables and diagrams. It changes the page for everyone who reads it. Phones do not show it, since a phone’s screen is already filled.')),
      li(p(b('+ New,'), ' on a phone, starts a new page under this one. On a computer, use ', b('+ New page'), ' in the sidebar.')),
    ),

    h(2, 'In the ⋮ menu'),
    ul(
      li(p(b('Export as Markdown, HTML or PDF'), ' downloads the page as a file. Only the formats the space allows are offered. See ', pageLink('Exporting a page'), '.')),
      li(p(b('Watch this page'), ' tells you when it changes or gets a comment. See ', pageLink('Watching'), '.')),
      li(p(b('Save as template'), ' lets new pages start from this one. See ', pageLink('Templates'), '.')),
      li(p(b('Move…'), ' and ', b('Copy…'), ' put the page, or a copy of it, somewhere else. See ', pageLink('Moving and copying pages'), '.')),
      li(p(b('Delete'), ' moves the page and its sub-pages to the space’s trash. See ', pageLink('Trash'), '.')),
    ),

    h(2, 'Around the title'),
    ul(
      li(p(b('Add emoji'), ', which appears above the title as you point at it, gives the page an emoji. See ', pageLink('Page emoji'), '.')),
      li(p(b('+ Add label'), ', under the title, tags the page. See ', pageLink('Labels'), '.')),
    ),

    h(2, 'Below the page'),
    p('Four tabs under every page:'),
    ul(
      li(p(b('Comments:'), ' the discussion about the page. See ', pageLink('Comments'), '.')),
      li(p(b('Attachments:'), ' its files. See ', pageLink('Attachments'), '.')),
      li(p(b('History:'), ' every version. See ', pageLink('History and restoring'), '.')),
      li(p(b('Restrictions:'), ' who else may see or change it. See ', pageLink('Restrictions'), '.')),
    ),

    h(2, 'Who may delete a page'),
    p('Deleting needs edit rights on the page, and a right from your role as well:'),
    ul(
      li(p(b('Delete pages you created:'), ' everyone has it by default.')),
      li(p(b('Delete pages created by others:'), ' administrators have it by default.')),
    ),
    p('See ', pageLink('Roles'), ' for how an administrator changes these.'),
  ))

  // ------------------------------------------------------------ Page emoji
  await page('Page emoji', pagesSection, doc(
    p('A page can have an emoji to go with its title, such as 🚀 on a launch plan or 📘 on a handbook. It is shown large above the title, and small before the page’s name in the page tree, which makes pages easy to spot in a long tree. It is also shown in a space exported as a website, and copies of the page keep it.'),
    p('Anyone who can edit the page can give it an emoji. Readers just see it.'),

    h(2, 'Adding an emoji'),
    step(1, 'Point at the title'),
    p(b('Add emoji'), ' appears just above the title while the pointer is over it. On a phone or tablet it is always shown.'),
    ...(await picture(emoji, 'emoji-add', 'Add emoji above a page’s title', 'Add emoji appears above the title.')),
    step(2, 'Choose an emoji'),
    p('Choose ', b('Add emoji'), ' and a picker opens.'),
    ...(await picture(emoji, 'emoji-picker', 'The emoji picker', 'Search at the top, the emoji below, and a box for any other.')),
    ul(
      li(p(b('Search'), ' by name, such as ', i('rocket'), ' or ', i('book'), '.')),
      li(p(b('Emoji, Numbers and Bullets'), ' are the three groups; scroll down the picker for the numbers and bullets.')),
      li(p(b('Or paste any emoji'), ' takes one the picker does not have: paste it and choose ', b('Use'), '.')),
    ),
    p('The emoji is saved as soon as you choose it. It is not part of the page’s content, so it adds no version to the history and nobody is notified.'),

    h(2, 'Changing or removing it'),
    p('Choose the emoji itself, above the title, to open the picker again. Choose another to replace it, or ', b('Remove the emoji'), ' at the bottom of the picker to take it away.'),

    h(2, 'Numbers and bullets'),
    p('The ', b('Numbers'), ' group (1️⃣ 2️⃣ 3️⃣ and so on) and the ', b('Bullets'), ' group (• ◦ ▪ ➤ ★ ✔ and others) are for pages that are read in order, or that are listed rather than illustrated, where a picture would only get in the way.'),
    p('A number emoji stays exactly where you put it. To number every page in a space instead, and have the numbers follow along when pages are added or moved, use the space’s ', b('Numbered'), ' page tree; see ', pageLink('The page tree and reordering'), '.'),
    panel('note', p(b('One emoji, not words.'), ' A page’s emoji has to be a single emoji. Letters and words are refused.')),
  ))

  // --------------------------------------------------------------- Labels
  await page('Labels', pagesSection, doc(
    p('Labels are short tags you add to pages, such as ', c('how-to'), ', ', c('meeting-notes'), ' or ', c('release'), '. Spaces group pages by who they are for; labels group them by what they are, across every space. Label every how-to guide ', c('how-to'), ', and one click finds them all, whichever space they are in.'),

    h(2, 'Adding a label'),
    step(1, 'Choose + Add label'),
    p('It is under the page’s title, after any labels the page already has.'),
    step(2, 'Type the label, then Add'),
    p('Type it and choose ', b('Add'), ', or press ', b('Enter'), '.'),
    ...(await picture(labels, 'labels-add', 'Adding a label under a page’s title', 'A new label being typed, beside the page’s other labels.')),
    p('A label can be up to 50 characters of lower-case letters, digits, dots, dashes and underscores, starting with a letter or a digit. Spaces are not allowed, so use a dash: ', c('meeting-notes'), '. Capitals are turned into lower case, so ', c('Release'), ' and ', c('release'), ' are the same label.'),
    p('To take a label off a page, choose the ', b('×'), ' beside it. Adding and removing labels needs edit rights on the page.'),

    h(2, 'Finding pages by label'),
    ul(
      li(p(b('Choose a label'), ' on any page to list every page that has it, with the key of the space each is in.')),
      li(p(b('All labels,'), ' at the top of that list, shows every label in use and how many pages have it, with a box to filter them. Its address is ', c('/labels'), ', so you can bookmark it.')),
    ),
    p('Both only count and list pages you can see.'),

    h(2, 'Lists of labeled pages on a page'),
    p('To keep a list of labeled pages on a page, such as every page labeled ', c('meeting-notes'), ' on a “Meeting notes” page, use the ', pageLink('Content by label'), ' element; it stays up to date by itself. ', pageLink('Labels list'), ' shows the labels in use. See ', pageLink('Live content'), '.'),

    h(2, 'Good practice'),
    ul(
      li(p(b('Agree on a few labels'), ' and write them down, so everyone uses ', c('how-to'), ' rather than a mix of ', c('howto'), ', ', c('how-to'), ' and ', c('guide'), '.')),
      li(p(b('Label what a page is,'), ' not what is in it: search already finds the words on a page.')),
    ),
  ))

  // ----------------------------------------------------------- Attachments
  await page('Attachments', pagesSection, doc(
    p('An attachment is a file kept with a page: a spreadsheet, a PDF, a picture, a recording. The ', b('Attachments'), ' tab below a page lists them all. Pictures you paste or drop into the editor are attachments too, and appear here as well.'),

    h(2, 'Adding a file'),
    step(1, 'Open the Attachments tab'),
    p('It is below the page, beside Comments.'),
    step(2, 'Choose Upload file'),
    p('Pick the file. It can be of any type, up to 25 MB, one file at a time.'),
    ...(await picture(attachments, 'attachments-upload', 'The Attachments tab with the Upload file button', 'Upload file, on the Attachments tab.')),
    p('To show a file inside the page, rather than only in the list, use the ', pageLink('File or video'), ' element.'),

    h(2, 'Opening and deleting files'),
    ul(
      li(p(b('Choose a file’s name'), ' to download it.')),
      li(p(b('Delete'), ' removes the file, after asking. Anywhere the page shows it stops showing it. Unlike a page, a deleted file does not go to the trash, so this cannot be undone.')),
    ),
    p('Uploading and deleting need edit rights on the page. Anyone who can read the page can download its files, and nobody else can, even with the file’s address.'),
    p('Copying a page copies its files too, so the copy does not depend on the original. See ', pageLink('Moving and copying pages'), '.'),
  ))

  // ------------------------------------------------- History and restoring
  await page('History and restoring', pagesSection, doc(
    p('Tesria keeps every version of every page. Each time a page is published or updated, the version before it is kept, with who made the change, when, and the note they left. So you can see how a page came to be as it is, find out what changed and when, and put back an earlier version if a change went wrong.'),

    h(2, 'Looking back'),
    p('Open the ', b('History'), ' tab below the page. It lists every version, newest first: its number (the newest is marked ', i('current'), '), who made it, when, and what they wrote in ', i('What changed?'), '.'),
    p(b('Preview'), ' beside a version shows the page as it was then, below the list. ', b('Close'), ' hides it again.'),

    h(2, 'Comparing two versions'),
    p('To see exactly what changed between two versions, compare them.'),
    step(1, 'Tick two versions'),
    p('Tick the box at the start of each. If you tick a third, the first one you ticked is unticked.'),
    step(2, 'Choose Compare'),
    p('The button appears once two are ticked, and names them, such as ', b('Compare v1 and v2'), '.'),
    ...(await picture(history, 'history-compare', 'Two versions ticked in the history, and the Compare button', 'Two versions ticked, and Compare.')),
    p('The later version is shown below the list, with what it added highlighted and what it removed struck through.'),
    ...(await picture(history, 'history-diff', 'Two versions compared', 'Additions are highlighted; removals are struck through.')),
    p('Paragraphs are compared whole: if one word in a paragraph changed, the whole paragraph is shown as changed.'),

    h(2, 'Restoring an earlier version'),
    step(1, 'Choose Restore beside the version'),
    p('Every version but the current one has it.'),
    step(2, 'Confirm'),
    p('Tesria asks first, then puts that version’s content back.'),
    p('Restoring does not remove anything from the history. It adds a new version on top, with the old content, named ', i('Restored from version'), ' and its number, so even the restore can be undone. Anyone watching the page is told, as for any update. Restoring needs edit rights on the page.'),
    panel('note', p(b('The title is not part of a version.'), ' Versions keep the page’s content, so restoring one never changes the title back.')),
    panel('success', p(b('Leave a note when you update.'), ' A few words in ', b('What changed?'), ' make the history easy to read later: “added the budget” finds the right version faster than a list of dates.')),
  ))

  // --------------------------------------------------------- Restrictions
  await page('Restrictions', pagesSection, doc(
    p('A restriction keeps one page, and everything under it, to particular people or groups, inside a space that others can use. It suits a page that should not be seen by everyone who can see the space, such as the salary table in a team’s space, or an announcement still being drafted. To limit a whole space instead, see ', pageLink('Who can see a space'), '.'),

    h(2, 'The two kinds'),
    ul(
      li(p(b('View:'), ' only the people listed can see the page. For everyone else it disappears: from the tree, search, label lists and the trash, and they are not told when it changes.')),
      li(p(b('Edit:'), ' everyone who can see the space can still read the page, but only the people listed can change it. Use it for a page that should stay as it is, such as a signed-off policy.')),
    ),

    h(2, 'Restricting a page'),
    step(1, 'Open the Restrictions tab'),
    p('It is below the page, beside History.'),
    step(2, 'Choose who, and View or Edit'),
    p('Choose ', b('User'), ' or ', b('Group'), ', then the person or group, then ', b('View'), ' or ', b('Edit'), '.'),
    ...(await picture(restrictions, 'restrictions-form', 'The Restrictions tab and its form', 'Choose who, and View or Edit, then Restrict.')),
    step(3, 'Choose Restrict'),
    p('The first time, Tesria adds you as well, at the same level, so you do not lock yourself out. Add everyone else who needs the page the same way.'),
    p('Anyone who can edit the page can restrict it, and ', b('Remove'), ' takes a restriction away again.'),

    h(2, 'What a restriction reaches'),
    ul(
      li(p(b('Sub-pages:'), ' a restriction covers every page under the restricted one, including pages added later.')),
      li(p(b('Pages above:'), ' the tab lists only the restrictions set on this page. One set on a page above it still applies, and is changed on that page.')),
      li(p(b('Administrators of a private space'), ' can always see and edit its restricted pages. In an open space nobody is exempt, so a restriction really does keep everyone else out.')),
    ),
    panel('warning', p(b('Restricted pages are never public.'), ' In a space published for public reading, any restriction, View or Edit, hides the page and everything under it from people who are not signed in.')),
    p('A restriction stays with the page if it moves to another space, but a copy of the page does not bring it. See ', pageLink('Moving and copying pages'), '.'),
  ))

  // ------------------------------------------------------------------ Trash
  await page('Trash', pagesSection, doc(
    p('Deleting a page does not destroy it straight away. It goes to the space’s ', b('trash'), ', with every page under it, and waits there until someone restores it or deletes it for good. So a page deleted by mistake can be brought back, history and all.'),

    h(2, 'Deleting a page'),
    p('On the page, open the ', b('⋮'), ' menu and choose ', b('Delete'), ', then ', b('Move to the trash'), '. Its sub-pages go with it. See ', pageLink('Page actions'), ' for who may delete pages.'),

    h(2, 'Finding the trash'),
    p('Choose ', b('Space settings'), ' at the bottom of the space’s sidebar, then the ', b('Trash'), ' tab.'),
    ...(await picture(trash, 'trash-tab', 'The Trash tab in Space settings', 'The trash is a tab of Space settings.')),
    p('It lists the pages that were deleted, newest first, with when. A page deleted with its sub-pages is listed once, and stands for all of them. You only see pages you would be able to see if they had not been deleted.'),

    h(2, 'Restoring a page'),
    p(b('Restore'), ' puts the page back where it was, with its sub-pages, its history and its files. If the page it was under has gone, it comes back at the top of the space instead. Anyone who can edit the page can restore it.'),

    h(2, 'Deleting a page for good'),
    panel('error', p(b('This cannot be undone.'), ' The page and its sub-pages are destroyed, with every version, comment and attachment. Only a backup taken beforehand still holds them.')),
    p(b('Delete permanently'), ' asks first, and then, if you have not entered your password in the last few minutes, asks for it again. It needs administrator access to the space, and the same right from your role as deleting the page did.'),
    p('Nothing leaves the trash on its own: pages stay there until someone restores them or deletes them permanently. Deleting the whole space empties its trash too; see ', pageLink('Archiving and deleting a space'), '.'),
  ))
}

// Pictures these pages no longer use, taken down so they do not linger in the
// page's attachments or in the exported pack.
const RETIRED = {
  'What a space is': ['spaces-list.png', 'spaces-list.phone.png'],
  'Creating a space': ['space-create.png'],
  'The space home and watching': ['space-home.png', 'space-home.phone.png'],
  'Who can see a space': ['space-permissions.phone.png'],
  'Space icons': ['space-icon.phone.png'],
  'Archiving and deleting a space': ['space-archive.phone.png'],
  'The page tree and reordering': ['page-tree.png', 'page-tree.phone.png', 'page-tree-reorder.png', 'page-tree-reorder.phone.png'],
  'Labels': ['labels.png', 'labels.phone.png'],
  'Attachments': ['attachments-tab.png', 'attachments-tab.phone.png'],
  'History and restoring': ['history-tab.png', 'history-tab.phone.png'],
  'Restrictions': ['restrictions-tab.png', 'restrictions-tab.phone.png'],
  'Trash': ['trash.png', 'trash.phone.png'],
}

/** Moves `title` straight after `afterTitle` among the children of User manual → `parentTitle`. */
async function placeAfter(author, spaceId, parentTitle, title, afterTitle) {
  const tree = await author.call('GET', `/api/pages/tree?spaceId=${spaceId}`)
  const manual = tree.find((n) => n.title === 'User manual')
  const parent = manual && (manual.children ?? []).find((n) => n.title === parentTitle)
  if (!parent) return
  const kids = parent.children ?? []
  const after = kids.findIndex((n) => n.title === afterTitle)
  const at = kids.findIndex((n) => n.title === title)
  if (after >= 0 && at >= 0 && at !== after + 1) {
    await author.call('PUT', `/api/pages/${kids[at].id}/move`, { parentPageId: parent.id, index: at > after ? after + 1 : after })
    console.log(`  moved ${title} after ${afterTitle}`)
  }
}

export async function cleanup({ author }) {
  const space = await author.call('GET', '/api/spaces/DOCS')
  const tree = await author.call('GET', `/api/pages/tree?spaceId=${space.id}`)
  const find = (nodes, title) => {
    for (const n of nodes) {
      if (n.title === title) return n
      const hit = find(n.children ?? [], title)
      if (hit) return hit
    }
    return null
  }

  // Templates goes after Creating a page, where the old text mentioned them.
  const manual = find(tree, 'User manual')
  const pagesSection = manual && (manual.children ?? []).find((n) => n.title === 'Pages')
  if (pagesSection) {
    const kids = pagesSection.children ?? []
    const after = kids.findIndex((n) => n.title === 'Creating a page')
    const at = kids.findIndex((n) => n.title === 'Templates')
    if (after >= 0 && at >= 0 && at !== after + 1) {
      await author.call('PUT', `/api/pages/${kids[at].id}/move`, { parentPageId: pagesSection.id, index: at > after ? after + 1 : after })
      console.log('  moved Templates after Creating a page')
    }
  }

  // The two new pages go beside the pages they follow on from; a new page
  // lands at the end. Each reads the tree afresh, after the move before it.
  await placeAfter(author, space.id, 'Pages', 'Page emoji', 'Drafts, Publish and Update')
  await placeAfter(author, space.id, 'Pages', 'Moving and copying pages', 'The page tree and reordering')

  // Only the Spaces and Pages sections: other sections have pages with
  // some of the same titles.
  const spacesSection = manual && (manual.children ?? []).find((n) => n.title === 'Spaces')
  const mine = [spacesSection, pagesSection].filter(Boolean)
  for (const [title, files] of Object.entries(RETIRED)) {
    const node = find(mine, title)
    if (!node) continue
    for (const a of await author.call('GET', `/api/pages/${node.id}/attachments`)) {
      if (files.includes(a.filename)) {
        await author.call('DELETE', `/api/attachments/${a.id}`)
        console.log(`  - ${title}: ${a.filename}`)
      }
    }
  }
}
