/**
 * Screenshot harness for the Tesria user manual.
 *
 * Runs inside a one-off container from the tesria-pdf image (which already
 * carries a Chromium matched to playwright-core) attached to the compose
 * network, so it talks to the app service directly and never goes near
 * Caddy's local CA.
 *
 * A "shot" is one entry in a JSON spec: where to go, what to wait for, what
 * to crop to, and any annotations to draw over it first. Annotations are
 * real DOM overlays drawn before the capture rather than pixels painted
 * afterwards, so circles and arrows come out as crisp as the UI under them.
 */
import { chromium, webkit, firefox } from 'playwright-core'
import { readFileSync, mkdirSync, copyFileSync, rmSync, mkdtempSync, statSync } from 'node:fs'
import path from 'node:path'
import os from 'node:os'

const BASE = process.env.BASE || 'https://tesria.localhost'
const EMAIL = process.env.EMAIL
const PASSWORD = process.env.PASSWORD
const OUT = process.env.OUT || '/out'
const specPath = process.argv[2]
const only = process.argv[3] || null

const spec = JSON.parse(readFileSync(specPath, 'utf8'))
mkdirSync(OUT, { recursive: true })

const ANNOTATE = `
(spec) => {
  document.querySelectorAll('.__ann').forEach(n => n.remove())
  const layer = document.createElement('div')
  layer.className = '__ann'
  layer.style.cssText = 'position:fixed;inset:0;z-index:2147483647;pointer-events:none'
  const svgns = 'http://www.w3.org/2000/svg'
  const svg = document.createElementNS(svgns, 'svg')
  svg.setAttribute('width', '100%'); svg.setAttribute('height', '100%')
  svg.style.cssText = 'position:absolute;inset:0;overflow:visible'
  const defs = document.createElementNS(svgns, 'defs')
  defs.innerHTML = '<marker id="__arw" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="5.5" markerHeight="5.5" orient="auto-start-reverse"><path d="M0 0 L10 5 L0 10 z" fill="#f0446e"/></marker>'
  svg.appendChild(defs)
  const COLOR = '#f0446e'

  const rectOf = (sel, nth) => {
    const els = document.querySelectorAll(sel)
    const el = els[nth || 0]
    if (!el) throw new Error('annotation target not found: ' + sel)
    const r = el.getBoundingClientRect()
    return { x: r.x, y: r.y, w: r.width, h: r.height, cx: r.x + r.width / 2, cy: r.y + r.height / 2 }
  }

  for (const a of spec) {
    if (a.type === 'circle') {
      const r = rectOf(a.target, a.nth)
      const pad = a.pad == null ? 6 : a.pad
      const e = document.createElementNS(svgns, 'ellipse')
      e.setAttribute('cx', r.cx); e.setAttribute('cy', r.cy)
      e.setAttribute('rx', r.w / 2 + pad); e.setAttribute('ry', r.h / 2 + pad)
      e.setAttribute('fill', 'none'); e.setAttribute('stroke', COLOR); e.setAttribute('stroke-width', '2.5')
      svg.appendChild(e)
    } else if (a.type === 'box') {
      const r = rectOf(a.target, a.nth)
      const pad = a.pad == null ? 4 : a.pad
      const e = document.createElementNS(svgns, 'rect')
      e.setAttribute('x', r.x - pad); e.setAttribute('y', r.y - pad)
      e.setAttribute('width', r.w + pad * 2); e.setAttribute('height', r.h + pad * 2)
      e.setAttribute('rx', '6')
      e.setAttribute('fill', 'none'); e.setAttribute('stroke', COLOR); e.setAttribute('stroke-width', '2.5')
      svg.appendChild(e)
    } else if (a.type === 'arrow') {
      const r = rectOf(a.target, a.nth)
      const dir = a.from || 'left'
      const len = a.len == null ? 70 : a.len
      const gap = a.gap == null ? 10 : a.gap
      let x2, y2, x1, y1
      if (dir === 'left')  { x2 = r.x - gap;        y2 = r.cy; x1 = x2 - len; y1 = y2 }
      if (dir === 'right') { x2 = r.x + r.w + gap;  y2 = r.cy; x1 = x2 + len; y1 = y2 }
      if (dir === 'above') { x2 = r.cx;             y2 = r.y - gap; x1 = x2; y1 = y2 - len }
      if (dir === 'below') { x2 = r.cx;             y2 = r.y + r.h + gap; x1 = x2; y1 = y2 + len }
      if (a.dx) x1 += a.dx
      if (a.dy) y1 += a.dy
      const l = document.createElementNS(svgns, 'line')
      l.setAttribute('x1', x1); l.setAttribute('y1', y1); l.setAttribute('x2', x2); l.setAttribute('y2', y2)
      l.setAttribute('stroke', COLOR); l.setAttribute('stroke-width', '2.5')
      l.setAttribute('stroke-linecap', 'round'); l.setAttribute('marker-end', 'url(#__arw)')
      svg.appendChild(l)
      if (a.label) {
        const t = document.createElement('div')
        t.textContent = a.label
        const align = (dir === 'left') ? 'right:' + (window.innerWidth - x1 + 8) + 'px;'
          : (dir === 'right') ? 'left:' + (x1 + 8) + 'px;'
          : 'left:' + (x1 + 10) + 'px;'
        t.style.cssText = 'position:absolute;' + align + 'top:' + (y1 - 11) + 'px;'
          + 'background:' + COLOR + ';color:#fff;font:600 13px/1.4 system-ui,sans-serif;'
          + 'padding:3px 8px;border-radius:5px;white-space:nowrap'
        layer.appendChild(t)
      }
    } else if (a.type === 'note') {
      const r = rectOf(a.target, a.nth)
      const t = document.createElement('div')
      t.textContent = a.label
      t.style.cssText = 'position:absolute;left:' + (r.x + (a.dx || 0)) + 'px;top:' + (r.y + (a.dy || 0)) + 'px;'
        + 'background:' + COLOR + ';color:#fff;font:600 13px/1.4 system-ui,sans-serif;'
        + 'padding:3px 8px;border-radius:5px;white-space:nowrap'
      layer.appendChild(t)
    }
  }
  layer.appendChild(svg)
  document.body.appendChild(layer)
}
`

// SHOT_BROWSER=webkit runs the same spec in Safari's engine — the image
// ships all three — which is how a layout bug that only shows on an iPhone
// gets reproduced without an iPhone. Not Safari itself (no address-bar
// collapse, no software keyboard), but the same rendering engine.
const engines = { chromium, webkit, firefox }
const engine = engines[process.env.SHOT_BROWSER || 'chromium'] || chromium
const browser = await engine.launch(engine === chromium ? { args: ['--font-render-hinting=none'] } : {})

// Uncaught errors and console errors, tagged with the shot that was running.
// A layout or routing change is checked by walking every route and reading
// this list — there are no frontend tests to catch a blank screen.
let current = '(startup)'
const problems = []
function watch(p) {
  p.on('pageerror', (e) => problems.push(`${current}: uncaught ${e.message.split('\n')[0]}`))
  p.on('console', (m) => {
    if (m.type() !== 'error') return
    const t = m.text()
    if (/Failed to load resource/.test(t)) return // 401/404 probes are normal here
    problems.push(`${current}: console ${t.slice(0, 160)}`)
  })
}
// Documentation is shot in one appearance so the pictures agree with each
// other: light theme, the default blue accent. Both are per-browser
// preferences (theme.ts writes them to localStorage), so they are seeded
// before the app's first paint rather than clicked afterwards — a click
// would leave the first screenshot of every run in whatever the previous
// one ended on. SHOT_THEME / SHOT_ACCENT override for a run that needs
// something else.
// Clips are shot at a fixed, smaller viewport than stills: the onboarding
// screens show them at a few hundred pixels wide, and every pixel is paid
// for twice over in the size budget (dev-plan 10.4).
const RECORD_VIEWPORT = { width: 1280, height: 800 }
// The video is scaled down from that viewport rather than shrinking the
// viewport itself: the layout stays the one people actually use, while the
// file pays for two thirds of the pixels. Onboarding shows these a few
// hundred pixels wide, so the detail is not missed.
const RECORD_VIDEO = { width: 864, height: 540 }
const THEME = process.env.SHOT_THEME || 'light'
const ACCENT = process.env.SHOT_ACCENT || 'blue'
const seedAppearance = `
  try {
    if (${JSON.stringify(THEME)} === 'system') localStorage.removeItem('tesria-theme')
    else localStorage.setItem('tesria-theme', ${JSON.stringify(THEME)})
    if (${JSON.stringify(ACCENT)} === 'blue') localStorage.removeItem('tesria-accent')
    else localStorage.setItem('tesria-accent', ${JSON.stringify(ACCENT)})
  } catch { /* a browser with storage blocked still renders, just at defaults */ }
`
const contextOptions = {
  viewport: { width: 1440, height: 900 },
  // Documentation stills are shot at 2x so they stay sharp when scaled. A
  // set that ships inside the app pays for that in bytes, so a spec can ask
  // for 1x (dev-plan 10.4).
  deviceScaleFactor: spec.deviceScaleFactor || 2,
  ...(process.env.SHOT_MOBILE ? { isMobile: true, hasTouch: true, deviceScaleFactor: 3, viewport: { width: 390, height: 844 } } : {}),
  // Matches THEME so that anything reading prefers-color-scheme (the editor's
  // embedded frames, a "system" preference) agrees with the seeded choice.
  colorScheme: THEME === 'dark' ? 'dark' : 'light',
  ignoreHTTPSErrors: true,
}

const ctx = await browser.newContext(contextOptions)
await ctx.addInitScript(seedAppearance)
const page = await ctx.newPage()
watch(page)

// Sign in once; every shot reuses the session.
await page.goto(BASE + '/login', { waitUntil: 'domcontentloaded' })
await page.waitForSelector('input[type="email"]', { timeout: 20000 })
await page.fill('input[type="email"]', EMAIL)
await page.fill('input[type="password"]', PASSWORD)
await Promise.all([
  page.waitForURL(u => !u.pathname.startsWith('/login'), { timeout: 20000 }),
  page.click('button[type="submit"]'),
])
console.log('signed in as', EMAIL)

// A second, signed-out context for shots of what an anonymous visitor sees.
let anonPage = null
async function pageFor(s) {
  if (!s.anon) return page
  if (!anonPage) {
    const anonCtx = await browser.newContext(contextOptions)
    await anonCtx.addInitScript(seedAppearance)
    anonPage = await anonCtx.newPage()
    watch(anonPage)
  }
  return anonPage
}

/**
 * One step of a shot. Shared by stills and recordings so a clip and the
 * screenshot beside it are produced by the same instructions.
 */
async function runSteps(pg, steps, name) {
  for (const step of steps || []) {
    if (step.click) await pg.click(step.click)
    if (step.type) await pg.fill(step.selector, step.type)
    if (step.press) await pg.press(step.selector || 'body', step.press)
    if (step.keys) await pg.keyboard.type(step.keys, { delay: 12 })
    // Typing that reads as typing (dev-plan 10.4). A clip of someone using
    // the editor is unwatchable at fill() speed and unreadable at 12ms.
    if (step.typeSlowly) {
      if (step.selector) await pg.click(step.selector)
      await pg.keyboard.type(step.typeSlowly, { delay: step.delay == null ? 55 : step.delay })
    }
    // A recording has no visible cursor, so movement is conveyed by what
    // lights up on the way. Moving in steps lets hover states actually fire.
    if (step.moveTo) {
      const el = await pg.locator(step.moveTo).first().boundingBox()
      if (el) await pg.mouse.move(el.x + el.width / 2, el.y + el.height / 2, { steps: step.moveSteps || 18 })
    }
    if (step.css) await pg.addStyleTag({ content: step.css })
    if (step.hover) await pg.hover(step.hover)
    // A real touch tap (needs SHOT_MOBILE, which turns on hasTouch).
    if (step.tap) await pg.tap(step.tap)
    // Evaluate an expression and print its result: DOM facts beside the picture.
    if (step.probe) console.log('PROBE', name, step.label || '', JSON.stringify(await pg.evaluate(step.probe)))
    if (step.tripleClick) await pg.click(step.tripleClick, { clickCount: 3 })
    // A drag slow enough to see. dragAndDrop() jumps; the page tree's own
    // drop indicator is half of what the clip is showing (dev-plan 10.4).
    if (step.dragTo) {
      const from = await pg.locator(step.dragTo.from).first().boundingBox()
      const to = await pg.locator(step.dragTo.to).first().boundingBox()
      if (from && to) {
        await pg.mouse.move(from.x + from.width / 2, from.y + from.height / 2, { steps: 10 })
        await pg.mouse.down()
        await pg.waitForTimeout(250)
        await pg.mouse.move(to.x + to.width / 2, to.y + to.height / 2, { steps: step.dragTo.steps || 25 })
        await pg.waitForTimeout(450)
        await pg.mouse.up()
      }
    }
    if (step.scrollTo) await pg.locator(step.scrollTo).first().scrollIntoViewIfNeeded().catch(() => {})
    if (step.eval) await pg.evaluate(step.eval)
    // Teardown (dev-plan 10.4). Deleting a space needs the key typed back
    // and the password in the same request (11.3), and the password is the
    // harness's, not the spec's — so this is a step rather than an `eval`,
    // and the credential never appears in the JSON or on screen.
    for (const key of [].concat(step.deleteSpace || [])) {
      const result = await pg.evaluate(async ([key, password]) => {
        const res = await fetch(`/api/spaces/${key}`, {
          method: 'DELETE',
          credentials: 'include',
          headers: { 'Content-Type': 'application/json', 'X-Requested-With': 'Tesria' },
          body: JSON.stringify({ confirmKey: key, password }),
        })
        return res.status
      }, [key, PASSWORD])
      if (result !== 204 && result !== 404) {
        throw new Error(`could not delete ${key}: HTTP ${result}`)
      }
      console.log(result === 204 ? 'removed space' : 'no space to remove:', key)
    }
    if (step.wait) await pg.waitForTimeout(step.wait)
    if (step.waitFor) await pg.waitForSelector(step.waitFor, { timeout: 15000 })
  }
}

/**
 * A clip (dev-plan 10.4).
 *
 * Playwright records a context, not a page, and names the file itself, so a
 * recording gets its own short-lived context and the file is renamed
 * afterwards. The session is carried over as storage state rather than
 * signing in again: a sign-in at the head of every clip would be twelve
 * more sign-ins and would show in the first frames.
 *
 * The poster is the last frame's state captured as a PNG before the context
 * closes, so the still and the clip cannot drift apart.
 */
async function record(s) {
  const viewport = s.viewport || RECORD_VIEWPORT
  const size = s.record.size || RECORD_VIDEO
  const dir = mkdtempSync(path.join(os.tmpdir(), 'clip-'))
  const recCtx = await browser.newContext({
    ...contextOptions,
    viewport,
    // 1x: a clip is watched at its own size, and 2x quadruples the bytes
    // against a budget measured in hundreds of kilobytes.
    deviceScaleFactor: 1,
    storageState: await ctx.storageState(),
    recordVideo: { dir, size },
  })
  await recCtx.addInitScript(seedAppearance)
  const pg = await recCtx.newPage()
  watch(pg)

  if (s.url) {
    await pg.goto(BASE + s.url, { waitUntil: 'domcontentloaded' })
    await pg.waitForLoadState('load').catch(() => {})
  }
  if (s.waitFor) await pg.waitForSelector(s.waitFor, { timeout: 15000 })
  // Applied before the held opening frame rather than as a step, so it is
  // in place for the very first frame the viewer sees.
  if (s.css) await pg.addStyleTag({ content: s.css })
  // Hold the opening frame, so a loop does not start mid-motion.
  await pg.waitForTimeout(s.lead == null ? 700 : s.lead)

  await runSteps(pg, s.steps, s.name)

  // And hold the closing frame. The spec asks the first and last frames to
  // match; where they cannot, this at least stops the loop snapping.
  await pg.waitForTimeout(s.tail == null ? 900 : s.tail)

  // The poster, from the state the clip ends in.
  await pg.addStyleTag({ content: '*{caret-color:transparent !important}' }).catch(() => {})
  await pg.screenshot({ path: path.join(OUT, `${s.name}.${THEME}.png`) })

  const video = pg.video()
  await recCtx.close() // flushes the video file
  const produced = await video.path()
  // Copy rather than rename: the temporary directory and /out are different
  // filesystems inside the container (a bind mount), and rename across them
  // fails with EXDEV.
  copyFileSync(produced, path.join(OUT, `${s.name}.${THEME}.webm`))
  rmSync(dir, { recursive: true, force: true })

  // Clips are cheap to make and easy to bloat; the size is part of the
  // output, so it is printed rather than discovered later by the budget.
  const kb = Math.round(statSync(path.join(OUT, `${s.name}.${THEME}.webm`)).size / 1024)
  console.log('clip', `${s.name}.${THEME}.webm`, kb + ' KB')
}

for (const s of spec.shots) {
  if (only && s.name !== only) continue
  current = s.name
  if (s.record) {
    try { await record(s) } catch (err) { console.error('FAILED', s.name, '::', err.message) }
    continue
  }
  const pg = await pageFor(s)
  try {
    if (s.url) { await pg.goto(BASE + s.url, { waitUntil: 'domcontentloaded' }); await pg.waitForLoadState('load').catch(() => {}) }
    if (s.viewport) await pg.setViewportSize(s.viewport)
    await runSteps(pg, s.steps, s.name)
    if (s.waitFor) await pg.waitForSelector(s.waitFor, { timeout: 15000 })
    await pg.waitForTimeout(s.settle == null ? 450 : s.settle)
    // setup and teardown are shots so that they run in order with the rest;
    // they are not pictures of anything (dev-plan 10.4).
    if (s.skipCapture) { console.log('ran', s.name); continue }
    if (s.hideCaret !== false) {
      await pg.addStyleTag({ content: '*{caret-color:transparent !important} *::selection{background:transparent}' })
    }
    if (s.annotate) await pg.evaluate(`(${ANNOTATE})(${JSON.stringify(s.annotate)})`)

    // The onboarding set ships a light and a dark variant of everything, so
    // its stills carry the theme in the filename the way its clips do.
    const file = path.join(OUT, spec.nameByTheme ? `${s.name}.${THEME}.png` : s.name + '.png')
    if (s.clipTo) {
      const sels = Array.isArray(s.clipTo) ? s.clipTo : [s.clipTo]
      const boxes = []
      for (const sel of sels) {
        const el = await pg.$(sel)
        if (!el) throw new Error('clipTo not found: ' + sel)
        boxes.push(await el.boundingBox())
      }
      const x0 = Math.min(...boxes.map(b => b.x)), y0 = Math.min(...boxes.map(b => b.y))
      const x1 = Math.max(...boxes.map(b => b.x + b.width)), y1 = Math.max(...boxes.map(b => b.y + b.height))
      const scroll = await pg.evaluate(() => ({ x: window.scrollX, y: window.scrollY }))
      const box = { x: x0 + scroll.x, y: y0 + scroll.y, width: x1 - x0, height: y1 - y0 }
      const p = s.clipPad == null ? 0 : s.clipPad
      await pg.screenshot({
        path: file,
        fullPage: true,
        clip: {
          x: Math.max(0, box.x - p), y: Math.max(0, box.y - p),
          width: box.width + p * 2, height: box.height + p * 2 - (s.clipTrim || 0),
        },
      })
    } else if (s.clip) {
      await pg.screenshot({ path: file, clip: s.clip })
    } else {
      await pg.screenshot({ path: file, fullPage: !!s.fullPage })
    }
    // Typing into the editor reaches the collaborative document immediately,
    // whether or not the page is ever saved — so a shot that types has to put
    // the document back, or the next run photographs the last run's leftovers.
    for (const step of s.after || []) {
      if (step.press) await pg.press(step.selector || 'body', step.press)
      if (step.repeat) for (let i = 0; i < step.repeat; i++) await pg.keyboard.press(step.key)
      if (step.wait) await pg.waitForTimeout(step.wait)
    }
    console.log('shot', s.name)
  } catch (err) {
    console.error('FAILED', s.name, '::', err.message)
  }
  if (s.viewport) await pg.setViewportSize({ width: 1440, height: 900 })
}

if (problems.length) {
  console.error('\n--- page errors ---')
  for (const p of problems) console.error(' ', p)
} else {
  console.log('\nno page errors')
}

await browser.close()
