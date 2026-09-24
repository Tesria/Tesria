// User manual: Tesria on phones and tablets (added by the owner, 2026-09-22).
//
// Facts from the phone layout (640px and narrower): Layout, SpacePage,
// SpaceHome, Toolbar, useToolbarOverflow, useHoveredTable, the sticky bars
// and index.css's phone rules, gathered 2026-09-23. Every picture is taken on
// a phone only; the editor ones on a new Tesria Demo page that is closed
// without publishing.

export const shots = ({ demo }) => [
  { name: 'phone-page', desktop: false, url: demo('Launch plan'), settle: 1500, steps: [{ wait: 2500 }] },
  {
    name: 'phone-menu', desktop: false, url: demo('Launch plan'), settle: 800,
    steps: [{ wait: 2500 }, { click: '.topbar__hamburger' }, { wait: 600 }],
  },
  { name: 'phone-space-home', desktop: false, url: '/spaces/DEMO', settle: 1500, steps: [{ wait: 2500 }] },
  {
    name: 'phone-space-actions', desktop: false, url: '/spaces/DEMO', settle: 800,
    steps: [{ wait: 2500 }, { click: 'button[title="Space actions"]' }, { wait: 500 }],
  },
  {
    name: 'phone-editor', desktop: false, url: '/spaces/DEMO/new', settle: 800,
    steps: [
      { wait: 3500 },
      { type: 'Release notes', selector: 'input[placeholder="Page title"]' },
      { click: '.ProseMirror' },
      { keys: 'What changed in this release, and why.' },
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
  },
  { name: 'phone-discarded', desktop: false, settle: 300, skipCapture: true, steps: [{ click: '.page-actionbar button:has-text("Close")' }, { wait: 1200 }] },
]

export async function build({ top, page, ensure, phoneFigure, doc, p, h, text, bold, ul, li, panel, table, live }) {
  const manual = top['User manual']
  const b = (t) => text(t, bold)

  const mobile = await ensure('Tesria on phones and tablets', manual)
  await page('Tesria on phones and tablets', manual, doc(
    p('Everything in Tesria works on a phone or a tablet, in portrait and landscape: reading, searching, commenting, and writing with the full editor. On a screen narrower than a small tablet (640 pixels), the layout changes to suit a thumb. These pages say what changes.'),
    p('Every element page in the manual shows the element on a phone as well as on a computer.'),
    live('children', { depth: '1', sort: 'position' }),
  ))

  const bar = await ensure('The phone top bar and menu', mobile)
  await page('The phone top bar and menu', mobile, doc(
    p('On a phone the top bar keeps the logo, search, the bell and a ☰ button. Everything else is in the menu that ☰ opens.'),
    ...(await phoneFigure(bar, 'phone-page', 'A page on a phone', 'A page on a phone: the top bar, then the page bar with Edit, + New and ⋮.')),
    ...(await phoneFigure(bar, 'phone-menu', 'The phone menu', 'The ☰ menu: Spaces, Admin and your profile, and inside a space, that space’s pages.')),
    p('Inside a space, the menu also holds the space’s name, ', b('+ New page'), ' and its page tree, so you can move around a space without leaving the page you are on.'),
  ))

  const moving = await ensure('Moving between spaces and pages', mobile)
  await page('Moving between spaces and pages', mobile, doc(
    p('The sidebar that sits beside a page on a computer is not shown on a phone. Instead:'),
    ul(
      li(p('A space’s home page shows its page tree, where you can also reorder pages by dragging.')),
      li(p('The ☰ menu shows the current space’s tree from any page in it.')),
      li(p('The bar under the top bar holds ', b('+ New'), ' and a ', b('⋮'), ' menu with ', b('Space settings'), '.')),
    ),
    ...(await phoneFigure(moving, 'phone-space-home', 'A space’s home on a phone', 'A space’s home on a phone, with its page tree.')),
    ...(await phoneFigure(moving, 'phone-space-actions', 'The space actions menu', 'The ⋮ menu beside a space’s name.')),
    p('On a page, ', b('+ New'), ' beside ', b('Edit'), ' starts a sub-page of the page you are reading.'),
  ))

  const editor = await ensure('The editor on a phone', mobile)
  await page('The editor on a phone', mobile, doc(
    p('The toolbar shrinks to two buttons, beside ', b('Publish'), ' or ', b('Update'), ' and ', b('Close'), ':'),
    ...(await phoneFigure(editor, 'phone-editor', 'The editor on a phone', 'The editor on a phone: Aa Style and + Insert.')),
    table([
      ['Button', 'Holds'],
      ['Aa Style', 'Normal text and the six headings, then every formatting control from the computer toolbar under Format, Color and Paragraph: bold to superscript, both color palettes, lists, indentation, alignment, and clear formatting.'],
      ['+ Insert', 'Every element, panel and live content block, the same list as the slash menu.'],
    ], [160, 540]),
    ...(await phoneFigure(editor, 'phone-style-menu', 'The Style menu', 'The Style menu holds every formatting control.')),
    ...(await phoneFigure(editor, 'phone-insert-menu', 'The Insert menu', 'The Insert menu.')),
    ul(
      li(p('Typing ', b('/'), ' opens the slash menu, as on a computer.')),
      li(p('A formatting choice applies where your cursor was before the menu opened, so a heading lands on the line you were on.')),
      li(p('Return in the title moves to the page body; it does not publish.')),
      li(p('Tapping a link shows its address, with Edit and Remove, instead of following it.')),
      li(p('Columns in a layout stack one above another.')),
    ),
  ))

  const tables = await ensure('Tables by touch', mobile)
  await page('Tables by touch', mobile, doc(
    p('Tap inside a table to show its controls: ', b('+'), ' buttons to add a row or column, and ', b('×'), ' to delete one. On a computer these appear when the pointer is over the table; any device without a pointer shows them on a tap instead, tablets included.'),
    ...(await phoneFigure(tables, 'phone-table', 'Editing a table on a phone', 'Editing a table on a phone. Wide tables scroll sideways.')),
    ul(
      li(p('A table wider than the screen scrolls sideways, and each column keeps a readable width rather than being squeezed to fit.')),
      li(p('The arrow in a cell opens the cell options, for background colors.')),
    ),
  ))

  await page('The keyboard and the sticky bars', mobile, doc(
    p('The top bar and the editor’s toolbar stay at the top of the screen as you scroll. When the phone’s keyboard opens, both move to stay at the top of what you can see, so the toolbar is never hidden behind the keyboard or scrolled away.'),
    p('Text fields use a large enough font that the phone does not zoom in on them when you tap, so the page stays the size you set.'),
  ))

  await page('What a phone does not offer', mobile, doc(
    ul(
      li(p(b('Resizing tables'), ': dragging a column border or the table’s edge needs a mouse or trackpad.')),
      li(p(b('Full width'), ': a phone’s page is already as wide as the screen, so the button is hidden.')),
      li(p(b('Keyboard shortcuts'), ', unless a keyboard is attached to the tablet or phone.')),
      li(p(b('Hiding the sidebar'), ': there is no sidebar on a phone to hide.')),
    ),
    panel('info', p('Everything else is the same on a phone as on a computer, including administration.')),
  ))
}
