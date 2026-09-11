// PDF rendering sidecar.
//
// The app already produces a print-ready, *self-contained* HTML export
// (ExportEndpoints): images are inlined as data URIs and a Mermaid diagram
// carries its own renderer. That is what makes this service safe to run
// with the network switched off entirely — it is handed a complete document
// and never fetches anything to render it.
//
// Isolated in a Node sidecar for the same reason the collab server is: the
// tool for the job (a real browser engine) is not a .NET library, and a
// ~400MB Chromium has no business inside the app image.
import { createServer } from 'node:http'
import { chromium } from 'playwright-core'

const PORT = Number(process.env.PDF_PORT ?? 8091)
const SECRET = process.env.PDF_SHARED_SECRET ?? ''
// A page of prose renders in well under a second; anything near this is a
// runaway, not a slow document.
const RENDER_TIMEOUT_MS = Number(process.env.PDF_TIMEOUT_MS ?? 20_000)
const MAX_BODY_BYTES = Number(process.env.PDF_MAX_BODY_BYTES ?? 64 * 1024 * 1024)

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

async function renderPdf(html) {
  const browser = await getBrowser()
  const context = await browser.newContext({ javaScriptEnabled: true, offline: true })
  try {
    const page = await context.newPage()
    // Belt and braces with `offline`: refuse every request that is not the
    // document itself, so a document that somehow carries an external
    // reference cannot make this service fetch it.
    await page.route('**/*', (route) =>
      route.request().url().startsWith('data:') ? route.continue() : route.abort())

    await page.setContent(html, { waitUntil: 'load', timeout: RENDER_TIMEOUT_MS })
    // Diagrams draw after load; wait for the page to go quiet rather than
    // guessing a delay, but never longer than the budget.
    await page.waitForTimeout(250)
    await page.emulateMedia({ media: 'print' })

    return await page.pdf({
      format: 'A4',
      printBackground: true,
      margin: { top: '18mm', bottom: '18mm', left: '16mm', right: '16mm' },
      timeout: RENDER_TIMEOUT_MS,
    })
  } finally {
    await context.close()
  }
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
  // secret means a compromised neighbour still cannot drive the renderer.
  if (!SECRET || req.headers['x-pdf-secret'] !== SECRET) {
    res.writeHead(401).end()
    return
  }

  try {
    const html = await readBody(req)
    const pdf = await renderPdf(html)
    res.writeHead(200, { 'content-type': 'application/pdf', 'content-length': pdf.length })
    res.end(pdf)
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
