// Shared by the scripts that write content into a Tesria instance through
// its API: scripts/demo/seed-demo.mjs (the Tesria Demo space) and
// scripts/support/publish-support.mjs (the Support space, dev-plan 10.5).
// Signing in, the document builders, drawn images, and `site()`, which finds
// or creates pages by title so a run can be repeated safely.

import { deflateSync, crc32 } from 'node:zlib'
import { createHash } from 'node:crypto'

export const BASE = (process.env.BASE || 'https://localhost').replace(/\/$/, '')

// ---------------------------------------------------------------- sessions

export function need(name) {
  const v = process.env[name]
  if (!v) throw new Error(`${name} is not set; add it to .debug-credentials`)
  return v
}

export async function signIn(email, password) {
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
    // One retry on a network failure (not an HTTP error): after minutes of
    // taking screenshots the pooled connection can be gone, and the first
    // request after that failed with a bare "fetch failed" (2026-09-23).
    let res
    try { res = await fetch(BASE + path, { method, headers, body: payload }) }
    catch { res = await fetch(BASE + path, { method, headers, body: payload }) }
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

export const text = (t, ...marks) => ({ type: 'text', text: t, ...(marks.length ? { marks } : {}) })
export const bold = { type: 'bold' }
export const italic = { type: 'italic' }
export const code = { type: 'code' }
export const link = (href) => ({ type: 'link', attrs: { href, target: '_blank', rel: 'noopener noreferrer nofollow' } })
export const p = (...content) => ({ type: 'paragraph', content: content.map((c) => (typeof c === 'string' ? text(c) : c)).filter((c) => c.text !== '' || c.type !== 'text') })
export const h = (level, t) => ({ type: 'heading', attrs: { level }, content: [text(t)] })
export const doc = (...content) => ({ type: 'doc', content })
export const li = (...content) => ({ type: 'listItem', content: content.map((c) => (typeof c === 'string' ? p(c) : c)) })
export const ul = (...items) => ({ type: 'bulletList', content: items.map((i) => (typeof i === 'string' ? li(i) : i)) })
export const ol = (...items) => ({ type: 'orderedList', attrs: { start: 1 }, content: items.map((i) => (typeof i === 'string' ? li(i) : i)) })
export const task = (checked, ...content) => ({ type: 'taskItem', attrs: { checked }, content: [p(...content)] })
export const tasks = (...items) => ({ type: 'taskList', content: items })
export const panel = (panelType, ...content) => ({ type: 'panel', attrs: { panelType }, content })
export const expand = (title, ...content) => ({ type: 'expand', attrs: { title }, content })
export const decision = (...content) => ({ type: 'decision', content: [p(...content)] })
export const quote = (...content) => ({ type: 'blockquote', content: [p(...content)] })
export const hr = () => ({ type: 'horizontalRule' })
export const codeBlock = (language, source) => ({ type: 'codeBlock', attrs: { language }, content: [text(source)] })
export const status = (t, color) => ({ type: 'status', attrs: { text: t, color } })
export const date = (iso) => ({ type: 'date', attrs: { date: iso } })
export const math = (latex, display) => ({ type: 'math', attrs: { latex, display } })
export const toc = () => ({ type: 'tableOfContents' })
export const excerpt = (...content) => ({ type: 'excerpt', content })
export const smartLink = (url, display = 'card') => ({ type: 'smartLink', attrs: { url, display } })
export const embed = (url) => ({ type: 'embed', attrs: { url } })
export const chart = (chartType, title, source = 1) => ({ type: 'chart', attrs: { chartType, title, source } })
export const live = (kind, params = {}) => ({ type: 'dynamicBlock', attrs: { kind, params } })
export const image = (attachmentId, alt, extra = {}) => ({ type: 'image', attrs: { src: `/api/attachments/${attachmentId}/download`, alt, ...extra } })
export const gallery = (...images) => ({ type: 'gallery', content: images })
export const fileBlock = (attachmentId, playback = 'player') => ({ type: 'attachmentBlock', attrs: { attachmentId, playback } })
export const layout = (widths, ...columns) => ({
  type: 'layoutSection',
  content: columns.map((content, i) => ({ type: 'layoutColumn', attrs: { width: widths[i] }, content })),
})
export const cell = (header, content, width) => ({
  type: header ? 'tableHeader' : 'tableCell',
  attrs: { colspan: 1, rowspan: 1, colwidth: width ? [width] : null },
  content: [typeof content === 'string' ? p(content) : content],
})
export const table = (rows, widths = []) => ({
  type: 'table',
  content: rows.map((row, r) => ({ type: 'tableRow', content: row.map((c, i) => cell(r === 0, c, widths[i])) })),
})
export const pageProperties = (rows) => ({ type: 'pageProperties', content: [table(rows, [160, 320])] })

// ------------------------------------------------------------------ images

/** A PNG from a pixel function, with no dependencies: rows, filter 0, deflate. */
export function png(width, height, pixel) {
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

export const mix = (a, b, t) => a.map((v, i) => Math.round(v + (b[i] - v) * t))

/** A simple landscape: a sky, a sun, two ranges of hills. Four times of day. */
export function landscape(theme) {
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
export function handoutPdf() {
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

export function findChild(nodes, parentId, title) {
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
export const canonical = (v) => Array.isArray(v) ? v.map(canonical)
  : v && typeof v === 'object' ? Object.fromEntries(Object.keys(v).sort().map((k) => [k, canonical(v[k])])) : v
export const same = (a, b) => JSON.stringify(canonical(a)) === JSON.stringify(canonical(b))

// ---------------------------------------------------------------- site()

/**
 * Pages, files and comments in one space, found by title so that running a
 * script again changes only what differs. `author` writes; `editor`, when
 * given, is who brings a page from its `draft` to its content, so a page can
 * show two people in its history.
 */
export async function site(author, spec, { editor } = {}) {
  let space
  try { space = await author.call('GET', `/api/spaces/${spec.key}`) }
  catch (e) {
    if (e.status !== 404) throw e
    space = await author.call('POST', '/api/spaces', { key: spec.key, name: spec.name, description: spec.description })
    console.log(`created space ${spec.key}`)
  }
  // The script says what the space is called: a rename there (Support to
  // Docs, 2026-09-24) reaches an instance that already has the space.
  if (space.name !== spec.name || (space.description ?? null) !== (spec.description ?? null)) {
    space = await author.call('PUT', `/api/spaces/${spec.key}`, { name: spec.name, description: spec.description })
    console.log(`updated space ${spec.key}: ${spec.name}`)
  }

  const directory = await author.call('GET', '/api/users')
  /** A mention of someone by display name, or plain text if there is no such account. */
  const person = (name) => {
    const u = directory.find((d) => d.displayName === name)
    return u ? { type: 'mention', attrs: { userId: u.id, label: u.displayName } } : text(`@${name}`)
  }

  let tree = await author.call('GET', `/api/pages/tree?spaceId=${space.id}`)
  const refresh = async () => { tree = await author.call('GET', `/api/pages/tree?spaceId=${space.id}`) }

  /** A page by title under a parent: created if missing, rewritten if its content differs. */
  async function page(title, parentId, content, { draft, labels = [], comment } = {}) {
    const existing = findChild(tree, parentId, title)
    let id = existing?.id
    if (!id) {
      const created = await author.call('POST', '/api/pages', {
        spaceId: space.id, parentPageId: parentId, title, contentJson: JSON.stringify(draft ?? content),
      })
      id = created.id
      await refresh()
      console.log(`  + ${title}`)
    }
    const current = await author.call('GET', `/api/pages/${id}`)
    if (!same(JSON.parse(current.contentJson || 'null'), content)) {
      const writer = draft && editor ? editor : author
      await writer.call('PUT', `/api/pages/${id}`, {
        title, contentJson: JSON.stringify(content),
        changeComment: comment ?? (draft ? 'Updated after the review' : 'Updated'),
        baseVersion: current.currentVersionNumber,
      })
      if (existing) console.log(`  ~ ${title}`)
    }
    const have = (await author.call('GET', `/api/pages/${id}/labels`)).map((l) => l.name)
    for (const name of labels) if (!have.includes(name)) await author.call('POST', `/api/pages/${id}/labels`, { name })
    return id
  }

  /** A page's id, created with an empty body if missing, for pages that hold their own files. */
  async function ensure(title, parentId) {
    const existing = findChild(tree, parentId, title)
    if (existing) return existing.id
    const created = await author.call('POST', '/api/pages', {
      spaceId: space.id, parentPageId: parentId, title, contentJson: JSON.stringify(doc({ type: 'paragraph' })),
    })
    await refresh()
    console.log(`  + ${title}`)
    return created.id
  }

  async function upload(pageId, filename, bytes, type) {
    const form = new FormData()
    form.append('file', new Blob([bytes], { type }), filename)
    const res = await author.call('POST', `/api/pages/${pageId}/attachments`, form)
    return (Array.isArray(res) ? res[0] : res).id
  }

  /** An attachment on a page by file name, uploaded once. */
  async function attach(pageId, filename, bytes, type) {
    const list = await author.call('GET', `/api/pages/${pageId}/attachments`)
    const found = list.find((a) => a.filename === filename)
    if (found) return found.id
    return upload(pageId, filename, bytes, type)
  }

  /**
   * An attachment kept current: if a file of that name is there with other
   * bytes, the new one is uploaded and the old removed, so a retaken
   * screenshot replaces the stale one rather than sitting beside it.
   */
  async function attachCurrent(pageId, filename, bytes, type) {
    const list = await author.call('GET', `/api/pages/${pageId}/attachments`)
    const found = list.filter((a) => a.filename === filename)
    for (const a of found) {
      const res = await author.call('GET', `/api/attachments/${a.id}/download`, undefined, true)
      const have = Buffer.from(await res.arrayBuffer())
      if (sha(have) === sha(bytes)) return a.id
    }
    const id = await upload(pageId, filename, bytes, type)
    for (const a of found) await author.call('DELETE', `/api/attachments/${a.id}`)
    return id
  }

  /** Comments on a page that has none, so a second run adds nothing. */
  async function discuss(pageId, thread) {
    const existing = await author.call('GET', `/api/pages/${pageId}/comments`)
    if (existing.length > 0) return
    let parent = null
    for (const [who, body] of thread) {
      const c = await who.call('POST', `/api/pages/${pageId}/comments`, { body, parentCommentId: parent, anchorJson: null })
      parent ??= c.id
    }
  }

  return { space, person, page, ensure, attach, attachCurrent, discuss, refresh, tree: () => tree }
}

const sha = (b) => createHash('sha256').update(b).digest('hex')
