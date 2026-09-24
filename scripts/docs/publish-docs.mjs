// Writes the Docs space (dev-plan 10.5 step 6), the pages of Tesria's
// docs. Run it through publish-docs.sh.
//
// DOCS_SHOTS=name,name retakes only the named pictures.
//
// Each section lives in scripts/docs/sections/<name>.mjs and exports:
//
//   shots     harness shots (scripts/screenshots, see shot.mjs), taken from
//             the Tesria Demo space. Every shot is taken twice, on a desktop
//             and on a phone, because the owner asked for both on every page.
//   build     writes the section's pages, given the helpers below.
//
// The wiki is the source of truth (the owner, 2026-09-21); this script is how
// its pages were written, kept so they can be rewritten after a redesign. The
// committed copy is the space exported as a wiki pack at the end of 10.5.

import { createHash } from 'node:crypto'
import { spawnSync } from 'node:child_process'
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'
import * as lib from '../lib/tesria.mjs'

const HERE = dirname(fileURLToPath(import.meta.url))
const ROOT = join(HERE, '..', '..')
// Docs, not Support (the owner, 2026-09-24): the exported site lives at
// tesria.com/docs, and tesria.com/support is where people support Tesria.
const SPACE = { key: 'DOCS', name: 'Docs', description: 'How to install, use and run Tesria.' }

/** The top of the tree, in this order: the approved outline (dev-plan 10.5). */
const TOP = [
  'Welcome to Tesria', 'Features', 'Getting started', 'Installation and operations', 'User manual', 'Administration',
  'Troubleshooting', 'FAQ', 'Glossary', 'Release notes', 'Security', 'License and credits',
  // Below the user sections (dev-plan 17): REST API and MCP live under it.
  'Developers',
]
const SECTIONS = ['welcome', 'features', 'getting-started', 'installation', 'manual-basics', 'manual-spaces', 'manual-editor', 'editor-elements-1', 'editor-elements-2', 'editor-live', 'manual-together', 'manual-mobile', 'administration', 'reference', 'api', 'developers']

/**
 * Which version each page describes (dev-plan 16.2), committed so it
 * survives a reset: `appliesTo` (the release the page was last written
 * for), `updated` (the day its words last changed), `note` (what changed)
 * and `hash` (of the words, without the table, pictures' ids aside).
 * "Applies to" moves only when a section says so with `since`, because a
 * page fixed for a typo in 0.9 still describes 0.5 and later.
 */
const VERSIONS_FILE = join(HERE, 'page-versions.json')
const versions = existsSync(VERSIONS_FILE) ? JSON.parse(readFileSync(VERSIONS_FILE, 'utf8')) : {}
const RELEASE = JSON.parse(readFileSync(join(ROOT, 'src/web/package.json'), 'utf8')).version.split('.').slice(0, 2).join('.')
const saveVersions = () => writeFileSync(VERSIONS_FILE,
  JSON.stringify(Object.fromEntries(Object.entries(versions).sort(([a], [b]) => a.localeCompare(b))), null, 2) + '\n')

/** The words of a page, as a hash: a retaken picture gets a new id, which is not a change to the page. */
function wordsHash(content) {
  const json = JSON.stringify(lib.canonical(content), (key, value) =>
    key === 'attachmentId' || (key === 'src' && typeof value === 'string' && value.startsWith('/api/attachments/')) ? undefined : value)
  return createHash('sha256').update(json).digest('hex').slice(0, 16)
}

const firstCellText = (node) => node?.content?.[0]?.content?.[0]?.content?.[0]?.content?.[0]?.text
const isVersionTable = (node) => node?.type === 'table' && firstCellText(node) === 'Applies to'

/** A page's content without the version table the publisher added. */
function withoutVersionTable(content) {
  const nodes = [...(content?.content ?? [])]
  if (!isVersionTable(nodes.at(-1))) return content
  nodes.pop()
  if (nodes.at(-1)?.type === 'horizontalRule') nodes.pop()
  return { ...content, content: nodes }
}

const usDate = (iso) => new Date(`${iso}T12:00:00Z`).toLocaleDateString('en-US', { month: 'long', day: 'numeric', year: 'numeric', timeZone: 'UTC' })

/** The small table at the end of every page. */
function versionTable(entry) {
  const row = (label, value) => ({ type: 'tableRow', content: [lib.cell(true, label, 110), lib.cell(false, value)] })
  return [lib.hr(), {
    type: 'table',
    content: [
      row('Applies to', `Tesria ${entry.appliesTo} and later`),
      // Short labels, so the first column stays at the 7rem a phone gives
      // every table column (index.css) and the values have the rest.
      row('Updated', usDate(entry.updated)),
      row('Changes', entry.note),
    ],
  }]
}

const args = process.argv.slice(2)
const shoot = !args.includes('--no-shoot')
const wanted = args.filter((a) => !a.startsWith('--'))

/** Runs the harness over a section's shots, once for a desktop and once for a phone. */
function takeShots(section, all) {
  // DOCS_SHOTS=name,name retakes only those pictures; the rest are kept
  // as they were, so one changed screen does not mean a whole section.
  const only = process.env.DOCS_SHOTS?.split(',').map((n) => n.trim()).filter(Boolean)
  const shots = only ? all.filter((s) => only.includes(s.name)) : all
  const dir = join(HERE, 'shots', section)
  mkdirSync(dir, { recursive: true })
  // Windows sized for the page, not for a monitor: a full-height phone
  // beside a desktop picture made every figure a tall column with a small
  // picture next to it (found on the first run, 2026-09-23).
  // A whole window is taken small, like a laptop, because it is shown at the
  // page's full width, where 1024 pixels still read at about 70%; a close-up
  // of one element keeps the larger window it was framed in.
  const cropped = (s) => Boolean(s.clipTo || s.clip)
  // `desktop: false` marks a picture only a phone has (the phone chapter).
  const desktop = { shots: shots.filter((s) => s.desktop !== false).map(({ phone: _phone, desktop: _d, ...s }) => ({ viewport: cropped(s) ? { width: 1280, height: 720 } : { width: 1024, height: 640 }, ...s })) }
  const phone = {
    shots: shots.filter((s) => s.phone !== false)
      .map(({ phone: overrides, viewport: _v, desktop: _d, ...s }) => ({ ...s, viewport: { width: 390, height: 700 }, ...(overrides ?? {}) })),
  }
  for (const [kind, spec, mobile] of [['desktop', desktop, ''], ['phone', phone, '1']]) {
    if (spec.shots.length === 0) continue
    const file = join(dir, `${kind}.json`)
    writeFileSync(file, JSON.stringify(spec, null, 2))
    const run = spawnSync(join(ROOT, 'scripts/screenshots/run.sh'), [file], {
      stdio: 'inherit', env: { ...process.env, SHOT_OUT: join(dir, kind), SHOT_MOBILE: mobile, SHOT_THEME: 'light', SHOT_ACCENT: 'blue' },
    })
    if (run.status !== 0) throw new Error(`the ${kind} screenshots for ${section} failed`)
  }
}

async function main() {
  const author = await lib.signIn(lib.need('SHOT_EMAIL'), lib.need('SHOT_PASSWORD'))
  console.log(`signed in as ${author.me.displayName}`)
  // Tips are overlays: on in a screenshot, they cover what it is showing.
  await author.call('PUT', '/api/auth/me/onboarding', { tipsEnabled: false })
  // And an unread count on the bell is noise in every picture.
  await author.call('POST', '/api/notifications/read-all')
  // Every screenshot run signs in, and the sessions pile up in the profile's
  // Sessions list: hundreds of them pushed API tokens past the height Chromium
  // can capture, and three pictures came out white (2026-09-24). The example
  // account's other sessions are ended first, so the list stays short.
  await author.call('DELETE', '/api/auth/me/sessions/others')

  const s = await lib.site(author, SPACE)

  // Every page a section writes ends with its version table (dev-plan 16.2).
  // A section may pass `since` for a page about something newer than the
  // release it was first written for, and `changed` to say what changed.
  // A section may also export `changes`, { title: note }, for the pages it
  // changed for a new version, rather than threading `changed` through each
  // call.
  const writePage = s.page
  let sectionChanges = {}
  s.page = async (title, parentId, content, options = {}) => {
    const { since, changed: given, ...rest } = options
    const changed = given ?? sectionChanges[title]
    const parentTitle = parentId ? (function find(nodes) {
      for (const n of nodes) {
        if (n.id === parentId) return n.title
        const hit = find(n.children ?? [])
        if (hit) return hit
      }
      return null
    })(s.tree()) : null
    const key = parentTitle ? `${parentTitle} / ${title}` : title
    const hash = wordsHash(content)
    const before = versions[key]
    if (!before || before.hash !== hash || (since && since !== before.appliesTo) || (changed && changed !== before.note)) {
      let updated = new Date().toISOString().slice(0, 10)
      // A page seen for the first time keeps the day its words were last
      // published, if they are these words.
      if (!before) {
        const existing = lib.findChild(s.tree(), parentId, title)
        if (existing) {
          const current = await author.call('GET', `/api/pages/${existing.id}`)
          if (lib.same(withoutVersionTable(JSON.parse(current.contentJson || 'null')), content)) updated = current.updatedAt.slice(0, 10)
        }
      }
      const appliesTo = since ?? before?.appliesTo ?? RELEASE
      versions[key] = {
        appliesTo,
        updated: before?.hash === hash ? before.updated : updated,
        note: changed ?? (before ? (before.hash === hash ? before.note : 'Revised.') : `Written for Tesria ${appliesTo}.`),
        hash,
      }
      saveVersions()
    }
    return writePage(title, parentId, { ...content, content: [...content.content, ...versionTable(versions[key])] }, rest)
  }

  // Shots point at Tesria Demo pages, which are addressed by id: a section
  // names them by title and this finds them.
  const demoSpace = await author.call('GET', '/api/spaces/DEMO')
  const demoTree = await author.call('GET', `/api/pages/tree?spaceId=${demoSpace.id}`)
  const demoId = (title) => demo(title).split('/').pop()
  const demo = (title) => {
    const walk = (nodes) => {
      for (const n of nodes) {
        if (n.title === title) return n
        const found = walk(n.children ?? [])
        if (found) return found
      }
      return null
    }
    const found = walk(demoTree)
    if (!found) throw new Error(`no page called "${title}" in Tesria Demo; run scripts/demo/seed-demo.sh`)
    return `/spaces/DEMO/pages/${found.id}`
  }

  // The top level first, so the tree keeps its order whichever section runs.
  const top = {}
  for (const title of TOP) top[title] = await s.ensure(title, null)

  // Named sections run in the order given; a section outside the default
  // list runs only when it is named.
  for (const name of wanted.length ? wanted : SECTIONS) {
    if (!existsSync(join(HERE, 'sections', `${name}.mjs`))) throw new Error(`no section called ${name}`)
    const section = await import(`./sections/${name}.mjs`)
    sectionChanges = section.changes ?? {}
    console.log(`\n${name}`)
    // A section may set the stage before its pictures are taken: activity
    // that has to exist for a screen to show anything, such as notifications.
    const prepared = section.prepare ? (await section.prepare({ lib, author, demo, demoId })) ?? {} : {}
    // A prepare step may rename or move a docs page (reference.mjs did,
    // and the stale tree made a second page beside the renamed one).
    if (section.prepare) await s.refresh()
    const shots = typeof section.shots === 'function' ? section.shots({ demo, ...prepared }) : section.shots ?? []
    const specOf = Object.fromEntries(shots.map((x) => [x.name, x]))
    if (shoot && shots.length) takeShots(name, shots)

    /**
     * A screenshot as the page shows it, on a desktop and on a phone.
     *
     * A close-up (a shot cropped to one element) sits beside its phone view.
     * A whole window is too wide for that: side by side it shrank to about
     * 40% and its text could not be read (the first run, 2026-09-23). So it
     * takes the page's full width, and its phone view sits below, beside the
     * caption. Layouts stack on a phone either way. The files go on the page
     * they illustrate and are replaced when a new run takes them again.
     */
    async function figure(pageId, shot, alt, caption) {
      const read = (kind) => {
        const f = join(HERE, 'shots', name, kind, `${shot}.png`)
        return existsSync(f) ? readFileSync(f) : null
      }
      const desk = read('desktop')
      if (!desk) {
        const how = shot.startsWith('setup-') ? 'take them with scripts/docs/shoot-setup.sh' : 'run without --no-shoot'
        throw new Error(`no screenshot ${name}/${shot}; ${how}`)
      }
      const deskId = await s.attachCurrent(pageId, `${shot}.png`, desk, 'image/png')
      const phoneBytes = read('phone')
      const nodes = []
      const captionNode = caption ? lib.p(lib.text(caption, lib.italic)) : null
      if (!phoneBytes) {
        nodes.push(lib.image(deskId, alt))
        if (captionNode) nodes.push(captionNode)
        return nodes
      }
      const phoneId = await s.attachCurrent(pageId, `${shot}.phone.png`, phoneBytes, 'image/png')
      const spec = specOf[shot] ?? {}
      // A PNG's width and height sit at bytes 16 to 23 of its header.
      const wide = desk.readUInt32BE(16) / desk.readUInt32BE(20) > 2.2
      // A close-up sits beside its phone view, unless it is wide and short
      // (a row of switches, say): at 64% of the page its text came out too
      // small to read (the kill switches, 2026-09-23), so it is laid out like
      // a whole window instead.
      if ((spec.clipTo || spec.clip) && !wide) {
        nodes.push(lib.layout([64, 36],
          [lib.image(deskId, `${alt}, on a computer`)],
          [lib.image(phoneId, `${alt}, on a phone`)]))
        if (captionNode) nodes.push(captionNode)
      } else {
        nodes.push(lib.image(deskId, `${alt}, on a computer`))
        nodes.push(lib.layout([64, 36],
          [captionNode ?? lib.p(lib.text(`${alt}, and the same on a phone.`, lib.italic))],
          [lib.image(phoneId, `${alt}, on a phone`)]))
      }
      return nodes
    }

    /** A picture only a phone has: shown at its own size, beside its caption. */
    async function phoneFigure(pageId, shot, alt, caption) {
      const f = join(HERE, 'shots', name, 'phone', `${shot}.png`)
      if (!existsSync(f)) throw new Error(`no screenshot ${name}/phone/${shot}; run without --no-shoot`)
      const id = await s.attachCurrent(pageId, `${shot}.phone.png`, readFileSync(f), 'image/png')
      return [lib.layout([36, 64],
        [lib.image(id, `${alt}, on a phone`)],
        [lib.p(lib.text(caption ?? alt, lib.italic))])]
    }

    // ---- The rules from the owner's review (dev-plan 15.6, 2026-09-23) ----
    // One picture per row, never two side by side; every picture has a
    // shadow; a phone picture only where the phone is different, in its own
    // "On a phone" section. The caption is the image's own, so it moves with
    // the picture. figure() and phoneFigure() above are the first version's
    // side-by-side layouts, kept until every section is rewritten.
    const shotFile = (kind, file) => {
      const f = join(HERE, 'shots', name, kind, file)
      if (!existsSync(f)) throw new Error(`no screenshot ${name}/${kind}/${file}; run without --no-shoot`)
      return readFileSync(f)
    }

    /**
     * A desktop picture on its own row, shown at its own size: a close-up
     * taken in a narrow window is not stretched across the column, so its
     * text is about the size of the page's. On a phone a resized picture
     * fills the column (index.css), which is about the width it was taken at.
     */
    async function picture(pageId, shot, alt, caption, { width } = {}) {
      const bytes = shotFile('desktop', `${shot}.png`)
      const id = await s.attachCurrent(pageId, `${shot}.png`, bytes, 'image/png')
      // Desktop pictures are taken at 2x; the docs page's column is about
      // 720 CSS pixels wide.
      const cssWidth = bytes.readUInt32BE(16) / 2
      const fit = width ?? (cssWidth < 690 ? Math.round((cssWidth / 720) * 100) : undefined)
      return [lib.image(id, alt, { shadow: true, ...(caption ? { caption } : {}), ...(fit ? { width: fit } : {}) })]
    }

    /** A phone picture on its own row, at about the size of a phone held at arm's length. */
    async function phonePicture(pageId, shot, alt, caption, { width = 42 } = {}) {
      const id = await s.attachCurrent(pageId, `${shot}.phone.png`, shotFile('phone', `${shot}.png`), 'image/png')
      return [lib.image(id, alt, { shadow: true, width, ...(caption ? { caption } : {}) })]
    }

    /** A recording, playing as an animation: silent, looping, starting by itself. */
    async function animation(pageId, shot, caption) {
      const id = await s.attachCurrent(pageId, `${shot}.webm`, shotFile('desktop', `${shot}.light.webm`), 'video/webm')
      return [lib.fileBlock(id, 'animation'), ...(caption ? [lib.p(lib.text(caption, lib.italic))] : [])]
    }

    /**
     * A link to another docs page, by its title, with the title (or
     * `label`) as its text. An exported site turns it into a link between
     * its own files. A page that does not exist yet, on a first run before
     * its section has been written, is bold text instead, with a warning.
     */
    function pageLink(title, label) {
      const walk = (nodes) => {
        for (const n of nodes) {
          if (n.title === title) return n.id
          const hit = walk(n.children ?? [])
          if (hit) return hit
        }
        return null
      }
      const id = walk(s.tree())
      if (!id) {
        console.warn(`  (no page "${title}" to link to yet; bold text instead)`)
        return lib.text(label ?? title, lib.bold)
      }
      return lib.text(label ?? title, { type: 'link', attrs: { href: `/spaces/${SPACE.key}/pages/${id}` } })
    }

    // How to get somewhere, said in full every time (the owner, 2026-09-24:
    // pages named a screen and assumed the reader knew where it was).
    // Spread into a paragraph: p('Grant it in ', ...adminAt('Roles'), '.').
    const strong = (t) => lib.text(t, lib.bold)
    const adminAt = (tab) => [strong('Admin'), ', ', strong(tab), ' (', strong('Admin'), ' is in the top bar; in a narrower window it is under ', strong('More'), ', and on a phone in the ', strong('☰'), ' menu)']
    const profileAt = (card) => ['your profile (your picture or initials at the top right of any page)',
      ...(card ? [' and scroll to its ', strong(card), ' card'] : [])]

    await section.build({ ...lib, ...s, top, figure, phoneFigure, picture, phonePicture, animation, pageLink, adminAt, profileAt, ...prepared })
    if (section.cleanup) await section.cleanup({ lib, author, ...prepared })
  }
  // The tree is numbered (dev-plan 15.8, the owner's choice over emoji):
  // the numbers are drawn from the tree's order, never stored in a title.
  if (s.space.treeStyle !== 1) {
    await author.call('PUT', `/api/spaces/${SPACE.key}`, { name: s.space.name, description: s.space.description, treeStyle: 1 })
    console.log('  numbered the page tree')
  }
  // The section emoji of 15.7 were replaced by the numbers; take any off.
  const tree = await author.call('GET', `/api/pages/tree?spaceId=${s.space.id}`)
  const walk = async (nodes) => {
    for (const n of nodes) {
      if (n.emoji) {
        await author.call('PUT', `/api/pages/${n.id}/emoji`, { emoji: null })
        console.log(`  removed ${n.emoji} from ${n.title}`)
      }
      await walk(n.children ?? [])
    }
  }
  await walk(tree)
  console.log('\ndone')
}

main().catch((err) => {
  console.error(err.message)
  process.exit(1)
})
