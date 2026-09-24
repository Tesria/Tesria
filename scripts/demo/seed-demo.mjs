// Seeds the Tesria Demo space (dev-plan 10.5 step 5). Run it through
// seed-demo.sh, which supplies the sign-ins and trusts the local certificate.
//
// What it builds, and why each part exists:
//
//   Element gallery   one clean page per editor element, matching the
//                     Docs space's Elements pages one for one, so every
//                     screenshot shows one element on its own.
//   Kestrel launch    a small fictional team wiki (a company launching a
//                     product), because live content needs real material:
//                     a task report needs tasks, a properties report needs
//                     page properties, contributors need two contributors.
//   Reports           every live content block, pointed at the launch.
//
// Everything is fictional: Kestrel Labs, its product and its people. The
// people are the accounts the owner created for this (Alex Rivera, Priya
// Natarajan, Sam Okafor, Mei Chen); the script mentions whichever exist.
//
// Idempotent: pages are found by title under their parent and rewritten only
// when their content differs, labels and attachments are added once, and
// comments only to a page that has none. Deleting the space and running it
// again rebuilds the same thing.
//
// Images and the PDF are drawn here rather than committed as binaries, so
// the repository holds how they were made and not just what they look like.
// The one clip is the onboarding recording already in src/web/public.

import { readFileSync } from 'node:fs'
import {
  need, signIn, site, text, bold, italic, code, link, p, h, doc, li, ul, ol, task, tasks, panel, expand,
  decision, quote, hr, codeBlock, status, date, math, toc, excerpt, smartLink, embed, chart, live, image,
  gallery, fileBlock, layout, table, pageProperties, landscape, handoutPdf,
} from '../lib/tesria.mjs'

const SPACE = { key: 'DEMO', name: 'Tesria Demo', description: 'The pages the Tesria docs are illustrated from. Everything here is fictional.' }

async function main() {
  const alex = await signIn(need('SHOT_EMAIL'), need('SHOT_PASSWORD'))
  const sam = await signIn(need('SHOT2_EMAIL'), need('SHOT2_PASSWORD'))
  console.log(`signed in as ${alex.me.displayName} and ${sam.me.displayName}`)

  // Alex writes; Sam brings a page from its draft to its content, so the
  // launch pages show two people in their history.
  const { person, page, ensure, attach, discuss } = await site(alex, SPACE, { editor: sam })

  // --------------------------------------------------------- the launch wiki
  console.log('Kestrel launch')
  const launch = await page('Kestrel Sync 2 launch', null, doc(
    p('Everything about launching Kestrel Sync 2: the plan, the meetings, the checklist and what we tell customers. Start with the ', text('Launch plan', bold), '.'),
    live('children', { depth: '2', sort: 'position' }),
  ), { labels: ['launch'] })

  const planDraft = doc(
    pageProperties([['Property', 'Value'], ['Status', p(status('Planning', 'blue'))], ['Owner', p(person('Priya Natarajan'))], ['Launch date', p(date('2026-10-14'))]]),
    excerpt(p('Kestrel Sync 2 ships on October 14, 2026, with offline editing, shared folders and a first sync three times faster than today.')),
    toc(),
    h(2, 'Goals'),
    ul('Offline editing that merges cleanly when a laptop comes back online.', 'Shared folders a whole team can see without a separate invitation each.', 'A first sync three times faster on a typical account.'),
    h(2, 'Timeline'),
    table([['Milestone', 'Date', 'State'], ['Feature freeze', p(date('2026-09-25')), p(status('Done', 'green'))], ['Release candidate', p(date('2026-10-02')), p(status('In progress', 'yellow'))], ['Launch', p(date('2026-10-14')), p(status('Not started', 'grey'))]], [220, 180, 180]),
    h(2, 'Open questions'),
    tasks(
      task(false, person('Sam Okafor'), ' confirm the migration path for accounts over 50 GB'),
      task(false, person('Mei Chen'), ' draft the pricing page copy'),
      task(true, person('Alex Rivera'), ' book the launch review'),
    ),
  )
  const plan = await page('Launch plan', launch, doc(
    pageProperties([['Property', 'Value'], ['Status', p(status('On track', 'green'))], ['Owner', p(person('Priya Natarajan'))], ['Launch date', p(date('2026-10-14'))]]),
    ...planDraft.content.slice(1, -1),
    tasks(
      task(true, person('Sam Okafor'), ' confirm the migration path for accounts over 50 GB'),
      task(false, person('Mei Chen'), ' draft the pricing page copy'),
      task(true, person('Alex Rivera'), ' book the launch review'),
    ),
  ), { draft: planDraft, labels: ['launch', 'plan'] })
  await discuss(plan, [
    [sam, 'Migration for large accounts is confirmed: they move in the background over the first week, and nothing is unavailable while it happens.'],
    [alex, 'Thanks, Sam. I have moved the status to On track.'],
  ])

  const meetings = await page('Meeting notes', launch, doc(
    p('One page per meeting, newest first. Each ends with its decisions and actions.'),
    live('children', { depth: '1', sort: 'title' }),
  ), { labels: ['meeting-notes'] })

  const meeting = (title, iso, attendees, notes, decided, actions) => page(title, meetings, doc(
    pageProperties([['Property', 'Value'], ['Date', p(date(iso))], ['Attendees', p(...attendees.flatMap((a, i) => (i ? [text(', '), person(a)] : [person(a)])))], ['Status', p(status('Final', 'green'))]]),
    h(2, 'Notes'),
    ul(...notes),
    h(2, 'Decisions'),
    ...decided.map((d) => decision(d)),
    h(2, 'Actions'),
    tasks(...actions.map(([done, who, what]) => task(done, person(who), ` ${what}`))),
  ), { labels: ['meeting-notes', 'launch'] })

  await meeting('Kickoff, September 2', '2026-09-02', ['Alex Rivera', 'Priya Natarajan', 'Sam Okafor', 'Mei Chen'],
    ['Scope agreed: offline editing, shared folders, faster first sync.', 'Mobile apps follow in November and are not part of this launch.'],
    ['Launch on October 14, with a release candidate by October 2.'],
    [[true, 'Priya Natarajan', 'write the launch plan'], [true, 'Sam Okafor', 'size the migration work']])
  await meeting('Design review, September 9', '2026-09-09', ['Priya Natarajan', 'Mei Chen', 'Sam Okafor'],
    ['The conflict screen shows both versions side by side rather than asking a question.', 'Shared folders get their own icon in the sidebar.'],
    ['Conflicts are resolved by showing both versions, never by guessing.'],
    [[false, 'Mei Chen', 'update the conflict screen mock-ups'], [true, 'Sam Okafor', 'prototype the shared folder sidebar']])
  await meeting('Go or no-go, September 16', '2026-09-16', ['Alex Rivera', 'Priya Natarajan', 'Sam Okafor'],
    ['Crash rate on the beta is below the target for the second week running.', 'Support has the FAQ draft.'],
    ['Go, provided the migration dry run on September 30 passes.'],
    [[false, 'Alex Rivera', 'schedule the migration dry run'], [false, 'Priya Natarajan', 'brief the support team']])

  const checklistDraft = doc(
    p('Everything that has to be true on launch morning. Tick it off as it happens.'),
    tasks(
      task(false, 'Release candidate signed off by ', person('Sam Okafor')),
      task(false, 'Migration dry run passed'),
      task(false, 'Pricing page live, owned by ', person('Mei Chen')),
      task(false, 'Support FAQ published'),
      task(false, 'Status page updated'),
    ),
  )
  const checklist = await page('Release checklist', launch, doc(
    p('Everything that has to be true on launch morning. Tick it off as it happens.'),
    tasks(
      task(true, 'Release candidate signed off by ', person('Sam Okafor')),
      task(true, 'Migration dry run passed'),
      task(false, 'Pricing page live, owned by ', person('Mei Chen')),
      task(false, 'Support FAQ published'),
      task(false, 'Status page updated'),
    ),
  ), { draft: checklistDraft, labels: ['launch'] })
  await discuss(checklist, [[sam, 'The dry run passed at 09:40 with no errors. Ticking it off.']])

  await page('Architecture overview', launch, doc(
    p('How Kestrel Sync 2 moves a change from a laptop to everyone else.'),
    { type: 'codeBlock', attrs: { language: 'mermaid' }, content: [text('flowchart LR\n  A[Laptop] -->|change| B(Sync service)\n  B --> C[(Storage)]\n  B -->|notify| D[Other devices]\n  D -->|fetch| C')] },
    layout([50, 50],
      [h(3, 'Offline edits'), p('Each change is kept locally with the version it was made against, and replayed in order when the device reconnects.')],
      [h(3, 'Conflicts'), p('When two changes touch the same lines, both versions are kept and shown side by side. Nothing is merged by guessing.')]),
    codeBlock('typescript', "export interface Change {\n  id: string\n  baseVersion: number\n  ops: Operation[]\n}"),
  ), { labels: ['launch', 'engineering'] })

  await page('Support FAQ', launch, doc(
    p('Answers the support team can give as they are. Open a question to read its answer.'),
    expand('Do I have to do anything to upgrade?', p('No. Kestrel Sync updates itself, and your files stay where they are.')),
    expand('What happens to files I edit offline?', p('They are kept on your device and sent as soon as you reconnect. If someone else changed the same file, you see both versions.')),
    expand('Is there a mobile app?', p('Not in this release. The mobile apps follow in November.')),
  ), { labels: ['launch', 'support'] })

  // --------------------------------------------------------------- reports
  console.log('Reports')
  await page('Reports', null, doc(
    p('Every kind of live content, pointed at the launch. These update by themselves as the pages they read change.'),
    h(2, 'Open tasks'), live('task-report', { scope: 'space', status: 'open', assignee: 'any', limit: '25' }),
    h(2, 'Launch pages and their properties'), live('page-properties-report', { labels: 'launch', limit: '25' }),
    h(2, 'Meeting notes'), live('content-by-label', { labels: 'meeting-notes', match: 'any', scope: 'space', limit: '25' }),
    h(2, 'Recently updated'), live('recently-updated', { scope: 'space', limit: '10' }),
    h(2, 'Popular labels'), live('labels', { mode: 'popular', limit: '20' }),
  ))

  // ------------------------------------------------------ element gallery
  console.log('Element gallery')
  const gallerySpace = await page('Element gallery', null, doc(
    p('One page per element, each showing that element on its own. The docs’ pictures of the editor are taken here.'),
    live('children', { depth: '1', sort: 'position' }),
  ))
  const el = (title, ...content) => page(title, gallerySpace, doc(...content))

  await el('Normal text', p('Normal text is what you type when you start typing. It wraps, it reflows, and it takes every kind of formatting.'))
  await el('Headings', h(1, 'Heading 1'), h(2, 'Heading 2'), h(3, 'Heading 3'), h(4, 'Heading 4'), h(5, 'Heading 5'), h(6, 'Heading 6'))
  await el('Text formatting', p(text('Bold', bold), ', ', text('italic', italic), ', ', text('underline', { type: 'underline' }), ', ', text('strikethrough', { type: 'strike' }), ', ', text('inline code', code), ', H', text('2', { type: 'subscript' }), 'O and E = mc', text('2', { type: 'superscript' }), '.'))
  await el('Colors', p(text('Blue text', { type: 'textColor', attrs: { color: 'blue' } }), ', ', text('green text', { type: 'textColor', attrs: { color: 'green' } }), ', ', text('a yellow highlight', { type: 'highlight', attrs: { color: '#fff0b3' } }), ' and ', text('a light blue one', { type: 'highlight', attrs: { color: '#deebff' } }), '.'))
  await el('Blockquote', quote('The best way to find out if you can trust somebody is to trust them.'))
  await el('Divider', p('Above the line.'), hr(), p('Below the line.'))
  await el('Bullet list', ul('Offline editing', li(p('Shared folders'), ul('For a team', 'For the whole company')), 'A faster first sync'))
  await el('Ordered list', ol('Sign in', 'Choose a folder', 'Invite your team'))
  await el('Task list', tasks(task(true, 'Write the release notes'), task(false, 'Review them with ', person('Priya Natarajan')), task(false, 'Publish them')))
  await el('Link', p('Read the ', text('launch plan', link('https://example.com/launch-plan')), ' before the review.'))
  await el('Panels',
    panel('info', p('An info panel, for background worth knowing.')),
    panel('note', p('A note panel, for an aside.')),
    panel('success', p('A tip panel, for a better way to do something.')),
    panel('warning', p('A warning panel, for something to be careful of.')),
    panel('error', p('An error panel, for something that has gone wrong.')))
  await el('Expand', expand('What is in Kestrel Sync 2?', p('Offline editing, shared folders and a first sync three times faster.')))
  await el('Decision', decision('Launch on October 14, with a release candidate by October 2.'))
  await el('Layout', layout([33.33, 66.67],
    [h(3, 'Sidebar'), p('A narrow column for a summary or links.')],
    [h(3, 'Main column'), p('The wider column holds the content. Columns sit side by side on a wide screen and stack on a phone.')]))
  await el('Table', table([['Plan', 'Price', 'Storage'], ['Free', '$0', '5 GB'], ['Team', '$8 a person', '1 TB'], ['Business', '$15 a person', 'Unlimited']], [200, 160, 160]))
  await el('Code block', codeBlock('python', 'def greet(name: str) -> str:\n    return f"Hello, {name}"'))
  await el('Diagram', { type: 'codeBlock', attrs: { language: 'mermaid' }, content: [text('flowchart LR\n  Draft --> Review --> Publish')] })
  await el('Math', p('Inline, as in ', math('e^{i\\pi} + 1 = 0', false), ', or on its own line:'), p(math('\\int_0^\\infty e^{-x^2}\\,dx = \\frac{\\sqrt{\\pi}}{2}', true)))
  await el('Chart', table([['Month', 'Sign-ups'], ['July', '120'], ['August', '180'], ['September', '260']], [200, 160]), chart('column', 'Sign-ups by month', 1))
  await el('Status', p(status('Not started', 'grey'), ' ', status('In progress', 'yellow'), ' ', status('Done', 'green'), ' ', status('Blocked', 'red'), ' ', status('In review', 'blue'), ' ', status('Idea', 'purple')))
  await el('Date', p('The launch is on ', date('2026-10-14'), '.'))
  await el('Mention', p('Thanks to ', person('Sam Okafor'), ' and ', person('Mei Chen'), ' for the review.'))
  await el('Emoji', p('Launch day \u{1F680}, a job well done \u{2705}, and cake \u{1F370}.'))
  await el('Table of contents', toc(), h(2, 'Overview'), p('What the page is about.'), h(2, 'Details'), h(3, 'First detail'), h(3, 'Second detail'), h(2, 'Next steps'))
  await el('Excerpt', excerpt(p('This paragraph is the excerpt: the part other pages can include.')), p('This paragraph is not.'))
  await el('Page properties', pageProperties([['Property', 'Value'], ['Status', p(status('In review', 'blue'))], ['Owner', p(person('Mei Chen'))]]))
  await el('Smart link', smartLink('https://example.com', 'card'))
  // An open film, so it can appear on a public docs site: Big Buck Bunny,
  // (c) Blender Foundation, CC BY 3.0, from Blender's own channel. YouTube is
  // on the default embed allowlist, so no setting changes for it.
  await el('Embed',
    embed('https://www.youtube.com/watch?v=aqz-KE-bpKQ'),
    p(text('Big Buck Bunny', italic), ' \u00a9 Blender Foundation, ', text('CC BY 3.0', link('https://creativecommons.org/licenses/by/3.0/')), '.'))

  // Pages whose element holds a file: the file goes on the page first.
  const imagePage = await ensure('Image', gallerySpace)
  const imageId = await attach(imagePage, 'mountains-at-dawn.png', landscape('dawn'), 'image/png')
  await el('Image', image(imageId, 'Mountains at dawn'))

  const galleryPage = await ensure('Gallery', gallerySpace)
  const shots = []
  for (const theme of ['dawn', 'day', 'dusk', 'night']) {
    shots.push(image(await attach(galleryPage, `mountains-at-${theme}.png`, landscape(theme), 'image/png'), `Mountains at ${theme}`))
  }
  await el('Gallery', gallery(...shots))

  const filePage = await ensure('File or video', gallerySpace)
  const pdfId = await attach(filePage, 'kestrel-launch-handout.pdf', handoutPdf(), 'application/pdf')
  const clip = readFileSync(new URL('../../src/web/public/onboarding/editor-slash.light.webm', import.meta.url))
  const clipOnFile = await attach(filePage, 'slash-menu.webm', clip, 'video/webm')
  await el('File or video', fileBlock(pdfId), fileBlock(clipOnFile, 'player'))

  const animationPage = await ensure('Animation', gallerySpace)
  const clipId = await attach(animationPage, 'slash-menu.webm', clip, 'video/webm')
  await el('Animation', fileBlock(clipId, 'animation'))

  // Live content, one page each, pointed at the launch.
  const planId = plan
  // Children display lists the pages under its own page, so it gets some.
  const childrenPage = await el('Children display', p('The pages under this one:'), live('children', { depth: '1', sort: 'position' }))
  await page('Getting started with Kestrel Sync', childrenPage, doc(p('Install the app and sign in.')))
  await page('Sharing a folder', childrenPage, doc(p('Right-click a folder and choose Share.')))
  await page('Working offline', childrenPage, doc(p('Keep editing; changes are sent when you reconnect.')))
  await el('Recently updated', live('recently-updated', { scope: 'space', limit: '10' }))
  await el('Content by label', live('content-by-label', { labels: 'meeting-notes', match: 'any', scope: 'space', limit: '25' }))
  // Attachments lists its own page's files, so it gets two.
  const attachmentsPage = await ensure('Attachments', gallerySpace)
  await attach(attachmentsPage, 'kestrel-launch-handout.pdf', handoutPdf(), 'application/pdf')
  await attach(attachmentsPage, 'mountains-at-day.png', landscape('day'), 'image/png')
  await el('Attachments', p('Everything attached to this page:'), live('attachments', {}))
  await el('Change history', live('change-history', { limit: '10' }))
  await el('Contributors', live('contributors', { scope: 'page' }))
  await el('Include page', live('include-page', { page: planId }))
  await el('Excerpt include', live('excerpt-include', { page: planId }))
  await el('Page properties report', live('page-properties-report', { labels: 'meeting-notes', limit: '25' }))
  await el('Labels list', live('labels', { mode: 'popular', limit: '20' }))
  await el('Task report', live('task-report', { scope: 'space', status: 'all', assignee: 'any', limit: '25' }))
  await el('Page tree', live('page-tree', { root: 'space', depth: '2' }))

  console.log('done')
}

main().catch((err) => {
  console.error(err.message)
  process.exit(1)
})
