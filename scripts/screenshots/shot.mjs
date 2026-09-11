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
import { chromium } from 'playwright-core'
import { readFileSync, mkdirSync } from 'node:fs'
import path from 'node:path'

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

const browser = await chromium.launch({ args: ['--font-render-hinting=none'] })
const ctx = await browser.newContext({
  viewport: { width: 1440, height: 900 },
  deviceScaleFactor: 2,
  colorScheme: 'dark',
  ignoreHTTPSErrors: true,
})
const page = await ctx.newPage()

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
    const anonCtx = await browser.newContext({
      viewport: { width: 1440, height: 900 }, deviceScaleFactor: 2, colorScheme: 'dark',
      ignoreHTTPSErrors: true,
    })
    anonPage = await anonCtx.newPage()
  }
  return anonPage
}

for (const s of spec.shots) {
  if (only && s.name !== only) continue
  const pg = await pageFor(s)
  try {
    if (s.url) { await pg.goto(BASE + s.url, { waitUntil: 'domcontentloaded' }); await pg.waitForLoadState('load').catch(() => {}) }
    if (s.viewport) await pg.setViewportSize(s.viewport)
    for (const step of s.steps || []) {
      if (step.click) await pg.click(step.click)
      if (step.type) await pg.fill(step.selector, step.type)
      if (step.press) await pg.press(step.selector || 'body', step.press)
      if (step.keys) await pg.keyboard.type(step.keys, { delay: 12 })
      if (step.hover) await pg.hover(step.hover)
      if (step.eval) await pg.evaluate(step.eval)
      if (step.wait) await pg.waitForTimeout(step.wait)
      if (step.waitFor) await pg.waitForSelector(step.waitFor, { timeout: 15000 })
    }
    if (s.waitFor) await pg.waitForSelector(s.waitFor, { timeout: 15000 })
    await pg.waitForTimeout(s.settle == null ? 450 : s.settle)
    if (s.hideCaret !== false) {
      await pg.addStyleTag({ content: '*{caret-color:transparent !important} *::selection{background:transparent}' })
    }
    if (s.annotate) await pg.evaluate(`(${ANNOTATE})(${JSON.stringify(s.annotate)})`)

    const file = path.join(OUT, s.name + '.png')
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
      const box = { x: x0, y: y0, width: x1 - x0, height: y1 - y0 }
      const p = s.clipPad == null ? 0 : s.clipPad
      await pg.screenshot({
        path: file,
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
    console.log('shot', s.name)
  } catch (err) {
    console.error('FAILED', s.name, '::', err.message)
  }
  if (s.viewport) await pg.setViewportSize({ width: 1440, height: 900 })
}

await browser.close()
