// Seeds the Tesria Demo space (dev-plan 10.5 step 5). Run it through
// seed-demo.sh, which supplies the sign-ins and trusts the local certificate.
//
// What it builds, and why each part exists:
//
//   Element gallery   one clean page per editor element, matching the
//                     Support space's Elements pages one for one, so every
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
import { deflateSync, crc32 } from 'node:zlib'

const BASE = (process.env.BASE || 'https://localhost').replace(/\/$/, '')
const SPACE = { key: 'DEMO', name: 'Tesria Demo', description: 'The pages the Tesria support site is illustrated from. Everything here is fictional.' }

// ---------------------------------------------------------------- sessions

function need(name) {
  const v = process.env[name]
  if (!v) throw new Error(`${name} is not set; add it to .debug-credentials`)
  return v
}

async function signIn(email, password) {
  let cookie = ''
  const remember = (res) => {
    const set = res.headers.getSetCookie?.() ?? []
    const jar = new Map(cookie ? cookie.split('; ').map((c) => c.split(/=(.*)/s).slice(0, 2)) : [])
    for (const line of set) {
      const [pair] = line.split(';')
      const [k, v] = pair.split(/=(.*)/s)
      jar.set(k, v)
    }
    cookie = [...jar].map(([k, v]) => `${k}=${v}`).join('; ')
  }
  const call = async (method, path, body, raw = false) => {
    const headers = { 'X-Requested-With': 'Tesria', ...(cookie ? { Cookie: cookie } : {}) }
    let payload
    if (body instanceof FormData) payload = body
    else if (body !== undefined) { headers['Content-Type'] = 'application/json'; payload = JSON.stringify(body) }
    const res = await fetch(BASE + path, { method, headers, body: payload })
    remember(res)
    if (raw) return res
    const text = await res.text()
    const data = text ? JSON.parse(text) : null
    if (!res.ok) {
      const err = new Error(`${method} ${path} -> ${res.status} ${text.slice(0, 300)}`)
      err.status = res.status
      throw err
    }
    return data
  }
  let who
  try { who = await call('POST', '/api/auth/login', { email, password }) }
  catch (e) {
    if (e.status === 401) throw new Error(`Could not sign in as ${email}: check that address and its password in .debug-credentials`)
    throw e
  }
  if (who?.requiresTotp) throw new Error(`${email} has two-factor sign-in on; the seed cannot sign in as it`)
  const me = await call('GET', '/api/auth/me')
  return { call, me }
}

// --------------------------------------------------------- document builders

const text = (t, ...marks) => ({ type: 'text', text: t, ...(marks.length ? { marks } : {}) })
const bold = { type: 'bold' }
const italic = { type: 'italic' }
const code = { type: 'code' }
const link = (href) => ({ type: 'link', attrs: { href, target: '_blank', rel: 'noopener noreferrer nofollow' } })
const p = (...content) => ({ type: 'paragraph', content: content.map((c) => (typeof c === 'string' ? text(c) : c)).filter((c) => c.text !== '' || c.type !== 'text') })
const h = (level, t) => ({ type: 'heading', attrs: { level }, content: [text(t)] })
const doc = (...content) => ({ type: 'doc', content })
const li = (...content) => ({ type: 'listItem', content: content.map((c) => (typeof c === 'string' ? p(c) : c)) })
const ul = (...items) => ({ type: 'bulletList', content: items.map((i) => (typeof i === 'string' ? li(i) : i)) })
const ol = (...items) => ({ type: 'orderedList', attrs: { start: 1 }, content: items.map((i) => (typeof i === 'string' ? li(i) : i)) })
const task = (checked, ...content) => ({ type: 'taskItem', attrs: { checked }, content: [p(...content)] })
const tasks = (...items) => ({ type: 'taskList', content: items })
const panel = (panelType, ...content) => ({ type: 'panel', attrs: { panelType }, content })
const expand = (title, ...content) => ({ type: 'expand', attrs: { title }, content })
const decision = (...content) => ({ type: 'decision', content: [p(...content)] })
const quote = (...content) => ({ type: 'blockquote', content: [p(...content)] })
const hr = () => ({ type: 'horizontalRule' })
const codeBlock = (language, source) => ({ type: 'codeBlock', attrs: { language }, content: [text(source)] })
const status = (t, color) => ({ type: 'status', attrs: { text: t, color } })
const date = (iso) => ({ type: 'date', attrs: { date: iso } })
const maths = (latex, display) => ({ type: 'math', attrs: { latex, display } })
const toc = () => ({ type: 'tableOfContents' })
const excerpt = (...content) => ({ type: 'excerpt', content })
const smartLink = (url, display = 'card') => ({ type: 'smartLink', attrs: { url, display } })
const embed = (url) => ({ type: 'embed', attrs: { url } })
const chart = (chartType, title, source = 1) => ({ type: 'chart', attrs: { chartType, title, source } })
const live = (kind, params = {}) => ({ type: 'dynamicBlock', attrs: { kind, params } })
const image = (attachmentId, alt, extra = {}) => ({ type: 'image', attrs: { src: `/api/attachments/${attachmentId}/download`, alt, ...extra } })
const gallery = (...images) => ({ type: 'gallery', content: images })
const fileBlock = (attachmentId, playback = 'player') => ({ type: 'attachmentBlock', attrs: { attachmentId, playback } })
const layout = (widths, ...columns) => ({
  type: 'layoutSection',
  content: columns.map((content, i) => ({ type: 'layoutColumn', attrs: { width: widths[i] }, content })),
})
const cell = (header, content, width) => ({
  type: header ? 'tableHeader' : 'tableCell',
  attrs: { colspan: 1, rowspan: 1, colwidth: width ? [width] : null },
  content: [typeof content === 'string' ? p(content) : content],
})
const table = (rows, widths = []) => ({
  type: 'table',
  content: rows.map((row, r) => ({ type: 'tableRow', content: row.map((c, i) => cell(r === 0, c, widths[i])) })),
})
const pageProperties = (rows) => ({ type: 'pageProperties', content: [table(rows, [160, 320])] })

// ------------------------------------------------------------------ images

/** A PNG from a pixel function, with no dependencies: rows, filter 0, deflate. */
function png(width, height, pixel) {
  const raw = Buffer.alloc((width * 4 + 1) * height)
  let o = 0
  for (let y = 0; y < height; y++) {
    raw[o++] = 0
    for (let x = 0; x < width; x++) {
      const [r, g, b] = pixel(x, y)
      raw[o++] = r; raw[o++] = g; raw[o++] = b; raw[o++] = 255
    }
  }
  const chunk = (type, data) => {
    const len = Buffer.alloc(4); len.writeUInt32BE(data.length)
    const body = Buffer.concat([Buffer.from(type), data])
    const crc = Buffer.alloc(4); crc.writeUInt32BE(crc32(body) >>> 0)
    return Buffer.concat([len, body, crc])
  }
  const ihdr = Buffer.alloc(13)
  ihdr.writeUInt32BE(width, 0); ihdr.writeUInt32BE(height, 4)
  ihdr[8] = 8; ihdr[9] = 6; ihdr[10] = 0; ihdr[11] = 0; ihdr[12] = 0
  return Buffer.concat([
    Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
    chunk('IHDR', ihdr), chunk('IDAT', deflateSync(raw, { level: 9 })), chunk('IEND', Buffer.alloc(0)),
  ])
}

const mix = (a, b, t) => a.map((v, i) => Math.round(v + (b[i] - v) * t))

/** A simple landscape: a sky, a sun, two ranges of hills. Four times of day. */
function landscape(theme) {
  const W = 960, H = 600
  const t = {
    dawn: { sky: [[255, 214, 170], [255, 158, 135]], sun: [255, 244, 214], far: [196, 128, 150], near: [120, 86, 120], sunY: 0.62 },
    day: { sky: [[120, 190, 255], [205, 232, 255]], sun: [255, 250, 220], far: [118, 170, 140], near: [70, 128, 96], sunY: 0.22 },
    dusk: { sky: [[64, 56, 120], [240, 128, 96]], sun: [255, 200, 150], far: [92, 72, 118], near: [52, 42, 80], sunY: 0.58 },
    night: { sky: [[14, 20, 48], [40, 52, 96]], sun: [236, 236, 220], far: [34, 44, 74], near: [20, 26, 48], sunY: 0.2 },
  }[theme]
  const sunX = W * 0.7, sunY = H * t.sunY, sunR = 58
  const ridge = (x, a, b, c, base) => base + Math.sin(x / a) * b + Math.sin(x / c + 1.3) * b * 0.6
  return png(W, H, (x, y) => {
    let c = mix(t.sky[0], t.sky[1], y / H)
    const d = Math.hypot(x - sunX, y - sunY)
    if (d < sunR) c = t.sun
    else if (d < sunR * 2.2) c = mix(t.sun, c, (d - sunR) / (sunR * 1.2))
    if (theme === 'night' && ((x * 7919 + y * 104729) % 9973) < 3 && y < H * 0.55) c = [255, 255, 240]
    if (y > ridge(x, 90, 26, 37, H * 0.62)) c = t.far
    if (y > ridge(x, 140, 34, 53, H * 0.76)) c = t.near
    return c
  })
}

/** A one-page PDF with a title and three lines, written out by hand. */
function handoutPdf() {
  const lines = [
    ['F2', 20, 'Kestrel Sync 2: launch handout'],
    ['F1', 12, 'What is new: offline editing, shared folders and a faster first sync.'],
    ['F1', 12, 'Available to every customer on October 14, 2026.'],
    ['F1', 12, 'Questions go to the support team, not to engineering.'],
  ]
  const stream = lines.map(([f, size, s], i) => `BT /${f} ${size} Tf 72 ${720 - i * 34} Td (${s.replace(/[()\\]/g, '\\$&')}) Tj ET`).join('\n')
  const objects = [
    '<< /Type /Catalog /Pages 2 0 R >>',
    '<< /Type /Pages /Kids [3 0 R] /Count 1 >>',
    '<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources << /Font << /F1 5 0 R /F2 6 0 R >> >> >>',
    `<< /Length ${Buffer.byteLength(stream)} >>\nstream\n${stream}\nendstream`,
    '<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>',
    '<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold >>',
  ]
  let out = '%PDF-1.4\n'
  const offsets = []
  objects.forEach((o, i) => { offsets.push(Buffer.byteLength(out)); out += `${i + 1} 0 obj\n${o}\nendobj\n` })
  const xref = Buffer.byteLength(out)
  out += `xref\n0 ${objects.length + 1}\n0000000000 65535 f \n`
  out += offsets.map((n) => `${String(n).padStart(10, '0')} 00000 n \n`).join('')
  out += `trailer\n<< /Size ${objects.length + 1} /Root 1 0 R >>\nstartxref\n${xref}\n%%EOF\n`
  return Buffer.from(out)
}

// ------------------------------------------------------------------ helpers

function findChild(nodes, parentId, title) {
  const walk = (list, pid) => {
    for (const n of list) {
      if (n.title === title && pid === parentId) return n
      const found = walk(n.children ?? [], n.id)
      if (found) return found
    }
    return null
  }
  return walk(nodes, null)
}

/**
 * Whether two documents are the same content. The server stores them as
 * jsonb, which reorders every object's keys, so a plain JSON.stringify of
 * what comes back never matches what was sent, and every run rewrote every
 * page. Sorting keys on both sides compares the content, not the spelling.
 */
const canonical = (v) => Array.isArray(v) ? v.map(canonical)
  : v && typeof v === 'object' ? Object.fromEntries(Object.keys(v).sort().map((k) => [k, canonical(v[k])])) : v
const same = (a, b) => JSON.stringify(canonical(a)) === JSON.stringify(canonical(b))

async function main() {
  const alex = await signIn(need('SHOT_EMAIL'), need('SHOT_PASSWORD'))
  const sam = await signIn(need('SHOT2_EMAIL'), need('SHOT2_PASSWORD'))
  console.log(`signed in as ${alex.me.displayName} and ${sam.me.displayName}`)

  // The space.
  let space
  try { space = await alex.call('GET', `/api/spaces/${SPACE.key}`) }
  catch (e) {
    if (e.status !== 404) throw e
    space = await alex.call('POST', '/api/spaces', { key: SPACE.key, name: SPACE.name, description: SPACE.description })
    console.log(`created space ${SPACE.key}`)
  }

  // People to mention, by display name, whichever exist.
  const directory = await alex.call('GET', '/api/users')
  const person = (name) => {
    const u = directory.find((d) => d.displayName === name)
    return u ? { type: 'mention', attrs: { userId: u.id, label: u.displayName } } : text(`@${name}`)
  }

  let tree = await alex.call('GET', `/api/pages/tree?spaceId=${space.id}`)
  const refresh = async () => { tree = await alex.call('GET', `/api/pages/tree?spaceId=${space.id}`) }

  /**
   * A page by title under a parent: created if missing, rewritten if its
   * content differs. `draft`, when given, is what Alex writes first and
   * Sam then brings up to `content`, so the page has two authors.
   */
  async function page(title, parentId, content, { draft, labels = [] } = {}) {
    const existing = findChild(tree, parentId, title)
    let id = existing?.id
    if (!id) {
      const created = await alex.call('POST', '/api/pages', {
        spaceId: space.id, parentPageId: parentId, title, contentJson: JSON.stringify(draft ?? content),
      })
      id = created.id
      await refresh()
      console.log(`  + ${title}`)
    }
    const current = await alex.call('GET', `/api/pages/${id}`)
    if (!same(JSON.parse(current.contentJson || 'null'), content)) {
      const author = draft ? sam : alex
      await author.call('PUT', `/api/pages/${id}`, {
        title, contentJson: JSON.stringify(content),
        changeComment: draft ? 'Updated after the review' : 'Seeded',
        baseVersion: current.currentVersionNumber,
      })
      if (existing) console.log(`  ~ ${title}`)
    }
    const have = (await alex.call('GET', `/api/pages/${id}/labels`)).map((l) => l.name)
    for (const name of labels) if (!have.includes(name)) await alex.call('POST', `/api/pages/${id}/labels`, { name })
    return id
  }

  /**
   * A page's id, creating it with a placeholder if it is missing. For pages
   * whose content refers to their own attachments: the page has to exist
   * before a file can be attached to it, and `page()` then writes the real
   * content once. Calling `page()` twice instead would flip the content
   * back and forth on every run.
   */
  async function ensure(title, parentId) {
    const existing = findChild(tree, parentId, title)
    if (existing) return existing.id
    const created = await alex.call('POST', '/api/pages', {
      spaceId: space.id, parentPageId: parentId, title, contentJson: JSON.stringify(doc({ type: 'paragraph' })),
    })
    await refresh()
    console.log(`  + ${title}`)
    return created.id
  }

  /** An attachment on a page by file name, uploaded once. */
  async function attach(pageId, filename, bytes, type) {
    const list = await alex.call('GET', `/api/pages/${pageId}/attachments`)
    const found = list.find((a) => a.filename === filename)
    if (found) return found.id
    const form = new FormData()
    form.append('file', new Blob([bytes], { type }), filename)
    const res = await alex.call('POST', `/api/pages/${pageId}/attachments`, form)
    return (Array.isArray(res) ? res[0] : res).id
  }

  /** Comments on a page that has none, so a second run adds nothing. */
  async function discuss(pageId, thread) {
    const existing = await alex.call('GET', `/api/pages/${pageId}/comments`)
    if (existing.length > 0) return
    let parent = null
    for (const [who, body] of thread) {
      const c = await who.call('POST', `/api/pages/${pageId}/comments`, { body, parentCommentId: parent, anchorJson: null })
      parent ??= c.id
    }
  }

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
    p('One page per element, each showing that element on its own. The support site’s pictures of the editor are taken here.'),
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
  await el('Math', p('Inline, as in ', maths('e^{i\\pi} + 1 = 0', false), ', or on its own line:'), p(maths('\\int_0^\\infty e^{-x^2}\\,dx = \\frac{\\sqrt{\\pi}}{2}', true)))
  await el('Chart', table([['Month', 'Sign-ups'], ['July', '120'], ['August', '180'], ['September', '260']], [200, 160]), chart('column', 'Sign-ups by month', 1))
  await el('Status', p(status('Not started', 'grey'), ' ', status('In progress', 'yellow'), ' ', status('Done', 'green'), ' ', status('Blocked', 'red'), ' ', status('In review', 'blue'), ' ', status('Idea', 'purple')))
  await el('Date', p('The launch is on ', date('2026-10-14'), '.'))
  await el('Mention', p('Thanks to ', person('Sam Okafor'), ' and ', person('Mei Chen'), ' for the review.'))
  await el('Emoji', p('Launch day \u{1F680}, a job well done \u{2705}, and cake \u{1F370}.'))
  await el('Table of contents', toc(), h(2, 'Overview'), p('What the page is about.'), h(2, 'Details'), h(3, 'First detail'), h(3, 'Second detail'), h(2, 'Next steps'))
  await el('Excerpt', excerpt(p('This paragraph is the excerpt: the part other pages can include.')), p('This paragraph is not.'))
  await el('Page properties', pageProperties([['Property', 'Value'], ['Status', p(status('In review', 'blue'))], ['Owner', p(person('Mei Chen'))]]))
  await el('Smart link', smartLink('https://example.com', 'card'))
  // An open film, so it can appear on a public support site: Big Buck Bunny,
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
