// Export capture sidecar (dev-plan 12.1).
//
// It used to be handed a self-contained HTML document built by a second
// renderer in the app, and printed that. The second renderer is why exports
// looked nothing like the page: it was a hand-written copy of the editor's
// styling, fifteen lines of CSS against the app's two thousand. So this now
// loads the *real* page instead, from a chrome-free route of the SPA, and
// either prints it or serializes its DOM. One renderer, and an export cannot
// drift from the page without the page breaking too.
//
// That is a deliberate change of posture. The service used to run with the
// network switched off; it now reaches the app, and nothing else. Two
// independent limits keep that honest: the APP_ORIGIN allowlist below
// aborts every request that is not the app or a data: URI, and the compose
// network gives it nowhere else to go. It still never reaches the internet.
//
// Isolated in a Node sidecar for the same reason the collab server is: the
// tool for the job (a real browser engine) is not a .NET library, and a
// ~400MB Chromium has no business inside the app image.
import { createServer } from 'node:http'
import { lookup } from 'node:dns/promises'
import { chromium } from 'playwright-core'

const PORT = Number(process.env.PDF_PORT ?? 8091)
const SECRET = process.env.PDF_SHARED_SECRET ?? ''
// A page of prose renders in well under a second; anything near this is a
// runaway, not a slow document.
const RENDER_TIMEOUT_MS = Number(process.env.PDF_TIMEOUT_MS ?? 20_000)
const MAX_BODY_BYTES = Number(process.env.PDF_MAX_BODY_BYTES ?? 64 * 1024 * 1024)
// The one origin a capture may load. Anything else is aborted, so a document
// that somehow carries an external reference cannot make this service fetch
// it, and a caller cannot point the renderer at somewhere of its choosing.
const APP_ORIGIN = process.env.PDF_APP_ORIGIN ?? 'http://app:8080'

/**
 * The same origin with its hostname resolved to an address.
 *
 * Chromium upgrades a plain-http navigation to https whenever the host is a
 * name, and does so whatever `--disable-features=HttpsUpgrades` says; inside
 * the compose network there is no TLS on the app's port, so the navigation
 * dies as ERR_SSL_PROTOCOL_ERROR. A literal address is exempt from that
 * upgrade. The host header still says the address, which the app does not
 * care about, and the page's own fetches stay same-origin.
 *
 * Resolved per capture rather than cached: a container that is recreated
 * comes back on a different address, and a cached one would strand the
 * renderer until it was restarted too.
 */
async function resolvedOrigin() {
  const url = new URL(APP_ORIGIN)
  if (/^[\d.]+$|^\[/.test(url.hostname)) return APP_ORIGIN
  const { address } = await lookup(url.hostname)
  return `${url.protocol}//${address}:${url.port || (url.protocol === 'https:' ? 443 : 80)}`
}

/** One browser for the life of the process; a page per request. Launching Chromium per PDF would dominate the time. */
let browserPromise = null

function getBrowser() {
  if (!browserPromise) {
    browserPromise = chromium.launch({
      args: [
        // No sandbox: the container is the sandbox, and the nested user
        // namespaces Chromium's own sandbox needs are not available to an
        // unprivileged container. The service is not reachable from outside
        // the compose network and renders only what the app hands it.
        '--no-sandbox',
        '--disable-dev-shm-usage',
      ],
    })
    browserPromise.catch(() => { browserPromise = null })
  }
  return browserPromise
}

/**
 * Loads a page of the app and waits for it to finish becoming itself.
 *
 * The page publishes `data-export-ready` on <html> when its editor has
 * mounted, its images have settled and its diagrams, maths and live blocks
 * have all answered (web/src/export/ready.ts). Waiting for a signal beats
 * waiting for a guess: a page of prose is ready in well under a second and a
 * page of diagrams takes as long as it takes.
 */
// How many pages are captured at once. Each capture is a browser context
// with its own memory, and nothing else bounded how many could run: a burst
// of exports (anonymous PDF exports on a public instance, say) could exhaust
// the container (the 14.1 review). The rest wait their turn.
const MAX_CONCURRENT = Math.max(1, Number(process.env.PDF_MAX_CONCURRENT) || 3)
let running = 0
const waiting = []
async function slot() {
  if (running < MAX_CONCURRENT) { running++; return }
  await new Promise((resolve) => waiting.push(resolve))
}
function release() {
  const next = waiting.shift()
  if (next) next()
  else running--
}

async function withCapturedPage(url, token, fn) {
  await slot()
  try {
    return await capture(url, token, fn)
  } finally {
    release()
  }
}

async function capture(url, token, fn) {
  // Checked against the configured origin, which is what the app was told to
  // use; the address substitution below is a navigation detail and must not
  // be a way to widen what may be loaded.
  if (!url.startsWith(APP_ORIGIN + '/')) {
    throw new Error('refusing to render a url outside ' + APP_ORIGIN)
  }
  const origin = await resolvedOrigin()
  const target = origin + url.slice(APP_ORIGIN.length)
  const browser = await getBrowser()
  const context = await browser.newContext({
    javaScriptEnabled: true,
    // The render token, on every request the page makes: the page fetches
    // its own content, and it has to do so as whoever asked for the export.
    extraHTTPHeaders: token ? { Authorization: `Bearer ${token}` } : {},
    // A PDF is paper-sized; a wider viewport makes the print layout decide
    // it has a desktop's room and lay tables out for one.
    viewport: { width: 1100, height: 1400 },
    deviceScaleFactor: 2,
  })
  try {
    const page = await context.newPage()
    await page.route('**/*', (route) => {
      const requested = route.request().url()
      if (requested.startsWith('data:')
        || requested.startsWith(origin + '/')
        || requested.startsWith(APP_ORIGIN + '/')) return route.continue()
      return route.abort()
    })

    await page.goto(target, { waitUntil: 'domcontentloaded', timeout: RENDER_TIMEOUT_MS })
    await page
      .waitForFunction(() => document.documentElement.hasAttribute('data-export-ready'),
        undefined, { timeout: RENDER_TIMEOUT_MS })
      // A page that never signals is still exported, from whatever it managed
      // to render. An export that hangs is worse than one that is incomplete.
      .catch(() => console.warn('[pdf] no ready signal; capturing anyway'))

    return await fn(page)
  } finally {
    await context.close()
  }
}

async function renderPdf(url, token, title) {
  return withCapturedPage(url, token, async (page) => {
    await page.emulateMedia({ media: 'print' })
    return page.pdf({
      format: 'A4',
      printBackground: true,
      margin: { top: '18mm', bottom: '20mm', left: '16mm', right: '16mm' },
      // A document somebody might cite needs to say which page they are on.
      displayHeaderFooter: true,
      headerTemplate: '<div></div>',
      // The numbering is ONE flex item, deliberately. As four (the number,
      // the word between them, the total, each its own item, plus the title)
      // space-between spread them the whole width of the page and it read as
      // "Every element    1    of    4" rather than a page number.
      footerTemplate:
        '<div style="width:100%;margin:0 16mm;font:9px -apple-system,Segoe UI,Roboto,sans-serif;'
        + 'color:#6b778c;display:flex;justify-content:space-between;align-items:baseline">'
        + `<span>${escapeHtml(title ?? '')}</span>`
        + '<span style="white-space:nowrap">'
        + '<span class="pageNumber"></span> of <span class="totalPages"></span>'
        + '</span>'
        + '</div>',
      timeout: RENDER_TIMEOUT_MS,
    })
  })
}

/**
 * The page's own DOM, as one file. Scripts go (except the theme script the
 * app marks as portable), the stylesheet is inlined, and everything
 * contenteditable stops being editable, so what is left is a document rather
 * than an application.
 */
async function renderHtml(url, token, { inlineAssets = true } = {}) {
  return withCapturedPage(url, token, async (page) => {
    const html = await page.evaluate(async (inline) => {
      // Make the file work on its own. Every image and file link still points
      // at the instance it came from, which is no use once the file has been
      // emailed to somebody; the page is authenticated, so it can fetch its
      // own assets and carry them. The site export turns this off: it writes
      // real files into assets/ instead, which keeps the pages small.
      if (inline) {
        const asDataUri = async (href) => {
          const res = await fetch(href, { credentials: 'include' })
          if (!res.ok) return null
          const blob = await res.blob()
          return await new Promise((resolve) => {
            const reader = new FileReader()
            reader.onload = () => resolve(reader.result)
            reader.onerror = () => resolve(null)
            reader.readAsDataURL(blob)
          })
        }
        for (const img of document.querySelectorAll('img[src^="/"]')) {
          const uri = await asDataUri(img.getAttribute('src'))
          // A broken image stays broken rather than disappearing: the alt
          // text is still the author's, and silence would hide the failure.
          if (uri) img.setAttribute('src', uri)
        }
        for (const a of document.querySelectorAll('a[href^="/api/attachments/"]')) {
          const uri = await asDataUri(a.getAttribute('href'))
          if (uri) { a.setAttribute('href', uri); a.setAttribute('download', a.textContent?.trim() || 'file') }
        }
      }

      // Inline every stylesheet the page is using. Same-origin, so the rules
      // are readable; an unreadable one is skipped rather than fatal.
      const css = [...document.styleSheets].map((sheet) => {
        try { return [...sheet.cssRules].map((r) => r.cssText).join('\n') } catch { return '' }
      }).join('\n')

      const doc = document.cloneNode(true)
      doc.querySelectorAll('script:not([data-export-keep])').forEach((n) => n.remove())
      // Every <link> that points at the application: stylesheets are inlined
      // below, and a modulepreload or prefetch of a script that is no longer
      // there is a guaranteed 404 on whatever host this ends up on.
      doc.querySelectorAll(
        'link[rel="stylesheet"], link[rel="modulepreload"], link[rel="preload"], link[rel="prefetch"]',
      ).forEach((n) => n.remove())
      doc.querySelectorAll('[contenteditable]').forEach((n) => n.removeAttribute('contenteditable'))
      // ProseMirror's own editing affordances mean nothing in a file.
      doc.querySelectorAll('.ProseMirror-gapcursor, .column-resize-handle, .tip, .dynamic-block__refresh').forEach((n) => n.remove())
      // The editor selects the first node when it is an atom, and a selected
      // node is outlined. Nobody selected anything in a file.
      doc.querySelectorAll('.ProseMirror-selectednode').forEach((n) => n.classList.remove('ProseMirror-selectednode'))
      doc.querySelectorAll('.dynamic-block.is-selected').forEach((n) => n.classList.remove('is-selected'))
      doc.documentElement.removeAttribute('data-export-ready')

      const style = doc.createElement('style')
      style.textContent = css
      doc.head.appendChild(style)

      return '<!doctype html>\n' + doc.documentElement.outerHTML
    }, inlineAssets)
    return Buffer.from(html, 'utf8')
  })
}

function escapeHtml(value) {
  return String(value).replace(/[&<>"]/g, (c) =>
    ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' })[c])
}

function readBody(req) {
  return new Promise((resolve, reject) => {
    const chunks = []
    let size = 0
    req.on('data', (chunk) => {
      size += chunk.length
      if (size > MAX_BODY_BYTES) {
        reject(new Error('too large'))
        req.destroy()
        return
      }
      chunks.push(chunk)
    })
    req.on('end', () => resolve(Buffer.concat(chunks).toString('utf8')))
    req.on('error', reject)
  })
}

const server = createServer(async (req, res) => {
  if (req.method === 'GET' && req.url === '/health') {
    res.writeHead(200, { 'content-type': 'text/plain' })
    res.end('ok')
    return
  }
  if (req.method !== 'POST' || req.url !== '/render') {
    res.writeHead(404).end()
    return
  }
  // The service is only reachable inside the compose network, but a shared
  // secret means a compromised neighbor still cannot drive the renderer.
  if (!SECRET || req.headers['x-pdf-secret'] !== SECRET) {
    res.writeHead(401).end()
    return
  }

  try {
    const body = JSON.parse(await readBody(req))
    const format = body.format === 'html' ? 'html' : 'pdf'
    const out = format === 'html'
      ? await renderHtml(body.url, body.token, { inlineAssets: body.inlineAssets !== false })
      : await renderPdf(body.url, body.token, body.title)
    res.writeHead(200, {
      'content-type': format === 'html' ? 'text/html; charset=utf-8' : 'application/pdf',
      'content-length': out.length,
    })
    res.end(out)
  } catch (err) {
    console.error('[pdf] render failed:', err?.message ?? err)
    res.writeHead(500, { 'content-type': 'text/plain' })
    res.end('render failed')
  }
})

server.listen(PORT, () => console.log(`[pdf] listening on ${PORT}`))

for (const signal of ['SIGINT', 'SIGTERM']) {
  process.on(signal, async () => {
    server.close()
    try { (await browserPromise)?.close() } catch { /* shutting down anyway */ }
    process.exit(0)
  })
}
