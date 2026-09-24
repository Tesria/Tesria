// User manual, The editor (dev-plan 10.5, rewritten for 15.6): the editor in
// general, and the two overview pages that lead to the elements and the live
// content blocks.
//
//   The editor                  what the section holds, and where to start
//   Editor tour                 the parts of the editor, saving, leaving,
//                               live editing, full width, the page emoji
//   The slash menu              / and the + menu, and wrapping a selection
//   Text formatting             bold to superscript, clearing it
//   Colors                      text color and highlight, shown for real
//   Alignment and indentation   shown for real
//   Markdown shortcuts          what typing turns into
//   Keyboard shortcuts          every binding, grouped
//   Floating menus              the menus that appear where you work
//   Elements                    an overview of every element page
//   Live content                an overview of every live content page
//
// The pages for each element and each live content block are written by
// editor-elements-1.mjs, editor-elements-2.mjs and editor-live.mjs (and
// Panels by pilot.mjs). This module only makes sure they exist, empty if
// need be, so the overviews can link to them on a first run.
//
// Facts checked against src/web/src/editor (Toolbar, TextStyleMenu,
// InsertMenu, slash/*, SelectionBubbleMenu, ImageHoverMenu, LayoutMenu,
// WrapperMenu, LinkMenu, TableCellMenu, TableControls, TableWidthControls,
// textFormatting.ts, palette.ts), the TipTap extensions' own key bindings and
// input rules in node_modules/@tiptap, routes/PageEditor.tsx,
// routes/LeaveEditorDialog.tsx, editor/CollabStatus.tsx and
// components/PageEmoji.tsx, on 2026-09-24.
//
// No page is retired: every title the first version wrote is kept.

// ------------------------------------------------------------ The children
// Titles are exact: the element and live content modules write these pages,
// and the overviews link to them by title. Grouped by what they are for.
const ELEMENT_GROUPS = [
  {
    heading: 'Writing and structuring text',
    intro: 'The everyday building blocks. Most pages need nothing more.',
    items: [
      [['Normal text'], 'the body text you get when you start typing.'],
      [['Headings'], 'six levels, for the sections of a page. They are what a table of contents lists and what a link can jump to.'],
      [['Bullet list', 'Ordered list', 'Task list'], 'points in any order, steps in order, and checkboxes for things to do.'],
      [['Blockquote'], 'a quotation, set off from the text around it.'],
      [['Divider'], 'a line across the page between sections.'],
      [['Link'], 'to another page, a website, an email address, or a heading on the same page.'],
    ],
  },
  {
    heading: 'Making things stand out',
    intro: 'For the parts of a page a reader must not miss, or may happily skip.',
    items: [
      [['Panels'], 'a colored box for background, a tip, a warning or an error.'],
      [['Expand'], 'detail most readers can skip, behind a title they click to open.'],
      [['Decision'], 'something the team agreed, marked with a check so it stands out.'],
    ],
  },
  {
    heading: 'Inside a sentence',
    intro: 'Small things that sit in a line of text, or in a table cell.',
    items: [
      [['Status'], 'a colored label such as IN PROGRESS or DONE.'],
      [['Date'], 'a calendar date, shown to each reader in their own format.'],
      [['Mention'], 'names a person, who is told when you publish.'],
      [['Emoji'], 'type a colon and a word, such as :rocket.'],
    ],
  },
  {
    heading: 'Arranging the page',
    items: [
      [['Layout'], 'two or three columns side by side, which stack on a phone.'],
      [['Table of contents'], 'the page’s headings as links, kept up to date as you write.'],
    ],
  },
  {
    heading: 'Tables, numbers and code',
    items: [
      [['Table'], 'rows and columns, with header rows, merged cells and colored backgrounds.'],
      [['Chart'], 'a bar, column, line or pie chart drawn from a table on the same page.'],
      [['Math'], 'equations, on a line of their own or inside a sentence.'],
      [['Code block'], 'code, colored for the language it is written in.'],
      [['Diagram (Mermaid)'], 'a flowchart or sequence diagram drawn from a few lines of text.'],
    ],
  },
  {
    heading: 'Pictures, video and files',
    items: [
      [['Image'], 'a picture, pasted or dragged onto the page.'],
      [['Gallery'], 'several pictures tiled in a grid.'],
      [['File or video'], 'a video or audio player, a PDF, or a card for any file attached to the page.'],
      [['Animation'], 'a short video that plays silently on a loop, like a GIF.'],
      [['Embed'], 'a video, design or board from another site, such as YouTube or Figma.'],
      [['Smart link'], 'a link that shows a preview of what it points to.'],
    ],
  },
  {
    heading: 'Sharing with other pages',
    intro: 'Two elements that come into their own when another page collects them.',
    items: [
      [['Excerpt'], 'marks the part of a page that other pages can show.'],
      [['Page properties'], 'a small table of facts about the page, such as its owner and status, that a report can gather from many pages.'],
    ],
  },
]

const LIVE_GROUPS = [
  {
    heading: 'Finding your way',
    items: [
      [['Children display'], 'the pages under this one. A section’s front page is the classic place for it.'],
      [['Page tree'], 'the tree of pages as nested links, from this page or from the top of the space.'],
      [['Content by label'], 'every page carrying a label, such as all the pages labeled release.'],
      [['Labels list'], 'labels as links: this page’s own, or the most used in the space.'],
    ],
  },
  {
    heading: 'What changed, and who changed it',
    items: [
      [['Recently updated'], 'the pages changed most recently, with who and when.'],
      [['Change history'], 'this page’s latest versions, and what each one changed.'],
      [['Contributors'], 'the people who have edited this page, most edits first.'],
    ],
  },
  {
    heading: 'Reusing what is written elsewhere',
    items: [
      [['Include page'], 'another page, shown whole inside this one, always as it is now.'],
      [['Excerpt include'], 'just the excerpt another page has marked.'],
    ],
  },
  {
    heading: 'Reports gathered from many pages',
    items: [
      [['Task report'], 'task list items from this page and those under it, from a space, or from everywhere, with who each is assigned to.'],
      [['Page properties report'], 'the page properties of every page with a label, as one table.'],
    ],
  },
  {
    heading: 'This page’s files',
    items: [
      [['Attachments (live content)'], 'the files attached to this page, each a download link.'],
    ],
  },
]

const titlesOf = (groups) => groups.flatMap((g) => g.items.flatMap(([titles]) => titles))

// ------------------------------------------------------------------ Shots
// Close-ups and recordings in a narrow window (WRITING.md). All open a new
// page in Tesria Demo: a recording's draft is discarded by the harness, and a
// still's by the "-closed" shot after it.
const NARROW = { width: 480, height: 900 }
// Tips cover what a picture shows; the template picker above the title is
// not what these pictures are about.
const QUIET = '.tip, .onboarding-tip { display: none !important; } .editor-form > label.change-comment { display: none !important; }'
const CLOSE_NEW_PAGE = (name) => ({ name, settle: 300, skipCapture: true, phone: false, steps: [{ click: '.page-actionbar button:has-text("Close")' }, { wait: 1200 }] })

const BOLD = '.floating-menu button[title="Bold"]'
const HIGHLIGHT = '.floating-menu button[title="Highlight selected text"]'
// The + menu's items carry their description as a title.
const DECISION = '.toolbar-dropdown__menu--insert button[title="Record something that was agreed"]'

export const shots = () => [
  // ---- Editor tour: where the style menu, + and Publish are. A window wide
  // enough for the computer toolbar, so the strip is shown at about its size.
  {
    name: 'editor-toolbar', url: '/spaces/DEMO/new', viewport: { width: 1100, height: 600 }, phone: false,
    steps: [{ wait: 2500 }, { css: QUIET }, { click: '.ProseMirror' }, { wait: 500 }],
    clipTo: '.page-actionbar--editor', clipPad: 6,
    annotate: [
      { type: 'box', target: '.page-actionbar--editor .toolbar-dropdown--text', pad: 2 },
      { type: 'box', target: '.page-actionbar--editor .toolbar-dropdown--insert', pad: 2 },
      { type: 'box', target: '.page-actionbar--editor .page-actionbar__secondary .btn--primary', pad: 2 },
    ],
  },
  CLOSE_NEW_PAGE('editor-toolbar-closed'),

  // ---- The slash menu: /h2 turns the line typed into a heading, then
  // /table on a new line adds a table.
  {
    name: 'slash-menu', url: '/spaces/DEMO/new', phone: false,
    viewport: { width: 480, height: 480 }, record: { size: { width: 480, height: 480 } },
    waitFor: '.ProseMirror', lead: 900, tail: 1600, css: QUIET,
    steps: [
      { click: '.ProseMirror' }, { wait: 400 },
      { typeSlowly: 'Goals for the quarter', delay: 45 }, { wait: 700 },
      { typeSlowly: ' /h2', delay: 130 }, { wait: 1200 },
      { press: 'Enter', selector: '.ProseMirror' }, { wait: 1000 },
      { press: 'Enter', selector: '.ProseMirror' }, { wait: 300 },
      { typeSlowly: '/table', delay: 130 }, { wait: 1200 },
      { press: 'Enter', selector: '.ProseMirror' }, { wait: 600 },
    ],
  },

  // ---- The slash menu, the + menu: a sentence selected, then + and
  // Decision wraps it.
  {
    name: 'insert-wrap', url: '/spaces/DEMO/new', phone: false,
    viewport: { width: 480, height: 560 }, record: { size: { width: 480, height: 560 } },
    waitFor: '.ProseMirror', lead: 900, tail: 1600, css: QUIET,
    steps: [
      { click: '.ProseMirror' }, { wait: 400 },
      { typeSlowly: 'We will launch on October 14.', delay: 45 }, { wait: 700 },
      { tripleClick: '.ProseMirror p' }, { wait: 900 },
      { moveTo: '.toolbar__insert' }, { wait: 400 },
      { click: '.toolbar__insert' }, { wait: 1000 },
      { scrollTo: DECISION }, { moveTo: DECISION }, { wait: 600 },
      { click: DECISION }, { wait: 800 },
    ],
  },

  // ---- Floating menus: the menu over selected text, making a sentence
  // bold and highlighted.
  {
    name: 'selection-bubble', url: '/spaces/DEMO/new', phone: false,
    viewport: { width: 480, height: 400 }, record: { size: { width: 480, height: 400 } },
    waitFor: '.ProseMirror', lead: 900, tail: 1600, css: QUIET,
    steps: [
      { click: '.ProseMirror' }, { wait: 400 },
      { typeSlowly: 'Back up the database before you upgrade.', delay: 45 }, { wait: 700 },
      { tripleClick: '.ProseMirror p' }, { wait: 1000 },
      { moveTo: BOLD }, { wait: 500 }, { click: BOLD }, { wait: 900 },
      { moveTo: HIGHLIGHT }, { wait: 500 }, { click: HIGHLIGHT }, { wait: 900 },
      { press: 'ArrowRight', selector: '.ProseMirror' }, { wait: 600 },
    ],
  },

  // ---- Floating menus: a new table's Cell options, open. The + and ×
  // controls around the table show too, since the pointer is in it.
  {
    name: 'table-cell-menu', url: '/spaces/DEMO/new', viewport: NARROW, phone: false,
    steps: [
      { wait: 2500 }, { css: QUIET },
      { click: '.ProseMirror' }, { wait: 300 },
      { keys: '/table' }, { wait: 800 },
      { press: 'Enter', selector: '.ProseMirror' }, { wait: 600 },
      { keys: 'Owner' }, { wait: 300 },
      { click: '.cell-menu__trigger' }, { wait: 600 },
    ],
    clipTo: ['.ProseMirror table', '.cell-menu__panel'], clipPad: 40,
    annotate: [{ type: 'box', target: '.cell-menu__trigger', pad: 3 }],
  },
  CLOSE_NEW_PAGE('table-cell-menu-closed'),
]

export async function build({ top, page, ensure, doc, p, h, text, bold, italic, code, ul, ol, li, panel, table, picture, animation, pageLink }) {
  const manual = top['User manual']
  const b = (t) => text(t, bold)
  const c = (t) => text(t, code)
  const i = (t) => text(t, italic)
  const mark = (t, type, attrs) => text(t, attrs ? { type, attrs } : { type })
  /** A paragraph with block attributes (alignment, indentation). */
  const para = (attrs, ...content) => ({ ...p(...content), attrs })

  // Every page under The editor exists before anything links to it, in the
  // order the tree shows them. ensure() leaves a page that exists alone.
  const editor = await ensure('The editor', manual)
  const tour = await ensure('Editor tour', editor)
  const slash = await ensure('The slash menu', editor)
  for (const title of ['Text formatting', 'Colors', 'Alignment and indentation', 'Markdown shortcuts', 'Keyboard shortcuts']) await ensure(title, editor)
  const menus = await ensure('Floating menus', editor)
  const elements = await ensure('Elements', editor)
  for (const title of titlesOf(ELEMENT_GROUPS)) await ensure(title, elements)
  const liveRoot = await ensure('Live content', editor)
  for (const title of titlesOf(LIVE_GROUPS)) await ensure(title, liveRoot)

  /** An overview group: a heading, a line on what they share, a link to each page. */
  const group = ({ heading, intro, items }) => [
    h(2, heading),
    ...(intro ? [p(intro)] : []),
    ul(...items.map(([titles, what]) => li(p(
      ...titles.flatMap((t, n) => [...(n === 0 ? [] : [n === titles.length - 1 ? ' and ' : ', ']), pageLink(t)]),
      ': ', what,
    )))),
  ]

  // ================================================================ The editor
  await page('The editor', manual, doc(
    p('The editor is where pages are written. It works much like a word processor: type, select words to format them, and choose ', b('Publish'), ' when you are done. What makes it more than a word processor is what you can add. Type ', c('/'), ' on any line and you can put in a table, a colored panel, a checklist, a diagram, a chart, a video, or a list of pages that keeps itself up to date.'),
    p('You do not need to learn it all before you start. Most pages need only headings, lists and the odd table; the rest is here for when a page calls for it.'),
    panel('success', p(b('The safest place to practice is a new page.'), ' Choose ', b('+ New page'), ' in a space, try anything you like, and choose ', b('Close'), ' when you are done. Close throws a new page away, so nothing is left behind and nobody sees it.')),

    h(2, 'Start here'),
    ul(
      li(p(pageLink('Editor tour'), ': the parts of the editor, how your work is saved, and what happens when someone else is editing the same page.')),
      li(p(pageLink('The slash menu'), ': the quickest way to add anything, and the ', b('+'), ' menu, which lists the same things.')),
      li(p(pageLink('Floating menus'), ': the small menus that appear over selected text, tables, pictures and panels.')),
    ),
    h(2, 'Formatting text'),
    ul(
      li(p(pageLink('Text formatting'), ': bold, italic, code and the rest, and how to clear them.')),
      li(p(pageLink('Colors'), ': text colors and highlights.')),
      li(p(pageLink('Alignment and indentation'), ': centering a line, or moving a paragraph in.')),
    ),
    h(2, 'Working faster'),
    ul(
      li(p(pageLink('Keyboard shortcuts'), ': every key combination the editor knows.')),
      li(p(pageLink('Markdown shortcuts'), ': type ', c('## '), ' for a heading or ', c('- '), ' for a list, and it happens as you type.')),
    ),
    h(2, 'Everything you can add'),
    ul(
      li(p(pageLink('Elements'), ': a page for each thing you can put on a page, from a heading to a video, with every variant shown.')),
      li(p(pageLink('Live content'), ': blocks that look something up each time the page is read, such as the pages under this one or the tasks assigned to you.')),
    ),
    p('On a phone the editor is the same, with a smaller toolbar: see ', pageLink('The editor on a phone'), '.'),
  ))

  // =============================================================== Editor tour
  await page('Editor tour', editor, doc(
    p('The editor opens when you choose ', b('Edit'), ' on a page, or ', b('+ New page'), ' in a space. This page walks around it: what each part is for, how your work is saved, and what happens when someone else is editing the same page at the same time.'),

    h(2, 'Opening the editor'),
    ul(
      li(p(b('Edit'), ', at the top of any page you are allowed to change, opens that page.')),
      li(p(b('+ New page'), ', in the space’s sidebar, starts a new page. If you are looking at a page when you choose it, the new page goes under that one; otherwise it goes at the top of the space.')),
      li(p('On a phone, ', b('+ New'), ' beside ', b('Edit'), ' starts a page under the one you are reading.')),
    ),

    h(2, 'A quick look around'),
    ...(await picture(tour, 'editor-toolbar', 'The editor’s toolbar, with the text style menu, the + menu and Publish boxed', 'Boxed, from the left: the text style menu, the + menu, and Publish.')),
    p('From the top down:'),
    ul(
      li(p(b('The toolbar.'), ' Formatting at the left, then ', b('+'), ' for adding things. At the right: ', b('Full width'), ', ', b('Publish'), ' (', b('Update'), ' on a page that is already published) and ', b('Close'), '.')),
      li(p(b('Where the page sits:'), ' the space and the pages above this one.')),
      li(p(b('The title.'), ' Press ', b('Enter'), ' in it to jump to the first line of the page.')),
      li(p(b('The page itself.'), ' Click anywhere and type.')),
      li(p(b('What changed? (optional)'), ', under the page, when you edit a page that is already published. A few words here are kept with the new version, so the page’s history can say why it changed: ', i('Added the October dates'), ', say, or ', i('Fixed the phone number'), '.')),
    ),

    h(2, 'The toolbar'),
    p('Point at any button to see its name. From the left:'),
    ul(
      li(p(b('The text style menu.'), ' It shows the style of the line you are on, such as ', b('Normal text'), ' or ', b('Heading 2'), ', and lists Normal text and Heading 1 to Heading 6. In a narrow window it reads ', b('Aa Style'), ', and any buttons that do not fit on the row move into this menu, under the styles, so nothing is ever out of reach.')),
      li(p(b('B, I, U, S'), ' and ', b('<>'), ': bold, italic, underline, strikethrough and inline code. See ', pageLink('Text formatting'), '.')),
      li(p(b('Highlight color'), ' and ', b('Text color'), ': two palettes. See ', pageLink('Colors'), '.')),
      li(p(b('Bullet list'), ', ', b('Ordered list'), ' and ', b('Task list'), '.')),
      li(p(b('Outdent'), ' and ', b('Indent'), ', which move a paragraph or heading in and out. See ', pageLink('Alignment and indentation'), '.')),
      li(p(b('Subscript'), ' (x₂), ', b('Superscript'), ' (x²) and ', b('Clear formatting'), ', which takes every style off the selected text.')),
      li(p(b('Alignment'), ': left, center, right or justified.')),
      li(p(b('+'), ': everything else you can add, from a table to a video. See ', pageLink('The slash menu'), '.')),
    ),
    p('There is no link button on the row. Select the words and press ', b('Ctrl+K'), ' (', b('⌘K'), ' on a Mac), or choose the link button in the menu that appears over them.'),
    p('On a phone the toolbar is just ', b('Aa Style'), ' and ', b('+ Insert'), ', and every formatting control is inside Aa Style. See ', pageLink('The editor on a phone'), '.'),

    h(2, 'Saving your work'),
    p('Readers never see a change until you decide they should. The button at the top right says what it will do:'),
    ul(
      li(p(b('Publish'), ', on a new page. Until you publish it, a new page is yours alone: it is not in the page tree, search or anyone’s notifications, even if you have added pictures to it. Publishing makes it version 1, where everyone who can see the space can read it. A page needs a title first.')),
      li(p(b('Update'), ', on a page that is already published. Your changes become a new version, which readers see straight away, and the page’s history keeps the one before.')),
      li(p(b('Close'), ' leaves the editor without publishing.')),
    ),
    p('Either way, anyone you have just mentioned with ', c('@'), ' is told about the page. More on versions in ', pageLink('Drafts, Publish and Update'), '.'),
    panel('warning', p(b('Close does not ask first.'), ' On a new page it throws the page away, which is handy when you were only trying something out. If you meant to keep it, choose ', b('Publish'), '. On a page that is already published, Close leaves the published page as it was; what happens to your changes depends on live editing, below.')),

    h(3, 'If you leave some other way'),
    p('Leaving the editor by anything other than those buttons, such as a link at the top of the screen, a page in the page tree or your browser’s Back button, stops you with ', b('You are leaving the editor'), ', so a stray click never costs you a page. It offers three ways out:'),
    ul(
      li(p(b('Publish and leave'), ' (', b('Update and leave'), ' on a published page).')),
      li(p(b('Discard page'), ' on a new page, or ', b('Leave unpublished'), ' on a published one.')),
      li(p(b('Stay in the editor'), '.')),
    ),
    p('Reloading the page or closing the browser tab gets your browser’s own warning instead: it is the only kind a web page is allowed to show at that moment.'),

    h(2, 'Editing with other people'),
    p('If your administrator has turned on ', b('live editing'), ', several people can edit a published page at once. Each of you sees the others’ changes as they are typed, and each person’s cursor in its own color, with their name beside it. It is the easiest way to write meeting notes together, or to fix a page while its author watches.'),
    p('A small bar above the title tells you how it is going:'),
    ul(
      li(p(b('Live: changes are shared as you type.'), ' All is well.')),
      li(p(b('Connecting to collaboration…'), ' Just opened, or finding the server again.')),
      li(p(b('Offline: your changes are local until reconnected.'), ' Keep writing: what you type is shared once the connection is back.')),
    ),
    p('With live editing, changes to a published page wait in a ', b('shared draft'), ' until someone chooses ', b('Update'), '. You can close the editor today and find your changes, and everyone else’s, still there tomorrow. Update publishes all of them as one new version.'),
    ul(
      li(p(b('A new page is written by one person.'), ' Live editing starts once it has been published.')),
      li(p(b('Undo takes back only your own changes'), ', never someone else’s.')),
      li(p(b('Changes made by an assistant or through the API'), ' while you edit appear highlighted in your draft, with ', b('Accept all'), ' and ', b('Reject all'), ' above the page. Update accepts any you have not decided on. See ', pageLink('Changes from assistants and the API'), '.')),
      li(p(b('If the page was updated some other way while you were editing,'), ' Update is refused rather than overwrite it. The difference is highlighted for you to accept or reject, and then you update again.')),
    ),
    panel('note', p(b('Without live editing,'), ' there is no shared draft: changes you leave unpublished are lost when you leave the editor, so choose ', b('Update'), ' before you go. If two people edit the same page at once, the last to choose Update wins. See ', pageLink('Editing at the same time'), '.')),

    h(2, 'Full width'),
    p('A page normally sits in a column narrow enough to read comfortably. ', b('Full width'), ', at the top right, lets it use the whole window, which suits a table with many columns or a large diagram. ', b('Normal width'), ' puts it back.'),
    p('It changes the page for everyone, straight away, without waiting for Publish or Update. You can also change it while reading the page. In a narrow window the button shows only ⤢, and on a phone it is hidden, since the page already fills the screen.'),

    h(2, 'Giving the page an emoji'),
    p('An emoji before the title makes a page easy to spot in the page tree: 📅 for meeting notes, 🚀 for a launch plan, 📘 for a guide. It is chosen on the page itself, not in the editor:'),
    ol(
      li(p('Open the page, and choose ', b('Add emoji'), ' just before its title. You see it on any page you are allowed to change.')),
      li(p('Pick one. Search by name, such as ', i('rocket'), ' or ', i('book'), ', or choose from ', b('Numbers'), ' (1️⃣ 2️⃣ 3️⃣, for pages meant to be read in order) or ', b('Bullets'), '. For anything else, paste it into ', b('Or paste any emoji'), ' and choose ', b('Use'), '.')),
    ),
    p('It is saved as soon as you choose. To change it, click the emoji; ', b('Remove the emoji'), ' is at the bottom of the picker.'),
  ))

  // ============================================================ The slash menu
  await page('The slash menu', editor, doc(
    p('The slash menu is the quickest way to add anything to a page, without taking your hands off the keyboard. Type ', c('/'), ' and a list of everything you can add appears. Type a few letters to narrow it and press ', b('Enter'), ': ', c('/table'), ' gives you a table, ', c('/info'), ' a blue panel, ', c('/todo'), ' a checklist.'),
    p('It opens at the start of a line or after a space, so typing a date like 9/24 or a phrase like and/or never opens it by accident.'),
    ...(await animation(slash, 'slash-menu', 'Typing /h2 at the end of a line makes that line a heading; /table on the next line adds a table.')),

    h(2, 'Using it'),
    ol(
      li(p('Type ', c('/'), ' at the start of a line, or after a space.')),
      li(p('Keep typing to narrow the list: ', c('/tab'), ' leaves ', b('Table'), ' and ', b('Table of contents'), '. If nothing matches, the menu says ', b('No matching blocks'), '.')),
      li(p('Press ', b('Enter'), ' for the highlighted item, or click one. The ', b('↑'), ' and ', b('↓'), ' keys move the highlight. ', b('Escape'), ' closes the menu and leaves what you typed as it was.')),
    ),

    h(2, 'Words it understands'),
    p('Each item answers to its name and to a few other words, so you can type whatever comes to mind first:'),
    table([
      ['Type', 'To find'],
      ['/text', 'Normal text'],
      ['/h1 to /h6', 'Heading 1 to Heading 6'],
      ['/todo', 'Task list, and Task report'],
      ['/quote', 'Blockquote'],
      ['/hr', 'Divider'],
      ['/columns', 'Layout'],
      ['/latex', 'Math and Inline math'],
      ['/gif', 'Animation'],
    ], [200, 500]),
    p('Each element’s page lists the words it answers to. See ', pageLink('Elements'), '.'),

    h(2, 'Changing the line you are on'),
    p('Some items change the line the cursor is on rather than adding a new one. Type ', c('/h2'), ' at the end of a sentence (after a space) and that whole line becomes Heading 2. Type ', c('/text'), ' at the end of a heading and it goes back to normal text. ', c('/quote'), ' and the panels (', c('/info'), ' and the rest) wrap the line the same way, and inside a panel, choosing another panel changes its color.'),

    h(2, 'The + menu'),
    p('Everything in the slash menu except text styles and lists is also in the ', b('+'), ' menu on the toolbar (', b('+ Insert'), ' on a phone). It is the same list, so whichever you learn first, the other holds no surprises. It comes in three groups: ', b('Insert'), ' (tables, pictures, code, layouts and the rest), ', b('Panels'), ', and ', b('Live content'), '.'),
    p('The + menu does one thing the slash menu cannot: it can wrap text you have already written. Select one or more paragraphs, then choose ', b('Blockquote'), ', ', b('Code block'), ', ', b('Expand'), ', ', b('Decision'), ', ', b('Excerpt'), ' or any of the panels, and your text goes inside it. Choose ', b('Link'), ' with words selected, and they become the link’s text.'),
    ...(await animation(slash, 'insert-wrap', 'A sentence selected, then + and Decision: it is marked as a decision.')),
    p('Anything else chosen with text selected replaces the selection, just as typing would.'),
    panel('success', p(b('Which to use?'), ' The slash menu while you are writing, because your hands stay on the keys. The + menu when you are browsing for ideas, or have text to wrap.')),
  ))

  // =========================================================== Text formatting
  await page('Text formatting', editor, doc(
    p('Formatting is how a reader skims. Bold picks out the words that matter, code marks what to type exactly, and a highlight says “look here”. There are three ways to apply it, so use whichever is nearest:'),
    ul(
      li(p(b('The menu over selected text.'), ' Select some words and it appears above them, with bold, italic, underline, strikethrough, inline code, highlight and link. See ', pageLink('Floating menus'), '.')),
      li(p(b('The toolbar,'), ' which has all of those except link, and also text color, subscript, superscript and clear formatting.')),
      li(p(b('The keyboard:'), ' a shortcut, or Markdown as you type (below).')),
    ),
    p('With nothing selected, a button or shortcut applies to whatever you type next.'),

    h(2, 'Each style, and when to use it'),
    p('The shortcuts are for Windows and Linux; on a Mac, use ⌘ where they say Ctrl.'),
    ul(
      li(p(mark('Bold', 'bold'), ': the few words a reader skimming must not miss. ', b('Ctrl+B'), ', or type ', c('**text**'), '.')),
      li(p(mark('Italic', 'italic'), ': a title, a term being defined, or a light stress. ', b('Ctrl+I'), ', or ', c('*text*'), '.')),
      li(p(mark('Underline', 'underline'), ': use it sparingly, because readers take underlined words for links. ', b('Ctrl+U'), '.')),
      li(p(mark('Strikethrough', 'strike'), ': something no longer true that readers should still see, such as a canceled date. ', b('Ctrl+Shift+S'), ', or ', c('~~text~~'), '.')),
      li(p(mark('Inline code', 'code'), ': anything to be typed exactly, such as a command, a file name or a setting. ', b('Ctrl+E'), ', or ', c('`text`'), '.')),
      li(p(mark('Highlight', 'highlight'), ': the one line someone must look at. ', b('Ctrl+Shift+H'), ', or ', c('==text=='), '. Other colors are in ', pageLink('Colors'), '.')),
      li(p('H', mark('2', 'subscript'), 'O and E = mc', mark('2', 'superscript'), ': subscript and superscript, for formulas and footnote marks. ', b('Ctrl+,'), ' and ', b('Ctrl+.'), '.')),
    ),

    h(2, 'Clearing formatting'),
    p(b('Clear formatting'), ' on the toolbar, or ', b('Ctrl+\\'), ', takes every style off the selected text, links and colors included. On a heading it also turns the heading back into normal text, and it puts the line back to the left with no indent.'),
    panel('success', p(b('Pasted text brought its own look with it?'), ' Text copied from a website or a document often carries its fonts, sizes and colors. Select it and press ', b('Ctrl+\\'), ' (', b('⌘\\'), ' on a Mac) to keep the words and drop the rest.')),

    h(2, 'Formatting as you type'),
    p('Type ', c('**done**'), ' and it turns bold the moment you type the closing stars. Every style with a Markdown form is listed in ', pageLink('Markdown shortcuts'), '.'),
  ))

  // ==================================================================== Colors
  const color = (t, name) => mark(t, 'textColor', { color: name })
  const hl = (t, hex) => mark(t, 'highlight', { color: hex })
  await page('Colors', editor, doc(
    p('Color draws the eye, which is exactly why a little goes a long way. Use it to carry meaning a reader picks up at a glance, like red for a risk, green for done, or a yellow highlight on the one line that matters, and it helps. Scatter it about and readers stop noticing it.'),

    h(2, 'Text color'),
    p('Select the words, choose ', b('Text color'), ' on the toolbar, and pick one of eight:'),
    p(color('Gray', 'grey'), ', ', color('Blue', 'blue'), ', ', color('Teal', 'teal'), ', ', color('Green', 'green'), ', ', color('Yellow', 'yellow'), ', ', color('Orange', 'orange'), ', ', color('Red', 'red'), ' and ', color('Purple', 'purple'), '.'),
    p(b('Default color'), ', at the end of the palette, takes it off again.'),

    h(2, 'Highlight'),
    p('A highlight colors the background behind the words, like a marker pen. Select the words, choose ', b('Highlight color'), ' on the toolbar, and pick one of twelve, six light and six stronger:'),
    p(hl('Light blue', '#deebff'), ', ', hl('light teal', '#e6fcff'), ', ', hl('light green', '#e3fcef'), ', ', hl('light yellow', '#fffae6'), ', ', hl('light red', '#ffebe6'), ', ', hl('light purple', '#eae6ff'), '.'),
    p(hl('Blue', '#b3d4ff'), ', ', hl('teal', '#b3f5ff'), ', ', hl('green', '#abf5d1'), ', ', hl('yellow', '#fff0b3'), ', ', hl('red', '#ffbdad'), ', ', hl('purple', '#c0b6f2'), '.'),
    p(b('No highlight'), ' takes it off. For a quick one, the highlight button in the menu over selected text, or ', b('Ctrl+Shift+H'), ', adds ', mark('a plain yellow highlight', 'highlight'), ' without asking for a color.'),

    h(2, 'In the dark theme'),
    p('Both stay readable when you, or a reader, switch to the dark theme: text colors turn lighter, and highlighted words keep dark lettering on their colored background.'),

    h(2, 'Good practice'),
    ul(
      li(p(b('Give each color one meaning, and keep to it.'), ' If red means “at risk” on one page, it should not mean “important” on the next.')),
      li(p(b('Never let color carry the meaning alone.'), ' Some readers cannot tell red from green. Write “at risk” as well as coloring it, or use a ', pageLink('Status'), ' label, which has its words built in.')),
      li(p(b('For a whole paragraph, use a panel.'), ' A colored box reads better than a colored paragraph. See ', pageLink('Panels'), '.')),
    ),
  ))

  // ================================================= Alignment and indentation
  await page('Alignment and indentation', editor, doc(
    p('Text reads best lined up on the left, which is where it starts. Alignment and indentation are for the exceptions: a centered line of thanks at the end of an announcement, or a note tucked in under the paragraph it belongs to. Both apply to whole paragraphs and headings, not to a few words.'),

    h(2, 'Alignment'),
    p(b('Left.'), ' Where every paragraph starts, and right for almost all of them.'),
    para({ textAlign: 'center' }, b('Centered.'), ' For a short line that stands on its own, such as a closing line.'),
    para({ textAlign: 'right' }, b('Right.'), ' For a signature, or a date at the end of a letter.'),
    para({ textAlign: 'justify' }, b('Justified.'), ' Both edges straight, like a printed book. The spaces between words stretch to make each line fill the width, which reads well in long paragraphs on a wide page and poorly in a narrow column, where the gaps grow large.'),
    p('Choose ', b('Alignment'), ' on the toolbar, or use a shortcut:'),
    table([
      ['Shortcut', 'Aligns'],
      ['Ctrl+Shift+L', 'Left'],
      ['Ctrl+Shift+E', 'Center'],
      ['Ctrl+Shift+R', 'Right'],
      ['Ctrl+Shift+J', 'Justified'],
    ], [240, 460]),

    h(2, 'Indentation'),
    p('The main point of a section, as a normal paragraph.'),
    para({ textIndent: 1 }, 'A detail that belongs to it, indented one step.'),
    para({ textIndent: 2 }, 'A detail of that detail, two steps in.'),
    p(b('Indent'), ' and ', b('Outdent'), ' on the toolbar, or ', b('Ctrl+]'), ' and ', b('Ctrl+['), ', move a paragraph or heading in or out by one step, up to four steps.'),
    panel('note', p(b('In a list, use Tab.'), ' Tab nests a list item under the one above it, and Shift+Tab moves it back out. Indent only moves the item’s text to the right. Tab does not indent an ordinary paragraph: outside lists and tables, use Indent.')),
    p('On a Mac, use ⌘ where the shortcuts say Ctrl. Clear formatting (', b('Ctrl+\\'), ') puts a paragraph back to the left with no indent.'),
  ))

  // ======================================================== Markdown shortcuts
  await page('Markdown shortcuts', editor, doc(
    p('If you have written in Markdown, or in a chat app that borrows from it, your fingers already know these. Type the characters and Tesria turns them into formatting as you go. If it changes something you did not mean it to, press ', b('Backspace'), ' straight away and you get your characters back.'),

    h(2, 'At the start of a line'),
    table([
      ['Type', 'You get'],
      ['# and a space', 'Heading 1'],
      ['## to ###### and a space', 'Heading 2 to Heading 6'],
      ['- or * or + and a space', 'Bullet list'],
      ['1. and a space', 'Ordered list'],
      ['[] and a space', 'Task list'],
      ['[x] and a space', 'Task list, already checked'],
      ['> and a space', 'Blockquote'],
      ['``` and Enter', 'Code block'],
      ['```python and Enter', 'Code block in Python'],
      ['---', 'Divider'],
    ], [300, 400]),
    p('An ordered list starts from whatever number you type: ', c('4. '), ' starts it at 4.'),

    h(2, 'Anywhere in a line'),
    table([
      ['Type', 'You get'],
      ['**text** or __text__', 'Bold'],
      ['*text* or _text_', 'Italic'],
      ['~~text~~', 'Strikethrough'],
      ['`text`', 'Inline code'],
      ['==text==', 'Highlight'],
      ['A web address, then a space', 'A link'],
      ['![description](https://…)', 'A picture from that address'],
    ], [300, 400]),
    p('Pasting a web address over selected words links the words to it.'),
    panel('note', p(b('Links written as [text](address) stay as they are.'), ' Select the words and press ', b('Ctrl+K'), ' (', b('⌘K'), ' on a Mac) instead.')),
  ))

  // ======================================================== Keyboard shortcuts
  const keys = (rows) => table([['Press', 'To'], ...rows], [260, 440])
  await page('Keyboard shortcuts', editor, doc(
    p('Everything the editor does with a key combination, grouped by what it is for. You do not need them all: learn the three or four you reach for most, and the rest will follow.'),
    panel('info', p(b('On a Mac,'), ' use ⌘ (Command) where these say Ctrl, and ⌥ (Option) where they say Alt. So ', b('Ctrl+B'), ' is ', b('⌘B'), ', and ', b('Ctrl+Alt+2'), ' is ', b('⌘⌥2'), '.')),

    h(2, 'Styling text'),
    keys([
      ['Ctrl+B', 'Bold'],
      ['Ctrl+I', 'Italic'],
      ['Ctrl+U', 'Underline'],
      ['Ctrl+Shift+S', 'Strikethrough'],
      ['Ctrl+E', 'Inline code'],
      ['Ctrl+Shift+H', 'Highlight'],
      ['Ctrl+,', 'Subscript'],
      ['Ctrl+.', 'Superscript'],
      ['Ctrl+K', 'Add or change a link'],
      ['Ctrl+\\', 'Clear formatting'],
    ]),

    h(2, 'Lines and blocks'),
    keys([
      ['Ctrl+Alt+0', 'Normal text'],
      ['Ctrl+Alt+1 to 6', 'Heading 1 to 6 (again for normal text)'],
      ['Ctrl+Shift+7', 'Ordered list'],
      ['Ctrl+Shift+8', 'Bullet list'],
      ['Ctrl+Shift+9', 'Task list'],
      ['Ctrl+Shift+B', 'Blockquote'],
      ['Ctrl+Alt+C', 'Code block'],
      ['Shift+Enter', 'A new line in the same paragraph'],
    ]),

    h(2, 'Paragraphs'),
    keys([
      ['Ctrl+Shift+L', 'Align left'],
      ['Ctrl+Shift+E', 'Align center'],
      ['Ctrl+Shift+R', 'Align right'],
      ['Ctrl+Shift+J', 'Justify'],
      ['Ctrl+]', 'Indent'],
      ['Ctrl+[', 'Outdent'],
    ]),

    h(2, 'Lists and tables'),
    keys([
      ['Tab', 'Nest a list item; in a table, the next cell'],
      ['Shift+Tab', 'Un-nest a list item; in a table, the cell before'],
      ['Tab in the last cell', 'Add a row to the table'],
      ['Enter on an empty item', 'Step out of the list, or out one level'],
    ]),

    h(2, 'Menus'),
    keys([
      ['/', 'The slash menu, for adding anything'],
      ['@', 'Mention someone'],
      [': and two letters', 'An emoji, such as :tada'],
      ['↑ and ↓', 'Move through the menu'],
      ['Enter', 'Choose (Tab works too for @ and :)'],
      ['Escape', 'Close the menu'],
    ]),
    p('Each works at the start of a line or after a space. See ', pageLink('The slash menu'), ', ', pageLink('Mention'), ' and ', pageLink('Emoji'), '.'),

    h(2, 'Undo'),
    keys([
      ['Ctrl+Z', 'Undo'],
      ['Ctrl+Shift+Z or Ctrl+Y', 'Redo'],
    ]),
    p('With live editing on, undo takes back only your own changes, never someone else’s.'),

    h(2, 'Outside the page'),
    ul(
      li(p(b('Enter'), ' in the title moves to the first line of the page. It never publishes.')),
    ),
  ))

  // ============================================================ Floating menus
  await page('Floating menus', editor, doc(
    p('Some controls stay out of the way until you need them, then appear right where you are working: over the words you have selected, around the table you are in, above the picture you clicked. This page lists them all, so you know what to look for. Each one disappears again when you click elsewhere.'),

    h(2, 'Over selected text'),
    p('Select some words and a small menu appears above them: ', b('Bold'), ', ', b('Italic'), ', ', b('Underline'), ', ', b('Strikethrough'), ', ', b('Inline code'), ', ', b('Highlight'), ', ', b('Add link'), ' and ', b('Comment'), '.'),
    ...(await animation(menus, 'selection-bubble', 'Selecting a sentence brings up the menu. Here the sentence is made bold, then highlighted.')),
    p(b('Comment'), ' starts a comment attached to exactly those words, which is the clearest way to ask about one sentence rather than the whole page. See ', pageLink('Comments'), '.'),
    p('The menu does not appear inside a code block, a link or a picture, which have menus of their own.'),

    h(2, 'In a link'),
    p('Click into a link and its address appears below it, with ', b('Edit'), ' and ', b('Remove'), '. Links do not open when clicked in the editor, so clicking one to change it never takes you away from your page; click the address to open it in a new tab. See ', pageLink('Link'), '.'),

    h(2, 'In a panel, expand, decision, excerpt or page properties'),
    p('A bar appears above the box the cursor is in. For a ', pageLink('Panels', 'panel'), ', five buttons change its type. For all of them, a remove button (', b('Remove panel'), ', ', b('Remove expand'), ' and so on) takes the box away and keeps everything that was in it, as ordinary text.'),

    h(2, 'In a layout'),
    p('Click into a column and a bar appears above the columns: the five arrangements (two or three columns, with or without sidebars), ', b('Centered'), ', ', b('Wide'), ' and ', b('Full width'), ', and ', b('Remove layout'), '. See ', pageLink('Layout'), '.'),
    p('It shows only while the cursor is in the column’s own text. Click into a panel, a table or a picture inside a column and that thing’s menu shows instead, so two menus never sit on top of each other. Click back into the text and the layout’s bar returns.'),

    h(2, 'On a picture'),
    p('Click a picture and a bar appears above it:'),
    ul(
      li(p(b('Border'), ' and ', b('Shadow'), ', each on or off.')),
      li(p(b('Left'), ', ', b('Center'), ', ', b('Right'), ' and ', b('Full'), ', to place it. ', b('Original size'), ' appears once you have resized it.')),
      li(p(b('Caption'), ', a line under the picture, and ', b('Alt text'), ', what it shows, for people who cannot see it.')),
      li(p(b('Comment'), ', about the picture.')),
    ),
    p('Drag the small square at its bottom right corner to make it bigger or smaller. See ', pageLink('Image'), '.'),

    h(2, 'In a table'),
    p('Point at a table and its controls appear around it:'),
    ul(
      li(p(b('+'), ' between the rows and columns adds one there.')),
      li(p(b('×'), ' beside a row or above a column deletes it.')),
      li(p(b('⤢'), ' above the right corner makes the table full width; the right edge drags it wider or narrower.')),
    ),
    p('On a touch screen, tap inside the table to show them.'),
    p('The cell the cursor is in also has a small arrow at its top right, ', b('Cell options'), ', which opens:'),
    ul(
      li(p(b('Header row'), ' and ', b('Header column'), ', on or off.')),
      li(p(b('Merge cells'), ', once you have selected two or more cells by dragging across them, and ', b('Split cell'), ' to undo a merge.')),
      li(p(b('Delete table'), '.')),
      li(p(b('Background color'), ': choose ', b('Cell'), ', ', b('Row'), ' or ', b('Column'), ', then a color, to color that cell, its whole row or its whole column. ', b('No color'), ' takes it off.')),
    ),
    ...(await picture(menus, 'table-cell-menu', 'Cell options open on a new table, with the arrow that opens it boxed', 'The boxed arrow opens Cell options. The + and × around the table add and delete rows and columns.')),
    p('See ', pageLink('Table'), ' for everything else a table can do.'),

    h(2, 'Everything else'),
    p('Click a ', pageLink('Status'), ', a ', pageLink('Date'), ', a ', pageLink('Table of contents'), ' or a ', pageLink('Live content', 'live content block'), ' and its settings appear. Each element’s page describes its own.'),
  ))

  // ================================================================= Elements
  await page('Elements', editor, doc(
    p('An element is anything you add to a page besides plain text: a heading, a checklist, a colored panel, a table, a video. Each has a page of its own here, with what it is for, how to add it, every variant shown for real, and a few tips for using it well.'),
    p('Add any of them by typing ', c('/'), ' on a new line, or from ', b('+'), ' on the toolbar. See ', pageLink('The slash menu'), '.'),
    panel('success', p(b('Not sure which you need?'), ' Type ', c('/'), ' and read down the list: every item says in a few words what it does.')),
    ...ELEMENT_GROUPS.flatMap(group),
    p('And for blocks that fill themselves in, such as a list of the pages under this one, see ', pageLink('Live content'), '.'),
  ))

  // ============================================================= Live content
  await page('Live content', editor, doc(
    p('Most of a page is what someone wrote. Live content is different: a block that looks something up each time the page is read, and shows the answer. A list of the pages under this one that grows as pages are added; the last ten changes in the space; every open task assigned to you, wherever it was written. Nobody has to keep them up to date, because they do it themselves.'),
    panel('success', p(b('For example,'), ' a project’s front page with ', pageLink('Children display'), ' at the top, a ', pageLink('Task report'), ' of the open tasks under it and ', pageLink('Recently updated'), ' at the bottom stays current with no one looking after it.')),

    h(2, 'How they work'),
    ul(
      li(p(b('Add one'), ' from the slash menu, such as ', c('/children'), ', or from ', b('+'), ' on the toolbar, under ', b('Live content'), '.')),
      li(p(b('Click it'), ' to change its settings, such as how many pages to list. It shows the new result straight away.')),
      li(p(b('↻'), ', beside the block’s name, looks again without reloading the page.')),
      li(p(b('Everyone sees only what they may see.'), ' A page a reader is not allowed to open never appears in their list, whoever added the block.')),
      li(p(b('An export'), ' writes each block out as it stood at that moment.')),
    ),
    ...LIVE_GROUPS.flatMap(group),
  ))
}

// Pictures these pages no longer use: the first version's side-by-side
// figures, taken down so they do not linger in the attachments or in the
// exported pack. The element pages' own el-*.png files belong to the modules
// that now write those pages.
const RETIRED = {
  'Editor tour': ['editor.png', 'editor.phone.png', 'toolbar.png', 'toolbar.phone.png'],
  'The slash menu': ['slash-menu.png', 'slash-menu.phone.png', 'insert-menu.png', 'insert-menu.phone.png'],
  'Floating menus': ['selection-bubble.png', 'selection-bubble.phone.png'],
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
  const editorNode = find(tree, 'The editor')
  if (!editorNode) return
  for (const [title, files] of Object.entries(RETIRED)) {
    const node = (editorNode.children ?? []).find((n) => n.title === title)
    if (!node) continue
    for (const a of await author.call('GET', `/api/pages/${node.id}/attachments`)) {
      if (files.includes(a.filename)) {
        await author.call('DELETE', `/api/attachments/${a.id}`)
        console.log(`  - ${title}: ${a.filename}`)
      }
    }
  }
}
