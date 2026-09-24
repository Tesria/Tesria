// User manual, The editor: the editor in general, one page per element, and
// one per live content block (dev-plan 10.5).
//
// Facts from src/web/src/editor (the slash items, toolbar, menus, node views
// and extensions) and the live-content kinds, gathered 2026-09-23, after that
// day's editor fixes (Normal text in the slash menu, heading aliases, wrapping
// from the + menu, the gallery grid, image files in File or video).
//
// Every element page shows the element on its Tesria Demo page, on a desktop
// and a phone. The editor's own pictures are taken on a new page in Tesria
// Demo that is closed without publishing, so nothing is left behind.

// ------------------------------------------------------------ The elements
// title: the manual page, and the Demo page shown (unless `demo` says other).
const ELEMENTS = [
  {
    title: 'Normal text', slash: '/text',
    intro: 'Plain body text: what you get when you start typing.',
    insert: ['Toolbar: the style menu (Aa) → Normal text.', 'Keyboard: Mod+Alt+0.', 'Clear formatting (Mod+\\) also turns a heading back into normal text.'],
  },
  {
    title: 'Headings', slash: '/h1 to /h6',
    intro: 'Six levels of heading, for the sections of a page. Headings are what the table of contents lists and what links to a part of a page point at.',
    insert: ['Markdown: # to ###### followed by a space.', 'Toolbar: the style menu (Aa).', 'Keyboard: Mod+Alt+1 to Mod+Alt+6.'],
    notes: ['Every heading gets an anchor, so a link can point at it: the link dialog lists the page’s headings.', 'Two headings with the same text get anchors ending -2, -3 and so on.'],
  },
  {
    title: 'Blockquote', slash: '/quote',
    intro: 'Quoted text, set off with a bar at the left.',
    insert: ['Markdown: > followed by a space.', 'Keyboard: Mod+Shift+B.', 'The + menu, which wraps any text you have selected.'],
  },
  {
    title: 'Divider', slash: '/divider or /hr',
    intro: 'A horizontal line between sections.',
    insert: ['Markdown: --- or *** or ___ on a line of its own.', 'The + menu.'],
  },
  {
    title: 'Bullet list', slash: '/ul or /bullet',
    intro: 'A list of points. Items nest, to any depth.',
    insert: ['Markdown: - or * or + followed by a space.', 'Toolbar: the bullet list button.', 'Keyboard: Mod+Shift+8.'],
    notes: ['Tab nests an item under the one above; Shift+Tab moves it back out.', 'Return on an empty item ends the list.'],
  },
  {
    title: 'Ordered list', slash: '/ol or /numbered',
    intro: 'A numbered list, for steps in order.',
    insert: ['Markdown: 1. followed by a space. Starting with another number starts the list there.', 'Toolbar: the ordered list button.', 'Keyboard: Mod+Shift+7.'],
    notes: ['Tab and Shift+Tab nest and un-nest items.'],
  },
  {
    title: 'Task list', slash: '/todo',
    intro: 'Checkboxes, for things to do. Anyone who can read the page sees which are done.',
    insert: ['Markdown: [] or [ ] followed by a space; [x] makes a checked item.', 'Toolbar: the task list button.', 'Keyboard: Mod+Shift+9.'],
    notes: ['Mention someone in a task (type @ and their name) and they become its assignee. The Task report live content block collects tasks by assignee.', 'Tasks nest, like other list items.'],
  },
  {
    title: 'Link', slash: '/link',
    intro: 'A link to another page, a website, an email address, or a heading on this page.',
    insert: ['Keyboard: Mod+K.', 'Select text and choose the link button in the bubble that appears.', 'Type or paste a web address: it becomes a link when you press space. Pasting an address over selected text links the text.'],
    options: [['Address', 'Where it goes. https:// is added if you leave it out.'], ['Display text', 'The words that carry the link.'], ['Headings on this page', 'Choose one to link to that part of the page.']],
    notes: ['In the editor a link does not open when clicked: clicking shows its address, with Edit and Remove. Readers’ clicks open it in a new tab.', 'Markdown’s [text](address) is not converted; use Mod+K.'],
  },
  {
    title: 'Panels', slash: '/info, /note, /tip, /warning or /error (or /panel)',
    intro: 'A colored box that makes a paragraph stand out: Info (blue), Note (purple), Tip (green), Warning (yellow) and Error (red).',
    insert: ['The + menu → Panels, which wraps whatever you have selected.'],
    options: [['Type', 'The five buttons above the panel switch between the types.'], ['Remove panel', 'Takes the panel away and keeps what was inside it.']],
    notes: ['A panel can hold anything: lists, tables, code, images.'],
  },
  {
    title: 'Expand', slash: '/expand',
    intro: 'A section with a title that readers click to open, for detail most people can skip.',
    insert: ['The + menu, which wraps whatever you have selected.'],
    options: [['Title', 'Typed at the top; Return moves into the body.'], ['Remove expand', 'Takes the expand away and keeps its contents.']],
    notes: ['It is open while you edit and closed for readers. One with no title reads “Click to expand”.', 'PDF and HTML exports print it open.'],
  },
  {
    title: 'Decision', slash: '/decision',
    intro: 'Marks something that was agreed, with a check mark beside it, so it stands out in meeting notes.',
    insert: ['The + menu, which wraps whatever you have selected.'],
    options: [['Remove decision', 'Takes the mark away and keeps the text.']],
  },
  {
    title: 'Layout', slash: '/layout or /columns',
    intro: 'Two or three columns side by side. On a phone the columns stack.',
    insert: ['The + menu.'],
    options: [
      ['Two columns, Three columns', 'Equal columns.'],
      ['Left sidebar, Right sidebar', 'A narrow column beside a wide one.'],
      ['Three with sidebars', 'A wide middle column with a narrow one each side.'],
      ['Centered, Wide, Full width', 'How much of the page the columns span.'],
      ['Remove layout', 'Takes the columns away and keeps their contents.'],
    ],
    notes: ['The options are in the bar above the columns while you are editing inside them.', 'A layout sits at the top level of a page, not inside a panel, an expand or another layout.', 'Switching to fewer columns moves the extra columns’ contents into the last one.'],
  },
  {
    title: 'Table', slash: '/table',
    intro: 'Rows and columns, starting as three by three with a header row.',
    insert: ['The + menu.'],
    options: [
      ['+ between rows or columns', 'Adds one there. On a touch screen, tap inside the table to show these.'],
      ['× on a row or column', 'Deletes it.'],
      ['Column borders', 'Drag to change a column’s width.'],
      ['The table’s right edge', 'Drag to change the whole table’s width; ⤢ makes it full width.'],
      ['Cell options (the arrow in a cell)', 'Header row and Header column on or off; Merge cells (select two or more by dragging across them first) and Split cell; Delete table; and a background color for the cell, its row or its column.'],
    ],
    notes: ['Tab moves to the next cell, and Tab in the last cell adds a row.', 'A table wider than the page scrolls sideways, on a phone too.', 'A chart can draw the numbers in a table: see Chart.'],
  },
  {
    title: 'Code block', slash: '/code',
    intro: 'Code, with its colors for the language it is in.',
    insert: ['Markdown: ``` (optionally followed by a language, such as ```python) and Return.', 'Keyboard: Mod+Alt+C.', 'The + menu.'],
    options: [
      ['Language', 'Plain text, JavaScript, TypeScript, Python, C#, Bash, JSON, YAML, SQL, HTML, CSS, Go, Rust, Java, Dockerfile, Markdown, or Mermaid diagram.'],
      ['#', 'Line numbers on or off.'],
      ['Copy', 'Copies the code. Readers have this too.'],
    ],
    notes: ['Tab does not indent inside a code block. Press Return three times, or the down arrow on the last line, to leave it.'],
  },
  {
    title: 'Diagram (Mermaid)', demo: 'Diagram', slash: '/mermaid or /diagram',
    intro: 'A flowchart, sequence diagram or other chart drawn from text, using Mermaid.',
    insert: ['A code block set to the Mermaid diagram language.', 'Markdown: ```mermaid and Return.'],
    options: [['Source / Diagram', 'Switches between the text and the drawing.'], ['Copy', 'Copies the text.']],
    notes: ['A mistake in the text shows Mermaid’s own error message instead of the drawing.', 'The drawing follows the light or dark theme.', 'Mermaid’s documentation lists every kind of diagram and how to write it: mermaid.js.org.'],
  },
  {
    title: 'Math', slash: '/math, or /inline for math inside a sentence',
    intro: 'Equations, written in LaTeX and drawn with KaTeX, either on a line of their own or inside a sentence.',
    insert: ['The + menu.'],
    options: [['Double-click', 'Edits the equation. Return or clicking away saves; Escape cancels.'], ['Inline / Own line', 'Moves it into the sentence or onto its own line.']],
    notes: ['There is no $…$ shortcut.'],
  },
  {
    title: 'Chart', slash: '/chart',
    intro: 'A bar, column, line or pie chart of the numbers in a table on the same page.',
    insert: ['The + menu.'],
    options: [
      ['Table', 'Which table on the page to draw: Table 1 is the first.'],
      ['Type', 'Bar (horizontal), Column (vertical), Line or Pie.'],
      ['Title', 'Optional.'],
    ],
    notes: [
      'The first row names the series and the first column labels the points. Values such as 1,234, 45% and $9.50 count as numbers.',
      'A pie chart uses the first column of numbers. Negative values count as zero in bars and pies.',
      'Tables are numbered by where they are on the page, so adding a table above the chart’s one changes which it draws.',
    ],
  },
  {
    title: 'Image', slash: '/image',
    intro: 'A picture, uploaded to the page.',
    insert: ['Paste a picture, or drag one or more picture files onto the page.', 'Markdown: ![description](https://…) shows a picture from a web address without uploading it.'],
    options: [
      ['Size', 'Drag the handle at the picture’s corner. The size is a share of the column, so it holds on a phone and in exports. Original size goes back.'],
      ['Left, Center, Right, Full', 'Where the picture sits in its column. Text does not wrap around it; use a Layout for a picture beside text.'],
      ['Caption', 'A line under the picture.'],
      ['Alt text', 'What the picture shows, for people using a screen reader. It starts as the file’s name.'],
      ['Border, Shadow', 'A thin border, or a drop shadow.'],
      ['Comment', 'Starts a comment about the picture.'],
    ],
    notes: ['The options appear in a bar above the picture when you click it while editing.', 'An uploaded picture becomes one of the page’s attachments.'],
  },
  {
    title: 'Gallery', slash: '/gallery',
    intro: 'Pictures tiled in a grid, each cropped to the same shape.',
    insert: ['The + menu. Then paste, drop or upload pictures inside it.'],
    notes: ['Tiles are at least 200 pixels wide, so a phone shows one or two to a row and a wide screen three or more.'],
  },
  {
    title: 'File or video', slash: '/file or /video',
    intro: 'Shows a file attached to the page inside the page: a video or audio player, a PDF viewer, a picture, or a card for anything else.',
    insert: ['The + menu. Then Upload a file, or choose one already attached to the page.'],
    options: [['File', 'Which attachment to show.'], ['Upload', 'Adds a file to the page and shows it, on a new page too.'], ['Show as', 'For a video: A video with controls, or An animation (below).']],
    notes: ['A PDF shows in the browser’s own viewer, with a download link under it.', 'A picture shows as the picture.'],
  },
  {
    title: 'Animation', slash: '/animation or /gif',
    intro: 'A short video that plays like a GIF: silently, on a loop, starting by itself, with no player controls. A fraction of the size of a GIF.',
    insert: ['A File or video block with Show as set to An animation.'],
    notes: ['A small button pauses and plays it; on a touch screen the button is always showing.', 'For readers whose device asks for less motion, it starts paused.', 'Any video attached to the page works. Keep it short and small: it downloads for every reader.'],
  },
  {
    title: 'Embed', slash: '/embed or /video',
    intro: 'A video, design or board from another site, shown inside the page: YouTube, Vimeo, Loom, Figma, Miro, CodePen, and Google Docs or Drive.',
    insert: ['The + menu. Paste the address and choose Embed.'],
    notes: ['YouTube videos use YouTube’s privacy-enhanced player.', 'Other sites have to be on the instance’s allowed list, which an administrator edits in Administration → Settings. An address that is not shows why, with a link to open it instead.', 'Exports show an embed as a card with its link.'],
  },
  {
    title: 'Smart link', slash: '/smart or /preview',
    intro: 'A link that shows what it points at: the page’s title, description, picture and site.',
    insert: ['The + menu. Paste the address and choose Show.'],
    options: [['Card', 'Picture, title, description and site, as a block of its own.'], ['Inline', 'The title as a link inside a sentence. Click it while editing to change the address or turn it back into a card.']],
    notes: ['The preview is fetched by the server and kept; people reading without signing in see the kept one.'],
  },
  {
    title: 'Status', slash: '/status or /lozenge',
    intro: 'A small colored label such as IN PROGRESS or DONE, inside a sentence or a table.',
    insert: ['The + menu.'],
    options: [['Text', 'Up to 40 characters, always shown in capitals.'], ['Color', 'Gray, Red, Yellow, Green, Blue or Purple.']],
    notes: ['Click a status to change it.'],
  },
  {
    title: 'Date', slash: '/date or /today',
    intro: 'A calendar date, shown to each reader in their own format.',
    insert: ['The + menu. It starts as today.'],
    options: [['Date', 'Click the date to pick another from a calendar.']],
  },
  {
    title: 'Mention', slash: 'none: type @',
    intro: 'Names a person in the page. They are told about it when you publish or update.',
    insert: ['Type @ at the start of a line or after a space, then part of their name or email, and choose them.'],
    notes: ['Only people newly mentioned in that version are told, and only if they can see the page.', 'A mention in a task makes that person the task’s assignee.', 'Comments take mentions too: type @ in a comment box.'],
  },
  {
    title: 'Emoji', slash: 'none: type :',
    intro: 'An emoji in the text.',
    insert: ['Type : at the start of a line or after a space, then at least two letters of its name, such as :rocket or :check, and choose one.', 'Or type or paste any emoji straight from your keyboard.'],
    notes: ['The list offers 40 common emoji by name; any other can be pasted.'],
  },
  {
    title: 'Table of contents', slash: '/toc or /contents',
    intro: 'A list of the page’s headings, each a link to its section, kept up to date as the page changes.',
    insert: ['The + menu.'],
    options: [
      ['Display as', 'Vertical list or Horizontal list.'],
      ['Bullet style', 'Bullet, Mixed, Circle, Square, Numbered or None.'],
      ['Heading levels', 'Which levels to list, from 1 to 6.'],
      ['Include section numbers', 'Numbers such as 1, 1.1, 1.2.'],
      ['Advanced', 'Indentation, headings to include or exclude by a pattern such as Appendix*, CSS class names, and leaving it out of PDF exports.'],
    ],
    notes: ['Click the table of contents to change its settings.'],
  },
  {
    title: 'Excerpt', slash: '/excerpt',
    intro: 'Marks part of a page that other pages can show, with the Excerpt include live content block.',
    insert: ['The + menu, which wraps whatever you have selected.'],
    options: [['Remove excerpt', 'Takes the mark away and keeps the text.']],
    notes: ['Only a page’s first excerpt is used.'],
  },
  {
    title: 'Page properties', slash: '/properties',
    intro: 'A two-column table of facts about the page, such as status and owner. The Page properties report live content block collects them from many pages into one table.',
    insert: ['The + menu. It starts with Status and Owner.'],
    options: [['Remove page properties', 'Takes the mark away and keeps the table.']],
    notes: ['The first column is the name and the second the value. Rows with no name are skipped.', 'Only a page’s first page properties table is collected.'],
  },
]

// ----------------------------------------------------------- Live content
const LIVE = [
  {
    title: 'Children display', slash: '/children',
    intro: 'A list of the pages under this one, kept up to date.',
    options: [['Depth', '1 to 3 levels. 1 unless changed.'], ['Sort by', 'Tree order, Title, or Recently updated.']],
  },
  {
    title: 'Recently updated', slash: '/recently',
    intro: 'A table of the pages changed most recently, with who changed them and when.',
    options: [['Look in', 'This space, or This page and below.'], ['Show', '1 to 50 pages. 10 unless changed.']],
  },
  {
    title: 'Content by label', slash: '/content',
    intro: 'A list of the pages carrying one or more labels.',
    options: [['Labels', 'Separated by commas, such as release, api.'], ['Match', 'Any label, or All labels.'], ['Look in', 'This space, or Everywhere.'], ['Show', '1 to 100 pages. 25 unless changed.']],
  },
  {
    title: 'Attachments (live content)', demo: 'Attachments', slash: '/attachments',
    intro: 'A table of the files attached to this page, each a download link, with size, who uploaded it and when.',
  },
  {
    title: 'Change history', slash: '/history',
    intro: 'The page’s latest versions: number, who, when and what changed.',
    options: [['Show', '1 to 50 versions. 10 unless changed.']],
  },
  {
    title: 'Contributors', slash: '/contributors',
    intro: 'The people who have edited the page, most edits first.',
    options: [['Count edits on', 'This page, or This page and below.']],
  },
  {
    title: 'Include page', slash: '/include',
    intro: 'Shows the whole of another page inside this one, always its current version. Useful for text that belongs in several places, such as a standard disclaimer.',
    options: [['Page', 'Search for the page to include.']],
    notes: ['Live content inside the included page is not shown, so includes cannot loop.', 'Readers who cannot see the included page see a note instead.'],
  },
  {
    title: 'Excerpt include', slash: '/excerpt',
    intro: 'Shows the excerpt from another page: the part its author marked with the Excerpt element.',
    options: [['Page', 'Search for the page.']],
  },
  {
    title: 'Page properties report', slash: '/report',
    intro: 'Collects the Page properties tables from every page with a label into one table: one row per page, one column per property.',
    options: [['Labels', 'Which pages to collect.'], ['Show', '1 to 100 pages. 25 unless changed.']],
  },
  {
    title: 'Labels list', slash: '/labels',
    intro: 'Labels as links: this page’s, the most used in the space, or those used with this page’s labels.',
    options: [['List', 'This page’s labels, Popular in this space, or Related labels.'], ['Show', '1 to 100 labels. 20 unless changed.']],
  },
  {
    title: 'Task report', slash: '/report',
    intro: 'A table of task list items from many pages, with who each is assigned to.',
    options: [['Look in', 'This page and below, This space, or Everywhere.'], ['State', 'Not done, Done, or All.'], ['Assigned to', 'Anyone, or Me.'], ['Show', '1 to 100 tasks. 25 unless changed.']],
  },
  {
    title: 'Page tree', slash: '/tree',
    intro: 'The tree of pages as nested links, from this page or from the top of the space.',
    options: [['Start at', 'This page, or Space root.'], ['Depth', '1 to 6 levels. 3 unless changed.']],
  },
]

const slug = (t) => t.toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '')
const demoOf = (e) => e.demo ?? e.title

// A new page in Tesria Demo, for the pictures of the editor itself. The last
// shot closes it without publishing, which throws the draft away.
const EDITOR = [
  {
    name: 'editor', url: '/spaces/DEMO/new', settle: 800,
    steps: [
      { wait: 3500 },
      { type: 'Release notes', selector: 'input[placeholder="Page title"]' },
      { click: '.ProseMirror' },
      { keys: 'What changed in this release, and why.' },
    ],
  },
  { name: 'toolbar', settle: 400, steps: [], clipTo: '.page-actionbar--editor' },
  {
    name: 'slash-menu', settle: 600,
    steps: [{ press: 'Enter', selector: '.ProseMirror' }, { keys: '/' }, { wait: 600 }],
    clipTo: ['.ProseMirror', '.slash-menu'], clipPad: 8,
  },
  {
    name: 'insert-menu', settle: 600,
    steps: [{ press: 'Escape', selector: '.ProseMirror' }, { press: 'Backspace', selector: '.ProseMirror' }, { click: '.toolbar__insert' }, { wait: 500 }],
    clipTo: '.toolbar-dropdown__menu--insert', clipPad: 8,
  },
  {
    name: 'selection-bubble', settle: 600,
    steps: [{ press: 'Escape', selector: 'body' }, { tripleClick: '.ProseMirror p' }, { wait: 600 }],
    clipTo: ['input.title-input', '.ProseMirror p', '.floating-menu'], clipPad: 12,
  },
  { name: 'editor-discarded', settle: 300, skipCapture: true, steps: [{ press: 'Escape', selector: '.ProseMirror' }, { click: '.page-actionbar button:has-text("Close")' }, { wait: 1200 }] },
]

export const shots = ({ demo }) => [
  ...EDITOR,
  ...[...ELEMENTS, ...LIVE].map((e) => ({
    name: `el-${slug(e.title)}`,
    url: demo(demoOf(e)),
    settle: ['Embed', 'Animation', 'File or video', 'Diagram (Mermaid)', 'Smart link'].includes(e.title) ? 4000 : 1500,
    steps: [{ wait: 3000 }],
    clipTo: '.page-body',
    clipPad: 12,
  })),
]

export async function build({ top, page, ensure, figure, doc, p, h, text, bold, code, ul, li, panel, table, live }) {
  const manual = top['User manual']
  const b = (t) => text(t, bold)
  const c = (t) => text(t, code)

  const editor = await ensure('The editor', manual)
  await page('The editor', manual, doc(
    p('Everything about writing a page: the toolbar, the slash menu, formatting, shortcuts, and a page for every element you can add.'),
    live('children', { depth: '1', sort: 'position' }),
  ))

  // -------------------------------------------------------- Editor tour
  const tourPage = await ensure('Editor tour', editor)
  await page('Editor tour', editor, doc(
    p('Choose ', b('Edit'), ' on a page, or ', b('+ New page'), ' in a space, to open the editor.'),
    ...(await figure(tourPage, 'editor', 'The editor')),
    h(2, 'The toolbar'),
    ...(await figure(tourPage, 'toolbar', 'The editor’s toolbar')),
    table([
      ['Control', 'What it does'],
      ['Style (Aa)', 'Normal text or Heading 1 to 6. On a narrow window, controls that do not fit move into this menu.'],
      ['B, I, U, S, <>', 'Bold, italic, underline, strikethrough, inline code.'],
      ['Highlight color, Text color', 'Colors; see Colors.'],
      ['Lists', 'Bullet, ordered and task lists.'],
      ['Outdent, Indent', 'Move a paragraph or heading in or out.'],
      ['x₂, x²', 'Subscript and superscript.'],
      ['Clear formatting', 'Back to plain normal text.'],
      ['Alignment', 'Left, center, right or justified.'],
      ['+', 'Insert an element: the same list as the slash menu.'],
      ['Full width', 'Widens the page for everyone.'],
      ['Publish or Update', 'Saves; see Drafts, Publish and Update.'],
      ['Close', 'Leaves the editor.'],
    ], [220, 480]),
    p('On a phone the toolbar shows only ', b('Aa Style'), ' and ', b('+ Insert'), '; every formatting control is inside the Style menu. See ', b('Tesria on phones and tablets'), '.'),
  ))

  // ------------------------------------------------------- Slash menu
  const slashPage = await ensure('The slash menu', editor)
  await page('The slash menu', editor, doc(
    p('Type ', c('/'), ' at the start of a line, or after a space, to insert anything. Keep typing to narrow the list, then press Return or click. Escape closes it.'),
    ...(await figure(slashPage, 'slash-menu', 'The slash menu')),
    p('Words other than the element’s name work too: ', c('/h2'), ' for Heading 2, ', c('/todo'), ' for a task list, ', c('/columns'), ' for a layout, ', c('/gif'), ' for an animation. Each element’s page lists its words.'),
    p('The ', b('+'), ' button on the toolbar lists the same elements, grouped as ', b('Insert'), ', ', b('Panels'), ' and ', b('Live content'), '. With text selected, the elements that wrap text (panels, quotes, expands, decisions, excerpts, code) wrap the selection.'),
    ...(await figure(slashPage, 'insert-menu', 'The + menu')),
  ))

  // --------------------------------------------------------- Formatting
  await page('Text formatting', editor, doc(
    p('Select text, then use the toolbar, the bubble that appears above the selection, or a shortcut.'),
    table([
      ['Formatting', 'Shortcut', 'Markdown'],
      ['Bold', 'Mod+B', '**text**'],
      ['Italic', 'Mod+I', '*text*'],
      ['Underline', 'Mod+U', ''],
      ['Strikethrough', 'Mod+Shift+S', '~~text~~'],
      ['Inline code', 'Mod+E', '`text`'],
      ['Highlight', 'Mod+Shift+H', '==text=='],
      ['Subscript', 'Mod+,', ''],
      ['Superscript', 'Mod+.', ''],
      ['Clear formatting', 'Mod+\\', ''],
    ], [220, 200, 200]),
    p('Mod is Cmd on a Mac and Ctrl on Windows and Linux. Clear formatting removes every mark, turns a heading back into normal text, and resets alignment and indentation.'),
  ))

  await page('Colors', editor, doc(
    p('Two palettes on the toolbar:'),
    ul(
      li(p(b('Text color'), ': Gray, Blue, Teal, Green, Yellow, Orange, Red and Purple. ', b('Default color'), ' removes it.')),
      li(p(b('Highlight color'), ': six light shades and six strong ones in blue, teal, green, yellow, red and purple. ', b('No highlight'), ' removes it.')),
    ),
    p('Colors are stored by name, not by value, so they stay readable in the dark theme. The highlight button in the selection bubble and Mod+Shift+H apply a plain highlight without asking for a color.'),
  ))

  await page('Alignment and indentation', editor, doc(
    p('Both apply to paragraphs and headings.'),
    table([
      ['', 'Shortcut'],
      ['Align left', 'Mod+Shift+L'],
      ['Align center', 'Mod+Shift+E'],
      ['Align right', 'Mod+Shift+R'],
      ['Justify', 'Mod+Shift+J'],
      ['Indent', 'Mod+]'],
      ['Outdent', 'Mod+['],
    ], [240, 240]),
    p('A paragraph indents up to four steps. Tab does not indent paragraphs: it nests list items and moves between table cells.'),
  ))

  await page('Markdown shortcuts', editor, doc(
    p('Type these and they turn into formatting as you go. The ones for blocks work at the start of a line.'),
    table([
      ['Type', 'You get'],
      ['# then a space (up to ######)', 'Heading 1 to 6'],
      ['- or * or + then a space', 'Bullet list'],
      ['1. then a space', 'Ordered list'],
      ['[] or [ ] then a space', 'Task list; [x] for a checked item'],
      ['> then a space', 'Blockquote'],
      ['``` then Return (optionally ```python)', 'Code block'],
      ['--- or *** or ___', 'Divider'],
      ['**text** or __text__', 'Bold'],
      ['*text* or _text_', 'Italic'],
      ['~~text~~', 'Strikethrough'],
      ['`text`', 'Inline code'],
      ['==text==', 'Highlight'],
      ['![description](https://…)', 'A picture from a web address'],
      ['A web address then a space', 'A link'],
    ], [320, 380]),
    p('Markdown links written as [text](address) are not converted: select the text and press Mod+K instead.'),
  ))

  await page('Keyboard shortcuts', editor, doc(
    p('Mod is Cmd on a Mac and Ctrl on Windows and Linux.'),
    table([
      ['Shortcut', 'Does'],
      ['Mod+Alt+0', 'Normal text'],
      ['Mod+Alt+1 to 6', 'Heading 1 to 6'],
      ['Mod+Shift+7, 8, 9', 'Ordered, bullet, task list'],
      ['Mod+Shift+B', 'Blockquote'],
      ['Mod+Alt+C', 'Code block'],
      ['Mod+K', 'Link'],
      ['Mod+B, I, U, E', 'Bold, italic, underline, inline code'],
      ['Mod+Shift+S, H', 'Strikethrough, highlight'],
      ['Mod+, and Mod+.', 'Subscript, superscript'],
      ['Mod+Shift+L, E, R, J', 'Align left, center, right, justify'],
      ['Mod+] and Mod+[', 'Indent, outdent'],
      ['Mod+\\', 'Clear formatting'],
      ['Shift+Return', 'A line break inside a paragraph'],
      ['Mod+Z, Mod+Shift+Z', 'Undo, redo (Mod+Y also redoes)'],
      ['Tab, Shift+Tab', 'Nest or un-nest a list item; next or previous table cell'],
    ], [240, 460]),
  ))

  const menus = await ensure('Floating menus', editor)
  await page('Floating menus', editor, doc(
    p('Some controls appear only when they apply.'),
    h(2, 'Selecting text'),
    ...(await figure(menus, 'selection-bubble', 'The bubble over selected text')),
    p('Bold, italic, underline, strikethrough, inline code, highlight, link, and ', b('Comment on this selection'), '.'),
    h(2, 'Everything else'),
    table([
      ['Where the cursor is', 'What appears'],
      ['In a link', 'The address, with Edit and Remove.'],
      ['In a panel, expand, decision, excerpt or page properties', 'A bar to change the panel type or remove the wrapper, keeping what is inside.'],
      ['In a layout', 'The column choices and widths.'],
      ['On a picture', 'Border, Shadow and Comment.'],
      ['In a table', 'Buttons to add and delete rows and columns, and the cell options arrow.'],
      ['On a status, date, table of contents or live content block', 'That element’s settings.'],
    ], [300, 400]),
  ))

  // ---------------------------------------------------------- Elements
  const elements = await ensure('Elements', editor)
  await page('Elements', editor, doc(
    p('One page for each thing you can put on a page, with how to add it, its options, and what to know. Each shows the element as a reader sees it, on a computer and on a phone.'),
    live('children', { depth: '1', sort: 'title' }),
  ))
  for (const e of ELEMENTS) await elementPage(elements, e)

  // ------------------------------------------------------- Live content
  const liveRoot = await ensure('Live content', editor)
  await page('Live content', editor, doc(
    p('Live content blocks show information that is looked up each time the page is read: the pages under this one, recent changes, pages with a label, tasks from across a space. Nobody has to keep them up to date.'),
    ul(
      li(p('Insert one from the slash menu or the ', b('+'), ' menu’s ', b('Live content'), ' section.')),
      li(p('Click it to change its settings; changes save at once. ↻ fetches it again.')),
      li(p('Each reader sees only what they are allowed to see: a page they cannot open never appears in a list.')),
      li(p('Exports and exported websites keep what the block showed at the moment of export.')),
    ),
    live('children', { depth: '1', sort: 'position' }),
  ))
  for (const e of LIVE) await elementPage(liveRoot, e, true)

  async function elementPage(parent, e, isLive = false) {
    const id = await ensure(e.title, parent)
    const body = [p(e.intro)]
    body.push(...(await figure(id, `el-${slug(e.title)}`, isLive ? `${e.title.replace(' (live content)', '')}, as a reader sees it` : `${e.title}, as a reader sees it`)))
    body.push(h(2, 'Adding it'))
    body.push(ul(
      li(p('Slash menu: ', c(e.slash), '.')),
      ...(isLive ? [li(p('The + menu → Live content.'))] : (e.insert ?? []).map((t) => li(p(t)))),
    ))
    if (e.options?.length) {
      body.push(h(2, isLive ? 'Settings' : 'Options'))
      body.push(table([[isLive ? 'Setting' : 'Option', 'What it does'], ...e.options], [220, 480]))
    } else if (isLive) {
      body.push(h(2, 'Settings'))
      body.push(p('None.'))
    }
    if (e.notes?.length) {
      body.push(h(2, 'Good to know'))
      body.push(ul(...e.notes.map((t) => li(p(t)))))
    }
    await page(e.title, parent, doc(...body))
  }
}
