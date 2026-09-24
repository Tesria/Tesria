// User manual → The editor → Live content: one page per live content block,
// written to the owner's rules (scripts/docs/WRITING.md, element pages).
// Each page shows the real block, working on the page itself, once for each
// setting that gives something worth seeing in the Docs space.
//
// Facts from src/web/src/editor/dynamicBlockKinds.ts (the kinds, their
// settings and defaults), slash/items.ts (the slash menu matches a word
// against each item's name and keywords, in catalog order, so some words
// list another item first), InsertMenu.tsx (the + menu's Live content group),
// DynamicBlockMenu.tsx and DynamicBlockView.tsx (the settings box and what a
// reader sees), and src/Api/Features/Blocks (what each kind looks up, its
// sort order and its empty messages), checked 2026-09-24.
//
// Pages written (titles kept from manual-editor.mjs, so they are rewritten in
// place): Children display, Recently updated, Content by label, Attachments
// (live content), Change history, Contributors, Include page, Excerpt include,
// Page properties report, Labels list, Task report, Page tree.
//
// New page, for the owner to confirm: "How live content works", under Live
// content. It holds the notes every block shares, and it is the source the
// Include page and Excerpt include examples show (its first paragraph is an
// excerpt). cleanup() puts it first in the section.
//
// So that the label-driven blocks have something real to find, every page
// here carries the label live-content and one or two that say what kind of
// block it is, and each has a small Page properties table (Shows, Looks in,
// Type this), which the Page properties report page collects into one table.
// The Attachments page has a file attached (live-content-blocks.csv, made
// here from BLOCKS) so its block has a row to show.
//
// For the owner: manual-editor.mjs still writes these twelve pages from its
// LIVE array, and takes an el-* picture of each from Tesria Demo. Once this
// section runs, those entries should go, or the two would rewrite each other.
// cleanup() below takes the old el-* pictures off these pages.

// One entry per page: the block's name in the editor, its Page properties
// facts (short, so the report reads as a table), and its labels.
const BLOCKS = [
  { title: 'Children display', name: 'Children display', type: '/children', shows: 'The pages under this one', looksIn: 'This page’s sub-pages', labels: ['page-lists'] },
  { title: 'Recently updated', name: 'Recently updated', type: '/recent', shows: 'The pages changed most recently', looksIn: 'This space, or this page and below', labels: ['page-lists'] },
  { title: 'Content by label', name: 'Content by label', type: '/content', shows: 'Pages with the labels you name', looksIn: 'This space, or every space', labels: ['page-lists', 'uses-labels', 'needs-setup'] },
  { title: 'Attachments (live content)', name: 'Attachments', type: '/attachments', shows: 'The files attached to this page', looksIn: 'This page', labels: ['this-page'] },
  { title: 'Change history', name: 'Change history', type: '/history', shows: 'This page’s latest versions', looksIn: 'This page', labels: ['this-page'] },
  { title: 'Contributors', name: 'Contributors', type: '/contributors', shows: 'Who edited, most edits first', looksIn: 'This page, or this page and below', labels: ['this-page'] },
  { title: 'Include page', name: 'Include page', type: '/include', shows: 'The whole of another page', looksIn: 'One page you choose', labels: ['includes', 'needs-setup'] },
  { title: 'Excerpt include', name: 'Excerpt include', type: '/excerpt', shows: 'The excerpt from another page', looksIn: 'One page you choose', labels: ['includes', 'needs-setup'] },
  { title: 'Page properties report', name: 'Page properties report', type: '/report', shows: 'Page properties tables, collected', looksIn: 'Labeled pages in every space', labels: ['reports', 'uses-labels', 'needs-setup'] },
  { title: 'Labels list', name: 'Labels list', type: '/labels', shows: 'Labels, as links', looksIn: 'This page, or this space', labels: ['uses-labels'] },
  { title: 'Task report', name: 'Task report', type: '/action', shows: 'Task list items, collected', looksIn: 'This page and below, this space, or everywhere', labels: ['reports'] },
  { title: 'Page tree', name: 'Page tree', type: '/sitemap', shows: 'Pages as nested links', looksIn: 'From this page, or the whole space', labels: ['page-lists'] },
]
const BASICS = 'How live content works'
const CHANGE = 'Rewritten, with each setting shown live'

/** The cheat sheet attached to the Attachments page: one row per block. */
function cheatSheet() {
  const q = (s) => (/[",\n]/.test(s) ? `"${s.replace(/"/g, '""')}"` : s)
  const rows = [['Block', 'Type this', 'Shows', 'Looks in'], ...BLOCKS.map((x) => [x.name, x.type, x.shows, x.looksIn])]
  return Buffer.from(rows.map((r) => r.map(q).join(',')).join('\r\n') + '\r\n', 'utf8')
}

// No pictures: every block is shown working on its own page, and where its
// settings are and what they do is said in words.
export const shots = () => []

export async function build({ top, page, ensure, attachCurrent, doc, p, h, text, bold, italic, code, ul, li, panel, table, live, expand, excerpt, cell, pageLink }) {
  const b = (t) => text(t, bold)
  const c = (t) => text(t, code)
  const i = (t) => text(t, italic)

  const editor = await ensure('The editor', top['User manual'])
  const liveParent = await ensure('Live content', editor)
  // Every page first, so links between them resolve on a first run.
  const basicsId = await ensure(BASICS, liveParent)
  const id = {}
  for (const x of BLOCKS) id[x.title] = await ensure(x.title, liveParent)
  const info = Object.fromEntries(BLOCKS.map((x) => [x.title, x]))

  /** The page's facts, as a Page properties table the report can collect. */
  const glance = (title) => {
    const x = info[title]
    const row = (k, v) => ({ type: 'tableRow', content: [cell(false, p(b(k)), 160), cell(false, v, 320)] })
    return { type: 'pageProperties', content: [{ type: 'table', content: [row('Shows', p(x.shows)), row('Looks in', p(x.looksIn)), row('Type this', p(c(x.type)))] }] }
  }
  /** Writes one of the twelve pages, with its labels. */
  const write = (title, ...content) => page(title, liveParent, doc(...content), { labels: ['live-content', ...info[title].labels], comment: CHANGE })

  // ---- The parts every page shares, word for word
  const insertTable = (rows) => table([['Type this', 'To get'], ...rows], [200, 500])
  const insertHow = () => p('Type the command at the start of a line, or after a space, and press ', b('Enter'), '. When the list shows more than one block, use the arrow keys or click the one you want. Or choose ', b('+'), ' on the toolbar: live content blocks are at the end, under ', b('Live content'), '. With text selected, the ', b('+'), ' menu puts the block in place of that text, so click on an empty line first.')
  const settingsHow = () => p('While you are editing, click the block. It gets a colored outline, and a small box opens above it with the block’s name and its settings. A change shows in the block straight away; readers see it once you publish or update the page.')
  const removeHow = () => p('To remove the block, click it and press ', b('Delete'), ' or ', b('Backspace'), '. Only the block goes: the pages, files or tasks it showed are not touched.')
  const examplesHow = (name) => p('Each example below is a real ', name, ' block on this page, with its settings in the heading above it.')
  const notLive = (why) => p(i(`Not shown here: ${why}`))
  const basicsLink = () => li(p(pageLink(BASICS), ': what every live content block has in common, such as who sees what, and what happens in an export.'))

  // ============================================================ The shared notes
  // Kept free of headings and live blocks, because other pages show it inside
  // theirs: Include page shows all of it, Excerpt include its first paragraph.
  await page(BASICS, liveParent, doc(
    excerpt(p('A live content block asks a question, such as “which pages are under this one?”, and Tesria answers it each time the page is opened. Nobody has to keep it up to date, and each reader sees only what they are allowed to see.')),
    ul(
      li(p(b('Looked up when the page opens.'), ' Add a page, tick off a task or attach a file, and every block that should show it does, the next time the page is opened. While editing, ↻ beside a block’s name looks it up again. For people reading without signing in, a result can be up to a minute old.')),
      li(p(b('Only what you may see.'), ' A page you cannot open never appears in a list, and is not counted either: not in how many pages carry a label, and not in how many edits someone has made. Two people can open the same page and see different lists.')),
      li(p(b('Published pages only.'), ' Lists of pages leave out drafts and pages in the trash.')),
      li(p(b('Readers see just the result.'), ' In the editor, a block has a dashed outline with its name above it. On the published page both are gone, and the list or table reads as part of the page.')),
      li(p(b('A snapshot in an export.'), ' A PDF, a Markdown file or a website made from a space shows what each block showed at the moment it was exported.')),
      li(p(b('One level deep.'), ' A live block inside a page that Include page or Excerpt include is showing is not looked up; it says ', i('Shown on the page'), ' instead. That is what stops two pages from including each other forever.')),
    ),
  ), { labels: ['live-content'], comment: 'The notes every live content block shares' })

  // ============================================================ Children display
  await write('Children display',
    p('Children display lists the pages under the page it is on, as links. Add a page beneath it, rename one or move one away, and the list follows by itself, so the front page of a section never needs updating by hand.'),
    p('It is the easiest way to give a section its own table of contents. Most section pages on this site start with one; the ', pageLink('Live content'), ' page, one level up, lists the pages in this section with it.'),
    glance('Children display'),

    h(2, 'Insert it'),
    insertTable([['/children', 'Children display'], ['/subpages', 'Children display']]),
    insertHow(),
    p('It starts with its usual settings: one level, in tree order.'),

    h(2, 'Each setting, and when to use it'),
    p('A Children display always starts at the page it is on. This page has nothing under it, so a block here has nothing to list, and says so. This is the real block:'),
    live('children', { depth: '1', sort: 'position' }),
    p('That is also what you see if you add one to a page before writing the pages that go under it. The settings:'),
    h(3, 'Depth 1 (the default)'),
    p('Only the pages directly under this one. Right for most section pages: a short list of what the section holds.'),
    h(3, 'Depth 2 or 3'),
    p('Their sub-pages too, nested under each one, up to three levels. Use it on the front page of a section that is several levels deep, so readers can jump straight to a page two levels down.'),
    h(3, 'Sort by Tree order (the default)'),
    p('The same order as the page tree beside the page, which you set by dragging pages in it. Use it whenever the order means something, such as steps or chapters.'),
    h(3, 'Sort by Title'),
    p('A to Z. Use it for a list people scan for a name, such as a glossary, a list of products or a page per team member.'),
    h(3, 'Sort by Recently updated'),
    p('The most recently changed page first. Use it for a folder of meeting notes or reports, where the newest is the one people want.'),

    h(2, 'Changing and removing it'),
    settingsHow(),
    ul(
      li(p(b('Depth:'), ' how many levels to show, from 1 to 3. 1 unless you change it.')),
      li(p(b('Sort by:'), ' ', b('Tree order'), ', ', b('Title'), ' or ', b('Recently updated'), '. Tree order unless you change it.')),
    ),
    p('A page someone cannot see is left out of their list, together with everything under it.'),
    removeHow(),

    h(2, 'Good practice'),
    ul(
      li(p(b('Put it near the top'), ' of a section’s front page, after a sentence that says what the section is for.')),
      li(p(b('Give pages titles that work in a list.'), ' The list shows titles only, so “Meeting notes, March 3” helps more than “Notes”.')),
      li(p(b('Stay shallow.'), ' Depth 1 or 2 reads well; at 3, a big section becomes a wall of links.')),
    ),

    h(2, 'Related'),
    ul(
      li(p(pageLink('Page tree'), ' can start at the top of the space and go six levels deep.')),
      li(p(pageLink('Recently updated'), ' lists what changed, from the whole space or from here down.')),
      li(p(pageLink('The page tree and reordering'), ': how to change the tree order.')),
      basicsLink(),
    ),
  )

  // ============================================================ Recently updated
  await write('Recently updated',
    p('Recently updated is a table of the pages that changed most recently, with who changed each one and when. Put it on a space’s home page and everyone who opens it sees what is new, without anyone keeping a “what’s new” list by hand.'),
    p('It is also a quick health check for a section: at a glance you can see whether its pages are looked after, or have not been touched in months.'),
    glance('Recently updated'),

    h(2, 'Insert it'),
    insertTable([['/recent', 'Recently updated'], ['/changes', 'Recently updated']]),
    insertHow(),
    p('It starts with its usual settings: the ten latest pages in this space.'),

    h(2, 'Each setting, live'),
    examplesHow('Recently updated'),
    h(3, 'This space, 10 pages (the default)'),
    p('The ten most recently changed pages anywhere in the space. The right choice for a space’s home page.'),
    live('recently-updated', { scope: 'space', limit: '10' }),
    h(3, 'This space, 3 pages'),
    p('A short list, for the top of a page where you want a hint of what is new rather than a report.'),
    live('recently-updated', { scope: 'space', limit: '3' }),
    h(3, 'This page and below'),
    p('This page and every page beneath it, at any depth. Use it on the front page of a project that shares a space with other work, so it reports that project and nothing else.'),
    notLive('this page has nothing beneath it, so the list would hold only this page.'),

    h(2, 'Changing and removing it'),
    settingsHow(),
    ul(
      li(p(b('Look in:'), ' ', b('This space'), ' or ', b('This page and below'), '. This space unless you change it.')),
      li(p(b('Show:'), ' how many pages, from 1 to 50. 10 unless you change it.')),
    ),
    p('The table has three columns: ', b('Page'), ', a link to it; ', b('Updated by'), ', who made the latest change; and ', b('When'), ', the date of that change, written the way each reader’s device writes dates.'),
    removeHow(),

    h(2, 'Good practice'),
    ul(
      li(p(b('One is usually enough,'), ' on the space’s home page, where people start.')),
      li(p(b('Keep it short beside other content.'), ' Three to five pages are a glance; fifty are a report.')),
      li(p(b('Use This page and below for a project'), ' inside a bigger space, so other teams’ changes do not crowd it out.')),
    ),

    h(2, 'Related'),
    ul(
      li(p(pageLink('Change history'), ' shows the changes to one page, with the note left about each.')),
      li(p(pageLink('Contributors'), ' shows who has edited a page, most edits first.')),
      basicsLink(),
    ),
  )

  // ============================================================ Content by label
  await write('Content by label',
    p('Content by label lists every page that carries the labels you name. Label your meeting notes ', c('meeting-notes'), ', and one block on the team’s home page lists all of them, including the ones written next week.'),
    p('Labels are short tags under a page’s title (see ', pageLink('Labels'), '). This block turns them into lists that keep themselves up to date, which is how a wiki gets indexes nobody has to maintain.'),
    glance('Content by label'),

    h(2, 'Insert it'),
    insertTable([['/content', 'Content by label, listed after Table of contents']]),
    insertHow(),
    p('Until you name a label in its settings, it says ', i('Name at least one label.')),

    h(2, 'Each setting, live'),
    p('The pages in this section carry labels, so these examples have something to find. Every one is labeled ', c('live-content'), ', and each has one or more labels that say what kind of block it is: ', c('page-lists'), ', ', c('this-page'), ', ', c('includes'), ', ', c('reports'), ', ', c('uses-labels'), ' and ', c('needs-setup'), ' (for the blocks that show nothing until you choose a setting).'),
    examplesHow('Content by label'),
    h(3, 'One label: uses-labels'),
    p('The simplest use: every page with one label. Here, the blocks that work with labels. Beside each page, in gray, is the label it matched.'),
    live('content-by-label', { labels: 'uses-labels', match: 'any', scope: 'space', limit: '25' }),
    h(3, 'Any label: reports, includes'),
    p('Pages with at least one of the labels. Use it to gather related kinds of page into one list, such as ', c('meeting-notes'), ' and ', c('decisions'), '.'),
    live('content-by-label', { labels: 'reports, includes', match: 'any', scope: 'space', limit: '25' }),
    h(3, 'All labels: uses-labels, needs-setup'),
    p('Only pages that carry every label you name, which narrows a list down. Here, the blocks that work with labels and also need a setting before they show anything.'),
    live('content-by-label', { labels: 'uses-labels, needs-setup', match: 'all', scope: 'space', limit: '25' }),
    h(3, 'Show 3'),
    p('At most three pages. Handy for a short list beside other content, but remember the list is in title order, A to Z, not newest first.'),
    live('content-by-label', { labels: 'live-content', match: 'any', scope: 'space', limit: '3' }),
    h(3, 'Look in Everywhere'),
    p('Matching pages from every space you can see, not just this one. Use it for an index that crosses teams, such as every page labeled ', c('incident'), '.'),
    notLive('these labels are used only in this space, so it would show the same lists as above.'),

    h(2, 'Changing and removing it'),
    settingsHow(),
    ul(
      li(p(b('Labels:'), ' one or more label names, separated by commas, such as ', c('meeting-notes, decisions'), '. Capital letters make no difference.')),
      li(p(b('Match:'), ' ', b('Any label'), ' or ', b('All labels'), '. Any label unless you change it.')),
      li(p(b('Look in:'), ' ', b('This space'), ' or ', b('Everywhere'), '. This space unless you change it.')),
      li(p(b('Show:'), ' how many pages, from 1 to 100. 25 unless you change it.')),
    ),
    p('When nothing matches, the block says so and names the labels, such as ', i('No pages are labeled release or api.')),
    removeHow(),

    h(2, 'Good practice'),
    ul(
      li(p(b('Agree on label names as a team.'), ' One spelling, used by everyone: ', c('meeting-notes'), ' on some pages and ', c('meetings'), ' on others makes two half lists.')),
      li(p(b('Label as you write.'), ' A page labeled later is a page missing from every list until then.')),
      li(p(b('Want facts, not just titles?'), ' If each page has a status or an owner you want side by side, use ', pageLink('Page properties report'), ' instead.')),
    ),

    h(2, 'Related'),
    ul(
      li(p(pageLink('Labels list'), ' shows the labels themselves, as links.')),
      li(p(pageLink('Page properties report'), ' collects facts from labeled pages into a table.')),
      li(p(pageLink('Label pages'), ': clicking a label lists its pages, without a block.')),
      basicsLink(),
    ),
  )

  // ================================================== Attachments (live content)
  const attachmentsId = id['Attachments (live content)']
  await attachCurrent(attachmentsId, 'live-content-blocks.csv', cheatSheet(), 'text/csv')
  await write('Attachments (live content)',
    p('The Attachments block is a table of the files attached to the page it is on. Each file name is a link that downloads it, with its size, who uploaded it and when. Upload a form or a handout to the page and the table lists it, the next time the page is opened.'),
    p('Every page already lists its files under the ', b('Attachments'), ' tab below it. This block brings that list up into the page, where readers see it without going looking: a Downloads section on a page of forms, templates or slides.'),
    glance('Attachments (live content)'),

    h(2, 'Insert it'),
    insertTable([['/attachments', 'Attachments'], ['/files', 'Attachments']]),
    insertHow(),
    p('In the editor’s lists it is called just ', b('Attachments'), '.'),

    h(2, 'Live on this page'),
    p('This page has one file attached: a list of every live content block, with the word to type for each, to open in any spreadsheet. Here is the real block, listing it:'),
    live('attachments'),
    p('On a page with no files, it says ', i('This page has no attachments.')),

    h(2, 'Changing and removing it'),
    p('It has no settings: click it while editing and its box says ', i('No options.'), ' It always lists the files of the page it is on, by name, A to Z. To add a file, upload it to the page as usual (see ', pageLink('Attachments'), ') and it appears in the table.'),
    removeHow(),

    h(2, 'Good practice'),
    ul(
      li(p(b('Put it under a heading'), ' such as “Downloads”, so readers know what the links are for.')),
      li(p(b('Name files for readers.'), ' The table shows file names as they are: ', c('expense-form.pdf'), ' says more than ', c('final_v3.pdf'), '.')),
      li(p(b('Everything attached is listed,'), ' including pictures pasted into the page. On a page with many pictures, the table gets long.')),
    ),

    h(2, 'Related'),
    ul(
      li(p(pageLink('File or video'), ' shows one file inside the page, such as a video or a PDF, rather than a link to it.')),
      li(p(pageLink('Attachments'), ': uploading files to a page, and the Attachments tab.')),
      basicsLink(),
    ),
  )

  // ============================================================ Change history
  await write('Change history',
    p('Change history shows the latest versions of the page it is on: the version number, who saved it, when, and the note they left about what changed. Tesria keeps every version of every page; this block puts the most recent ones where readers see them.'),
    p('It answers the question every reader of a policy or a procedure has: is this still current? A block at the bottom that says the page was changed last week, and why, answers it before anyone has to ask.'),
    glance('Change history'),

    h(2, 'Insert it'),
    insertTable([['/history', 'Change history'], ['/versions', 'Change history']]),
    insertHow(),

    h(2, 'Each setting, live'),
    examplesHow('Change history'),
    h(3, 'Show 10 (the default)'),
    p('The ten most recent versions, newest first. Good at the foot of a document people rely on, such as a policy or a price list.'),
    live('change-history', { limit: '10' }),
    h(3, 'Show 1'),
    p('Only the latest change: who last touched the page, when and why. A compact “last updated” line for the top or the bottom of a page.'),
    live('change-history', { limit: '1' }),

    h(2, 'Changing and removing it'),
    settingsHow(),
    ul(li(p(b('Show:'), ' how many versions, from 1 to 50. 10 unless you change it.'))),
    p('The table’s columns are ', b('Version'), ', ', b('By'), ', ', b('When'), ' and ', b('What changed'), '. What changed is the note typed into ', b('What changed? (optional)'), ' when someone updated the page, so an empty cell means nobody wrote one.'),
    removeHow(),

    h(2, 'Good practice'),
    ul(
      li(p(b('Fill in What changed?'), ' when you update a page. It is what this block shows, and “fixed the phone number” helps a reader far more than an empty cell.')),
      li(p(b('Show a few, not all.'), ' One to three versions answer “is this current?”; the full list belongs on the History tab.')),
    ),

    h(2, 'Related'),
    ul(
      li(p(pageLink('History and restoring'), ': every version, comparing two, and putting an old one back.')),
      li(p(pageLink('Contributors'), ' sums up who has edited the page.')),
      li(p(pageLink('Recently updated'), ' shows the latest changes across a space.')),
      basicsLink(),
    ),
  )

  // ============================================================ Contributors
  await write('Contributors',
    p('Contributors lists the people who have edited the page, the one with the most edits first, with how many each has made. It tells readers who to ask about a page, and gives credit to the people who keep it going.'),
    p('Every saved version counts as one edit by whoever saved it.'),
    glance('Contributors'),

    h(2, 'Insert it'),
    insertTable([['/contributors', 'Contributors'], ['/authors', 'Contributors']]),
    insertHow(),

    h(2, 'Each setting, live'),
    h(3, 'This page (the default)'),
    p('The people who have edited this page. This is the real block:'),
    live('contributors', { scope: 'page' }),
    h(3, 'This page and below'),
    p('Edits on this page and on every page beneath it, added up per person. Put it on the front page of a project to see who has done the writing across the whole of it.'),
    notLive('this page has nothing beneath it, so it would show the same as above.'),

    h(2, 'Changing and removing it'),
    settingsHow(),
    ul(li(p(b('Count edits on:'), ' ', b('This page'), ' or ', b('This page and below'), '. This page unless you change it.'))),
    p('Names are plain text, not links. Only pages the reader can see are counted, so someone whose edits are all on pages hidden from that reader is not listed.'),
    removeHow(),

    h(2, 'Good practice'),
    ul(
      li(p(b('Put it at the foot of the page,'), ' next to ', pageLink('Change history'), ': together they say who looks after the page and what they last did.')),
      li(p(b('It counts saves, not effort.'), ' Ten small fixes count more than one long rewrite, so do not read it as a ranking.')),
    ),

    h(2, 'Related'),
    ul(
      li(p(pageLink('Change history'), ' shows each recent version and the note left with it.')),
      li(p(pageLink('Recently updated'), ' shows who changed which page most recently.')),
      basicsLink(),
    ),
  )

  // ============================================================ Include page
  await write('Include page',
    p('Include page shows the whole of another page inside this one. Write something once, such as a disclaimer, the office address or a “how to get help” box, include it wherever it belongs, and when it changes, it changes everywhere.'),
    p('Readers always see the other page’s current published version, so there is never an old copy to track down.'),
    glance('Include page'),

    h(2, 'Insert it'),
    insertTable([['/include', 'Include page, listed before Excerpt include']]),
    insertHow(),
    p('Until you choose a page in its settings, it says ', i('Choose a page to include.')),

    h(2, 'Live on this page'),
    p('The page ', pageLink(BASICS), ' holds the notes that apply to every live content block. Rather than repeat them on each page, here they are, included. Everything between this paragraph and the next one comes from that page, as it is today.'),
    live('include-page', { page: basicsId }),
    p('The line down the left of its first paragraph marks that paragraph as the page’s excerpt. ', pageLink('Excerpt include'), ' shows only that part.'),

    h(2, 'Changing and removing it'),
    settingsHow(),
    ul(li(p(b('Page:'), ' the page to show. Type two or more letters of its title in ', b('Search for a page'), ', then choose it from the list; each result shows its space’s key. The chosen page’s title appears above the search box. You can also paste a page’s id there.'))),
    p('The page can be in any space. A few things the block says instead of showing it:'),
    ul(
      li(p(i('Nothing to show: the page is missing, or you cannot see it.'), ' The page was deleted, or this reader is not allowed to see it. It never says which, so a hidden page stays hidden.')),
      li(p(i('A page cannot include itself.'), ' Choose another page.')),
      li(p(i('Shown on the page'), ', in place of a live block inside the included page. Blocks are looked up one level deep, so two pages cannot include each other forever.')),
    ),
    removeHow(),

    h(2, 'Good practice'),
    ul(
      li(p(b('Keep the shared page short and self-contained:'), ' a paragraph, a list or a panel that makes sense wherever it lands.')),
      li(p(b('Want only part of a page?'), ' Mark that part as an ', pageLink('Excerpt'), ' and use ', pageLink('Excerpt include'), '.')),
      li(p(b('Check who can see the source.'), ' Readers who cannot open it see the message above, not its content.')),
    ),

    h(2, 'Related'),
    ul(
      li(p(pageLink('Excerpt include'), ' shows one marked part of a page.')),
      li(p(pageLink('Excerpt'), ' marks that part.')),
      basicsLink(),
    ),
  )

  // ============================================================ Excerpt include
  await write('Excerpt include',
    p('Excerpt include shows one marked part of another page, rather than the whole of it. The part is marked on that page with the ', pageLink('Excerpt'), ' element, and is usually its summary: the paragraph that says what the page is about.'),
    p('For example, give every project page an excerpt that says what the project is and where it stands. A status page can then show each project’s excerpt, one after another, and when a project’s summary changes, the status page follows.'),
    glance('Excerpt include'),

    h(2, 'Insert it'),
    insertTable([['/excerpt', 'Excerpt include, listed after Excerpt']]),
    insertHow(),
    panel('note', p(b('Excerpt and Excerpt include are two different things.'), ' ', b('Excerpt'), ', listed first, is the marker you put on the page you borrow from. ', b('Excerpt include'), ' is this block, on the page that shows it.')),
    p('Until you choose a page in its settings, it says ', i('Choose the page whose excerpt to include.')),
    h(3, 'Step 1: Mark the excerpt'),
    p('Open the page to borrow from and edit it. Select the part to share, and choose ', b('Excerpt'), ' from the ', b('+'), ' menu. See ', pageLink('Excerpt'), '.'),
    h(3, 'Step 2: Include it'),
    p('On the page that should show it, insert Excerpt include, click it, and choose the page from step 1 in its settings.'),

    h(2, 'Live on this page'),
    p(pageLink(BASICS), ' marks its first paragraph as an excerpt. This block shows that paragraph and nothing else; compare it with the example on ', pageLink('Include page'), ', which shows the whole page.'),
    live('excerpt-include', { page: basicsId }),

    h(2, 'Changing and removing it'),
    settingsHow(),
    ul(li(p(b('Page:'), ' the page whose excerpt to show. Type two or more letters of its title in ', b('Search for a page'), ', then choose it; each result shows its space’s key. You can also paste a page’s id.'))),
    p('Only a page’s first excerpt is shown. If the page has none, the block says ', i('That page has no excerpt to include.'), ' If the page was deleted or the reader cannot see it, it says ', i('Nothing to show: the page is missing, or you cannot see it.')),
    removeHow(),

    h(2, 'Good practice'),
    ul(
      li(p(b('Write the excerpt to stand on its own.'), ' It is read without the page’s title and headings around it, so “This project…” works better than “As above…”.')),
      li(p(b('One excerpt per page.'), ' Only the first is ever shown.')),
      li(p(b('Keep it short:'), ' a sentence or two. For the whole page, use ', pageLink('Include page'), '.')),
    ),

    h(2, 'Related'),
    ul(
      li(p(pageLink('Excerpt'), ' marks the part to share.')),
      li(p(pageLink('Include page'), ' shows the whole of a page.')),
      li(p(pageLink('Page properties report'), ' collects facts from many pages, rather than text.')),
      basicsLink(),
    ),
  )

  // ============================================================ Page properties report
  await write('Page properties report',
    p('Page properties report collects the ', pageLink('Page properties'), ' tables of many pages into one table: a row for each page, a column for each property. Give every project page a small table of facts, such as status, owner and deadline, label the pages ', c('project'), ', and one report lists every project with its facts side by side.'),
    p('The facts stay on each page, where the people who own it keep them up to date, so the report is never out of step with them.'),
    glance('Page properties report'),

    h(2, 'Insert it'),
    insertTable([['/report', 'Page properties report, listed before Task report'], ['/properties', 'Page properties report, listed after Page properties']]),
    insertHow(),
    p('Until you name a label in its settings, it says ', i('Name at least one label.')),

    h(2, 'Each setting, live'),
    p('Every page in this section is ready for a report: the small table under each page’s first paragraphs is its Page properties table (Shows, Looks in and Type this), and each page carries the label ', c('live-content'), '.'),
    examplesHow('Page properties report'),
    h(3, 'Label live-content'),
    p('Every live content block, with its facts side by side: a one-table summary of this whole section that nobody had to write.'),
    live('page-properties-report', { labels: 'live-content', limit: '25' }),
    h(3, 'Label needs-setup'),
    p('A narrower label gives a shorter report: the four blocks that show nothing until you choose a setting.'),
    live('page-properties-report', { labels: 'needs-setup', limit: '25' }),

    h(2, 'Changing and removing it'),
    settingsHow(),
    ul(
      li(p(b('Labels:'), ' one or more label names, separated by commas. A page with any of them is included. Capital letters make no difference.')),
      li(p(b('Show:'), ' how many pages, from 1 to 100. 25 unless you change it.')),
    ),
    p('Rows are in title order, and the first column links to each page. The other columns come from the pages themselves, in the order they are first found, so adding a row to one page’s table adds a column to the report. A labeled page with no Page properties table is left out.'),
    panel('note', p(b('It looks in every space.'), ' Unlike ', pageLink('Content by label'), ', this report has no Look in setting: it collects labeled pages from every space you can see. Choose a label only your pages use, or one that means the same thing everywhere.')),
    removeHow(),

    h(2, 'Good practice'),
    ul(
      li(p(b('Spell property names the same on every page,'), ' capitals included, so each fact lands in the right column.')),
      li(p(b('Start pages from a template'), ' that already has the table, so every page has the same properties. See ', pageLink('Templates'), '.')),
      li(p(b('Keep values short.'), ' The report is a table: a status or a date fits; a paragraph does not.')),
      li(p(b('One table per page.'), ' Only a page’s first Page properties table is read.')),
    ),

    h(2, 'Related'),
    ul(
      li(p(pageLink('Page properties'), ' is the table each page carries.')),
      li(p(pageLink('Content by label'), ' lists labeled pages by title, from this space or everywhere.')),
      li(p(pageLink('Task report'), ' collects tasks rather than facts.')),
      basicsLink(),
    ),
  )

  // ============================================================ Labels list
  await write('Labels list',
    p('Labels list shows labels as links, each opening a list of every page with that label. It can show the labels on this page, the labels used most in the space, or the labels that tend to go with this page’s ones.'),
    p('Use it to help readers explore. On a space’s home page, the popular labels are a ready-made index of what the space covers; at the foot of a page, related labels lead to pages on neighboring subjects.'),
    glance('Labels list'),

    h(2, 'Insert it'),
    insertTable([['/labels', 'Labels list'], ['/popular', 'Labels list']]),
    insertHow(),

    h(2, 'Each setting, live'),
    p('This page carries two labels, ', c('live-content'), ' and ', c('uses-labels'), ', and the other pages in this section carry more, so each list below has something to show.'),
    examplesHow('Labels list'),
    h(3, 'This page’s labels (the default)'),
    p('The labels on the page the block is on, A to Z. Useful where readers might miss the labels under the title, such as at the end of a long page.'),
    live('labels', { mode: 'page', limit: '20' }),
    h(3, 'Popular in this space'),
    p('The labels on the most pages in the space, with how many pages carry each. On a home page, it shows at a glance what the space is about.'),
    live('labels', { mode: 'popular', limit: '20' }),
    h(3, 'Related labels'),
    p('Labels found on the same pages as this page’s labels, not counting its own, the most shared first. Here it finds the other labels used in this section, because they share pages with ', c('live-content'), '.'),
    live('labels', { mode: 'related', limit: '20' }),
    h(3, 'Popular, show 3'),
    p('Just the top three, for a small “popular topics” line.'),
    live('labels', { mode: 'popular', limit: '3' }),

    h(2, 'Changing and removing it'),
    settingsHow(),
    ul(
      li(p(b('List:'), ' ', b('This page’s labels'), ', ', b('Popular in this space'), ' or ', b('Related labels'), '. This page’s labels unless you change it.')),
      li(p(b('Show:'), ' how many labels, from 1 to 100. 20 unless you change it.')),
    ),
    p('Only pages the reader can see are counted, and a label used only on pages hidden from them is not listed. With nothing to show, the block says why: ', i('This page has no labels.'), ', ', i('No labels are in use here yet.'), ' or ', i('No related labels yet.')),
    removeHow(),

    h(2, 'Good practice'),
    ul(
      li(p(b('Popular on a home page, Related at the foot of a page.'), ' One maps the space; the other suggests where to go next.')),
      li(p(b('Keep labels tidy.'), ' Near twins such as ', c('meeting'), ' and ', c('meetings'), ' split every list in two. See ', pageLink('Labels'), '.')),
    ),

    h(2, 'Related'),
    ul(
      li(p(pageLink('Content by label'), ' lists the pages with a label, on the page itself.')),
      li(p(pageLink('Label pages'), ': what a label opens when clicked.')),
      basicsLink(),
    ),
  )

  // ============================================================ Task report
  await write('Task report',
    p('Task report collects task list items from many pages into one table: each task, whether it is done, who it is assigned to, and the page it is on. Tasks written in meeting notes stop getting lost there: one report on the team’s home page lists every open one.'),
    p('A task belongs to the first person mentioned in it: type ', c('@'), ' and their name inside the task. See ', pageLink('Task list'), '.'),
    glance('Task report'),

    h(2, 'Insert it'),
    insertTable([['/action', 'Task report'], ['/report', 'Task report, listed after Page properties report'], ['/task', 'Task report, listed after Task list']]),
    insertHow(),
    p('It starts with its usual settings: tasks not yet done, on this page and the pages beneath it.'),

    h(2, 'Each setting, live'),
    h(3, 'This space, not done, show 5'),
    p('The first five open tasks anywhere in the space. On this site they come from the checklists on pages such as ', pageLink('Security hardening'), '. Nobody is assigned to them, so the Assignee column is empty. This is the real block:'),
    live('task-report', { scope: 'space', status: 'open', assignee: 'any', limit: '5' }),
    h(3, 'This page and below (the default)'),
    p('Tasks on this page and every page beneath it. It is the default because it fits the most common case: a project page with its meeting notes under it.'),
    notLive('this page has no tasks and nothing beneath it.'),
    h(3, 'Everywhere'),
    p('Tasks from every space you can see. Together with ', b('Assigned to: Me'), ', it makes a personal to-do list that gathers your tasks from all over the wiki.'),
    h(3, 'Assigned to: Me'),
    p('Only tasks assigned to whoever is reading, so the same block shows each person their own list. People reading without signing in see ', i('Sign in to see tasks assigned to you.')),
    notLive('no task on this site is assigned to anyone, so it would be empty.'),
    h(3, 'State: Done or All'),
    p(b('Done'), ' lists finished tasks, for a record of what a project got through; ', b('All'), ' lists both. ', b('Not done'), ' is the default, because a report is usually for what is left.'),
    notLive('no task on this site is ticked, so Done would be empty and All would match the first example.'),

    h(2, 'Changing and removing it'),
    settingsHow(),
    ul(
      li(p(b('Look in:'), ' ', b('This page and below'), ', ', b('This space'), ' or ', b('Everywhere'), '. This page and below unless you change it.')),
      li(p(b('State:'), ' ', b('Not done'), ', ', b('Done'), ' or ', b('All'), '. Not done unless you change it.')),
      li(p(b('Assigned to:'), ' ', b('Anyone'), ' or ', b('Me'), '. Anyone unless you change it.')),
      li(p(b('Show:'), ' how many tasks, from 1 to 100. 25 unless you change it.')),
    ),
    p('The table’s columns are ', b('✓'), ', ', b('Task'), ', ', b('Assignee'), ' and ', b('Page'), '. The tick shows whether a task is done; you cannot tick it in the report, only on its page. Each page’s tasks are listed in the order they appear on it. Empty tasks are skipped, and when nothing matches, the block says ', i('No matching action items.')),
    removeHow(),

    h(2, 'Good practice'),
    ul(
      li(p(b('Mention someone in every task'), ' that is theirs, so it shows up in their list.')),
      li(p(b('Tick tasks off where they are written.'), ' The report follows the page.')),
      li(p(b('Keep the default for a project.'), ' This page and below keeps the report to the project’s own pages.')),
    ),
    panel('success', p(b('A “My tasks” page for everyone.'), ' Make one page with a Task report set to ', b('Everywhere'), ', ', b('Not done'), ' and ', b('Me'), '. Everyone who opens it sees their own open tasks, from every space.')),

    h(2, 'Related'),
    ul(
      li(p(pageLink('Task list'), ' is where tasks are written.')),
      li(p(pageLink('Mention'), ' is how a task gets its assignee.')),
      li(p(pageLink('Page properties report'), ' collects facts about pages, rather than tasks.')),
      basicsLink(),
    ),
  )

  // ============================================================ Page tree
  await write('Page tree',
    p('Page tree shows pages as nested links, like the page tree beside every page, starting either at the page it is on or at the top of the space. Use it for a site map on a space’s home page, or a map of one section on that section’s front page.'),
    p('It differs from ', pageLink('Children display'), ' in two ways: it can start at the top of the space, and it can go six levels deep instead of three.'),
    glance('Page tree'),

    h(2, 'Insert it'),
    insertTable([['/sitemap', 'Page tree'], ['/index', 'Page tree'], ['/tree', 'Page tree, listed after Children display']]),
    insertHow(),
    p('It starts with its usual settings: three levels, from this page.'),

    h(2, 'Each setting, live'),
    examplesHow('Page tree'),
    h(3, 'Space root, 1 level'),
    p('The top-level pages of the space: here, the sections of this site. A clean table of contents for a home page.'),
    live('page-tree', { root: 'space', depth: '1' }),
    h(3, 'Space root, 2 levels'),
    p('The sections and the pages directly in them: a site map. It is long, so here it sits inside an expand; open it to see this whole site two levels deep.'),
    expand('The site map, two levels deep', live('page-tree', { root: 'space', depth: '2' })),
    h(3, 'This page, 3 levels (the default)'),
    p('Starts beneath the page it is on, three levels down. Use it on the front page of a big section, where Children display’s three levels are not enough, or pick up to six.'),
    notLive('this page has nothing beneath it, so it would say This page has no sub-pages.'),

    h(2, 'Changing and removing it'),
    settingsHow(),
    ul(
      li(p(b('Start at:'), ' ', b('This page'), ' or ', b('Space root'), '. This page unless you change it.')),
      li(p(b('Depth:'), ' how many levels to show, from 1 to 6. 3 unless you change it.')),
    ),
    p('Pages are in tree order, the same order as the page tree beside the page. A page someone cannot see is left out of their tree, together with everything under it.'),
    removeHow(),

    h(2, 'Good practice'),
    ul(
      li(p(b('One or two levels for most uses.'), ' Every extra level multiplies the length.')),
      li(p(b('Put a long tree in an expand,'), ' as above, so it does not push the rest of the page out of sight.')),
      li(p(b('On a big space, start at a section'), ' rather than the root.')),
    ),

    h(2, 'Related'),
    ul(
      li(p(pageLink('Children display'), ' lists the pages under this one, with a choice of sort order.')),
      li(p(pageLink('Table of contents'), ' lists the headings on this page, rather than pages.')),
      li(p(pageLink('The page tree and reordering'), ': how to change the tree order.')),
      basicsLink(),
    ),
  )
}

// The first version's pictures of these blocks on Tesria Demo pages
// (manual-editor.mjs, figure()), taken down so they do not linger in the
// pages' attachments, in the Attachments block's table, or in the pack.
const slug = (t) => t.toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '')
const RETIRED = Object.fromEntries(BLOCKS.map((x) => [x.title, [`el-${slug(x.title)}.png`, `el-${slug(x.title)}.phone.png`]]))

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

  // How live content works goes first in the section: it is what the other
  // pages build on. A new page lands at the end.
  const editor = find(tree, 'The editor')
  const liveParent = editor && (editor.children ?? []).find((n) => n.title === 'Live content')
  if (liveParent) {
    const kids = liveParent.children ?? []
    const at = kids.findIndex((n) => n.title === BASICS)
    if (at > 0) {
      await author.call('PUT', `/api/pages/${kids[at].id}/move`, { parentPageId: liveParent.id, index: 0 })
      console.log(`  moved ${BASICS} to the top of Live content`)
    }
  }

  for (const [title, files] of Object.entries(RETIRED)) {
    const node = liveParent && (liveParent.children ?? []).find((n) => n.title === title)
    if (!node) continue
    for (const a of await author.call('GET', `/api/pages/${node.id}/attachments`)) {
      if (files.includes(a.filename)) {
        await author.call('DELETE', `/api/attachments/${a.id}`)
        console.log(`  - ${title}: ${a.filename}`)
      }
    }
  }
}
