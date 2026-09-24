// User manual → The editor → Elements, the first half: one page per element,
// written to the element page shape in WRITING.md, with every variant of the
// element built live on the page (dev-plan 15.6).
//
//   Normal text, Headings, Blockquote, Divider, Bullet list, Ordered list,
//   Task list, Link, Panels, Expand, Decision, Layout, Table, Code block,
//   Diagram (Mermaid)
//
// Panels is the approved pilot page (sections/pilot.mjs), moved here as it
// was, with its panel-insert animation. The other pages follow its shape.
//
// Facts checked against src/web/src/editor on 2026-09-24: slash/items.ts
// (titles and keywords, and which match comes first: /hr finds Link before
// Divider, and /ordered finds Bullet list first, so neither is offered),
// InsertMenu.tsx (what the + menu lists, and how it treats a selection),
// extensions.ts (the schema: code block line numbers, table width and
// layout, cell background colors, task assignees), layoutExtension.ts
// (LAYOUT_PRESETS and the section widths), TableCellMenu.tsx,
// TableControls.tsx, TableWidthControls.tsx, WrapperMenu.tsx, LinkDialog.tsx,
// LinkMenu.tsx, CodeBlockView.tsx, lowlight.ts, MermaidView.tsx and the
// diagram types in the installed Mermaid (11.17), and the Tiptap extensions'
// own input rules and shortcuts.
//
// The titles are the ones the first version used, so the pages are rewritten
// in place; manual-editor.mjs only makes sure they exist. pilot.mjs still
// writes the same Panels page, word for word, until it is taken out of there.
//
// A few pages mention the fictional Tesria Demo people (tasks with owners):
// person() makes them real mentions when those accounts exist.

// The Panels animation, as approved in the pilot: adding one and changing its
// type, in a narrow window so it reads on a phone. It opens a new page in
// Tesria Demo; the harness discards the draft after.
const PANEL_INSERT = {
  name: 'panel-insert', url: '/spaces/DEMO/new', phone: false,
  viewport: { width: 480, height: 400 }, record: { size: { width: 480, height: 400 } },
  waitFor: '.ProseMirror', lead: 900, tail: 1400,
  css: '.tip, .onboarding-tip { display: none !important; }',
  steps: [
    { click: '.ProseMirror' }, { wait: 400 },
    { typeSlowly: '/info', delay: 110 }, { wait: 900 },
    { press: 'Enter', selector: '.ProseMirror' }, { wait: 300 },
    { typeSlowly: 'The office is closed on Monday.', delay: 45 }, { wait: 1100 },
    { moveTo: '.floating-menu button[title="Warning panel"]' }, { wait: 500 },
    { click: '.floating-menu button[title="Warning panel"]' }, { wait: 1300 },
    { moveTo: '.floating-menu button[title="Tip panel"]' }, { wait: 500 },
    { click: '.floating-menu button[title="Tip panel"]' }, { wait: 800 },
  ],
}

// The table's Cell options are the one control on these pages that people
// cannot find by looking: a small arrow in the corner of the cell being
// edited. So the Table page shows it in use: a table made with /table, its
// header row typed, then Cell options turning on a header column and
// coloring the row. Same size and pattern as the Panels animation.
const TABLE_CELL_OPTIONS = {
  name: 'table-cell-options', url: '/spaces/DEMO/new', phone: false,
  viewport: { width: 480, height: 400 }, record: { size: { width: 480, height: 400 } },
  waitFor: '.ProseMirror', lead: 900, tail: 1400,
  css: '.tip, .onboarding-tip { display: none !important; }',
  steps: [
    { click: '.ProseMirror' }, { wait: 400 },
    { typeSlowly: '/table', delay: 110 }, { wait: 900 },
    { press: 'Enter', selector: '.ProseMirror' }, { wait: 500 },
    { typeSlowly: 'Room', delay: 60 }, { press: 'Tab', selector: '.ProseMirror' }, { wait: 200 },
    { typeSlowly: 'Monday', delay: 60 }, { press: 'Tab', selector: '.ProseMirror' }, { wait: 200 },
    { typeSlowly: 'Tuesday', delay: 60 }, { wait: 800 },
    { moveTo: '.cell-menu__trigger' }, { wait: 400 },
    { click: '.cell-menu__trigger' }, { wait: 1000 },
    { moveTo: '.cell-menu__action:has-text("Header column")' }, { wait: 400 },
    { click: '.cell-menu__action:has-text("Header column")' }, { wait: 1100 },
    { moveTo: '.cell-menu__scope:has-text("Row")' }, { wait: 300 },
    { click: '.cell-menu__scope:has-text("Row")' }, { wait: 600 },
    { moveTo: '.cell-menu__panel .swatch[title="Light blue"]' }, { wait: 400 },
    { click: '.cell-menu__panel .swatch[title="Light blue"]' }, { wait: 900 },
  ],
}

export const shots = [PANEL_INSERT, TABLE_CELL_OPTIONS]

// Cell background colors, from the editor's palette (palette.ts).
const LIGHT = { blue: '#deebff', green: '#e3fcef', yellow: '#fffae6', red: '#ffebe6', gray: '#f4f5f7' }

// The page titles, in the order they are written.
const TITLES = [
  'Normal text', 'Headings', 'Blockquote', 'Divider', 'Bullet list', 'Ordered list', 'Task list', 'Link',
  'Panels', 'Expand', 'Decision', 'Layout', 'Table', 'Code block', 'Diagram (Mermaid)',
]

export async function build({
  top, page, ensure, person, doc, p, h, text, bold, italic, code, link, ul, ol, li, panel, table,
  codeBlock, tasks, task, quote, hr, expand, decision, layout, date, animation, pageLink,
}) {
  const b = (t) => text(t, bold)
  const c = (t) => text(t, code)
  const i = (t) => text(t, italic)

  // ---- Marks and nodes the lib has no builder for, as extensions.ts stores them.
  const U = { type: 'underline' }
  const S = { type: 'strike' }
  const SUB = { type: 'subscript' }
  const SUP = { type: 'superscript' }
  const highlight = (color) => ({ type: 'highlight', attrs: { color } })
  const ink = (color) => ({ type: 'textColor', attrs: { color } })
  const br = { type: 'hardBreak' }
  /** A paragraph with alignment or indentation. */
  const para = (attrs, ...content) => ({ ...p(...content), attrs })
  const blockquote = (...content) => ({ type: 'blockquote', content })
  const decisionOf = (...content) => ({ type: 'decision', content })
  const olFrom = (start, ...items) => ({ ...ol(...items), attrs: { start } })
  /** A task with sub-tasks under it. */
  const taskWith = (checked, words, subtasks) => ({ type: 'taskItem', attrs: { checked }, content: [p(words), subtasks] })
  /**
   * A task that names its owner. The editor makes the first person mentioned
   * in a task its assignee (taskAssignee.ts); the same is stored here, so the
   * page is what the editor would have saved.
   */
  const owned = (checked, name, ...rest) => {
    const who = person(name)
    const t = task(checked, who, ...rest)
    if (who.type === 'mention') t.attrs = { ...t.attrs, assigneeId: who.attrs.userId, assigneeName: who.attrs.label }
    return t
  }
  const code2 = (language, source, extra) => ({ ...codeBlock(language, source), attrs: { language, ...extra } })
  const mermaid = (...lines) => codeBlock('mermaid', lines.join('\n'))
  const wideLayout = (width, widths, ...columns) => ({ ...layout(widths, ...columns), attrs: { width } })
  // Table cells with the attributes the lib's table() does not set.
  const cellOf = (header, content, { colspan = 1, rowspan = 1, width, bg } = {}) => ({
    type: header ? 'tableHeader' : 'tableCell',
    attrs: { colspan, rowspan, colwidth: width ? [width] : null, ...(bg ? { backgroundColor: bg } : {}) },
    content: [typeof content === 'string' ? (content ? p(content) : { type: 'paragraph' }) : content],
  })
  const th = (content, opts) => cellOf(true, content, opts)
  const td = (content, opts) => cellOf(false, content, opts)
  const row = (...cells) => ({ type: 'tableRow', content: cells })
  const tableOf = (attrs, ...rows) => ({ type: 'table', ...(attrs ? { attrs } : {}), content: rows })
  /** The Insert it table: a slash command and what it gives. */
  const commands = (...rows) => table([['Type this', 'To get'], ...rows], [200, 500])
  /** A link to a heading on another Support page. */
  const sectionLink = (title, anchor, label) => {
    const t = pageLink(title, label)
    const mark = t.marks?.find((m) => m.type === 'link')
    return mark ? { ...t, marks: [{ ...mark, attrs: { ...mark.attrs, href: `${mark.attrs.href}#${anchor}` } }] } : t
  }
  const mac = (keys) => text(` (${keys} on a Mac)`)

  const editor = await ensure('The editor', top['User manual'])
  const elements = await ensure('Elements', editor)
  // Every page first, so the links between them resolve on a first run.
  const ids = {}
  for (const title of TITLES) ids[title] = await ensure(title, elements)

  // ============================================================ Normal text
  await page('Normal text', elements, doc(
    p('Normal text is the plain body text of a page: what you get as soon as you start typing, and what most of any page is made of. Headings, lists, tables and every other element sit between paragraphs of it.'),
    p('This page is about the paragraph itself: turning other text back into it, breaking a line without starting a new paragraph, and aligning and indenting it. Formatting the words inside, such as bold or color, has pages of its own: ', pageLink('Text formatting'), ' and ', pageLink('Colors'), '.'),

    h(2, 'Insert it'),
    commands(['/text', 'Normal text'], ['/paragraph', 'Normal text']),
    p('You rarely need a command for it: pressing ', b('Enter'), ' at the end of a paragraph or a heading starts a new paragraph of normal text. The commands are for turning something back into it. Type ', c('/text'), ' after a space at the end of a heading, press ', b('Enter'), ', and the heading becomes an ordinary paragraph with the same words.'),
    ul(
      li(p(b('The style menu.'), ' At the left of the toolbar, the menu that reads ', b('Normal text'), ' (', b('Aa Style'), ' on a phone). Choose ', b('Normal text'), '.')),
      li(p(b('Keyboard:'), ' Ctrl+Alt+0', mac('⌘+Option+0'), '.')),
      li(p(b('Clear formatting:'), ' Ctrl+\\', mac('⌘+\\'), ' removes all formatting from the selected text, and also turns a heading back into normal text and resets its alignment and indentation.')),
    ),
    p('Normal text is not in the ', b('+'), ' menu: that menu is for inserting elements, and normal text is what is already there.'),

    h(2, 'What a paragraph can do, and when to use it'),
    h(3, 'A plain paragraph'),
    p('This is one. Use plain paragraphs for everything that is not a heading, a list or a special block: explanations, background, the story of what happened. Keep them short, three or four sentences, and give each one idea. Short paragraphs are easy to read on a phone and easy to skim on a computer.'),

    h(3, 'Formatted words'),
    p('Inside a paragraph, words can be ', text('bold', bold), ', ', text('italic', italic), ', ', text('underlined', U), ', ', text('struck through', S), ', ', text('code', code), ', ', text('highlighted', highlight('#fff0b3')), ' or ', text('colored', ink('blue')), ', and you can write chemistry and math such as H', text('2', SUB), 'O and 10', text('3', SUP), '.'),
    p('Use them for a reason a reader can see: bold for the one phrase a skimmer must not miss, italic for a title or a new term, code for anything typed exactly, such as a file name. A paragraph with half its words in bold has nothing that stands out. Each is explained, with its shortcut, in ', pageLink('Text formatting'), ' and ', pageLink('Colors'), '.'),

    h(3, 'Line breaks inside a paragraph'),
    p('Kestrel Software', br, '1200 Harbor Street, Suite 4', br, 'Springfield'),
    p('Press ', b('Shift+Enter'), ' to start a new line without starting a new paragraph. The lines stay together as one paragraph, with no gap between them, like the address above. Use it where lines belong together: an address, the lines of a signature, a short verse. For anything else, press ', b('Enter'), ' and start a new paragraph.'),

    h(3, 'Alignment'),
    para({ textAlign: 'left' }, b('Aligned left, the default.'), ' Every line starts at the same place, which is what makes text easy to read. Leave body text like this.'),
    para({ textAlign: 'center' }, b('Centered.'), ' For a short line that stands on its own, such as a motto under a heading.'),
    para({ textAlign: 'right' }, b('Aligned right.'), ' Rarely needed: a sign-off, or a date at the end of a letter.'),
    para({ textAlign: 'justify' }, b('Justified.'), ' Both edges are straight, and the spaces between the words stretch to fit. It suits long printed text, but in a narrow window it can leave wide gaps between words, so use it sparingly, and never for a line or two.'),
    p('Set alignment from the ', b('Alignment'), ' menu on the toolbar, or with Ctrl+Shift+L, E, R and J for left, center, right and justified', mac('⌘+Shift'), '. Headings can be aligned too. More in ', pageLink('Alignment and indentation'), '.'),

    h(3, 'Indentation'),
    para({ textIndent: 1 }, 'Indented one step.'),
    para({ textIndent: 2 }, 'Two steps.'),
    para({ textIndent: 3 }, 'Three steps.'),
    para({ textIndent: 4 }, 'Four steps, as far as it goes.'),
    p('Indenting moves a whole paragraph to the right, to set it under the one before, such as a note that belongs to the line above it. Use the ', b('Indent'), ' and ', b('Outdent'), ' buttons on the toolbar, or Ctrl+] and Ctrl+[', mac('⌘+] and ⌘+['), '. The Tab key does not indent a paragraph: it is kept for lists and tables. To show structure, a list is usually clearer than indented paragraphs.'),

    h(2, 'Changing and removing it'),
    p('Normal text has nothing to remove: it is what is left when everything else is taken away. To turn a paragraph into something else, put the cursor in it and choose the other style, or type its command after a space at the end of the line: ', c('/h2'), ' makes it a heading, ', c('/quote'), ' a quotation, ', c('/ul'), ' a bullet list.'),
    p(b('Clear formatting'), ' (Ctrl+\\) takes selected text back to plain: no bold, no color, no alignment and no indent.'),

    h(2, 'Good practice'),
    ul(
      li(p(b('One idea to a paragraph.'), ' When a paragraph changes subject, start a new one.')),
      li(p(b('Make a heading a heading.'), ' A line of bold text looks like a heading, but a table of contents does not list it and nobody can link to it. Use ', pageLink('Headings'), '.')),
      li(p(b('Reach for a list'), ' when you find yourself writing “first…, then…, and finally…”.')),
      li(p(b('Keep body text aligned left.'), ' Centered and justified paragraphs are harder to read the longer they get.')),
    ),

    h(2, 'Related elements'),
    ul(
      li(p(pageLink('Headings'), ' give a page its sections.')),
      li(p(pageLink('Bullet list'), ' and ', pageLink('Ordered list'), ' break a run of points out of a paragraph.')),
      li(p(pageLink('Blockquote'), ' sets apart someone else’s words; ', pageLink('Panels'), ' make your own stand out.')),
    ),
  ))

  // ============================================================== Headings
  await page('Headings', elements, doc(
    p('Headings divide a page into sections with titles, the way chapters and sections divide a book. They let a reader skim the page and jump to the part they need. They also do more than look big: a table of contents lists them, and every heading can be linked to directly.'),
    p('Tesria has six levels, from Heading 1, the largest, to Heading 6, the smallest. Use them like an outline: a Heading 3 belongs to the Heading 2 above it.'),

    h(2, 'Insert it'),
    commands(
      ['/h1', 'Heading 1'], ['/h2', 'Heading 2'], ['/h3', 'Heading 3'],
      ['/h4', 'Heading 4'], ['/h5', 'Heading 5'], ['/h6', 'Heading 6'],
    ),
    p('Type the command at the start of an empty line, press ', b('Enter'), ', and type the heading. Typed after a space at the end of a line you have already written, it turns that whole line into a heading. ', c('/heading'), ' lists all six.'),
    ul(
      li(p(b('Markdown:'), ' type ', c('#'), ' and a space at the start of a line for Heading 1, ', c('##'), ' for Heading 2, and so on up to ', c('######'), ' for Heading 6.')),
      li(p(b('The style menu'), ' at the left of the toolbar (', b('Aa Style'), ' on a phone).')),
      li(p(b('Keyboard:'), ' Ctrl+Alt+1 to Ctrl+Alt+6', mac('⌘+Option+1 to 6'), '. Pressing the same shortcut again turns the heading back into normal text.')),
    ),
    p('Headings are not in the ', b('+'), ' menu, which is for inserting elements; the style menu is where they live.'),

    h(2, 'The six levels, and when to use each'),
    p('Here are all six, as they look on a page. Choose a level by where the section sits in the outline, never by the size you would like: readers, and the table of contents, take a smaller heading to be part of the bigger one above it.'),
    h(1, 'Heading 1'),
    p('The largest. The page title already sits above everything in large type, so many pages need no Heading 1 at all. Use it on a long page with a few major parts, such as ', i('Before you start'), ', ', i('Your first day'), ' and ', i('Your first month'), ' in an onboarding guide.'),
    h(2, 'Heading 2'),
    p('The workhorse: the sections of an ordinary page, such as ', i('Background'), ', ', i('What we decided'), ' and ', i('Next steps'), '. This page uses Heading 2 for its own sections.'),
    h(3, 'Heading 3'),
    p('Parts of a section. Under ', i('Setting up your laptop'), ', for example: ', i('Email'), ', ', i('Chat'), ' and ', i('VPN'), '. The numbered steps on this site are Heading 3.'),
    h(4, 'Heading 4'),
    p('A detail within a part, such as ', i('On a Mac'), ' and ', i('On Windows'), ' under ', i('VPN'), '. If you reach for it often, the page may be holding too much: think about splitting it into pages of its own.'),
    h(5, 'Heading 5'),
    p('Rarely needed. Reference material with a deep structure, such as a policy with numbered clauses inside clauses.'),
    h(6, 'Heading 6'),
    p('The smallest, only a little larger than body text. It is there so that deep outlines, often brought in from other tools, keep every level.'),
    p('Headings can be aligned and indented like normal text: see ', pageLink('Normal text'), '.'),

    h(2, 'Linking to a heading'),
    p('Every heading has an address of its own, made from its words: lowercase, with hyphens for the spaces. A heading called ', i('Before you start'), ' is at the page’s address followed by ', c('#before-you-start'), '. Tesria makes these for you and keeps them unique: a second heading with the same words gets ', c('-2'), ' on the end, a third ', c('-3'), ', and so on.'),
    ul(
      li(p(b('On the same page:'), ' add a link (Ctrl+K) and choose the heading from ', b('Headings on this page'), ' in the dialog. For example, this link goes to ', text('Heading 3', link('#heading-3')), ' above.')),
      li(p(b('On another page:'), ' copy that page’s address and add ', c('#'), ' and the heading’s address to the end. Whoever opens it lands on that section.')),
      li(p(b('All of them at once:'), ' a ', pageLink('Table of contents'), ' lists every heading on the page as a link, and keeps up as the page changes.')),
    ),
    panel('note', p(b('Rewording a heading changes its address.'), ' A link to the old words still opens the page, but at the top rather than at the section. If other pages link to a section, keep its heading as it is.')),

    h(2, 'Changing and removing a heading'),
    p('To change a heading’s level, put the cursor in it and choose another level from the style menu, type another command such as ', c('/h3'), ' after a space at the end of it, or press that level’s shortcut. To make it ordinary text again, choose ', b('Normal text'), ', press Ctrl+Alt+0, or press its own level’s shortcut a second time. The words stay as they are.'),

    h(2, 'Good practice'),
    ul(
      li(p(b('Do not skip levels.'), ' After a Heading 2 comes a Heading 3, not a Heading 5. People using screen readers move around a page by its headings, and a gap makes them wonder what they missed.')),
      li(p(b('Make them specific.'), ' ', i('Refunds for annual plans'), ' tells a skimmer whether to stop; ', i('More information'), ' does not.')),
      li(p(b('Keep them short,'), ' a few words, with no full stop at the end.')),
      li(p(b('Do not use a heading to make text big.'), ' For a line that needs attention, use a ', pageLink('Panels', 'panel'), '.')),
    ),

    h(2, 'Related elements'),
    ul(
      li(p(pageLink('Table of contents'), ' lists the page’s headings as links.')),
      li(p(pageLink('Link'), ' can point at any heading, on this page or another.')),
      li(p(pageLink('Expand'), ' hides a section behind its title until a reader opens it.')),
    ),
  ))

  // ============================================================ Blockquote
  await page('Blockquote', elements, doc(
    p('A blockquote sets text apart with a bar down its left side, to show that the words are someone else’s: a customer’s feedback, a line from a contract, a message pasted from an email. Readers can tell at a glance where your writing stops and the quotation starts.'),

    h(2, 'Insert it'),
    commands(['/quote', 'A blockquote'], ['/blockquote', 'A blockquote']),
    p('Type the command at the start of an empty line and press ', b('Enter'), ', then type or paste the quotation. Or choose ', b('+'), ' on the toolbar, then ', b('Blockquote'), '. If you select some text first and use the ', b('+'), ' menu, the selected text goes inside the quote.'),
    ul(
      li(p(b('Markdown:'), ' ', c('>'), ' and a space at the start of a line.')),
      li(p(b('Keyboard:'), ' Ctrl+Shift+B', mac('⌘+Shift+B'), '.')),
    ),

    h(2, 'Kinds of quotation, and when to use each'),
    p('Every blockquote looks the same. What changes is what goes in it.'),
    h(3, 'A short quotation'),
    quote('Could the export keep our own colors? Our brand team has asked twice.'),
    p('One or two sentences, quoted exactly, to show what someone actually said. Customer feedback and notes from interviews are the classic use: the reader hears the customer, not your summary of them.'),
    h(3, 'A longer quotation, with its source'),
    blockquote(
      p('Either party may end this agreement with 90 days’ written notice.'),
      p('Notice sent by email counts as written notice once the other party acknowledges it.'),
      p(i('From the office lease, clause 7.2')),
    ),
    p('When the exact wording matters, such as a contract, a policy or a legal notice, quote it in full rather than putting it in your own words, and end with a line that says where it comes from, so a reader can check it. Press ', b('Enter'), ' inside a quote to start another paragraph in it.'),
    h(3, 'A quoted message, lists and all'),
    blockquote(
      p('Hi everyone, before Friday please:'),
      ul('Restart your laptop to finish the update.', 'Check that the VPN connects from home.'),
      p('Thanks, the IT team'),
    ),
    p('A quote can hold anything a page can, including lists, tables and code. Use it to show a message as it was sent, so readers can see it is a copy and not your own instructions.'),

    h(2, 'Changing and removing a quote'),
    p('To take the quote away and keep its text, put the cursor in it and press Ctrl+Shift+B, or choose ', b('Blockquote'), ' from the ', b('+'), ' menu again. To carry on writing below a quote, press ', b('Enter'), ' twice at its end: the second press on the empty line steps out of it.'),

    h(2, 'Good practice'),
    ul(
      li(p(b('Quote, do not paraphrase.'), ' If you change the words, it is no longer a quotation: write it as normal text.')),
      li(p(b('Say who said it,'), ' in the quote’s last line or in the sentence before it.')),
      li(p(b('Not for emphasis.'), ' To make your own words stand out, use a ', pageLink('Panels', 'panel'), '; a quote tells readers the words are not yours.')),
      li(p(b('Keep it to the part that matters,'), ' and link to the whole thing.')),
    ),

    h(2, 'Related elements'),
    ul(
      li(p(pageLink('Panels'), ' call out your own text: a tip, a warning.')),
      li(p(pageLink('Code block'), ' holds text that must be copied exactly, such as an error message or a log.')),
      li(p(pageLink('Normal text'), ' is where a paraphrase belongs.')),
    ),
  ))

  // =============================================================== Divider
  await page('Divider', elements, doc(
    p('A divider is a thin line across the page. It marks a change of subject that does not need a title of its own: the end of a summary and the start of the detail, or the point where one week’s update ends and the next begins.'),

    h(2, 'Insert it'),
    commands(['/divider', 'A divider'], ['/rule', 'A divider'], ['/separator', 'A divider']),
    p('Type the command at the start of an empty line and press ', b('Enter'), '. Or choose ', b('+'), ' on the toolbar, then ', b('Divider'), '. If text is selected, the divider takes its place.'),
    ul(
      li(p(b('Markdown:'), ' three hyphens, ', c('---'), ', at the start of an empty line. Three underscores or three asterisks followed by a space work too.')),
    ),

    h(2, 'What it looks like, and when to use it'),
    p('There is one kind of divider. Here it is, between a summary and the detail under it:'),
    p(b('We move to the new office on November 3.'), ' Everything below is detail for the people who need it.'),
    hr(),
    p('Parking: the new building has 40 spaces, given out by the front desk. Bicycles go in the basement, through the side door on Mill Lane.'),
    p('Good places for one:'),
    ul(
      li(p(b('Between a summary and the detail'), ', as above, so readers who only want the gist know where to stop.')),
      li(p(b('Between entries of the same kind'), ' on one page, such as weekly updates or a log of changes, where each already starts with its date.')),
      li(p(b('Before the end matter'), ' of a page, such as contacts or a disclaimer.')),
    ),

    h(2, 'Changing and removing it'),
    p('A divider has no settings. To remove one while editing, click it so that it is selected, then press ', b('Backspace'), ' or ', b('Delete'), '.'),

    h(2, 'Good practice'),
    ul(
      li(p(b('Prefer a heading when the next part has a name.'), ' A heading can be listed in a table of contents and linked to; a divider cannot.')),
      li(p(b('Not right before a heading.'), ' The heading already starts a new section; a line above it only adds clutter.')),
      li(p(b('One at a time.'), ' Two in a row, or one after every paragraph, make a page look broken.')),
    ),

    h(2, 'Related elements'),
    ul(
      li(p(pageLink('Headings'), ' start a section with a title.')),
      li(p(pageLink('Expand'), ' tucks the detail away instead of leaving it below a line.')),
      li(p(pageLink('Layout'), ' puts sections side by side rather than one after another.')),
    ),
  ))

  // =========================================================== Bullet list
  await page('Bullet list', elements, doc(
    p('A bullet list is a set of points where the order does not matter: what to bring, who is invited, what is new in a release. Each point gets its own line and a bullet, so a reader takes them in at a glance instead of digging them out of a paragraph.'),

    h(2, 'Insert it'),
    commands(['/ul', 'A bullet list'], ['/bullet', 'A bullet list'], ['/unordered', 'A bullet list']),
    p('Type the command at the start of an empty line, press ', b('Enter'), ' and type the first point. Press ', b('Enter'), ' for each next point, and ', b('Enter'), ' on an empty point to finish the list.'),
    ul(
      li(p(b('Markdown:'), ' a hyphen, ', c('-'), ', or ', c('*'), ' or ', c('+'), ', followed by a space at the start of a line.')),
      li(p(b('Toolbar:'), ' the ', b('Bullet list'), ' button (on a phone, in the ', b('Aa Style'), ' menu). With several paragraphs selected, it makes each one a point.')),
      li(p(b('Keyboard:'), ' Ctrl+Shift+8', mac('⌘+Shift+8'), '.')),
    ),
    p('Lists are not in the ', b('+'), ' menu, because they have their own buttons on the toolbar.'),

    h(2, 'Kinds of bullet list, and when to use each'),
    h(3, 'A simple list'),
    ul('A laptop and its charger', 'Your ID badge', 'Lunch: the kitchen is being refitted this week'),
    p('Things of the same kind, in no particular order. Here, what to bring on your first day.'),
    h(3, 'A nested list'),
    ul(
      li(p('Engineering'), ul(
        li(p('Platform'), ul('Sam Okafor', 'Jordan Brooks')),
        li(p('Mobile'), ul('Mei Chen')),
      )),
      li(p('Product'), ul('Priya Natarajan', 'Alex Rivera')),
    ),
    p('Points that belong under other points, like teams within a department. Press ', b('Tab'), ' in a point to move it under the one above, and ', b('Shift+Tab'), ' to move it back out. Each level has its own bullet (a dot, then a circle, then a square), so the levels are easy to tell apart. Lists nest as deep as you like, but two or three levels are as many as a reader can follow.'),
    h(3, 'Points that start with a label'),
    ul(
      li(p(b('Offline editing:'), ' pages open and save without a connection, and sync when you are back online.')),
      li(p(b('Shared folders:'), ' a whole team can work in one folder, with its own permissions.')),
      li(p(b('Faster first sync:'), ' a new laptop is ready in minutes rather than hours.')),
    ),
    p('When each point needs a sentence of explanation, start it with a few words in bold, so someone skimming the list still gets the gist. This site does it all the time.'),

    h(2, 'Changing and removing a list'),
    ul(
      li(p(b('To end a list,'), ' press ', b('Enter'), ' on an empty point. You are back in normal text.')),
      li(p(b('To turn it back into paragraphs,'), ' select the list and press the ', b('Bullet list'), ' button or Ctrl+Shift+8 again.')),
      li(p(b('To number it instead,'), ' select it and press the ', b('Ordered list'), ' button.')),
    ),

    h(2, 'Good practice'),
    ul(
      li(p(b('Bullets when the order does not matter.'), ' When it does, as in steps, use an ', pageLink('Ordered list'), '.')),
      li(p(b('Keep the points alike.'), ' Start them all the same way, all with a noun or all with a verb, and they read as a set.')),
      li(p(b('One point, one idea.'), ' A point that runs to several sentences wants to be a paragraph.')),
      li(p(b('No list of one.'), ' A single point is a sentence.')),
    ),

    h(2, 'Related elements'),
    ul(
      li(p(pageLink('Ordered list'), ' numbers the points, for steps in order.')),
      li(p(pageLink('Task list'), ' gives each point a checkbox, for things to do.')),
      li(p(pageLink('Table'), ' suits points that each have the same few facts about them.')),
    ),
  ))

  // ========================================================== Ordered list
  await page('Ordered list', elements, doc(
    p('An ordered list numbers its points: 1, 2, 3. Use it when the order matters, such as the steps of a procedure, or when people will refer to a point by its number (“see step 4”). Tesria keeps the numbers right as you add, remove and move points.'),

    h(2, 'Insert it'),
    commands(['/ol', 'An ordered list'], ['/numbered', 'An ordered list']),
    p('Type the command at the start of an empty line, press ', b('Enter'), ' and type the first step. Press ', b('Enter'), ' for each next step, and ', b('Enter'), ' on an empty step to finish the list.'),
    ul(
      li(p(b('Markdown:'), ' ', c('1.'), ' and a space at the start of a line. Start with another number, such as ', c('4.'), ', and the list counts from there.')),
      li(p(b('Toolbar:'), ' the ', b('Ordered list'), ' button (on a phone, in the ', b('Aa Style'), ' menu). With several paragraphs selected, it numbers each one.')),
      li(p(b('Keyboard:'), ' Ctrl+Shift+7', mac('⌘+Shift+7'), '.')),
    ),
    p('Lists are not in the ', b('+'), ' menu, because they have their own buttons on the toolbar.'),

    h(2, 'Kinds of ordered list, and when to use each'),
    h(3, 'Steps in order'),
    ol(
      'Turn the printer off and unplug it.',
      'Open the front cover and lift out the old toner cartridge.',
      'Shake the new cartridge gently from side to side, then slide it in until it clicks.',
      'Close the cover and turn the printer back on.',
    ),
    p('A procedure someone follows from top to bottom. Start each step with what to do, one action to a step.'),
    h(3, 'Steps with steps inside them'),
    ol(
      li(p('Book the room for the launch review.'), ol(
        'Find a free afternoon in the team calendar.',
        'Book the room in the office app.',
      )),
      li(p('Send the agenda to everyone invited.')),
    ),
    p('When a step has parts of its own. Press ', b('Tab'), ' in a step to move it under the one above; it is numbered from 1 again at its own level. ', b('Shift+Tab'), ' moves it back out.'),
    h(3, 'Bullets inside a numbered step'),
    ol(
      li(p('Pack what you need:'), ul('Laptop and charger', 'ID badge', 'Parking permit')),
      li(p('Leave by 8:30 to beat the traffic on the bridge.')),
    ),
    p('Things within a step whose order does not matter. Press ', b('Tab'), ' to move a point in, then press the ', b('Bullet list'), ' button, or Ctrl+Shift+8, to make that level bullets.'),
    h(3, 'A list that starts at another number'),
    ol(
      'Unpack the monitor and stand.',
      'Screw the stand onto the back of the monitor.',
      'Connect the cable to your laptop.',
    ),
    panel('note', p(b('No picture yet?'), ' Your laptop may need a moment to find the new screen. Wait ten seconds before going on.')),
    olFrom(4,
      'Choose your display settings.',
      'Put the packaging in the recycling room.',
    ),
    p('When a note, a picture or a table interrupts a procedure, the steps below it carry on from where they stopped: here, from 4. To start a list at another number, type that number, a period and a space at the start of an empty line, such as ', c('4.'), ' and a space.'),

    h(2, 'Changing and removing a list'),
    ul(
      li(p(b('The numbers look after themselves.'), ' Add, remove or move a step and the rest renumber.')),
      li(p(b('To end a list,'), ' press ', b('Enter'), ' on an empty step.')),
      li(p(b('To turn it back into paragraphs,'), ' select the list and press the ', b('Ordered list'), ' button or Ctrl+Shift+7 again. The ', b('Bullet list'), ' button turns it into bullets.')),
    ),

    h(2, 'Good practice'),
    ul(
      li(p(b('Numbers only when the order matters.'), ' Otherwise a ', pageLink('Bullet list'), ' reads more easily.')),
      li(p(b('One action to a step,'), ' starting with a verb: ', i('Open'), ', ', i('Choose'), ', ', i('Type'), '.')),
      li(p(b('Say what should happen'), ' when a reader needs to know a step worked: ', i('The light turns green.'))),
      li(p(b('For long procedures,'), ' give each step a heading, as this site does, so a step can hold pictures and notes of its own.')),
    ),

    h(2, 'Related elements'),
    ul(
      li(p(pageLink('Bullet list'), ' for points in no particular order.')),
      li(p(pageLink('Task list'), ' for things to do and tick off.')),
      li(p(pageLink('Headings'), ' for steps that need more than a line.')),
    ),
  ))

  // ============================================================= Task list
  await page('Task list', elements, doc(
    p('A task list is a list of checkboxes: things to do, ticked off as they are done. Use it for the action items from a meeting, a launch checklist, or anything a team has to get through. Name a person in a task and it becomes theirs, and a ', pageLink('Task report'), ' on another page can gather everyone’s open tasks in one place.'),

    h(2, 'Insert it'),
    commands(['/todo', 'A task list'], ['/task', 'A task list'], ['/checkbox', 'A task list']),
    p('Type the command at the start of an empty line, press ', b('Enter'), ' and type the first task. Press ', b('Enter'), ' for the next, and ', b('Enter'), ' on an empty task to finish the list.'),
    ul(
      li(p(b('Markdown:'), ' ', c('[]'), ' or ', c('[ ]'), ' followed by a space at the start of a line. ', c('[x]'), ' and a space makes a task that is already done.')),
      li(p(b('Toolbar:'), ' the ', b('Task list'), ' button (on a phone, in the ', b('Aa Style'), ' menu).')),
      li(p(b('Keyboard:'), ' Ctrl+Shift+9', mac('⌘+Shift+9'), '.')),
    ),
    p('Lists are not in the ', b('+'), ' menu, because they have their own buttons on the toolbar.'),

    h(2, 'Kinds of task, and when to use each'),
    h(3, 'Not done, and done'),
    tasks(
      task(false, 'Order name badges for the new starters'),
      task(true, 'Book the room for the launch review'),
    ),
    p('An open task has an empty box. A done one has a tick, and its words are grayed out and struck through, so what is left to do stands out. Tick a task when the work is finished, not when it starts.'),
    p('Tasks are ticked in the editor: choose ', b('Edit'), ', click the box, and publish or update the page as usual. Readers see the boxes but cannot change them.'),
    h(3, 'Tasks with an owner'),
    tasks(
      owned(false, 'Sam Okafor', ' confirm the migration path for accounts over 50 GB'),
      owned(false, 'Mei Chen', ' draft the pricing page copy'),
      owned(true, 'Alex Rivera', ' book the launch review'),
    ),
    p('In a task, type ', c('@'), ' and the start of someone’s name, and choose them from the list. The first person named in a task is its owner, and they are told they were mentioned when you publish or update the page, as long as they can see it. A task with an owner is one somebody will do: “draft the pricing copy” waits; “Mei Chen: draft the pricing copy” gets done. See ', pageLink('Mention'), '.'),
    h(3, 'Tasks with a date'),
    tasks(
      owned(false, 'Priya Natarajan', ' send the launch announcement by ', date('2026-10-14')),
      owned(false, 'Jordan Brooks', ' renew the domain before ', date('2026-11-30')),
    ),
    p('When a task has a deadline, put it in with a ', pageLink('Date'), ' (type ', c('/date'), '). Each reader sees it in their own date format, so nobody mistakes 10/11 for November 10.'),
    h(3, 'Tasks inside a task'),
    tasks(
      taskWith(false, 'Prepare the launch email', tasks(
        task(true, 'Write the draft'),
        task(false, 'Get the wording approved'),
        task(false, 'Schedule it for 9:00 on launch day'),
      )),
      task(false, 'Update the help pages'),
    ),
    p('A big task broken into steps. Press ', b('Tab'), ' in a task to move it under the one above, and ', b('Shift+Tab'), ' to move it back out. Each box is ticked on its own: ticking the big task does not tick the ones inside it.'),

    h(2, 'Collecting tasks from many pages'),
    p('A ', pageLink('Task report'), ' shows tasks from many pages in one table, with their owners. It can look at the page it is on and the pages under it, a whole space, or everywhere, and show tasks that are done, not done, or both, for anyone or only for you. Put one on a team’s home page and nobody has to go looking for their action items.'),

    h(2, 'Changing and removing a task list'),
    ul(
      li(p(b('To end a list,'), ' press ', b('Enter'), ' on an empty task.')),
      li(p(b('To turn tasks back into paragraphs,'), ' select them and press the ', b('Task list'), ' button or Ctrl+Shift+9 again.')),
      li(p(b('To change a task’s owner,'), ' change the name in it: the first person named is always the owner. Take the name out and the task has no owner.')),
    ),

    h(2, 'Good practice'),
    ul(
      li(p(b('Start with a verb:'), ' ', i('Send'), ', ', i('Book'), ', ', i('Review'), '. A task is something to do.')),
      li(p(b('One owner to a task.'), ' If two people share it, split it in two.')),
      li(p(b('Tick, do not delete.'), ' A ticked task shows the work was done; a deleted one leaves people wondering.')),
      li(p(b('Put a meeting’s action items together'), ' at the end of the notes, under a heading such as ', i('Action items'), ', so they are easy to find.')),
    ),

    h(2, 'Related elements'),
    ul(
      li(p(pageLink('Decision'), ' records what was agreed; the tasks say what happens next.')),
      li(p(pageLink('Task report'), ' collects tasks from many pages.')),
      li(p(pageLink('Ordered list'), ' is for steps to follow, rather than work to track.')),
    ),
  ))

  // ================================================================== Link
  await page('Link', elements, doc(
    p('A link turns words into a way to somewhere else: another page in the wiki, a website, an email address, or a section further down the same page. Good links keep a page short. Instead of explaining again how to book a room, link to the page that already does.'),

    h(2, 'Insert it'),
    commands(['/link', 'The Add link dialog'], ['/url', 'The Add link dialog']),
    p('The dialog has two boxes. ', b('Address'), ' is where the link goes, and ', b('Display text'), ' is the words readers see and click. Fill in both and choose ', b('Save'), '. If you leave ', b('Display text'), ' empty, the address itself is shown. Or choose ', b('+'), ' on the toolbar, then ', b('Link'), ': with words selected, they become the display text.'),
    ul(
      li(p(b('Keyboard:'), ' Ctrl+K', mac('⌘+K'), ' opens the same dialog.')),
      li(p(b('Select some words'), ' and choose the link button in the small bar that appears above them. The words become the display text.')),
      li(p(b('Type or paste a web address'), ' into the text: it becomes a link when you type a space after it. Pasting an address while words are selected links those words.')),
    ),
    p('You can leave out ', c('https://'), ': Tesria adds it, so ', c('example.com'), ' works.'),

    h(2, 'Kinds of link, and when to use each'),
    h(3, 'A link in a sentence'),
    p('Before you book travel, read ', text('the travel policy', link('https://www.example.com/travel-policy')), '. It says which flights and hotels the company pays for.'),
    p('The most common kind. Let the linked words say where the link goes, as part of the sentence. “Read the travel policy” tells a reader what they will get; “click here” does not, and neither does a long address.'),
    h(3, 'A link to another page in this wiki'),
    p('All six sizes of heading are shown on the ', pageLink('Headings'), ' page.'),
    p('Open the page you want, copy its address from the browser’s address bar, and paste it as the ', b('Address'), '. The link keeps working when the page is renamed, because a page’s address does not change with its title.'),
    h(3, 'A link to a section'),
    p('Jump to ', text('Changing and removing a link', link('#changing-and-removing-a-link')), ' further down, or to ', sectionLink('Headings', 'the-six-levels-and-when-to-use-each', 'the six levels of heading'), ' on another page.'),
    p('In the dialog, ', b('Headings on this page'), ' lists every heading on the page; choose one and its address fills in. Readers who click it are scrolled straight there. For a section of another page, add ', c('#'), ' and the heading’s address to the end of that page’s address: ', pageLink('Headings'), ' explains how that address is made.'),
    h(3, 'An email address or a phone number'),
    p('Questions go to ', text('the help desk', link('mailto:help@example.com')), ', or call ', text('+1 555 0100', link('tel:+15550100')), '.'),
    p('Type ', c('mailto:'), ' and the email address as the ', b('Address'), ', such as ', c('mailto:help@example.com'), ': clicking it opens the reader’s email app with a new message. ', c('tel:'), ' and a number does the same for calls, which is handy for anyone reading on a phone.'),
    h(3, 'A bare address'),
    p('The status page is at ', text('https://status.example.com', link('https://status.example.com')), '.'),
    p('When the address itself is what people need: to write down, read out, or type on another device. Otherwise, give the link words.'),

    h(2, 'Changing and removing a link'),
    p('While you edit, clicking a link does not follow it. It puts the cursor there and shows a small bar under it (on a phone, tap the link):'),
    ul(
      li(p(b('The address'), ' opens the link in a new tab, so you can check where it goes.')),
      li(p(b('Edit'), ' opens the dialog as ', b('Edit link'), ', to change the address, the words, or both, and ', b('Save'), '. It also has ', b('Remove link'), '.')),
      li(p(b('Remove'), ' takes the link away and keeps the words.')),
    ),
    p('When a reader clicks a link, it opens in a new tab, so they keep their place on your page. A link to a heading on the same page scrolls there instead.'),

    h(2, 'Good practice'),
    ul(
      li(p(b('Link the words that describe the destination,'), ' not “here” or “this page”.')),
      li(p(b('Link a thing once,'), ' where it is first mentioned, not every time it comes up.')),
      li(p(b('Link rather than copy.'), ' When two pages need the same explanation, write it once and link to it, so there is only one copy to keep up to date.')),
      li(p(b('Check links to other websites now and then.'), ' Sites move things, and a link that goes nowhere costs readers trust.')),
    ),

    h(2, 'Related elements'),
    ul(
      li(p(pageLink('Smart link'), ' shows a preview card of the page it points to: its title, picture and description.')),
      li(p(pageLink('Headings'), ' give every section an address to link to.')),
      li(p(pageLink('Table of contents'), ' links to every heading on a page at once.')),
    ),
  ))

  // ================================================================ Panels
  // Moved from sections/pilot.mjs as the owner approved it.
  const panels = ids['Panels']
  await page('Panels', elements, doc(
    p('A panel is a colored box around one or more paragraphs. It tells readers “stop and read this” before they have read a word of it, and its color tells them what kind of thing it is: background, a tip, a warning. Use one for the thing on a page that nobody should miss.'),

    h(2, 'Insert it'),
    table([
      ['Type this', 'To get'],
      ['/info', 'An Info panel (blue)'],
      ['/note', 'A Note panel (purple)'],
      ['/tip', 'A Tip panel (green)'],
      ['/warning', 'A Warning panel (yellow)'],
      ['/error', 'An Error panel (red)'],
    ], [200, 500]),
    p('Type the command at the start of an empty line and press ', b('Enter'), '. Or choose ', b('+'), ' on the toolbar, then ', b('Panels'), '. If you select some text first, either way puts that text inside the new panel.'),
    ...(await animation(panels, 'panel-insert', 'Typing /info makes a panel; the buttons above it change its type.')),

    h(2, 'The five kinds, and when to use each'),
    p('Pick the kind by what the text means, not by the color you like: readers learn what each color means, and a warning in a green box reads as good news.'),
    panel('info', p(b('Info: background worth knowing.'), ' Use it for context that helps but is not an instruction. For example: ', i('This page describes the release process as of version 2.'), ' Or: ', i('The figures below come from the finance team’s March report.'))),
    panel('note', p(b('Note: something to keep in mind.'), ' A detail that is easy to overlook and changes how you read the rest. For example: ', i('Everything here applies to the Berlin office as well, except where a step says otherwise.'))),
    panel('success', p(b('Tip: a better way to do it.'), ' A shortcut, a best practice, or a trick that saves time. For example: ', i('Press Ctrl+K to search from anywhere, without reaching for the mouse.'))),
    panel('warning', p(b('Warning: be careful.'), ' Something can go wrong, but it can be put right. For example: ', i('Changing the build settings affects everyone on the team. Tell the channel before you do.'))),
    panel('error', p(b('Error: do not do this.'), ' Something that causes real harm or cannot be undone. For example: ', i('Never share the admin password in chat. Use the password manager.'))),

    h(2, 'Changing and removing a panel'),
    p('Click anywhere inside a panel and a small menu appears above it:'),
    ul(
      li(p(b('The five colored buttons'), ' switch the panel to that kind. Whatever is inside stays as it is.')),
      li(p(b('Remove panel'), ' takes the box away and keeps everything that was in it, as ordinary text.')),
    ),
    p('A panel can hold anything a page can: several paragraphs, lists, tables, pictures, even code. To add another paragraph inside it, press ', b('Enter'), ' at the end of the last line; press ', b('Enter'), ' twice on an empty line to step out below the panel.'),

    h(2, 'Good practice'),
    ul(
      li(p(b('Use them sparingly.'), ' One or two on a page stand out. When every other paragraph is in a box, nothing stands out, and readers learn to skip them.')),
      li(p(b('Lead with the point.'), ' Start with a few words in bold that say what the panel is about, as the examples above do, so someone skimming gets it without reading on.')),
      li(p(b('Keep one idea to a panel.'), ' Two warnings in one box are easy to half-read; two boxes are not.')),
      li(p(b('Do not stack them.'), ' Several panels in a row read as a wall of color. Move the least important into the text.')),
    ),

    h(2, 'Related elements'),
    ul(
      li(p(pageLink('Expand'), ' hides detail most readers can skip behind a title they click to open.')),
      li(p(pageLink('Decision'), ' records a decision the team made, so it can be found later.')),
      li(p(pageLink('Blockquote'), ' sets apart a quotation, rather than something the reader must act on.')),
    ),
  ))

  // ================================================================ Expand
  await page('Expand', elements, doc(
    p('An expand is a section that starts closed. Readers see only its title, with an arrow, and click it to open the rest. It keeps a long page short: the detail is there for the few who need it, and everyone else reads straight past. Frequently asked questions, long logs, reference tables and “how this works underneath” all fit well.'),

    h(2, 'Insert it'),
    commands(['/expand', 'An expand'], ['/collapse', 'An expand'], ['/details', 'An expand']),
    p('Type the command at the start of an empty line and press ', b('Enter'), '. Type the title in the box at the top, press ', b('Enter'), ' to move into the body, and write what it holds. Or choose ', b('+'), ' on the toolbar, then ', b('Expand'), '. If you select text first and use the ', b('+'), ' menu, the selected text goes inside the expand.'),

    h(2, 'Kinds of expand, and when to use each'),
    p('Each example below is closed, as readers see it. Click a title to open it.'),
    h(3, 'Questions and answers'),
    expand('Can I work from another country?',
      p('Yes, for up to four weeks a year. Tell your manager a month ahead, and check with the People team that your laptop may leave the country.')),
    expand('Who approves my time off?',
      p('Your manager. If they are away, the person standing in for them can approve it.')),
    expand('What happens to days I do not take?',
      p('Up to five carry over to next year. Anything beyond that is lost on December 31.')),
    p('A list of questions, each opening to its answer. A reader scans the questions and opens only the one they came for.'),
    h(3, 'The detail behind a summary'),
    p('The import finished with one warning: three pictures were too large and were skipped.'),
    expand('Full import log (5 lines)',
      codeBlock('plaintext', [
        '09:14:02 Import started: 212 pages, 480 pictures',
        '09:14:07 Pages imported',
        '09:14:09 Skipped pictures over 25 MB: site-plan.png, lobby.png, roof.png',
        '09:14:10 Pictures imported: 477 of 480',
        '09:14:11 Import finished',
      ].join('\n'))),
    p('Say the result in the text, and put the evidence in an expand: the full log, a long table, the raw figures behind a chart. An expand can hold anything a page can: lists, tables, code, pictures, even panels.'),
    h(3, 'An expand inside an expand'),
    expand('Troubleshooting the office printer',
      p('Open the problem you are seeing.'),
      expand('It says it is offline',
        p('Turn it off and on again, and wait a minute for it to join the network. If it still says offline, tell the front desk.')),
      expand('Pages come out blank',
        p('The toner has run out. A new cartridge is in the cupboard beside the printer; see the steps on the inside of its front cover.')),
    ),
    p('Detail inside detail, such as a troubleshooting section with one expand for each problem. One level inside another is plenty: any deeper, and readers lose track of what they have opened.'),
    panel('info', p(b('Open while you edit, closed for readers.'), ' An expand is always open in the editor, so you can see and change what is in it, and always starts closed when the page is read. A reader opening one opens it only for themselves.')),

    h(2, 'Changing and removing an expand'),
    p('While you edit, click the title to change it. Click inside the expand and a small bar appears above it with ', b('Remove expand'), ', which takes the expand away and keeps everything that was in it, as ordinary content.'),

    h(2, 'Good practice'),
    ul(
      li(p(b('Say what is inside.'), ' ', i('Full import log (5 lines)'), ' lets a reader decide without opening it; ', i('Details'), ' does not.')),
      li(p(b('Always give it a title.'), ' One without a title reads “Click to expand”, which tells nobody anything.')),
      li(p(b('Do not hide what everyone needs.'), ' Steps everyone must follow and warnings belong in plain sight.')),
      li(p(b('Put the key words in the title.'), ' The browser’s own Find (Ctrl+F) does not look inside a closed expand.')),
    ),

    h(2, 'Related elements'),
    ul(
      li(p(pageLink('Panels'), ' make something stand out rather than hiding it.')),
      li(p(pageLink('Headings'), ' divide a page into sections that are always open.')),
      li(p(pageLink('Code block'), ' is the best way to show a log inside an expand.')),
    ),
  ))

  // ============================================================== Decision
  await page('Decision', elements, doc(
    p('A decision records something the team agreed, marked with a check mark so it stands out from the discussion around it. In meeting notes, it answers “so what did we decide?”. Months later, when someone asks why things are the way they are, the decision is easy to find, with the date and the context on the page around it.'),

    h(2, 'Insert it'),
    commands(['/decision', 'A decision'], ['/decided', 'A decision'], ['/agreed', 'A decision']),
    p('Type the command at the start of an empty line, press ', b('Enter'), ' and write what was decided. Or choose ', b('+'), ' on the toolbar, then ', b('Decision'), '. If you select text first and use the ', b('+'), ' menu, the selected text becomes the decision.'),

    h(2, 'Kinds of decision, and when to use each'),
    p('Every decision looks the same. What changes is how much you put in it.'),
    h(3, 'A one-line decision'),
    decision('Launch day is October 14.'),
    p('Most decisions fit in a sentence. Write it as a plain statement, in the present tense, so it still reads correctly a year from now.'),
    h(3, 'A decision with its reasons'),
    decisionOf(
      p(b('We keep the free plan, limited to three people.')),
      ul('Most paying customers started on it.', 'It costs us less than 5% of our support time.'),
    ),
    p('A decision can hold more than one paragraph, and lists. Add the one or two reasons people will ask about, so the decision is not reopened for want of them.'),
    h(3, 'Several decisions from one meeting'),
    p(b('Kickoff, September 2')),
    decision('The beta opens to 50 customers on September 30.'),
    decision('Mei Chen owns the pricing page.'),
    decision('We do not support Windows 7.'),
    p('One decision to a block, so each can be read, quoted and found on its own. Keep them together under a heading such as ', i('Decisions'), ', with the action items that follow from them.'),

    h(2, 'Changing and removing a decision'),
    p('Click inside a decision and a small bar appears above it with ', b('Remove decision'), ', which takes the check mark away and keeps the text. There is nothing else to set: a decision has no types or colors.'),

    h(2, 'Good practice'),
    ul(
      li(p(b('Record the outcome, not the debate.'), ' The discussion can go in normal text around it.')),
      li(p(b('Say who agreed, and when,'), ' if the page does not already: ', i('Agreed by the product team on September 2.'))),
      li(p(b('Do not rewrite an old decision.'), ' When it changes, add a new one that says what changed and why, and leave the old one where it is. The history is the point.')),
      li(p(b('Follow it with tasks.'), ' A decision usually means work: list it in a ', pageLink('Task list'), ' below, with owners.')),
    ),

    h(2, 'Related elements'),
    ul(
      li(p(pageLink('Task list'), ' says who does what because of the decision.')),
      li(p(pageLink('Panels'), ' call out information rather than an agreement.')),
      li(p(pageLink('Headings'), ' keep a meeting’s decisions in one findable place.')),
    ),
  ))

  // ================================================================ Layout
  await page('Layout', elements, doc(
    p('A layout puts content side by side, in two or three columns. Use it when things are meant to be compared, such as before and after or one plan against another, or to set a short summary beside the text it sums up. On a phone the columns stack one above another, so nothing gets squeezed.'),

    h(2, 'Insert it'),
    commands(['/layout', 'Two equal columns'], ['/columns', 'Two equal columns']),
    p('Type the command and press ', b('Enter'), ', or choose ', b('+'), ' on the toolbar, then ', b('Layout'), '. You get two equal columns, each with an empty line to type in, and a bar above them that changes their shape. Click where the layout should go first, without selecting anything: selected text is replaced.'),
    p('A layout always sits at the top level of a page. Inserted while the cursor is in a panel, a list or an expand, it goes below that instead, and a layout cannot go inside another layout.'),

    h(2, 'The five shapes, and when to use each'),
    p('Here is each shape, with the kind of content it suits. On a phone you see each one as a stack.'),
    h(3, 'Two columns'),
    layout([50, 50],
      [p(b('Today')), ul('Requests arrive by email', 'Answered within two working days', 'No record of past conversations')],
      [p(b('From November')), ul('Requests arrive in the help desk', 'Answered within one working day', 'Every conversation kept with the customer')],
    ),
    p('Two things of equal weight side by side: before and after, for and against, option A and option B.'),
    h(3, 'Three columns'),
    layout([33.33, 33.34, 33.33],
      [p(b('Starter')), p('Up to 10 people. Help by email.')],
      [p(b('Team')), p('Up to 100 people. Help by chat.')],
      [p(b('Company')), p('Any number of people. A named contact.')],
    ),
    p('Three things of the same kind: plans, offices, teams. Keep each column short; three columns of long text are narrow and tiring to read.'),
    h(3, 'Left sidebar'),
    layout([33.33, 66.67],
      [p(b('At a glance')), ul('Owner: Priya Natarajan', 'Status: in progress', 'Launch: October 14')],
      [p('Kestrel Sync 2 lets people keep working when they lose their connection. Pages open and save offline, and everything syncs the moment they are back online. Shared folders arrive in the same release, with a faster first sync on new laptops.')],
    ),
    p('A narrow column of facts or links beside the main text: the key facts about a project, the people to contact, related pages. On a phone the sidebar comes first, so put in it what a reader should see first.'),
    h(3, 'Right sidebar'),
    layout([66.67, 33.33],
      [p('To request a new laptop, fill in the equipment form and choose the model your manager approved. Laptops arrive within a week and are set up by IT before they reach you.')],
      [panel('success', p(b('Tip:'), ' order a week before a new starter arrives, and their laptop is ready on day one.'))],
    ),
    p('The main text first, with something extra beside it, such as a tip or a note. On a phone the main text comes first and the sidebar follows it, which is right when the side column is extra rather than essential. As here, a column can hold other elements: panels, lists, tables, pictures.'),
    h(3, 'Three with sidebars'),
    layout([25, 50, 25],
      [p(b('Before this')), p('Book the venue.')],
      [p(b('This step: send the invitations')), p('Send them six weeks ahead, from the shared events address, with the date, the place and a link to sign up.')],
      [p(b('After this')), p('Order the catering.')],
    ),
    p('A wide middle with a narrow column each side: for a page that sits in a sequence, with what comes before and after on either side, or to set one important thing in the center.'),

    h(2, 'How wide it is'),
    p('A layout can also be wider than the text around it. The bar above it offers ', b('Centered'), ', the width of the text, which is how every layout above is set; ', b('Wide'), ', a little wider; and ', b('Full width'), ', as wide as the page area allows. Here are the two wider ones:'),
    wideLayout('wide', [50, 50],
      [p(b('Wide.'), ' A little more room than the text, for columns that each hold a small table or a picture.')],
      [p('Use it when Centered feels cramped but the content is still meant to be read line by line.')],
    ),
    wideLayout('full', [33.33, 33.34, 33.33],
      [p(b('Full width.'), ' As wide as the page area.')],
      [p('For the one thing on a page that needs the room, such as a comparison with a lot in each column.')],
      [p('On a phone every width looks the same: the columns stack.')],
    ),

    h(2, 'Changing and removing a layout'),
    p('Click in any column and a bar appears above the layout:'),
    ul(
      li(p(b('The five shape buttons'), ' (', b('Two columns'), ', ', b('Three columns'), ', ', b('Left sidebar'), ', ', b('Right sidebar'), ' and ', b('Three with sidebars'), ') change the shape. Content stays where it is; going from three columns to two moves what was in the third into the second.')),
      li(p(b('Centered'), ', ', b('Wide'), ' and ', b('Full width'), ' set how wide it is.')),
      li(p(b('Remove layout'), ' takes the columns away and keeps their contents, one after another, in column order.')),
    ),
    p('While you edit, each column has a dashed outline so you can see where it starts and ends; readers do not see it. If the cursor is in a panel, a table or another element inside a column, that element’s own menu shows instead: click in plain text in the column to get the layout bar back.'),

    h(2, 'Good practice'),
    ul(
      li(p(b('Use columns for things read side by side,'), ' not to fit more on the screen.')),
      li(p(b('Keep the columns about the same length,'), ' or the page has a long empty gap beside the longest one.')),
      li(p(b('Think about the phone.'), ' The columns stack in order, first column first, so put first what should be read first.')),
      li(p(b('Two columns are usually enough.'), ' The narrower the columns, the harder they are to read.')),
    ),

    h(2, 'Related elements'),
    ul(
      li(p(pageLink('Table'), ' for comparing many things across the same few facts.')),
      li(p(pageLink('Image'), ' beside text: put the picture in one column and the words in the other.')),
      li(p(pageLink('Panels'), ' sit well in a sidebar.')),
    ),
  ))

  // ================================================================= Table
  const tableId = ids['Table']
  // Optional: the page stands without its animation if a run did not make it.
  let cellOptions = []
  try {
    cellOptions = await animation(tableId, 'table-cell-options', 'Typing /table makes a table. Cell options, the arrow at the top right of the cell you are in, turns on a header column and colors a row.')
  } catch (err) {
    console.warn(`  (Table: no cell options animation, ${err.message})`)
  }
  await page('Table', elements, doc(
    p('A table arranges information in rows and columns, so readers can look something up and compare along a row or down a column: who is on call which week, what each plan includes, which rooms are free. Use one when every item has the same few facts about it.'),

    h(2, 'Insert it'),
    commands(['/table', 'A table, three rows by three columns, with a header row']),
    p('Type the command and press ', b('Enter'), ', or choose ', b('+'), ' on the toolbar, then ', b('Table'), '. If text is selected, the table takes its place. The cursor starts in the first cell: type, then press ', b('Tab'), ' to move to the next cell (', b('Shift+Tab'), ' goes back). ', b('Tab'), ' in the last cell adds a new row, so you can keep typing.'),
    ...cellOptions,

    h(2, 'Table options, and when to use each'),
    p('Headers, merging and colors are all in ', b('Cell options'), ': the small arrow at the top right of the cell you are in, while you edit. Each option is shown here live.'),
    h(3, 'Header row'),
    table([
      ['Week of', 'On call', 'Backup'],
      ['September 28', 'Sam Okafor', 'Jordan Brooks'],
      ['October 5', 'Mei Chen', 'Sam Okafor'],
      ['October 12', 'Jordan Brooks', 'Mei Chen'],
    ]),
    p('The first row names the columns, in bold on a shaded background. Almost every table wants one, and a new table starts with it on. Turn it off or on with ', b('Header row'), ' in Cell options.'),
    h(3, 'Header column'),
    tableOf(null,
      row(th('Owner'), td('Priya Natarajan')),
      row(th('Status'), td('In progress')),
      row(th('Launch'), td('October 14')),
      row(th('Budget'), td('$40,000')),
    ),
    p('The first column names the rows instead. Use it for a short list of facts, one to a row, like this one. ', b('Header column'), ' in Cell options turns it on and off.'),
    h(3, 'Header row and header column together'),
    tableOf(null,
      row(th(''), th('Starter'), th('Team'), th('Company')),
      row(th('People'), td('Up to 10'), td('Up to 100'), td('Any number')),
      row(th('Help'), td('Email'), td('Chat'), td('A named contact')),
      row(th('Price per person'), td('$0'), td('$6'), td('$12')),
    ),
    p('A grid read both ways, such as a comparison of plans: the top names the options, the side names what is being compared. Turn both on.'),
    h(3, 'Merged cells'),
    tableOf(null,
      row(th('Time'), th('Room A'), th('Room B')),
      row(th('9:00'), td('Welcome, in both rooms', { colspan: 2 })),
      row(th('10:00'), td('Security basics (two hours)', { rowspan: 2 }), td('Design review')),
      row(th('11:00'), td('Hiring panel')),
    ),
    p('One cell stretched across several columns or rows, for something that spans them: a talk in both rooms, a session that runs two hours. To merge, drag across the cells to select them, then choose ', b('Merge cells'), ' in Cell options. ', b('Split cell'), ' turns a merged cell back into separate ones. Merge sparingly: a table with many merged cells is hard for readers to follow.'),
    h(3, 'Colored cells'),
    tableOf(null,
      row(th('Room'), th('Monday'), th('Tuesday'), th('Wednesday')),
      row(th('Oak'), td('Free', { bg: LIGHT.green }), td('Booked', { bg: LIGHT.red }), td('Free', { bg: LIGHT.green })),
      row(th('Pine'), td('Booked', { bg: LIGHT.red }), td('On hold', { bg: LIGHT.yellow }), td('Free', { bg: LIGHT.green })),
      row(th('Cedar'), td('Free', { bg: LIGHT.green }), td('Free', { bg: LIGHT.green }), td('Booked', { bg: LIGHT.red })),
    ),
    p('Green is free, yellow is on hold, red is booked.'),
    p('A background color for a cell, a whole row or a whole column. In Cell options, under ', b('Background color'), ', choose ', b('Cell'), ', ', b('Row'), ' or ', b('Column'), ', then a color from the palette of light, medium and bold shades; ', b('No color'), ' takes it off. Use color to make a pattern jump out, as here, and always say in words what each color means, because not everyone can tell colors apart.'),
    h(3, 'Column widths'),
    table([
      ['Term', 'What it means'],
      ['Space', 'A home for a set of pages that belong together, with its own page tree and its own list of who can read and edit it.'],
      ['Draft', 'Changes to a page that have not been published yet. Only people who can edit the page see them.'],
      ['Label', 'A word attached to a page, so pages on the same subject can be found together.'],
    ], [140, 560]),
    p('Drag the border between two columns to change their widths, while you edit. Give a column of short entries, such as names or dates, just the room it needs, and the rest to the column with something to say.'),
    h(3, 'Narrower than the page'),
    tableOf({ width: 320, layout: 'default' },
      row(th('Size'), th('Price')),
      row(td('Small'), td('$4')),
      row(td('Large'), td('$6')),
    ),
    p('Drag the right edge of the table to make the whole table narrower, or wider. A small table of short entries reads better at the size of its contents than stretched across the page.'),
    h(3, 'Full width'),
    tableOf({ width: null, layout: 'full-width' },
      row(th('Release'), th('Code freeze'), th('Testing'), th('Beta'), th('Launch'), th('Owner')),
      row(td('2.0'), td('September 1'), td('September 2 to 19'), td('September 30'), td('October 14'), td('Priya Natarajan')),
      row(td('2.1'), td('November 3'), td('November 4 to 14'), td('November 20'), td('December 1'), td('Sam Okafor')),
    ),
    p('Choose ', b('⤢'), ' at the top right corner of a table, while you edit, to spread it across the full width of the page area, past the edges of the text. For wide tables with many columns. Choose it again to go back. Inside a layout, a panel or an expand, a full-width table fills that instead.'),

    h(2, 'Changing and removing a table'),
    p('While you edit, point at a table (on a touch screen, tap in it) and controls appear around it:'),
    ul(
      li(p(b('+'), ' above the table and down its left side adds a column or a row at that point.')),
      li(p(b('×'), ' on the strip above each column, or beside each row, deletes that column or row.')),
      li(p(b('Column borders'), ' and ', b('the right edge'), ' drag to change widths, as above; ', b('⤢'), ' makes the table full width.')),
      li(p(b('Cell options'), ', the arrow at the top right of the cell you are in, has ', b('Header row'), ', ', b('Header column'), ', ', b('Merge cells'), ', ', b('Split cell'), ', the background colors, and ', b('Delete table'), ', which removes the whole table.')),
    ),
    p('On a phone, readers see every column at a readable width, and the table scrolls sideways rather than squeezing its columns into a word a line.'),

    h(2, 'Good practice'),
    ul(
      li(p(b('Tables for information, not for arranging a page.'), ' To put text side by side, use a ', pageLink('Layout'), '.')),
      li(p(b('Give it a header row,'), ' so every column says what it holds.')),
      li(p(b('Keep cells short:'), ' a word, a number, a short phrase. Longer explanations read better as a list below the table.')),
      li(p(b('Put the rows in an order readers expect,'), ' such as by date or alphabetically.')),
      li(p(b('Say what colors mean,'), ' in words, next to the table.')),
    ),

    h(2, 'Related elements'),
    ul(
      li(p(pageLink('Chart'), ' draws the numbers in a table on the same page as bars, columns, lines or a pie.')),
      li(p(pageLink('Layout'), ' puts text and pictures side by side.')),
      li(p(pageLink('Bullet list'), ' is simpler when each item has only one thing to say.')),
    ),
  ))

  // ============================================================ Code block
  await page('Code block', elements, doc(
    p('A code block holds text that has to be exactly right: a command to run, part of a program, a setting to paste into a file. It is set in a fixed-width font, colored to suit its language, keeps every space and line break as typed, and has a ', b('Copy'), ' button so readers can take it without missing a character.'),

    h(2, 'Insert it'),
    commands(['/code', 'A code block'], ['/snippet', 'A code block']),
    p('Type the command at the start of an empty line, press ', b('Enter'), ', then type or paste the code. Or choose ', b('+'), ' on the toolbar, then ', b('Code block'), '. If you select text first and use the ', b('+'), ' menu, that text becomes the code.'),
    ul(
      li(p(b('Markdown:'), ' three backticks, ', c('```'), ', at the start of a line, then ', b('Enter'), '. Add the language straight after them, such as ', c('```python'), ', and the block starts in that language.')),
      li(p(b('Keyboard:'), ' Ctrl+Alt+C', mac('⌘+Option+C'), '.')),
      li(p(b('Code inside a sentence,'), ' such as a file name, is ', b('inline code'), ' (Ctrl+E) instead: see ', pageLink('Text formatting'), '.')),
    ),

    h(2, 'Languages, and when to use each'),
    p('Choose the language from the menu at the top left of the block. It colors the code the way that language is usually shown, and readers see its name as a label, so they know what they are looking at. The languages are Plain text, JavaScript, TypeScript, Python, C#, Bash / Shell, JSON, YAML, SQL, HTML, CSS, Go, Rust, Java, Dockerfile and Markdown. One more, Mermaid diagram, draws a diagram instead: see ', pageLink('Diagram (Mermaid)'), '.'),
    h(3, 'Commands to run: Bash / Shell'),
    codeBlock('bash', 'cd ~/Downloads\nls -l *.pdf'),
    p('Commands someone types into Terminal or a command prompt. Put each command on its own line, and say in the text around it which computer to run it on.'),
    h(3, 'A program: Python, JavaScript and the rest'),
    codeBlock('python', 'def greet(name):\n    return f"Hello, {name}!"\n\nprint(greet("Mei"))'),
    p('A few lines of a program. Keep it to the lines that matter, and say what it does in a sentence before it.'),
    h(3, 'Settings and data: JSON and YAML'),
    codeBlock('json', '{\n  "name": "Kestrel Sync",\n  "version": "2.0.0",\n  "offline": true\n}'),
    codeBlock('yaml', 'name: Kestrel Sync\nversion: 2.0.0\noffline: true'),
    p('A settings file or data to copy. The colors make a missing quotation mark or comma easier to spot.'),
    h(3, 'A query: SQL'),
    codeBlock('sql', "SELECT name, email\nFROM customers\nWHERE plan = 'team'\nORDER BY name;"),
    p('A database query to run or to review. Put each part of the query on its own line, as here, so it can be read at a glance.'),
    h(3, 'Plain text'),
    codeBlock('plaintext', '2026-09-24 09:14:02  Import started\n2026-09-24 09:14:09  3 pictures were too large and were skipped\n2026-09-24 09:14:11  Import finished'),
    p('Text that must stay exact but is not code: log lines, an error message to search for, output to compare against. Plain text is not colored.'),
    h(3, 'With line numbers'),
    code2('javascript', 'const settings = {\n  retries: 3,\n  timeout: 30,\n  offline: true,\n}\n\nexport default settings', { lineNumbers: true }),
    p('Choose ', b('#'), ' at the top right of a block, while you edit, to number its lines; readers see the numbers too. Use them when the text refers to lines (line 3 sets the timeout, in seconds) or when the block is long. ', b('Copy'), ' takes only the code, never the numbers.'),

    h(2, 'Changing and removing a code block'),
    p('While you edit, the bar at the top of the block has:'),
    ul(
      li(p(b('The language menu'), ', at the left.')),
      li(p(b('#'), ', which turns line numbers on and off.')),
      li(p(b('Copy'), ', which copies the code. Readers have this button too.')),
    ),
    p('Inside a code block, ', b('Enter'), ' starts a new line of code, and ', b('Tab'), ' does not indent: type spaces instead. To leave the block, press ', b('Enter'), ' three times at the end, or the down arrow on the last line. To turn it back into normal text, put the cursor in it and press Ctrl+Alt+C.'),

    h(2, 'Good practice'),
    ul(
      li(p(b('Always choose the language.'), ' Readers get the colors and a label saying what it is.')),
      li(p(b('Never put a password or a key in a code block.'), ' Anyone who can read the page can copy it. Write a placeholder, such as ', c('your-api-key'), ', and say where the real one is kept.')),
      li(p(b('Keep it short.'), ' Show the lines that matter and link to the whole file.')),
      li(p(b('Hide long output'), ' in an ', pageLink('Expand'), ', with a sentence above it saying what it shows.')),
    ),

    h(2, 'Related elements'),
    ul(
      li(p(pageLink('Diagram (Mermaid)'), ' is a code block that draws a diagram from its text.')),
      li(p(pageLink('Expand'), ' tucks a long log or listing away.')),
      li(p(pageLink('Blockquote'), ' for quoting words rather than code.')),
    ),
  ))

  // ===================================================== Diagram (Mermaid)
  await page('Diagram (Mermaid)', elements, doc(
    p('A diagram draws a flowchart, a sequence of messages, a timeline or another kind of chart from a few lines of text, using a popular free tool called Mermaid. You write what is connected to what, and the drawing follows.'),
    p('Changing a diagram is as easy as editing a sentence: no boxes to drag, and no picture to make again in another program. The text lives in the page, so the next person can change it too.'),

    h(2, 'Insert it'),
    commands(
      ['/mermaid', 'A diagram, starting as a small flowchart'],
      ['/diagram', 'The same'],
      ['/flowchart', 'The same'],
      ['/sequence', 'The same'],
    ),
    p('Type the command and press ', b('Enter'), ', or choose ', b('+'), ' on the toolbar, then ', b('Diagram (Mermaid)'), '. You get a small example flowchart to change. If text is selected, the diagram takes its place.'),
    p('A diagram is a code block whose language is Mermaid diagram, so you can also make one from any ', pageLink('Code block'), ' by choosing ', b('Mermaid diagram'), ' in its language menu, or by typing ', c('```mermaid'), ' at the start of a line and pressing ', b('Enter'), '.'),
    p('To change the text, choose ', b('Source'), ' at the top right of the diagram; choose ', b('Diagram'), ' to see the drawing again.'),

    h(2, 'Kinds of diagram, and when to use each'),
    p('The first word of the text says what kind of diagram it is. Here are the most useful kinds, each drawn live from its text. Choose ', b('Source'), ' on any of them to see how it is written, and copy it as a start for your own.'),
    h(3, 'Flowchart'),
    mermaid(
      'flowchart TD',
      '  A[Expense to claim] --> B{More than 500?}',
      '  B -->|No| C[Submit it in the expenses app]',
      '  B -->|Yes| D[Ask your manager to approve it]',
      '  D --> C',
      '  C --> E[Paid with your next salary]',
    ),
    p('Steps and choices: a process, an approval path, what to do when something breaks. ', c('flowchart TD'), ' draws it top to bottom; ', c('flowchart LR'), ', left to right.'),
    h(3, 'Sequence diagram'),
    mermaid(
      'sequenceDiagram',
      '  participant C as Customer',
      '  participant S as Shop',
      '  participant P as Payment provider',
      '  C->>S: Place order',
      '  S->>P: Charge card',
      '  P-->>S: Payment approved',
      '  S-->>C: Confirmation email',
    ),
    p('Who sends what to whom, in order: how two systems talk to each other, or the hand-offs between teams.'),
    h(3, 'State diagram'),
    mermaid(
      'stateDiagram-v2',
      '  state "In review" as Review',
      '  [*] --> Draft',
      '  Draft --> Review: Submit',
      '  Review --> Draft: Changes requested',
      '  Review --> Approved: Approve',
      '  Approved --> [*]',
    ),
    p('The stages something moves through, and what moves it on: a document’s review, an order, a support request.'),
    h(3, 'Gantt chart'),
    mermaid(
      'gantt',
      '  title Office move',
      '  dateFormat YYYY-MM-DD',
      '  section Planning',
      '  Choose furniture :a1, 2026-10-01, 10d',
      '  Order furniture :a2, after a1, 5d',
      '  section Moving',
      '  Pack :b1, 2026-10-26, 5d',
      '  Move day :milestone, m1, 2026-11-02, 0d',
    ),
    p('A plan over time, with each task a bar on a calendar and what has to wait for what.'),
    h(3, 'Timeline'),
    mermaid(
      'timeline',
      '  title Kestrel Sync releases',
      '  2024 : Version 1',
      '  2025 : Shared folders',
      '       : Faster first sync',
      '  2026 : Version 2, with offline editing',
    ),
    p('Events in order, such as a project’s history or a product’s releases, without the detail of a Gantt chart.'),
    h(3, 'Pie chart'),
    mermaid(
      'pie title Support requests by topic',
      '  "Signing in" : 42',
      '  "Billing" : 28',
      '  "Exports" : 18',
      '  "Other" : 12',
    ),
    p('Shares of a whole, for a quick impression. When readers need the exact numbers, a table with a ', pageLink('Chart'), ' drawn from it serves them better.'),
    h(3, 'Mind map'),
    mermaid(
      'mindmap',
      '  root((Launch))',
      '    Marketing',
      '      Blog post',
      '      Newsletter',
      '    Support',
      '      Help pages',
      '      Training',
      '    Sales',
      '      Price list',
    ),
    p('Ideas branching out from one topic: a brainstorm, or the parts of a project at a glance. Indentation sets what belongs to what.'),
    h(3, 'User journey'),
    mermaid(
      'journey',
      '  title A new starter’s first week',
      '  section First day',
      '    Collect laptop: 3: New starter, IT',
      '    Meet the team: 5: New starter',
      '  section Rest of the week',
      '    Set up accounts: 2: New starter, IT',
      '    First project: 4: New starter',
    ),
    p('How an experience feels, step by step, each step scored from 1 (bad) to 5 (good) and with the people involved: onboarding, a customer’s first order.'),
    h(3, 'Quadrant chart'),
    mermaid(
      'quadrantChart',
      '  title Ideas by effort and impact',
      '  x-axis Low effort --> High effort',
      '  y-axis Low impact --> High impact',
      '  quadrant-1 Plan carefully',
      '  quadrant-2 Do first',
      '  quadrant-3 Maybe later',
      '  quadrant-4 Skip',
      '  Dark theme: [0.3, 0.8]',
      '  Offline mode: [0.8, 0.9]',
      '  New icons: [0.2, 0.25]',
      '  Custom fonts: [0.75, 0.2]',
    ),
    p('Things placed on two scales at once, such as ideas by effort and impact, to decide what to do first.'),
    h(3, 'Entity relationship diagram'),
    mermaid(
      'erDiagram',
      '  CUSTOMER ||--o{ ORDER : places',
      '  ORDER ||--|{ ORDER_LINE : contains',
      '  PRODUCT ||--o{ ORDER_LINE : "appears in"',
    ),
    p('How the tables in a database relate to each other: which one holds many of which. For developers and data teams.'),
    h(3, 'Class diagram'),
    mermaid(
      'classDiagram',
      '  class Customer {',
      '    name',
      '    email',
      '    placeOrder()',
      '  }',
      '  class Order {',
      '    date',
      '    total()',
      '  }',
      '  Customer "1" --> "*" Order : places',
    ),
    p('The parts of a program and how they connect, with what each one holds and does. For developers.'),
    h(3, 'Git graph'),
    mermaid(
      'gitGraph',
      '  commit',
      '  branch feature',
      '  checkout feature',
      '  commit',
      '  commit',
      '  checkout main',
      '  merge feature',
      '  commit',
    ),
    p('Branches and merges in a code repository, to explain how a team works with Git.'),
    h(3, 'And more'),
    p('The Mermaid in this version of Tesria also draws XY charts, Sankey diagrams, Kanban boards, block diagrams, packet diagrams, requirement diagrams and C4 architecture diagrams, among others. Mermaid’s own documentation, at ', text('mermaid.js.org', link('https://mermaid.js.org')), ', shows how to write every kind.'),

    h(2, 'Changing and removing a diagram'),
    ul(
      li(p(b('Source'), ' and ', b('Diagram'), ', at the top right, switch between the text and the drawing. While you edit, change the text in Source, then choose Diagram to check the result. Readers can open Source too, to see how it is made.')),
      li(p(b('Copy'), ' copies the text.')),
      li(p(b('The language menu'), ', at the left while you edit: choose another language and the diagram becomes an ordinary code block showing its text.')),
    ),
    p('A mistake in the text shows Mermaid’s own message where the drawing would be, usually naming the line that went wrong. Diagrams are drawn in light or dark colors to match the theme Tesria is in.'),

    h(2, 'Good practice'),
    ul(
      li(p(b('Start from an example'), ' and change one line at a time, checking with ', b('Diagram'), ' as you go.')),
      li(p(b('Keep it small.'), ' Ten or fifteen boxes are about as many as a reader can follow; split a bigger diagram into several.')),
      li(p(b('Label the arrows'), ' when the reason for a step is not obvious, as the Yes and No of the flowchart above.')),
      li(p(b('Say in a sentence what the diagram shows.'), ' Not every reader can see it, and the sentence helps those who can.')),
    ),

    h(2, 'Related elements'),
    ul(
      li(p(pageLink('Code block'), ' shows text as code rather than drawing it.')),
      li(p(pageLink('Chart'), ' draws the numbers in a table as bars, lines or a pie.')),
      li(p(pageLink('Image'), ' for a picture made in another program.')),
    ),
  ))
}
