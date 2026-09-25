// User manual: Tesria on phones and tablets (added by the owner, 2026-09-22),
// rewritten to the owner's rules of 2026-09-23 (WRITING.md, pilot.mjs).
//
// Facts checked 2026-09-24 against the phone layout (640 pixels and
// narrower): Layout (the ☰ menu, search inside it), SpacePage, SpaceHome,
// Toolbar and useToolbarOverflow (Aa Style and + Insert), useHoveredTable
// and TableControls (controls on a tap wherever there is no hover),
// TableCellMenu, TableWidthControls (mouse drag only), LinkMenu,
// useVisualViewportOffset (the sticky bars and the keyboard), index.css's
// 16px rule for form fields on phones and touch screens (the iOS zoom fix),
// ProfilePage's Trust this device card, the /trust guide (TrustEndpoints)
// and SessionsSection.
//
// This is the chapter where phone pictures belong: every shot is taken on a
// phone only (desktop: false). The editor ones are on a new Tesria Demo page
// that is closed without publishing, which discards its draft.
//
// New page: "Setting up a phone or tablet", placed first in the chapter by
// cleanup(). No page is proposed for removal.

export const shots = ({ demo }) => [
  // ---- Setting up a phone: the device choice and the download button in
  // the Trust this device guide, as a phone shows it.
  {
    name: 'phone-trust', desktop: false, url: '/trust', settle: 800,
    steps: [{ wait: 1500 }, { type: 'wiki-server.local', selector: '#trust-address' }, { click: '[data-device="ios"]' }, { wait: 400 }],
    clipTo: '[data-step="3"]', clipPad: 12,
    annotate: [{ type: 'box', target: '.trust-guide[data-for="ios"] [data-cert]', pad: 4 }],
  },

  // ---- The top bar and its menu.
  {
    name: 'phone-page', desktop: false, url: demo('Launch plan'), settle: 1500, steps: [{ wait: 2500 }],
    annotate: [{ type: 'box', target: '.topbar__hamburger', pad: 4 }],
  },
  {
    name: 'phone-menu', desktop: false, url: demo('Launch plan'), settle: 800,
    steps: [{ wait: 2500 }, { click: '.topbar__hamburger' }, { wait: 600 }],
    annotate: [{ type: 'box', target: '.topbar__search', pad: 4 }],
  },

  // ---- Moving around a space.
  { name: 'phone-space-home', desktop: false, url: '/spaces/DEMO', settle: 1500, steps: [{ wait: 2500 }] },
  {
    name: 'phone-space-actions', desktop: false, url: '/spaces/DEMO', settle: 800,
    steps: [{ wait: 2500 }, { click: 'button[title="Space actions"]' }, { wait: 500 }],
  },

  // ---- The editor, on one new page: its toolbar, both menus, a table and
  // the cell options; then closed without publishing.
  {
    name: 'phone-editor', desktop: false, url: '/spaces/DEMO/new', settle: 800,
    steps: [
      { wait: 3500 },
      { type: 'Release notes', selector: 'input[placeholder="Page title"]' },
      { click: '.ProseMirror' },
      { keys: 'What changed in this release, and why.' },
    ],
    annotate: [
      { type: 'box', target: '.toolbar-dropdown--text .toolbar-dropdown__trigger', pad: 3 },
      { type: 'box', target: '.toolbar__insert', pad: 3 },
    ],
  },
  {
    name: 'phone-style-menu', desktop: false, settle: 600,
    steps: [{ click: '.toolbar-dropdown--text .toolbar-dropdown__trigger' }, { wait: 500 }],
  },
  {
    name: 'phone-insert-menu', desktop: false, settle: 600,
    steps: [{ press: 'Escape', selector: 'body' }, { click: '.toolbar__insert' }, { wait: 500 }],
  },
  {
    name: 'phone-table', desktop: false, settle: 800,
    steps: [
      { press: 'Escape', selector: 'body' },
      { click: '.ProseMirror p' },
      { press: 'End', selector: '.ProseMirror' },
      { press: 'Enter', selector: '.ProseMirror' },
      { keys: '/table' }, { wait: 500 }, { press: 'Enter', selector: '.ProseMirror' }, { wait: 500 },
      { keys: 'Plan' }, { wait: 500 },
    ],
    annotate: [
      { type: 'box', target: '.table-hover__add--col', nth: 1, pad: 3 },
      { type: 'box', target: '.table-hover__grip--row', nth: 1, pad: 3 },
      { type: 'box', target: '.cell-menu__trigger', pad: 3 },
    ],
  },
  {
    name: 'phone-cell-menu', desktop: false, settle: 600,
    steps: [{ click: '.cell-menu__trigger' }, { wait: 500 }],
  },
  { name: 'phone-discarded', desktop: false, settle: 300, skipCapture: true, steps: [{ press: 'Escape', selector: 'body' }, { click: '.page-actionbar button:has-text("Close")' }, { wait: 1200 }] },
]

export async function build({ top, page, ensure, doc, p, h, text, bold, italic, code, ul, ol, li, panel, table, live, phonePicture, pageLink }) {
  const manual = top['User manual']
  const b = (t) => text(t, bold)
  const i = (t) => text(t, italic)
  const c = (t) => text(t, code)

  // Every page first, so links between them resolve on a first run.
  const mobile = await ensure('Tesria on phones and tablets', manual)
  const setup = await ensure('Setting up a phone or tablet', mobile)
  const bar = await ensure('The phone top bar and menu', mobile)
  const moving = await ensure('Moving between spaces and pages', mobile)
  const editor = await ensure('The editor on a phone', mobile)
  const tables = await ensure('Tables by touch', mobile)
  await ensure('The keyboard and the sticky bars', mobile)
  await ensure('What a phone does not offer', mobile)

  await page('Tesria on phones and tablets', manual, doc(
    p('Tesria works on a phone or a tablet, in portrait and in landscape, with nothing to install: you open it in the browser, as on a computer. You can read, search, comment and write with the full editor, and an administrator can run the whole wiki from one.'),
    p('What changes is the layout. On a screen 640 pixels wide or narrower, which means any phone, Tesria rearranges itself for a thumb: the sidebar moves into a menu, and the editor’s toolbar folds into two buttons. A tablet is wide enough for the computer layout, but still gets the touch features, such as table controls that appear on a tap.'),
    p('This chapter shows what is different, starting with getting a phone ready.'),
    live('children', { depth: '1', sort: 'position' }),
  ))

  await page('Setting up a phone or tablet', mobile, doc(
    p('A phone needs the same two things as a computer before Tesria is comfortable to use on it: the right address, and, on a Tesria that runs on your own network, trust in its security certificate. Both take a few minutes, once.'),

    step(1, 'Open Tesria by its name'),
    p('Open Tesria in the phone’s browser at the same address you use on your computer, such as ', c('https://wiki-server.local'), '. If Tesria runs on your own network, the phone has to be on that network too, and the name matters: opened by a number such as 192.168.1.50, the secure connection does not work. See ', pageLink('Opening Tesria by name'), '.'),

    step(2, 'Trust the server, if the browser warns you'),
    p('If the browser says the connection is not private, your Tesria makes its own certificate, and the phone has to be told to trust it. The ', b('Trust this device'), ' guide walks you through it. On the phone, open ', c('http://your-server/trust'), ' (with ', c('http'), ', not ', c('https'), '), where ', c('your-server'), ' is your Tesria’s address, and choose your kind of phone.'),
    ...(await phonePicture(setup, 'phone-trust', 'The Trust this device guide on an iPhone', 'The guide’s fourth step on an iPhone: download the certificate, compare its fingerprint, then switch trust on in Settings.')),
    p('You also need the server’s ', b('fingerprint'), ', a long code that only your server’s certificate has, so you can check that the certificate your phone received is really your server’s. Whoever runs your Tesria can read it on the server; ', pageLink('Trusting the local certificate'), ' says where.'),
    ul(
      li(p(b('On an iPhone or iPad,'), ' use ', b('Safari'), ': other browsers there cannot install certificates. It takes three parts: installing the certificate, comparing its fingerprint in the Settings app, and then switching on trust for it.')),
      li(p(b('On Android,'), ' the certificate is installed from Settings, and trusted at once, so compare its fingerprint straight afterwards. The menus differ a little between phone makers; the guide says where to look.')),
    ),
    p('Every step, with what to tap, is in ', pageLink('Trusting the local certificate'), ', under ', b('On a phone'), '. Once you are signed in, the same guide is on your profile, under ', b('Trust this device'), '.'),

    step(3, 'Sign in'),
    p('Sign in as on a computer. If you use two-factor sign-in, the code comes from the authenticator app as usual, even when that app is on the same phone.'),

    h(2, 'Signed in somewhere you should not be?'),
    p('Every phone you sign in on appears in your profile’s ', b('Sessions'), ' card, as something like ', i('Safari on iOS'), ' or ', i('Chrome on Android'), '. If you lose a phone, sign it out from there on any other device. See ', pageLink('Sessions'), '.'),
  ))

  await page('The phone top bar and menu', mobile, doc(
    p('On a computer, the top bar holds everything at once: Spaces, search, the bell and your profile. A phone is too narrow for that, so the top bar keeps only what you reach for most, and the rest goes behind the ☰ button at its left.'),
    ...(await phonePicture(bar, 'phone-page', 'A page on a phone', 'A page on a phone. ☰ opens the menu; below the top bar are Edit, + New and the page’s ⋮ menu.')),
    p('The top bar keeps the ☰ button, the logo (tap it for the list of spaces), the light and dark switch, the bell, your avatar for your profile, and ', b('Sign out'), '.'),
    h(2, 'The ☰ menu'),
    p('Tap ☰ and the menu opens under the top bar:'),
    ul(
      li(p(b('Spaces,'), ' and ', b('Admin'), ' or ', b('Invite people'), ' if your role has them.')),
      li(p(b('Search pages…'), ', the search box. Type and tap ', b('Go'), ' or ', b('Enter'), ' on the keyboard.')),
      li(p(b('The space you are in:'), ' its name, ', b('+ New page'), ', its whole page tree and ', b('Space settings'), '. Tap a page to go to it.')),
    ),
    ...(await phonePicture(bar, 'phone-menu', 'The ☰ menu on a phone', 'The ☰ menu, inside a space: the search box, then the space’s pages.')),
    p('Tap ✕, tap outside the menu, or choose anything in it to close it.'),
  ))

  await page('Moving between spaces and pages', mobile, doc(
    p('On a computer, a space’s page tree sits in a sidebar beside every page. A phone has no room for that, so the tree is in two other places instead:'),
    ul(
      li(p(b('In the ☰ menu,'), ' from any page in the space. This is the quick way from one page to another.')),
      li(p(b('On the space’s home page,'), ' under its name and description. There you can also reorder pages, the same way as in the sidebar.')),
    ),
    ...(await phonePicture(moving, 'phone-space-home', 'A space’s home on a phone', 'A space’s home on a phone, with its page tree.')),
    p('Under the top bar, a space’s home has ', b('+ New'), ' to create a page, and a ', b('⋮'), ' menu with ', b('Space settings'), ', which holds the space’s permissions, templates, webhooks and trash as well.'),
    ...(await phonePicture(moving, 'phone-space-actions', 'The space’s ⋮ menu on a phone', 'The ⋮ menu beside a space’s name leads to its settings.')),
    p('On a page, ', b('+ New'), ' beside ', b('Edit'), ' creates a new page under the one you are reading. On a computer the same is in the sidebar.'),
  ))

  await page('The editor on a phone', mobile, doc(
    p('The editor on a phone is the full editor: every element, every kind of formatting, tables and live content. Only the toolbar is different. The row of buttons a computer shows would not fit, so it folds into two menus, beside ', b('Publish'), ' (or ', b('Update'), ') and ', b('Close'), '.'),
    ...(await phonePicture(editor, 'phone-editor', 'The editor on a phone', 'The editor on a phone, with its two menus: Aa Style and + Insert.')),
    table([
      ['Menu', 'Holds'],
      ['Aa Style', 'Normal text, headings, and all formatting'],
      ['+ Insert', 'Every element, as in the slash menu'],
    ], [160, 540]),
    h(2, 'Aa Style'),
    p('At the top, ', b('Normal text'), ' and the six heading sizes. Below them, every formatting control from the computer’s toolbar: bold, italic and the rest under ', b('Format'), ', both color palettes under ', b('Color'), ', and lists, indentation and alignment under ', b('Paragraph'), '.'),
    ...(await phonePicture(editor, 'phone-style-menu', 'The Aa Style menu', 'Aa Style: text styles first, then every formatting control.')),
    h(2, '+ Insert'),
    p('Everything you can add to a page: tables, panels, pictures, code, dates, and every live content block. It is the same list as the slash menu.'),
    ...(await phonePicture(editor, 'phone-insert-menu', 'The + Insert menu', '+ Insert holds every element.')),
    h(2, 'Good to know'),
    ul(
      li(p(b('Typing / opens the slash menu,'), ' as on a computer. It is often quicker than + Insert: type ', c('/table'), ' and tap the match.')),
      li(p(b('Formatting lands where your cursor was.'), ' Opening a menu does not lose your place, so a heading choice changes the line you were on.')),
      li(p(b('Enter in the title moves to the page’s text;'), ' it does not publish the page.')),
      li(p(b('Tapping a link in the editor shows its address,'), ' with ', b('Edit'), ' and ', b('Remove'), ', instead of following it. On a published page, tapping a link follows it.')),
      li(p(b('Columns in a layout stack'), ' one above the other, so each stays readable.')),
    ),
  ))

  await page('Tables by touch', mobile, doc(
    p('On a computer, a table’s controls appear when the pointer moves over it. A touch screen has no pointer to hover, so there the controls appear when you tap inside the table, and go when you tap outside it. This works on any touch screen, tablets included, whatever its size.'),
    ...(await phonePicture(tables, 'phone-table', 'Editing a table on a phone', 'A table on a phone, with its controls: + adds a column or row, × deletes one, and the arrow opens the cell options.')),
    ul(
      li(p(b('+'), ' along the top adds a column at that point; ', b('+'), ' down the left side adds a row.')),
      li(p(b('×'), ' above a column, or beside a row, deletes it.')),
      li(p(b('The arrow in the cell'), ' you are in opens the cell options: header row, header column, merging and splitting cells, deleting the whole table, and a background color for the cell, its row or its column.')),
    ),
    ...(await phonePicture(tables, 'phone-cell-menu', 'The cell options on a phone', 'The cell options, from the arrow in the cell.')),
    h(2, 'Wide tables'),
    p('A table wider than the screen scrolls sideways; swipe it left and right. Each column keeps a readable width rather than being squeezed to fit, so a table with many columns stays legible.'),
    panel('note', p(b('Resizing is for a mouse or trackpad.'), ' Dragging a column border, or a table’s edge, does not work by touch. The table’s full-width button, which widens it to the whole page on a computer, does work with a tap.')),
  ))

  await page('The keyboard and the sticky bars', mobile, doc(
    p('The top bar and the editor’s toolbar stay at the top of the screen as you scroll, so the menus and ', b('Publish'), ' are always a tap away.'),
    h(2, 'When the keyboard opens'),
    p('On an iPhone in particular, opening the keyboard moves the visible part of the page, and bars that stay at the top of the page would slide out of sight. Tesria follows the visible part instead, so the top bar and the toolbar stay at the top of what you can see, above your typing and never behind the keyboard.'),
    h(2, 'No zooming when you tap a field'),
    p('Safari on an iPhone zooms in on any text field whose writing is smaller than a certain size, and stays zoomed after you leave the field. It makes a page suddenly too big, and pushes the bars partly off the screen.'),
    p('Tesria avoids it: on a phone, and on any touch screen, every text field, menu and box you type in uses writing large enough that the browser leaves the page alone. Signing in, searching, filtering the page tree and writing a comment all keep the page the size you had it.'),
    panel('info', p(b('Zoomed in anyway?'), ' Pinch with two fingers to zoom out. You can always zoom a page yourself; Tesria only stops the browser doing it for you.')),
  ))

  await page('What a phone does not offer', mobile, doc(
    p('Nearly everything is the same on a phone as on a computer, administration included. These few things are not:'),
    ul(
      li(p(b('Resizing tables by dragging.'), ' Column borders and a table’s edge need a mouse or trackpad. The table’s full-width button still works. See ', pageLink('Tables by touch'), '.')),
      li(p(b('A page’s Full width button.'), ' A phone’s page already fills the screen, so the button is hidden.')),
      li(p(b('The sidebar, and hiding it.'), ' Its page tree is in the ☰ menu and on the space’s home instead. See ', pageLink('Moving between spaces and pages'), '.')),
      li(p(b('Keyboard shortcuts,'), ' unless a keyboard is connected to the phone or tablet.')),
    ),
  ))

  /** A numbered step: a heading that says what to do, then how. */
  function step(n, title) { return h(3, `Step ${n}: ${title}`) }
}

// The new page goes first in the chapter, before the tour of the layout.
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
  const mobile = find(tree, 'Tesria on phones and tablets')
  if (!mobile) return
  const kids = mobile.children ?? []
  const at = kids.findIndex((n) => n.title === 'Setting up a phone or tablet')
  if (at > 0) {
    await author.call('PUT', `/api/pages/${kids[at].id}/move`, { parentPageId: mobile.id, index: 0 })
    console.log('  moved Setting up a phone or tablet to the start of its chapter')
  }
}
