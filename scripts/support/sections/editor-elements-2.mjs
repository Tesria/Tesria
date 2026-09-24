// User manual → The editor → Elements, second half (dev-plan 15.6): the
// element pages rewritten to the owner's rules (WRITING.md), on the model of
// the approved Panels page (pilot.mjs).
//
//   Math, Chart, Image, Gallery, File or video, Animation, Embed, Smart link,
//   Status, Date, Mention, Emoji, Table of contents, Excerpt, Page properties
//
// Every variant is the real element, live on the page, with when to use it.
// Facts checked against src/web/src/editor and src/Api on 2026-09-24: the
// slash catalog and its matching (slash/items.ts, first match wins on Enter),
// chartExtension.ts and ChartView.tsx (a chart's `source` is the table's
// number on the page, counting every table from the top), tocOptions.ts,
// imageExtension.ts and ImageHoverMenu.tsx, AttachmentView.tsx (what each
// kind of file draws as), the embed allowlist and providers
// (SiteSettings.cs, EmbedProviders.cs), the mention notifications
// (PageWriter.cs) and the page properties report (BlockDocuments.cs).
//
// These titles are also in manual-editor.mjs's ELEMENTS list, which writes
// them in the first version's style. Running manual-editor after this module
// puts the old pages back: the owner should take these fifteen out of
// ELEMENTS once these pages are approved.
//
// Animations only where doing it is the point: inserting a status, an
// @mention and an emoji. Each opens a new page in Tesria Demo, recorded at
// 480 × 400 and 1x like the Panels one; the harness discards the draft.

import { readFileSync } from 'node:fs'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'

const ROOT = join(dirname(fileURLToPath(import.meta.url)), '..', '..', '..')

const TITLES = [
  'Math', 'Chart', 'Image', 'Gallery', 'File or video', 'Animation', 'Embed', 'Smart link',
  'Status', 'Date', 'Mention', 'Emoji', 'Table of contents', 'Excerpt', 'Page properties',
]

// ------------------------------------------------------------- recordings
const CLIP = { width: 480, height: 400 }
const recording = (name, steps) => ({
  name, url: '/spaces/DEMO/new', phone: false,
  viewport: CLIP, record: { size: CLIP },
  waitFor: '.ProseMirror', lead: 900, tail: 1400,
  css: '.tip, .onboarding-tip { display: none !important; }',
  steps: [{ click: '.ProseMirror' }, { wait: 400 }, ...steps],
})

export const shots = () => [
  // A status: /status makes a gray one with its menu open and the label
  // field focused (StatusMenu.tsx). The field starts as STATUS, so it is
  // selected before typing over it.
  recording('status-insert', [
    { typeSlowly: 'Design review ', delay: 45 },
    { typeSlowly: '/status', delay: 110 }, { wait: 900 },
    { press: 'Enter', selector: '.ProseMirror' }, { wait: 700 },
    { tripleClick: '.chip-menu input' }, { wait: 300 },
    { typeSlowly: 'In review', delay: 90 }, { wait: 700 },
    { moveTo: '.chip-menu__colors button[title="Blue"]' }, { wait: 500 },
    { click: '.chip-menu__colors button[title="Blue"]' }, { wait: 1100 },
    { moveTo: '.chip-menu__colors button[title="Green"]' }, { wait: 500 },
    { click: '.chip-menu__colors button[title="Green"]' }, { wait: 600 },
  ]),
  // An @mention. "okaf" matches only Sam Okafor, one of the fictional
  // accounts, so no real account appears in the list.
  recording('mention-insert', [
    { typeSlowly: 'Thanks to ', delay: 45 },
    { typeSlowly: '@okaf', delay: 120 }, { wait: 1200 },
    { press: 'Enter', selector: '.ProseMirror' }, { wait: 500 },
    { typeSlowly: 'for the review.', delay: 45 }, { wait: 600 },
  ]),
  // Emoji by name, then by a keyword: ":ship" lists 🎉 first (emoji.ts).
  recording('emoji-insert', [
    { typeSlowly: 'Launch day ', delay: 45 },
    { typeSlowly: ':rock', delay: 130 }, { wait: 1100 },
    { press: 'Enter', selector: '.ProseMirror' }, { wait: 500 },
    { typeSlowly: ' went well ', delay: 45 },
    { typeSlowly: ':ship', delay: 130 }, { wait: 1100 },
    { press: 'Enter', selector: '.ProseMirror' }, { wait: 600 },
  ]),
]

// ------------------------------------------------------------ sample media
// Drawn here rather than committed, like the Demo space's (seed-demo.mjs).

const THEMES = {
  dawn: { sky: [[255, 214, 170], [255, 158, 135]], sun: [255, 244, 214], far: [196, 128, 150], near: [120, 86, 120], sunY: 0.62 },
  day: { sky: [[120, 190, 255], [205, 232, 255]], sun: [255, 250, 220], far: [118, 170, 140], near: [70, 128, 96], sunY: 0.22 },
  dusk: { sky: [[64, 56, 120], [240, 128, 96]], sun: [255, 200, 150], far: [92, 72, 118], near: [52, 42, 80], sunY: 0.58 },
  night: { sky: [[14, 20, 48], [40, 52, 96]], sun: [236, 236, 220], far: [34, 44, 74], near: [20, 26, 48], sunY: 0.2 },
}

/** lib.landscape at any size, so there is a small picture and a tall one too. */
function scene(lib, theme, W, H) {
  const t = THEMES[theme]
  const kx = W / 960, ky = H / 600
  const sunX = W * 0.7, sunY = H * t.sunY, sunR = 58 * Math.min(kx, ky)
  const ridge = (x, a, b, c, base) => base + Math.sin(x / (a * kx)) * b * ky + Math.sin(x / (c * kx) + 1.3) * b * ky * 0.6
  return lib.png(W, H, (x, y) => {
    let col = lib.mix(t.sky[0], t.sky[1], y / H)
    const d = Math.hypot(x - sunX, y - sunY)
    if (d < sunR) col = t.sun
    else if (d < sunR * 2.2) col = lib.mix(t.sun, col, (d - sunR) / (sunR * 1.2))
    if (theme === 'night' && ((x * 7919 + y * 104729) % 9973) < 3 && y < H * 0.55) col = [255, 255, 240]
    if (y > ridge(x, 90, 26, 37, H * 0.62)) col = t.far
    if (y > ridge(x, 140, 34, 53, H * 0.76)) col = t.near
    return col
  })
}

/** A light picture, like a screenshot of a form: white edges that vanish into the page without a border. */
function formPicture(lib) {
  const W = 640, H = 400
  const inside = (x, y, x0, y0, x1, y1) => x >= x0 && x < x1 && y >= y0 && y < y1
  return lib.png(W, H, (x, y) => {
    if (y < 40) {
      for (const [cx, dot] of [[24, [255, 95, 86]], [44, [255, 189, 46]], [64, [39, 201, 63]]]) if (Math.hypot(x - cx, y - 20) < 6) return dot
      return [242, 244, 247]
    }
    if (inside(x, y, 40, 72, 300, 92)) return [52, 69, 99]
    if (inside(x, y, 40, 108, 520, 118)) return [200, 206, 214]
    for (let k = 0; k < 3; k++) {
      const y0 = 150 + k * 62
      if (inside(x, y, 40, y0, 200, y0 + 10)) return [120, 130, 145]
      if (inside(x, y, 40, y0 + 18, 600, y0 + 46)) {
        const edge = x < 41 || x >= 599 || y < y0 + 19 || y >= y0 + 45
        return edge ? [200, 206, 214] : [255, 255, 255]
      }
    }
    if (inside(x, y, 40, 340, 180, 372)) return [12, 102, 228]
    return [255, 255, 255]
  })
}

/** A short two-note chime as a WAV file, for the sound player. */
function chime() {
  const rate = 16000, n = Math.round(rate * 1.4)
  const data = Buffer.alloc(n * 2)
  const notes = [[0, 659.25], [0.18, 880]]
  for (let i = 0; i < n; i++) {
    const t = i / rate
    let v = 0
    for (const [start, f] of notes) if (t >= start) v += Math.sin(2 * Math.PI * f * (t - start)) * Math.exp(-(t - start) * 4) * 0.35
    data.writeInt16LE(Math.round(Math.max(-1, Math.min(1, v)) * 32767), i * 2)
  }
  const head = Buffer.alloc(44)
  head.write('RIFF', 0); head.writeUInt32LE(36 + data.length, 4); head.write('WAVE', 8)
  head.write('fmt ', 12); head.writeUInt32LE(16, 16); head.writeUInt16LE(1, 20); head.writeUInt16LE(1, 22)
  head.writeUInt32LE(rate, 24); head.writeUInt32LE(rate * 2, 28); head.writeUInt16LE(2, 32); head.writeUInt16LE(16, 34)
  head.write('data', 36); head.writeUInt32LE(data.length, 40)
  return Buffer.concat([head, data])
}

const SIGNUPS_CSV = 'Month,Free,Team\nJuly,120,18\nAugust,180,26\nSeptember,260,41\n'

/**
 * Points each chart at its table. A chart's `source` is the table's number
 * on the page, counting every table from the top, the Insert it table
 * included (ChartView.readTable), so the numbers are worked out from the
 * finished page rather than written by hand: a table added above would
 * otherwise send every chart after it to the wrong one.
 */
function numberCharts(docNode) {
  const tables = []
  const collect = (n) => { if (n.type === 'table') tables.push(n); for (const c of n.content ?? []) collect(c) }
  collect(docNode)
  const point = (n) => {
    if (n.type === 'chart' && typeof n.attrs.source === 'object') n.attrs.source = tables.indexOf(n.attrs.source) + 1
    for (const c of n.content ?? []) point(c)
  }
  point(docNode)
  return docNode
}

export async function build(helpers) {
  const {
    top, page, ensure, attachCurrent, person, animation, pageLink,
    doc, p, h, text, bold, italic, code, link, ul, li, panel, table, status, date, math, excerpt,
    smartLink, embed, image, gallery, fileBlock, expand, live,
  } = helpers
  const lib = helpers
  const b = (t) => text(t, bold)
  const c = (t) => text(t, code)
  const i = (t) => text(t, italic)
  const insertNote = (what, extra = '') => p('Type the command at the start of a line, or after a space, and press ', b('Enter'), '. Or choose ', b('+'), ' on the toolbar, then ', b(what), '.', extra ? ` ${extra}` : '')

  const editor = await ensure('The editor', top['User manual'])
  const elements = await ensure('Elements', editor)
  const ids = {}
  for (const title of TITLES) ids[title] = await ensure(title, elements)

  // ================================================================== Math
  await page('Math', elements, doc(
    p('Math shows an equation the way a textbook prints it: fractions stacked, powers raised, symbols in their places. You type it in ', b('LaTeX'), ', the plain-text notation scientists and engineers use for writing math, and Tesria draws it. Use it wherever a formula matters: a pricing model, engineering notes, a statistics report, a physics lesson.'),
    p('If LaTeX is new to you, don’t worry. The handful of pieces further down this page cover most everyday formulas.'),

    h(2, 'Insert it'),
    table([
      ['Type this', 'To get'],
      ['/math', 'An equation on its own line'],
      ['/inline', 'Math inside a sentence'],
    ], [200, 500]),
    p('Type the command at the start of a line, or after a space, and press ', b('Enter'), '. Or choose ', b('+'), ' on the toolbar, then ', b('Math'), ' or ', b('Inline math'), '.'),
    p('Each starts with a sample, ', c('e = mc^2'), ' or ', c('x^2'), '. Double-click it to type your own. If you had text selected, the math takes its place. There is no shortcut with dollar signs: typing ', c('$x$'), ' stays as ordinary text.'),

    h(2, 'Inline or on its own line'),
    h(3, 'Inline: part of a sentence'),
    p('Use inline math for a short symbol or expression that belongs in what you are saying. It sits in the line like a word.'),
    panel('info', p('The area of a circle is ', math('A = \\pi r^2', false), ', so doubling the radius ', math('r', false), ' makes the circle four times as large.')),
    h(3, 'On its own line: an equation to study'),
    p('Use an equation on its own line for a formula readers need to look at closely, or one that is tall, such as a fraction or a sum. It is centered, with space above and below.'),
    panel('info',
      p('Savings with compound interest grow to'),
      p(math('A = P\\left(1 + \\frac{r}{n}\\right)^{nt}', true)),
      p('where ', math('P', false), ' is the amount put in, ', math('r', false), ' the yearly interest rate, ', math('n', false), ' how many times a year interest is added, and ', math('t', false), ' the number of years.')),
    p('Most pages mix the two, as that example does: the equation on its own line, and its letters explained inline in the sentence after it.'),

    h(2, 'A few pieces of LaTeX'),
    p('Each of these is typed as shown on the left and drawn as on the right.'),
    table([
      ['Type', 'You get'],
      [p(c('x^2')), p(math('x^2', false))],
      [p(c('x_1')), p(math('x_1', false))],
      [p(c('\\frac{a}{b}')), p(math('\\frac{a}{b}', false))],
      [p(c('\\sqrt{2}')), p(math('\\sqrt{2}', false))],
      [p(c('\\pi, \\alpha, \\Delta')), p(math('\\pi, \\alpha, \\Delta', false))],
      [p(c('\\times, \\div, \\pm')), p(math('\\times, \\div, \\pm', false))],
      [p(c('\\le, \\ge, \\ne, \\approx')), p(math('\\le, \\ge, \\ne, \\approx', false))],
      [p(c('\\sum_{i=1}^{n} x_i')), p(math('\\sum_{i=1}^{n} x_i', false))],
      [p(c('\\int_0^1 x\\,dx')), p(math('\\int_0^1 x\\,dx', false))],
      [p(c('\\text{cost} = 12 \\times n')), p(math('\\text{cost} = 12 \\times n', false))],
    ], [360, 340]),
    panel('success', p(b('Put anything longer than one character in braces.'), ' ', c('x^{10}'), ' raises the whole 10; ', c('x^10'), ' raises only the 1.')),
    p('KaTeX, which draws the math, lists every command it knows on ', text('its website', link('https://katex.org/docs/supported.html')), '.'),

    h(2, 'Changing and removing it'),
    ul(
      li(p(b('Double-click the math'), ' to change its LaTeX. Press ', b('Enter'), ' or click away to keep the change; ', b('Escape'), ' leaves it as it was.')),
      li(p(b('Inline'), ' and ', b('Own line'), ', beside the LaTeX while you edit it, move the math into the sentence or onto a line of its own.')),
      li(p(b('A mistake shows in place.'), ' If the LaTeX cannot be drawn, for example because a brace is missing, the message saying why appears where the math would be, until you fix it.')),
      li(p(b('To remove it,'), ' click it once to select it and press ', b('Delete'), '.')),
    ),

    h(2, 'Good practice'),
    ul(
      li(p(b('Explain the letters.'), ' A formula is only useful to readers who know what each letter stands for.')),
      li(p(b('Keep numbers in text as text.'), ' “Prices rise 3% a year” needs no math; save it for real formulas.')),
      li(p(b('One equation to a line.'), ' Several on one line are hard to read and harder to refer to.')),
    ),

    h(2, 'Related elements'),
    ul(
      li(p(pageLink('Chart'), ' draws the numbers in a table, when a picture says more than a formula.')),
      li(p(pageLink('Code block'), ' shows code, or LaTeX you want readers to copy rather than see drawn.')),
      li(p(pageLink('Diagram (Mermaid)'), ' draws flowcharts and other diagrams from text.')),
    ),
  ))

  // ================================================================= Chart
  const signups = table([
    ['Month', 'Free', 'Team'],
    ['July', '120', '18'], ['August', '180', '26'], ['September', '260', '41'],
    ['October', '310', '55'], ['November', '290', '62'], ['December', '240', '70'],
  ], [200, 160, 160])
  const questions = table([
    ['Topic', 'Questions'],
    ['Signing in on a new phone', '46'], ['Sharing a folder with a team', '31'],
    ['Editing offline', '22'], ['Changing the billing address', '12'], ['Installing on Linux', '7'],
  ], [360, 160])
  const readers = table([
    ['Week', 'Computer', 'Phone'],
    ['Aug 3', '410', '120'], ['Aug 10', '430', '150'], ['Aug 17', '420', '180'], ['Aug 24', '460', '230'],
    ['Aug 31', '450', '260'], ['Sep 7', '480', '310'], ['Sep 14', '470', '350'], ['Sep 21', '490', '400'],
  ], [200, 160, 160])
  const week = table([
    ['Activity', 'Hours'],
    ['Building', '18'], ['Meetings', '8'], ['Reviews', '6'], ['Support', '5'], ['Other', '3'],
  ], [240, 160])
  const chartOf = (tbl, chartType, title) => ({ type: 'chart', attrs: { chartType, title, source: tbl } })

  await page('Chart', elements, numberCharts(doc(
    p('A chart draws the numbers in a table on the same page as columns, bars, a line or a pie. The table stays the one place the numbers live: change a number and the chart redraws, so the two can never disagree. Use a chart when the shape of the numbers matters more than the numbers themselves: which month was best, how fast something is growing, what share each part takes.'),

    h(2, 'Insert it'),
    table([
      ['Type this', 'To get'],
      ['/bar or /pie', 'A chart, drawn from the first table on the page'],
      ['/chart', 'A list with Diagram (Mermaid) first and Chart second; choose Chart'],
    ], [200, 500]),
    insertNote('Chart', 'If you had text selected, the chart takes its place.'),
    p('A new chart starts as a column chart of the first table on the page. Choose the table you mean in its ', b('Table'), ' menu, as described under ', b('Changing and removing it'), '.'),

    h(2, 'How a chart reads a table'),
    ul(
      li(p(b('The first row names the series.'), ' Each column after the first becomes one color, listed in the legend under the chart.')),
      li(p(b('The first column labels the rows.'), ' Each row becomes a group of columns, a bar, a point on the line, or a slice.')),
      li(p(b('The other cells are the numbers.'), ' Values written the way people write them count: ', c('1,234'), ', ', c('45%'), ', ', c('$9.50'), '. A row with no numbers at all is skipped.')),
      li(p(b('Tables are numbered from the top of the page,'), ' every table counted. On this page the table under ', b('Insert it'), ' is Table 1, so the four charts below draw Tables 2 to 5.')),
    ),

    h(2, 'The four types, and when to use each'),
    h(3, 'Column: compare amounts across a few groups'),
    p('Upright columns, one group for each row, one color for each series. Use it to compare a handful of periods or groups side by side, and to set two or three series against each other. For example, sign-ups on the free and team plans, month by month:'),
    signups,
    chartOf(signups, 'column', 'Sign-ups by month'),
    p('The free plan peaked in October; the team plan kept growing. Nobody has to read twelve numbers to see that.'),

    h(3, 'Bar: long labels, or a ranking'),
    p('The same idea turned on its side: one bar for each row, its label to the left. Labels have room to be long, so use it when the rows are sentences rather than single words, or when you want to rank things. Sort the table from the largest number down and the chart reads as a ranking. For example, the questions the support team answered most:'),
    questions,
    chartOf(questions, 'bar', 'Support questions in September'),

    h(3, 'Line: change over time'),
    p('One line for each series, joining the rows in order from left to right. Use it for something measured again and again over time, where the trend matters more than any one point. For example, weekly readers on a computer and on a phone:'),
    readers,
    chartOf(readers, 'line', 'Weekly readers'),
    p('A line chart has no labels along its bottom edge: the first point is the table’s first row, and the last point its last row. Keep the table beside the chart, as here, so readers can find a week.'),

    h(3, 'Pie: parts of a whole'),
    p('One slice for each row, sized by its share of the total. Use it only when the rows add up to a whole that means something, such as a budget or a week, and there are five slices or fewer. For example, where a team’s 40-hour week goes:'),
    week,
    chartOf(week, 'pie', 'Where a 40-hour week goes'),
    panel('note', p(b('A pie uses the first column of numbers only.'), ' A table with more series draws just the first of them as a pie. Choose Column to see them all.')),
    p('Point at a column or a bar to see its value. Negative numbers count as zero in columns, bars and pies; a line goes below its starting level for them.'),

    h(2, 'Changing and removing it'),
    p('While you are editing, three controls sit above the chart:'),
    ul(
      li(p(b('Table'), ' chooses which table on the page to draw: Table 1 is the first.')),
      li(p(b('Type'), ' switches between Column (vertical), Bar (horizontal), Line and Pie. The table is not touched.')),
      li(p(b('Chart title (optional)'), ' is a line shown above the chart.')),
    ),
    p('If there is no table on the page yet, the chart says so; if its table has no numbers, it says that instead. To remove a chart, click its edge to select it and press ', b('Delete'), '. The table stays.'),
    panel('warning', p(b('Adding a table above a chart renumbers the tables.'), ' A chart pointing at Table 2 then draws the new one. After adding or removing a table, check the Table menu of every chart below it.')),

    h(2, 'Good practice'),
    ul(
      li(p(b('Say what the chart shows in its title,'), ' including when: “Sign-ups by month, 2026”, not “Chart”.')),
      li(p(b('Put the chart right after its table,'), ' so readers who want an exact number find it at once.')),
      li(p(b('Few series.'), ' Two or three colors compare well; eight become a puzzle.')),
      li(p(b('Pick the type by the question.'), ' Comparing: column or bar. Over time: line. Share of a whole: pie.')),
    ),

    h(2, 'Related elements'),
    ul(
      li(p(pageLink('Table'), ' holds the numbers a chart draws.')),
      li(p(pageLink('Diagram (Mermaid)'), ' draws flowcharts and timelines from text, rather than numbers.')),
      li(p(pageLink('Math'), ' shows the formula behind the numbers.')),
    ),
  )))

  // ================================================================= Image
  const imagePage = ids.Image
  const dawnId = await attachCurrent(imagePage, 'mountains-at-dawn.png', lib.landscape('dawn'), 'image/png')
  const dayId = await attachCurrent(imagePage, 'mountains-at-day.png', lib.landscape('day'), 'image/png')
  const smallId = await attachCurrent(imagePage, 'hills-small.png', scene(lib, 'day', 360, 225), 'image/png')
  const formId = await attachCurrent(imagePage, 'sign-up-form.png', formPicture(lib), 'image/png')
  await page('Image', elements, doc(
    p('An image puts a picture on the page: a photo, a diagram, a screenshot. You choose how big it is, where it sits, a caption under it, and a description for people who cannot see it. Pictures are uploaded to the page and kept with it, so they never go missing when a website somewhere else changes.'),

    h(2, 'Insert it'),
    table([
      ['Type this', 'To get'],
      ['/image', 'Your computer’s file picker, to choose a picture'],
    ], [200, 500]),
    insertNote('Image', 'If you had text selected, the picture takes its place.'),
    p('Two quicker ways: ', b('paste'), ' a picture you have copied, or ', b('drag'), ' one or more picture files onto the page. Each is uploaded and appears where the cursor is. And typing ', c('![description](https://…)'), ' shows a picture from a web address without uploading it. Your administrator may allow pictures only from certain sites; if so, a note under a picture from anywhere else says it will not show.'),

    h(2, 'Sizes'),
    h(3, 'Its own size'),
    p('A picture starts at its own size, as wide as the page at most. Keep small pictures, such as an icon or a close-up of one button, at their own size: stretched, they turn blurry.'),
    image(smallId, 'Green hills under a blue sky, at the picture’s own small size', { caption: 'A small picture at its own size.' }),
    h(3, 'A share of the page'),
    p('Drag the handle at the picture’s bottom-right corner and it takes a share of the page’s width, from 10% to 100%. It keeps that share on every screen, so a picture set to half the page is half the page wherever it is read.'),
    image(dawnId, 'Mountains at dawn, at a quarter of the page', { width: 25, caption: '25%: a portrait or a logo.' }),
    image(dawnId, 'Mountains at dawn, at half the page', { width: 50, caption: '50%: a photo that goes with the text.' }),
    image(dawnId, 'Mountains at dawn, at three quarters of the page', { width: 75, caption: '75%: a picture that is the point of the section.' }),
    panel('note', p(b('On a phone, a resized picture fills the screen’s width.'), ' A phone’s screen is already narrow, and a picture shrunk twice would be too small to make out. So these three look the same on a phone.')),

    h(2, 'Where it sits'),
    p('A picture narrower than the page can sit at the left, in the center or at the right. Text does not flow around it: a picture beside text belongs in a ', pageLink('Layout'), '.'),
    image(dayId, 'Mountains by day, at the left', { width: 40, align: 'left', caption: 'Left: in line with the text, like a figure in a report.' }),
    image(dayId, 'Mountains by day, in the center', { width: 40, caption: 'Center, where a picture starts: on its own, apart from the text.' }),
    image(dayId, 'Mountains by day, at the right', { width: 40, align: 'right', caption: 'Right: a small aside, such as a signature or a stamp.' }),
    image(dayId, 'Mountains by day, across the full width', { align: 'full', caption: 'Full: the whole width, for a wide diagram or a panorama.' }),

    h(2, 'Caption'),
    p('A caption is a line in small italics under the picture. Use it to say what the picture shows, or where it came from. Every picture above has one.'),

    h(2, 'Alt text'),
    p('Alt text describes the picture for people who cannot see it: screen readers read it aloud, and browsers show it if the picture cannot load. Nobody else sees it. It starts as the file’s name, which rarely helps, so replace it with what the picture shows.'),
    p('For example, the full-width picture above has the alt text ', i('Mountains by day, across the full width'), '. A screenshot’s alt text says what the screen shows, such as ', i('The Share dialog, with Anyone with the link selected'), '.'),

    h(2, 'Border and shadow'),
    p('A picture with white edges, such as a screenshot, runs into a white page, and readers cannot tell where it ends. Here is one as it starts:'),
    image(formId, 'A sign-up form with a title, three fields and a blue button', { width: 60 }),
    p('A ', b('border'), ' draws a thin line around it. Use it for screenshots and diagrams in plain documents:'),
    image(formId, 'The same sign-up form, with a border', { width: 60, border: true }),
    p('A ', b('shadow'), ' lifts it off the page. Use it for screenshots that should look like a window; every picture on these Support pages has one:'),
    image(formId, 'The same sign-up form, with a shadow', { width: 60, shadow: true }),

    h(2, 'Changing and removing it'),
    p('Click a picture while you are editing and a bar of options appears above it:'),
    ul(
      li(p(b('Border'), ' and ', b('Shadow'), ' turn each on or off.')),
      li(p(b('Left'), ', ', b('Center'), ', ', b('Right'), ' and ', b('Full'), ' place it. ', b('Original size'), ' appears once you have resized it, and puts it back to its own size.')),
      li(p(b('Caption'), ' and ', b('Alt text'), ' each open a box to type in; ', b('Save'), ' keeps it. Leave the box empty to remove it.')),
      li(p(b('Comment'), ' starts a comment about the picture.')),
    ),
    p('To resize it, drag the small square at its bottom-right corner. To remove it, click it and press ', b('Delete'), '; the file stays among the page’s attachments.'),

    h(2, 'Good practice'),
    ul(
      li(p(b('Write alt text for every picture'), ' that carries meaning. Only a picture that is pure decoration can do without.')),
      li(p(b('Crop before you upload.'), ' A picture of the part that matters is clearer than a whole screen with an arrow on it.')),
      li(p(b('Mind the file size.'), ' A photo straight from a phone camera can be several megabytes, and every reader downloads it.')),
    ),

    h(2, 'Related elements'),
    ul(
      li(p(pageLink('Gallery'), ' tiles several pictures in a grid.')),
      li(p(pageLink('Layout'), ' puts a picture beside text.')),
      li(p(pageLink('File or video'), ' shows a video, a PDF or any other file on the page.')),
    ),
  ))

  // =============================================================== Gallery
  const galleryPage = ids.Gallery
  const g = {}
  for (const theme of ['dawn', 'day', 'dusk', 'night']) g[theme] = await attachCurrent(galleryPage, `mountains-at-${theme}.png`, lib.landscape(theme), 'image/png')
  const tallId = await attachCurrent(galleryPage, 'mountains-tall.png', scene(lib, 'dusk', 480, 720), 'image/png')
  await page('Gallery', elements, doc(
    p('A gallery tiles pictures in a grid, each cropped to the same shape, so a set of photos reads as one tidy block instead of a long column you have to scroll through. Use it for pictures that belong together: photos from an event, the screens of an app, the options for a design.'),

    h(2, 'Insert it'),
    table([
      ['Type this', 'To get'],
      ['/gallery', 'An empty gallery, ready for pictures'],
    ], [200, 500]),
    insertNote('Gallery', 'If you had text selected, the gallery takes its place.'),
    p('While you are editing, the gallery is a dashed box. With the cursor inside it, paste pictures, drag picture files into it, or type ', c('/image'), ', and each one becomes a tile.'),

    h(2, 'How it looks'),
    h(3, 'A row of pictures'),
    p('Tiles are at least 200 pixels wide, so a computer screen fits three to a row and a phone one or two. Three pictures make one neat row on a computer. For example, one view at three times of day:'),
    gallery(
      image(g.dawn, 'Mountains at dawn'),
      image(g.day, 'Mountains by day'),
      image(g.dusk, 'Mountains at dusk'),
    ),
    h(3, 'More than fit in a row'),
    p('Extra pictures wrap onto the next row, and every tile stays the same size, so a last picture on its own does not stretch across the page:'),
    gallery(
      image(g.dawn, 'Mountains at dawn'),
      image(g.day, 'Mountains by day'),
      image(g.dusk, 'Mountains at dusk'),
      image(g.night, 'Mountains at night, under the stars'),
    ),
    h(3, 'Pictures of different shapes'),
    p('Every tile has the same shape, four wide by three high, and each picture is cropped to fit it from the middle. The first picture here is tall; the gallery shows its center. Put a picture whose edges matter in an ', pageLink('Image'), ' instead.'),
    gallery(
      image(tallId, 'A tall picture of mountains at dusk, cropped to a tile'),
      image(g.day, 'Mountains by day'),
      image(g.night, 'Mountains at night, under the stars'),
    ),

    h(2, 'Changing and removing it'),
    ul(
      li(p(b('To add a picture,'), ' put the cursor in the gallery and paste, drag or type ', c('/image'), '.')),
      li(p(b('To remove one,'), ' click it and press ', b('Delete'), '.')),
      li(p(b('Each picture keeps its options:'), ' click one for its bar of Border, Shadow, Alt text and the rest. Size and position do not apply here: the tiles decide both.')),
    ),

    h(2, 'Good practice'),
    ul(
      li(p(b('Choose pictures of the same shape'), ' when you can, so nothing important is cropped away.')),
      li(p(b('Give each picture alt text.'), ' Readers who cannot see the pictures have nothing else to go on.')),
      li(p(b('Say what the set is'), ' in a sentence above it.')),
    ),

    h(2, 'Related elements'),
    ul(
      li(p(pageLink('Image'), ' shows one picture, with a size, a position and a caption.')),
      li(p(pageLink('Layout'), ' puts pictures and text side by side in columns.')),
    ),
  ))

  // ========================================================= File or video
  const filePage = ids['File or video']
  const slashClip = readFileSync(join(ROOT, 'src/web/public/onboarding/editor-slash.light.webm'))
  const videoId = await attachCurrent(filePage, 'slash-menu.webm', slashClip, 'video/webm')
  const pdfId = await attachCurrent(filePage, 'kestrel-launch-handout.pdf', lib.handoutPdf(), 'application/pdf')
  const soundId = await attachCurrent(filePage, 'new-message-chime.wav', chime(), 'audio/wav')
  const csvId = await attachCurrent(filePage, 'kestrel-signups.csv', Buffer.from(SIGNUPS_CSV), 'text/csv')
  await page('File or video', elements, doc(
    p('File or video shows a file attached to the page right there in the page. A video plays in a player, a PDF opens in a viewer, a sound file gets a player, and anything else becomes a card to download it. Use it when readers should see or hear the file where they are reading, rather than hunting for it among the page’s attachments.'),
    p('What it shows is decided by the file itself: you choose the file, and Tesria draws it the right way.'),

    h(2, 'Insert it'),
    table([
      ['Type this', 'To get'],
      ['/file or /pdf', 'A File or video block, ready for a file'],
    ], [200, 500]),
    insertNote('File or video', 'If you had text selected, the block takes its place.'),
    p('The block has a ', b('File'), ' menu listing the page’s attachments, and an ', b('Upload'), ' button to add a file from your computer, which then shows at once. Typing ', c('/video'), ' lists Embed first, for a video from a site such as YouTube, and File or video second, for a video file of your own.'),

    h(2, 'What each kind of file looks like'),
    h(3, 'A video'),
    p('A video plays in a player with the usual controls: play, pause, sound, full screen, and a bar to jump back and forth. Use it for a walkthrough or a recorded talk, anything people want to pause and rewind. For example, a short recording of the slash menu:'),
    fileBlock(videoId, 'player'),
    p('A video can also play as an animation: silently, on a loop, with no controls, like a GIF. That is for short clips of something being done; see ', pageLink('Animation'), '.'),

    h(3, 'A PDF'),
    p('A PDF opens in the browser’s own viewer, with a link under it to download it. Use it for a document people should read in place: a handout, a signed policy, a form. For example, a launch handout:'),
    fileBlock(pdfId, 'player'),

    h(3, 'A sound'),
    p('A sound file gets a small player. Use it for a voice note, a recorded message, or a sound the team is choosing. For example, a new-message chime up for approval:'),
    fileBlock(soundId, 'player'),

    h(3, 'Any other file'),
    p('Anything else becomes a card with the file’s name and size; clicking it downloads the file. Use it for a spreadsheet, a zip file, a design file: things people open in another program. For example, the sign-up figures as a spreadsheet:'),
    fileBlock(csvId, 'player'),

    h(3, 'A picture'),
    p('A picture shows as the picture. An ', pageLink('Image'), ' is usually the better choice for one, because it has a size, a position and a caption.'),

    h(2, 'Changing and removing it'),
    ul(
      li(p(b('File'), ' switches to another of the page’s attachments; ', b('Upload'), ' adds a new one and shows it.')),
      li(p(b('Show as'), ', for a video, chooses ', b('A video with controls'), ' or ', b('An animation: silent, looping, no controls'), '.')),
      li(p(b('To remove it,'), ' click its edge to select it and press ', b('Delete'), '. The file stays among the page’s attachments.')),
    ),
    p('If the file is later deleted from the page’s attachments, the block says ', i('That file is no longer attached to this page'), ' instead.'),

    h(2, 'Good practice'),
    ul(
      li(p(b('Say what the file is'), ' in a sentence above it, so readers know whether to open it.')),
      li(p(b('Keep videos short and small.'), ' A long, large video is slow to start on a phone.')),
      li(p(b('Name files for people.'), ' ', c('kestrel-launch-handout.pdf'), ' tells readers what they are downloading; ', c('scan0042.pdf'), ' does not.')),
    ),

    h(2, 'Related elements'),
    ul(
      li(p(pageLink('Animation'), ' plays a short video silently on a loop, like a GIF.')),
      li(p(pageLink('Embed'), ' shows a video or document from another site, such as YouTube.')),
      li(p(pageLink('Image'), ' shows a picture, with a size, a position and a caption.')),
      li(p(pageLink('Attachments (live content)', 'Attachments'), ' lists every file attached to the page.')),
    ),
  ))

  // ============================================================= Animation
  const animationPage = ids.Animation
  // The Demo space's clip (seed-demo.mjs): the slash menu, in the tour space.
  const clipId = await attachCurrent(animationPage, 'slash-menu.webm', slashClip, 'video/webm')
  await page('Animation', elements, doc(
    p('An animation is a short video that plays like a GIF: silently, on a loop, starting by itself, with no player controls. It is the clearest way to show a small thing being done, such as a menu opening, a click, a drag, and a video file is a fraction of the size of the same GIF. The recordings on these Support pages are animations.'),

    h(2, 'Insert it'),
    table([
      ['Type this', 'To get'],
      ['/animation or /gif', 'A File or video block set to play as an animation'],
    ], [200, 500]),
    insertNote('Animation', 'If you had text selected, the block takes its place.'),
    p('Then choose a video in its ', b('File'), ' menu, or ', b('Upload'), ' one. Any video attached to the page works.'),

    h(2, 'As an animation, or with controls'),
    h(3, 'An animation'),
    p('It starts by itself, plays with no sound, and loops. Use it for a clip of a few seconds that shows how to do one thing. For example, adding a table with the slash menu:'),
    fileBlock(clipId, 'animation'),
    p(i('Typing / on a new line opens the slash menu; typing tab narrows it, and Enter adds a table.')),
    p('Point at an animation and a small button appears in its corner to pause it; on a phone or tablet the button is always there. Readers whose device is set to reduce motion see it paused on its first frame, and can start it with that button.'),

    h(3, 'The same video with controls'),
    p('Here is the same file as a video with controls. Use this for anything longer, anything with sound, or anything readers need to stop at a particular moment:'),
    fileBlock(clipId, 'player'),

    h(2, 'Changing and removing it'),
    ul(
      li(p(b('Show as'), ' switches between ', b('An animation: silent, looping, no controls'), ' and ', b('A video with controls'), '.')),
      li(p(b('File'), ' chooses another video; ', b('Upload'), ' adds one.')),
      li(p(b('To remove it,'), ' click its edge to select it and press ', b('Delete'), '. The video stays among the page’s attachments.')),
    ),

    h(2, 'Good practice'),
    ul(
      li(p(b('A few seconds, one action.'), ' Readers watch it loop; ten seconds is already long.')),
      li(p(b('End where it began,'), ' or hold the last moment, so the loop does not jump.')),
      li(p(b('Record only what matters.'), ' A small window, or a part of the screen, reads on a phone; a whole screen does not.')),
      li(p(b('Say what it shows'), ' in a line under it, as above. Some readers see it paused, and nobody can hear it.')),
      li(p(b('Use a common format.'), ' MP4 and WebM videos play in every current browser.')),
    ),

    h(2, 'Related elements'),
    ul(
      li(p(pageLink('File or video'), ' shows a video with controls, a PDF, a sound or any other file.')),
      li(p(pageLink('Embed'), ' shows a video from a site such as YouTube.')),
      li(p(pageLink('Image'), ' shows a still picture.')),
    ),
  ))

  // ================================================================= Embed
  await page('Embed', elements, doc(
    p('An embed shows something from another website inside your page, working just as it does there: a video that plays, a design you can move around in, a board you can scroll. Readers never leave the page to see it. Use one for a walkthrough video, the latest version of a design, or a planning board the team keeps elsewhere.'),

    h(2, 'Insert it'),
    table([
      ['Type this', 'To get'],
      ['/embed or /youtube', 'An embed, with a box for the address'],
    ], [200, 500]),
    insertNote('Embed', 'If you had text selected, the embed takes its place.'),
    p('Paste the address of the video or document into the box, exactly as it is in your browser’s address bar, and choose ', b('Embed'), '.'),

    h(2, 'A video'),
    p('The most common embed. Paste a YouTube video’s address and it plays right in the page:'),
    embed('https://www.youtube.com/watch?v=aqz-KE-bpKQ'),
    p(i('Big Buck Bunny'), ' © Blender Foundation, ', text('CC BY 3.0', link('https://creativecommons.org/licenses/by/3.0/')), ', from ', text('peach.blender.org', link('https://peach.blender.org/')), '.'),
    p('YouTube videos play in YouTube’s privacy-enhanced player, from youtube-nocookie.com, rather than the ordinary one.'),

    h(2, 'The sites you can embed'),
    p('Tesria embeds only sites on its allowed list, so that nobody can put an unknown site inside everyone else’s pages. The list starts with these:'),
    ul(
      li(p(b('YouTube:'), ' a video, a Short or a live stream, by its youtube.com or youtu.be address.')),
      li(p(b('Vimeo:'), ' a video.')),
      li(p(b('Loom:'), ' a recording, by its share link.')),
      li(p(b('Figma:'), ' a design file or a prototype.')),
      li(p(b('Miro:'), ' a board.')),
      li(p(b('CodePen:'), ' a pen.')),
      li(p(b('Google Docs and Google Drive:'), ' a document, spreadsheet, presentation or file, shown in Google’s preview. Readers see it only if its sharing settings in Google let them.')),
    ),
    p('An administrator can change the list in ', b('Admin'), ', ', b('Settings'), ', under ', b('Embeds'), ' (', b('Allowed embed hosts'), '). See ', pageLink('Settings (administration)'), '. A site added there that is not one of the above is shown at the address you paste.'),

    h(2, 'When a site is not allowed'),
    p('An address from any other site is not shown. Readers see why, and a link to open it in a new tab instead. This is an embed of ', c('https://www.example.com/'), ', which is not on the list:'),
    embed('https://www.example.com/'),
    p('Some sites also refuse to be shown inside other sites, whatever the list says. For those, a ', pageLink('Smart link'), ' is the better choice.'),

    h(2, 'Changing and removing it'),
    p('While you are editing, the address box stays above the embed. Paste another address and choose ', b('Embed'), ' to change it. To remove it, click its edge to select it and press ', b('Delete'), '.'),

    h(2, 'Good practice'),
    ul(
      li(p(b('Say what it is'), ' in a sentence above it: a frame gives no clue until it loads.')),
      li(p(b('Credit other people’s work,'), ' as the video above does.')),
      li(p(b('Mind who can see the page.'), ' An embed of a private document shows it to every reader who has access to it in that other site.')),
    ),

    h(2, 'Related elements'),
    ul(
      li(p(pageLink('Smart link'), ' shows a site’s title and picture, for sites that cannot be embedded.')),
      li(p(pageLink('File or video'), ' plays a video file attached to the page.')),
      li(p(pageLink('Animation'), ' plays a short clip on a loop, like a GIF.')),
    ),
  ))

  // ============================================================ Smart link
  const wikiUrl = 'https://en.wikipedia.org/wiki/Wiki'
  await page('Smart link', elements, doc(
    p('A smart link is a link that shows what it points at. Instead of a bare address, readers see the page’s own title and, as a card, its description, picture and site name, fetched from that page. Use one when the link is the point: an article everyone should read, the design doc a decision rests on, the home page of a tool the team uses.'),

    h(2, 'Insert it'),
    table([
      ['Type this', 'To get'],
      ['/smart or /preview', 'A smart link card, with a box for the address'],
    ], [200, 500]),
    insertNote('Smart link', 'If you had text selected, the smart link takes its place.'),
    p('Paste the address into the box and press ', b('Enter'), ', or choose ', b('Show'), '. It starts as a card.'),

    h(2, 'Card or inline'),
    h(3, 'Card'),
    p('A block of its own, with the page’s picture, title, description and site. Use it when the link deserves attention of its own, such as recommended reading at the end of a section:'),
    smartLink('https://en.wikipedia.org/wiki/Big_Buck_Bunny', 'card'),
    h(3, 'Inline'),
    p('The page’s title as a link, inside a sentence, with a small arrow after it. Use it when the link is part of what you are saying:'),
    panel('info', p('For the idea behind Tesria, read ', { type: 'smartLinkInline', attrs: { url: wikiUrl } }, ' before the workshop.')),
    h(3, 'Compared with an ordinary link'),
    p('The same address as an ordinary link reads as whatever words you give it, such as ', text('this article on wikis', link(wikiUrl)), '. Use an ordinary ', pageLink('Link'), ' for links in running text you want to word yourself, and a smart link when the page’s own title says it best.'),
    p('Your Tesria server fetches the title and picture from the page. If it cannot, because the site does not allow it or the server cannot reach the internet, the link still works and shows its address instead.'),

    h(2, 'Changing and removing it'),
    ul(
      li(p(b('A card:'), ' click it while you are editing. The address box appears above it, with ', b('Update'), ' to change the address, and ', b('Card'), ' and ', b('Inline'), ' to switch. Inline puts the link in a line of its own, where you can type around it.')),
      li(p(b('An inline link:'), ' click it for its address, ', b('Update'), ', and ', b('Card'), ', which turns it into a card below its paragraph.')),
      li(p(b('To remove either,'), ' click it to select it and press ', b('Delete'), '.')),
    ),

    h(2, 'Good practice'),
    ul(
      li(p(b('Cards for a few links that matter,'), ' not for every link: a page of cards is hard to read.')),
      li(p(b('Say why it is worth opening'), ' in a sentence beside it.')),
    ),

    h(2, 'Related elements'),
    ul(
      li(p(pageLink('Link'), ' turns any words into a link.')),
      li(p(pageLink('Embed'), ' shows the page itself inside yours, for sites that allow it.')),
    ),
  ))

  // ================================================================ Status
  const tracker = table([
    ['Work', 'Owner', 'Status'],
    ['Pricing page', p(person('Mei Chen')), p(status('In review', 'blue'))],
    ['Migration dry run', p(person('Sam Okafor')), p(status('Done', 'green'))],
    ['Support FAQ', p(person('Priya Natarajan')), p(status('At risk', 'yellow'))],
    ['Status page', p(person('Sam Okafor')), p(status('Blocked', 'red'))],
    ['Mobile apps', p(person('Mei Chen')), p(status('Not started', 'grey'))],
  ], [220, 220, 200])
  await page('Status', elements, doc(
    p('A status is a small colored label, such as ', status('Done', 'green'), ' or ', status('In progress', 'blue'), ', that sits in a line of text or a table cell. Readers see the state of something at a glance, before reading a word: what is finished, what is stuck, what has not started. Use statuses in project trackers, meeting notes, checklists and anywhere else things move from one state to the next.'),

    h(2, 'Insert it'),
    table([
      ['Type this', 'To get'],
      ['/status', 'A gray STATUS, with its menu open'],
    ], [200, 500]),
    insertNote('Status', 'If you had text selected, the status takes its place.'),
    p('Its menu opens under it, with the cursor in the label: replace STATUS with your own words, then choose a color.'),
    ...(await animation(ids.Status, 'status-insert', 'Typing /status makes a status; type its label and choose a color.')),

    h(2, 'The six colors, and when to use each'),
    p('The label says what the state is; the color says how readers should feel about it. Agree in your team what each color means, and keep to it, so a page of statuses reads at a glance.'),
    p(status('Not started', 'grey'), ' ', b('Gray: nothing is happening.'), ' Not started yet, on hold, or not needed. For example: ', i('NOT STARTED'), ', ', i('PAUSED'), ', ', i('N/A'), '.'),
    p(status('In progress', 'blue'), ' ', b('Blue: under way.'), ' Being worked on, or waiting for its next step. For example: ', i('IN PROGRESS'), ', ', i('IN REVIEW'), ', ', i('PLANNING'), '.'),
    p(status('At risk', 'yellow'), ' ', b('Yellow: needs attention.'), ' Still moving, but something could go wrong. For example: ', i('AT RISK'), ', ', i('WAITING'), ', ', i('NEEDS INFO'), '.'),
    p(status('Done', 'green'), ' ', b('Green: finished or going well.'), ' For example: ', i('DONE'), ', ', i('APPROVED'), ', ', i('ON TRACK'), '.'),
    p(status('Blocked', 'red'), ' ', b('Red: a problem.'), ' Stopped, late or failed, and someone has to act. For example: ', i('BLOCKED'), ', ', i('OVERDUE'), ', ', i('FAILED'), '.'),
    p(status('New', 'purple'), ' ', b('Purple: out of the ordinary.'), ' Something the other colors do not cover, such as a label for a kind of thing rather than a state. For example: ', i('NEW'), ', ', i('BETA'), ', ', i('IDEA'), '.'),

    h(2, 'In a sentence or in a table'),
    h(3, 'In a sentence'),
    p('One status in a line of text says where one thing stands:'),
    panel('info', p('The launch plan is ', status('On track', 'green'), ', and the pricing page is ', status('In review', 'blue'), ' with Mei.')),
    h(3, 'In a table'),
    p('A column of statuses turns a table into a tracker, where the colors show at a glance how the whole piece of work is going:'),
    tracker,
    p('A status can also go in a page’s ', pageLink('Page properties'), ', to give the page itself a state.'),

    h(2, 'Changing and removing it'),
    p('Click a status while you are editing and its menu opens under it:'),
    ul(
      li(p(b('The label:'), ' up to 40 characters. It is always shown in capitals, however you type it.')),
      li(p(b('The six colors:'), ' Gray, Red, Yellow, Green, Blue and Purple. Choose one to change it.')),
    ),
    p('Press ', b('Enter'), ' to go back to writing. To remove a status, click it and press ', b('Delete'), '.'),

    h(2, 'Good practice'),
    ul(
      li(p(b('One or two words.'), ' A status is read at a glance; a sentence belongs in the text.')),
      li(p(b('Keep it current.'), ' A status does not change by itself, and an old green one is worse than none.')),
      li(p(b('Let the word carry the meaning.'), ' Some readers cannot tell the colors apart, so ', i('BLOCKED'), ' has to say it on its own.')),
    ),

    h(2, 'Related elements'),
    ul(
      li(p(pageLink('Date'), ' goes beside a status for when something is due.')),
      li(p(pageLink('Page properties'), ' gives the whole page a status that reports can collect.')),
      li(p(pageLink('Task list'), ' tracks things to do with checkboxes.')),
    ),
  ))

  // ================================================================== Date
  await page('Date', elements, doc(
    p('A date shows a day of the calendar as a small chip, such as ', date('2026-10-14'), '. You set it once, and each reader sees it written the way they are used to: someone in the US reads Oct 14, 2026, someone in the UK 14 Oct 2026, someone in Germany 14. Okt. 2026. Nobody has to wonder whether 10/04 means the fourth of October or the tenth of April. Use one for deadlines, launch days, meeting dates: any day people must read correctly.'),

    h(2, 'Insert it'),
    table([
      ['Type this', 'To get'],
      ['/date or /today', 'Today’s date, with a box to pick another'],
    ], [200, 500]),
    insertNote('Date', 'If you had text selected, the date takes its place.'),

    h(2, 'In a sentence or in a table'),
    h(3, 'In a sentence'),
    p('A date in a line of text, for a single day that matters:'),
    panel('info', p('The release candidate is due on ', date('2026-10-02'), ', and the launch is on ', date('2026-10-14'), '.')),
    h(3, 'In a table'),
    p('A column of dates makes a timeline. Beside a ', pageLink('Status'), ', it says both when and how it is going:'),
    table([
      ['Milestone', 'Date', 'State'],
      ['Feature freeze', p(date('2026-09-25')), p(status('Done', 'green'))],
      ['Release candidate', p(date('2026-10-02')), p(status('In progress', 'blue'))],
      ['Launch', p(date('2026-10-14')), p(status('Not started', 'grey'))],
    ], [240, 200, 200]),
    p('A date does not change color once it has passed, so a status beside it is how readers see that something is late.'),

    h(2, 'Changing and removing it'),
    p('Click a date while you are editing and a date box opens under it: type a date, or pick one from its calendar. Press ', b('Enter'), ' to go back to writing. To remove a date, click it and press ', b('Delete'), '.'),

    h(2, 'Good practice'),
    ul(
      li(p(b('Use a date, not typed text,'), ' for any day people act on. Typed dates are read differently in different countries.')),
      li(p(b('Put the day that matters first'), ' in a table of milestones, and keep the rows in date order.')),
    ),

    h(2, 'Related elements'),
    ul(
      li(p(pageLink('Status'), ' shows how something is going, beside when it is due.')),
      li(p(pageLink('Page properties'), ' gives a page a due date that reports can collect.')),
      li(p(pageLink('Mention'), ' says who, as a date says when.')),
    ),
  ))

  // =============================================================== Mention
  const assigned = (checked, who, ...rest) => {
    const m = person(who)
    const attrs = m.type === 'mention' ? { checked, assigneeId: m.attrs.userId, assigneeName: m.attrs.label } : { checked }
    return { type: 'taskItem', attrs, content: [p(m, ...rest)] }
  }
  await page('Mention', elements, doc(
    p('A mention names a person on the page, such as ', person('Sam Okafor'), '. It shows who you mean without any doubt, and it tells them: when you publish the page, they get a notification that you mentioned them, with a link to it. Use one to ask someone for something, to credit their work, or to say who owns what.'),

    h(2, 'Insert it'),
    table([
      ['Type this', 'To get'],
      ['@ and part of a name', 'A list of people to choose from'],
    ], [200, 500]),
    p('Type ', c('@'), ' at the start of a line or after a space, then a few letters of the person’s name or email address. Choose them with the arrow keys and ', b('Enter'), ', or click them. Mentions are not in the ', b('+'), ' menu. An ', c('@'), ' in the middle of a word does nothing, so email addresses stay ordinary text.'),
    ...(await animation(ids.Mention, 'mention-insert', 'Typing @ and part of a name lists the people who match.')),

    h(2, 'Where to use one'),
    h(3, 'In a sentence'),
    p('To credit someone, or ask them something:'),
    panel('info', p('Thanks to ', person('Sam Okafor'), ' and ', person('Mei Chen'), ' for reviewing the plan. ', person('Priya Natarajan'), ', can you confirm the date?')),
    h(3, 'In a task'),
    p('In a task list, the first person mentioned in a task is the one it is assigned to. The ', pageLink('Task report'), ' can then list everything assigned to them, from every page:'),
    { type: 'taskList', content: [
      assigned(true, 'Sam Okafor', ' confirm the migration path for large accounts'),
      assigned(false, 'Mei Chen', ' draft the pricing page copy'),
      assigned(false, 'Priya Natarajan', ' brief the support team'),
    ] },
    h(3, 'In a table'),
    p('A column of mentions says who owns each row:'),
    table([
      ['Area', 'Owner'],
      ['Pricing page', p(person('Mei Chen'))],
      ['Migration', p(person('Sam Okafor'))],
      ['Support FAQ', p(person('Priya Natarajan'))],
    ], [300, 300]),
    p('Comments take mentions too: type ', c('@'), ' in a comment box in the same way.'),

    h(2, 'Who is told'),
    ul(
      li(p(b('People newly mentioned, when you publish or update.'), ' Someone already mentioned in the previous version is not told again, so fixing a typo does not notify everyone the page names.')),
      li(p(b('Only people who can see the page.'), ' Someone without access is not told, so a mention never reveals a page’s title to them.')),
      li(p(b('Not you.'), ' Mentioning yourself sends nothing.')),
    ),

    h(2, 'Changing and removing it'),
    p('A mention is one piece: ', b('Backspace'), ' just after it removes the whole name. To mention someone else, remove it and type ', c('@'), ' again. A mention keeps the name the person had when you mentioned them; if they change it later, mentions made before still show the old one.'),

    h(2, 'Good practice'),
    ul(
      li(p(b('Say what you need.'), ' “', person('Sam Okafor'), ', can you check the numbers by Friday?” gets an answer; a name on its own gets a puzzled look.')),
      li(p(b('Mention sparingly.'), ' Every mention is a notification someone has to read.')),
    ),

    h(2, 'Related elements'),
    ul(
      li(p(pageLink('Task list'), ' assigns tasks to the people mentioned in them.')),
      li(p(pageLink('Task report'), ' collects tasks by who they are assigned to.')),
      li(p(pageLink('Date'), ' says when, as a mention says who.')),
    ),
  ))

  // ================================================================= Emoji
  const EMOJI = [
    ['👍', 'thumbsup'], ['👎', 'thumbsdown'], ['✅', 'check'], ['❌', 'x'], ['⚠️', 'warning'], ['🚨', 'alert'], ['🔥', 'fire'], ['🎉', 'tada'],
    ['🚀', 'rocket'], ['🐛', 'bug'], ['💡', 'idea'], ['📝', 'memo'], ['📌', 'pin'], ['🔗', 'link'], ['🔒', 'lock'], ['🔑', 'key'],
    ['⏱️', 'timer'], ['📈', 'chart'], ['📉', 'chartdown'], ['🧪', 'test'], ['🛠️', 'tools'], ['🧹', 'broom'], ['📦', 'package'], ['🗑️', 'trash'],
    ['❓', 'question'], ['❗', 'exclamation'], ['👀', 'eyes'], ['🙏', 'pray'], ['👏', 'clap'], ['🤔', 'thinking'], ['😀', 'smile'], ['😅', 'sweat_smile'],
    ['😬', 'grimace'], ['🎯', 'target'], ['⭐', 'star'], ['❤️', 'heart'], ['☕', 'coffee'], ['🏗️', 'construction'], ['🧭', 'compass'], ['🔍', 'search'],
  ]
  const emojiRows = [['Emoji', 'Type', 'Emoji', 'Type']]
  for (let k = 0; k < EMOJI.length; k += 2) emojiRows.push([EMOJI[k][0], `:${EMOJI[k][1]}`, EMOJI[k + 1][0], `:${EMOJI[k + 1][1]}`])
  await page('Emoji', elements, doc(
    p('Emoji add a small picture to text: a rocket for a launch, a check mark for done, a warning sign for a catch. In a wiki they work best as signposts that help readers scan: ✅ beside what is finished, ❌ beside what is not. Tesria keeps an emoji as an ordinary character of text, so it can be searched for, copied and exported like any letter.'),

    h(2, 'Insert it'),
    table([
      ['Type this', 'To get'],
      [': and two letters of a name', 'A list of matching emoji'],
    ], [200, 500]),
    p('Type ', c(':'), ' at the start of a line or after a space, then at least two letters of the emoji’s name, such as ', c(':rock'), ' for 🚀. Choose one with ', b('Enter'), ' or a click. The list also matches other words for each emoji: ', c(':done'), ' finds ✅ and ', c(':ship'), ' finds 🎉 and 🚀. Emoji are not in the ', b('+'), ' menu.'),
    ...(await animation(ids.Emoji, 'emoji-insert', 'Typing a colon and a name lists the matching emoji.')),
    p('The list offers 40 emoji that wikis use most. For any other, use your computer’s own picker and it goes in like any character: ', b('Control-Command-Space'), ' on a Mac, the ', b('Windows key and period'), ' on Windows, or the emoji key on a phone’s keyboard.'),
    expand('The 40 emoji in the list, and their names', table(emojiRows, [90, 200, 90, 200])),

    h(2, 'Where to use them'),
    h(3, 'In a sentence'),
    p('For a little warmth, where a page can afford it:'),
    panel('info', p('Launch day 🚀. Thanks, everyone 🎉')),
    h(3, 'As signposts in a list'),
    p('One at the start of each item, so readers see how things stand before reading the words:'),
    ul(
      li(p('✅ Release candidate signed off')),
      li(p('✅ Migration dry run passed')),
      li(p('⚠️ Pricing page waiting for legal')),
      li(p('❌ Status page not started')),
    ),
    h(3, 'In a table'),
    p('Yes and no, readable at a glance:'),
    table([
      ['Feature', 'Computer', 'Phone'],
      ['Editing offline', '✅', '✅'],
      ['Shared folders', '✅', '✅'],
      ['Admin settings', '✅', '❌'],
    ], [280, 160, 160]),

    h(2, 'Changing and removing it'),
    p('An emoji is a character like any other: select it and type over it, or press ', b('Backspace'), ' after it to remove it.'),

    h(2, 'Good practice'),
    ul(
      li(p(b('A few, with a job to do.'), ' An emoji that marks something helps; a row of them is noise.')),
      li(p(b('Keep the words.'), ' Screen readers read an emoji’s name aloud, and some readers are not sure what a picture means. “✅ Done” is clearer than ✅.')),
      li(p(b('Same emoji, same meaning,'), ' all through a space.')),
    ),

    h(2, 'Related elements'),
    ul(
      li(p(pageLink('Status'), ' is a clearer way to show where something stands.')),
      li(p(pageLink('Task list'), ' has real checkboxes readers can tick.')),
    ),
  ))

  // ===================================================== Table of contents
  const tocWith = (attrs) => ({ type: 'tableOfContents', attrs })
  const SAMPLE = 'Choosing which headings|By level|By name|Patterns'
  const bullet = (style, label, why) => [
    p(b(label), ' ', why),
    tocWith({ bulletStyle: style, include: SAMPLE }),
  ]
  await page('Table of contents', elements, doc(
    p('A table of contents lists the headings on a page, each one a link to its section. It is built from the headings themselves and changes as you write, so it is never out of date. Put one at the top of any page long enough to scroll: readers see at once what is there, and jump to the part they need. Here is one with its settings as they start:'),
    tocWith({}),

    h(2, 'Insert it'),
    table([
      ['Type this', 'To get'],
      ['/toc', 'A table of contents of every heading on the page'],
    ], [200, 500]),
    insertNote('Table of contents', 'If you had text selected, it takes its place.'),
    p('It lists every heading, from Heading 1 to Heading 6, each under the heading before it that is a level higher. Click it to change its settings.'),

    h(2, 'Bullet styles'),
    p('Each example here lists only the four headings of ', b('Choosing which headings'), ', further down, to keep it short: that is the ', b('Include headings with'), ' setting, described there.'),
    ...bullet('bullet', 'Bullet,', 'where it starts. The browser picks the marks, usually a dot, then a circle, then a square as the levels go down. Right for most pages.'),
    ...bullet('mixed', 'Mixed:', 'always a dot, then a circle, then a square, going down the levels. The same pattern as Bullet usually gives, fixed so that it cannot vary.'),
    ...bullet('circle', 'Circle:', 'circles at every level. A lighter look.'),
    ...bullet('square', 'Square:', 'squares at every level.'),
    ...bullet('numbered', 'Numbered:', '1, 2, 3 at each level. Use it for a page read in order, such as a procedure.'),
    ...bullet('none', 'None:', 'no marks, only the indents. A clean look for a short list.'),

    h(2, 'Section numbers'),
    p(b('Include section numbers'), ' numbers the outline, 1, 2, 2.1, 2.2, 3, so readers can say “see 4.2”. Use it on long reference pages people cite. With the ', b('Numbered'), ' style, the section numbers take the place of the list’s own. This one lists the whole page:'),
    tocWith({ sectionNumbers: true, bulletStyle: 'numbered' }),
    p('Only the table of contents is numbered; the headings on the page are not.'),

    h(2, 'A horizontal list'),
    p(b('Display as Horizontal list'), ' puts the links on one line, separated by bars. Use it across the top of a page as a strip of links, or on a short page. Bullet styles do not apply. This one lists only this page’s main sections, using the heading levels below:'),
    tocWith({ display: 'horizontal', minLevel: 2, maxLevel: 2 }),

    h(2, 'Choosing which headings'),
    h(3, 'By level'),
    p(b('Heading levels'), ' sets the highest and lowest level listed, from 1 to 6. Levels 2 to 2 lists only the main sections, as the horizontal list above does. Levels 3 to 4 lists only the smaller headings:'),
    tocWith({ minLevel: 3, maxLevel: 4 }),
    h(3, 'By name'),
    p('Under ', b('Advanced'), ', ', b('Include headings with'), ' lists only the headings that match, and ', b('Exclude headings with'), ' leaves out the ones that match. This one leaves out the sections every element page has, with ', c('Insert it|Good practice|Related elements'), ':'),
    tocWith({ maxLevel: 2, exclude: 'Insert it|Good practice|Related elements' }),
    h(4, 'Patterns'),
    ul(
      li(p(c('*'), ' stands for any run of characters, and ', c('?'), ' for any one character. ', c('Step*'), ' matches ', i('Step 1: Install'), ' and ', i('Steps'), '.')),
      li(p(c('|'), ' separates alternatives: ', c('Setup|Install*'), ' matches either.')),
      li(p(b('The whole heading has to match.'), ' ', c('Step*'), ' does not match ', i('First step'), '.')),
      li(p(b('Capitals count.'), ' ', c('step*'), ' does not match ', i('Step 1'), '.')),
    ),

    h(2, 'Changing its settings'),
    p('Click the table of contents while you are editing and its settings open under it:'),
    ul(
      li(p(b('Display as:'), ' Vertical list or Horizontal list.')),
      li(p(b('Bullet style:'), ' Bullet, Mixed, Circle, Square, Numbered or None.')),
      li(p(b('Heading levels:'), ' the highest and lowest level to list.')),
      li(p(b('Include section numbers.'))),
      li(p(b('Advanced:'), ' ', b('Indent headings'), ' (how far each level steps in, such as 10px or 2em), ', b('Include headings with'), ', ', b('Exclude headings with'), ', a ', b('CSS class name'), ' for a site’s own styles, and ', b('Exclude in PDF export'), ', which leaves it out of PDFs and printouts.')),
    ),
    p('Changes show at once. To remove it, click it and press ', b('Delete'), '.'),

    h(2, 'Good practice'),
    ul(
      li(p(b('Put it at the top,'), ' after a sentence or two saying what the page is about.')),
      li(p(b('Write headings as signposts.'), ' The table of contents is only as useful as the headings in it: “Installing on Linux” beats “Notes”.')),
      li(p(b('Keep headings in order:'), ' Heading 2 for sections, Heading 3 inside them. The list nests the way the headings do.')),
      li(p(b('Leave it off short pages.'), ' With three headings on one screen, it only pushes the content down.')),
    ),

    h(2, 'Related elements'),
    ul(
      li(p(pageLink('Headings'), ' are what a table of contents lists.')),
      li(p(pageLink('Link'), ' can point at one heading on a page, from anywhere.')),
      li(p(pageLink('Page tree'), ' lists pages, as a table of contents lists sections.')),
    ),
  ))

  // =============================================================== Excerpt
  await page('Excerpt', elements, doc(
    excerpt(p('An excerpt marks part of a page as its summary: the few lines other pages can show with an Excerpt include block, always up to date.')),
    p('Write the summary once, on the page it belongs to, and it appears wherever it is included. Change it there and every page that includes it changes too. Common uses: the one-paragraph summary of each project, shown on a page listing them all; a product description shown on the sales, support and training pages. The first paragraph of this page is an excerpt: the line down its left side marks it.'),

    h(2, 'Insert it'),
    table([
      ['Type this', 'To get'],
      ['/excerpt', 'The paragraph you are in, marked as the excerpt'],
    ], [200, 500]),
    p('Type the command at the end of a paragraph, after a space, and press ', b('Enter'), '. Or choose ', b('+'), ' on the toolbar, then ', b('Excerpt'), '. If you select several paragraphs first, choosing it from the ', b('+'), ' menu marks them all.'),

    h(2, 'What an excerpt can hold'),
    h(3, 'A paragraph'),
    p('Most excerpts are one paragraph, like the one at the top of this page. Write it so it makes sense on its own, since readers of other pages see it without the rest.'),
    h(3, 'Several blocks'),
    p('An excerpt can hold anything a page can: paragraphs, a list, a table, a picture. This one holds a paragraph and a list:'),
    excerpt(
      p('Kestrel Sync 2 ships on October 14, 2026, with three changes:'),
      ul('Offline editing that merges cleanly.', 'Shared folders for a whole team.', 'A first sync three times faster.'),
    ),
    panel('note', p(b('Only the first excerpt on a page is included elsewhere.'), ' The one just above is the second on this page, so no other page can include it. It is here only to show what an excerpt can hold.')),

    h(2, 'Seen from another page'),
    p('This is an ', pageLink('Excerpt include'), ' block pointing at the ', pageLink('Page properties'), ' page. What it shows is the excerpt from that page, fetched as you read this one:'),
    live('excerpt-include', { page: ids['Page properties'] }),

    h(2, 'Changing and removing it'),
    p('Edit the text inside an excerpt as you would any text. With the cursor in it, a bar appears above it with ', b('Remove excerpt'), ', which takes the mark away and keeps the text. Pages that include it show the change once you publish.'),

    h(2, 'Good practice'),
    ul(
      li(p(b('Put it at the top'), ' of the page, as its opening summary.')),
      li(p(b('No “as described below”.'), ' On another page there is no below.')),
      li(p(b('Keep it short.'), ' An excerpt is a summary; for a whole page, use ', pageLink('Include page'), '.')),
    ),

    h(2, 'Related elements'),
    ul(
      li(p(pageLink('Excerpt include'), ' shows another page’s excerpt.')),
      li(p(pageLink('Include page'), ' shows the whole of another page.')),
      li(p(pageLink('Page properties'), ' marks facts about a page for a report to collect.')),
    ),
  ))

  // ======================================================= Page properties
  // As /properties makes it (excerptExtension.insertPageProperties): no
  // header row, which a report would read as a property called "Property".
  const properties = (rows) => ({ type: 'pageProperties', content: [{
    type: 'table',
    content: rows.map((row) => ({ type: 'tableRow', content: row.map((v, k) => lib.cell(false, v, k === 0 ? 160 : 320)) })),
  }] })
  await page('Page properties', elements, doc(
    excerpt(p('Page properties is a small two-column table of facts about a page, such as its status and owner, that a Page properties report can collect from many pages into one table.')),
    p('On its own it is a tidy summary at the top of a page. Its real use is the report: give every project page, or meeting page, or incident page, the same properties and the same label, and a ', pageLink('Page properties report'), ' elsewhere lists them all, one row per page, one column per property. Nobody has to keep that list up to date: it reads the pages each time.'),

    h(2, 'Insert it'),
    table([
      ['Type this', 'To get'],
      ['/properties', 'A page properties table with Status and Owner rows'],
    ], [200, 500]),
    insertNote('Page properties', 'If you had text selected, the table takes its place.'),
    p('Fill in the second column, and add rows for any other facts you want.'),

    h(2, 'What it can hold'),
    p('The first column names each property; the second holds its value. A value can be plain words, or a status, a person or a date. This is the page properties of this page, as a project page would have it:'),
    properties([
      ['Status', p(status('In progress', 'blue'))],
      ['Owner', p(person('Priya Natarajan'))],
      ['Due', p(date('2026-10-14'))],
      ['Team', 'Launch'],
    ]),
    p('Keep the names the same on every page a report collects, such as always ', i('Owner'), ' and never sometimes ', i('Lead'), ': each different name becomes a column of its own.'),
    panel('note', p(b('A report shows each value as plain text.'), ' A person comes through as their @name. A status or a date has no text of its own, so in the report it comes out empty; if the report should show it, type it as words.')),
    panel('info', p(b('One per page.'), ' A report reads only the first page properties on a page, and only its first two columns.')),

    h(2, 'Changing it'),
    p('It is an ordinary table, changed with the table controls: type in the cells, press ', b('Tab'), ' to move to the next one (in the last cell, ', b('Tab'), ' adds a row), and use the ', b('+'), ' between rows and the ', b('×'), ' on a row to add and delete rows. A row with nothing in its first column is left out of reports.'),

    h(2, 'Good practice'),
    ul(
      li(p(b('Put it at the top'), ' of the page, where readers look for the facts.')),
      li(p(b('Agree on the properties'), ' before the first few pages: a report is only as tidy as the pages it reads.')),
      li(p(b('Make it part of a template,'), ' so every new page of that kind starts with the same properties. See ', pageLink('Templates'), '.')),
    ),

    h(2, 'Related elements'),
    ul(
      li(p(pageLink('Page properties report'), ' collects page properties from many pages.')),
      li(p(pageLink('Status'), ', ', pageLink('Mention'), ' and ', pageLink('Date'), ' make good values.')),
      li(p(pageLink('Excerpt'), ' marks a summary of a page for other pages to show.')),
    ),
  ))
}

// The first version's pictures of these pages (manual-editor.mjs's figure of
// each element on its Demo page): no longer shown, so taken down.
const slug = (t) => t.toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '')

export async function cleanup({ author }) {
  const space = await author.call('GET', '/api/spaces/SUPPORT')
  const tree = await author.call('GET', `/api/pages/tree?spaceId=${space.id}`)
  const find = (nodes, title) => {
    for (const n of nodes) {
      if (n.title === title) return n
      const hit = find(n.children ?? [], title)
      if (hit) return hit
    }
    return null
  }
  const elements = find(tree, 'Elements')
  if (!elements) return
  for (const title of TITLES) {
    const node = (elements.children ?? []).find((n) => n.title === title)
    if (!node) continue
    const retired = [`el-${slug(title)}.png`, `el-${slug(title)}.phone.png`]
    for (const a of await author.call('GET', `/api/pages/${node.id}/attachments`)) {
      if (retired.includes(a.filename)) {
        await author.call('DELETE', `/api/attachments/${a.id}`)
        console.log(`  - ${title}: ${a.filename}`)
      }
    }
  }
}
