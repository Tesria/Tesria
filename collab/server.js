// Real-time collaboration sidecar (PLAN §1: the editor engine is JS-only, so
// co-editing is isolated in a small Node service rather than reshaping the
// .NET stack).
//
// Responsibilities:
//   - accept Yjs/Hocuspocus websocket connections from the TipTap editor
//   - authorise them using short-lived HMAC tokens issued by the .NET API
//   - persist document state into the main Postgres database, so live edits
//     survive restarts and are covered by the existing backups
import crypto from 'node:crypto'
import { Server } from '@hocuspocus/server'
import { Database } from '@hocuspocus/extension-database'
import pg from 'pg'
import * as Y from 'yjs'
// The editor's own schema and reconciliation, built from src/web at image
// build time (dev-plan 8.6). Not a copy: a node declared in extensions.ts
// and missing here would be dropped from every document this touched.
import { reconcile } from './vendor/collab-schema.js'

const PORT = Number(process.env.COLLAB_PORT ?? 8090)
const SECRET = process.env.COLLAB_SHARED_SECRET
// The least-privilege role (dev-plan 3.1) when one is configured; otherwise
// the owner, so an install without APP_DB_PASSWORD keeps working.
const DATABASE_URL = process.env.APP_DB_PASSWORD
  ? process.env.DATABASE_URL
  : (process.env.DATABASE_URL_FALLBACK ?? process.env.DATABASE_URL)

if (!SECRET) {
  console.error('[collab] COLLAB_SHARED_SECRET is required')
  process.exit(1)
}
if (!DATABASE_URL) {
  console.error('[collab] DATABASE_URL is required')
  process.exit(1)
}

const pool = new pg.Pool({ connectionString: DATABASE_URL })

// A document name is a page id. Checked before it is ever cast to uuid in
// SQL, so a malformed name is ignored rather than throwing.
const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
const SWEEP_MS = Number(process.env.COLLAB_SWEEP_MS ?? 15000)

/**
 * What a page currently says, and which version that is.
 *
 * A page with no current version is half-created (see PageWriter's
 * transaction); there is nothing to reconcile against, so it is left alone.
 */
async function currentVersion(documentName) {
  if (!UUID.test(documentName)) return null
  const result = await pool.query(
    `SELECT v."VersionNumber", v."ContentJson"::text AS content, u."DisplayName" AS author
     FROM "Pages" p
     JOIN "PageVersions" v ON v."Id" = p."CurrentVersionId"
     LEFT JOIN "Users" u ON u."Id" = v."AuthorId"
     WHERE p."Id" = $1::uuid`,
    [documentName],
  )
  const row = result.rows[0]
  return row ? { version: row.VersionNumber, contentJson: row.content, author: row.author } : null
}

/**
 * Brings a stored document up to date with the page before anyone opens it
 * (dev-plan 8.6, the "no session open" case).
 *
 * A write from the API or MCP changes the page version and never touches this
 * document, so a draft that was saved before it is stale. Until this existed,
 * opening that draft and pressing Update wrote the stale copy straight over
 * the assistant's work. Now the difference arrives as tracked changes and the
 * human decides.
 *
 * `meta.version` is the document's record of which page version it was last
 * reconciled to. **A document that has none is adopted, not reconciled**:
 * every draft that existed before this shipped is in that state, and their
 * unpublished edits are not changes an assistant made. Marking them all up on
 * the first load after a deploy would be noise, and noise in exactly the
 * feature whose whole job is to be believed.
 *
 * Failure is never fatal: the stored state is returned untouched. Refusing to
 * reconcile costs the reader a highlight, while writing a half-understood
 * document over a draft costs them their work.
 */
async function reconcileStored(documentName, state) {
  const page = await currentVersion(documentName)
  if (!page) return state

  const ydoc = new Y.Doc()
  if (state) Y.applyUpdate(ydoc, state)
  const meta = ydoc.getMap('meta')
  const seen = meta.get('version')

  if (seen === page.version) return state
  if (typeof seen !== 'number' && ydoc.getXmlFragment('default').length > 0) {
    meta.set('version', page.version)
    const adopted = Y.encodeStateAsUpdate(ydoc)
    await persist(documentName, Buffer.from(adopted))
    return adopted
  }

  const changed = reconcile(ydoc, JSON.parse(page.contentJson), {
    source: 'page',
    actor: page.author ?? null,
  })
  meta.set('version', page.version)
  const next = Y.encodeStateAsUpdate(ydoc)
  // Written now, not when the session next happens to save. Hocuspocus only
  // stores a document that changed while somebody was connected, so a
  // reconcile nobody then edited would be forgotten and run again from the
  // same stale state on the next load. See persist().
  await persist(documentName, Buffer.from(next))
  if (changed) console.log(`[collab] ${documentName} reconciled to version ${page.version}`)
  return next
}

/**
 * Writes a document's state, if its page still exists.
 *
 * Shared by the store hook and by the reconcile below, which needs it for a
 * reason worth stating: a reconcile that is not persisted is re-run from the
 * same stale state the next time the document is loaded, and because Yjs
 * merges rather than replaces, the second run's insertions land *beside* the
 * first's. The page's new paragraph then appears twice. Found exactly that
 * way, by restarting the sidecar with a page open.
 *
 * @returns whether a row was written (false means the page is gone).
 */
async function persist(documentName, state) {
  if (!UUID.test(documentName)) return false
  // The id is passed twice, as $1 and $3, because Postgres deduces one type
  // per parameter: $1 is the varchar document name, $3 the uuid.
  const written = await pool.query(
    `INSERT INTO "CollabDocuments" ("DocumentName", "State", "UpdatedAt")
     SELECT $1, $2, now()
     WHERE EXISTS (SELECT 1 FROM "Pages" WHERE "Id" = $3::uuid)
     ON CONFLICT ("DocumentName")
     DO UPDATE SET "State" = EXCLUDED."State", "UpdatedAt" = now()`,
    [documentName, state, documentName],
  )
  return written.rowCount > 0
}

/** Whether the page a document stands for is still there. */
async function pageExists(documentName) {
  if (!UUID.test(documentName)) return false
  const result = await pool.query('SELECT 1 FROM "Pages" WHERE "Id" = $1::uuid', [documentName])
  return result.rowCount > 0
}

const fromBase64Url = (value) =>
  Buffer.from(value.replaceAll('-', '+').replaceAll('_', '/'), 'base64')

/**
 * Verifies a token minted by the API. The API is the only component that can
 * evaluate the permission model, so a valid signature is our proof that this
 * user was allowed to edit this specific page. Returns the payload or null.
 */
function verifyToken(token, documentName) {
  if (typeof token !== 'string') return null
  const [payloadPart, signaturePart] = token.split('.')
  if (!payloadPart || !signaturePart) return null

  const expected = crypto.createHmac('sha256', SECRET).update(payloadPart).digest()
  const provided = fromBase64Url(signaturePart)
  // Constant-time compare; lengths must match before timingSafeEqual.
  if (expected.length !== provided.length) return null
  if (!crypto.timingSafeEqual(expected, provided)) return null

  let payload
  try {
    payload = JSON.parse(fromBase64Url(payloadPart).toString('utf8'))
  } catch {
    return null
  }

  if (typeof payload.exp !== 'number' || payload.exp * 1000 < Date.now()) return null
  // Bind the token to one document so it cannot be replayed against another page.
  if (payload.pageId !== documentName) return null
  return payload
}

/**
 * Tells Hocuspocus this request is dealt with.
 *
 * Its convention, from `requestHandler`: a hook that rejects with an *empty*
 * value means "handled, do nothing further", while rejecting with a real
 * error is rethrown. So this rejects with nothing on purpose. Returning
 * normally instead would have Hocuspocus append its own "Welcome to
 * Hocuspocus!" to a response already written.
 */
const handled = () => Promise.reject()

/**
 * Reads a JSON request body, with a ceiling.
 *
 * A page document can be large, but not unbounded: this endpoint is reachable
 * only from inside the compose network and only with the shared secret, and a
 * cap still beats letting one request decide how much memory the sidecar uses.
 */
async function readJson(request, limit = 8 * 1024 * 1024) {
  const chunks = []
  let size = 0
  for await (const chunk of request) {
    size += chunk.length
    if (size > limit) throw new Error('payload too large')
    chunks.push(chunk)
  }
  return JSON.parse(Buffer.concat(chunks).toString('utf8'))
}

/**
 * Applies a page write to a document somebody currently has open (dev-plan
 * 8.6, the live case). The app posts here after it commits.
 *
 * `openDirectConnection` would *load* a document that is not open, which is
 * deliberately not wanted: a page nobody is editing needs no live update, and
 * loading every written page into memory would make this sidecar's footprint
 * a function of how busy the API is rather than of how many people are
 * editing. Such a page is reconciled on its next load instead, by the same
 * code, which is the path step 3 built.
 *
 * A write from the editor itself carries no content worth showing: the
 * document already *is* that content, the human made it. Only the version is
 * recorded, so the next load does not mistake their own publish for somebody
 * else's change.
 *
 * Reconciling those too would look safer and is worse. Publishing and then
 * carrying on typing is ordinary, and by the time this notification arrives
 * the draft is legitimately ahead of the page; a diff would strike through
 * the words the human is still writing and attribute them to somebody else.
 * The gap that leaves is a cookie-session write that did *not* come from the
 * open editor, which the application has no flow for.
 */
async function applyWrite(documentName, { contentJson, source, version }) {
  if (!UUID.test(documentName)) return { status: 404, body: 'unknown document' }
  if (!server.hocuspocus.documents.has(documentName)) {
    // Not open. Nothing to do now; the next load reconciles.
    return { status: 202, body: 'not open' }
  }

  const connection = await server.hocuspocus.openDirectConnection(documentName)
  try {
    await connection.transact((doc) => {
      const meta = doc.getMap('meta')
      if (source === 'editor') {
        if (typeof version === 'number') meta.set('version', version)
        return
      }
      const changed = reconcile(doc, JSON.parse(contentJson), { source, actor: null })
      if (typeof version === 'number') meta.set('version', version)
      if (changed) console.log(`[collab] ${documentName} took a live ${source} write (version ${version})`)
    })
  } finally {
    await connection.disconnect()
  }
  return { status: 200, body: 'applied' }
}

const server = new Server({
  port: PORT,
  address: '0.0.0.0',

  /**
   * The one HTTP route this sidecar answers, beside the websocket: the app
   * telling it a page has been written (dev-plan 8.6). Guarded by the shared
   * secret the app already holds, and reachable only inside the compose
   * network, which is why there is no user identity here to check.
   */
  async onRequest({ request, response }) {
    const match = /^\/pages\/([^/]+)\/reconcile$/.exec((request.url ?? '').split('?')[0])
    if (request.method !== 'POST' || !match) return

    const provided = request.headers['x-collab-secret']
    // Constant-time, and length-checked first, as timingSafeEqual requires.
    const expected = Buffer.from(SECRET)
    const given = Buffer.from(typeof provided === 'string' ? provided : '')
    if (given.length !== expected.length || !crypto.timingSafeEqual(given, expected)) {
      response.writeHead(403).end('forbidden')
      return handled()
    }

    try {
      const result = await applyWrite(match[1], await readJson(request))
      response.writeHead(result.status).end(result.body)
    } catch (err) {
      // Never fatal: the app has already committed the page, and the
      // document reconciles on its next load regardless.
      console.error(`[collab] reconcile request failed for ${match[1]}`, err)
      response.writeHead(500).end('failed')
    }
    return handled()
  },

  async onAuthenticate({ token, documentName }) {
    const payload = verifyToken(token, documentName)
    if (!payload) throw new Error('Unauthorized')
    // A token stays valid for its lifetime, so closing a deleted page's
    // connections is not enough on its own: the client reconnects and
    // authenticates again with the same token. Refusing here is what actually
    // ends the session, and what stops a token outliving its page.
    if (!(await pageExists(documentName))) {
      console.warn(`[collab] refusing ${documentName}: no such page`)
      throw new Error('Unauthorized')
    }
    // Surfaced to other clients as the collaborator's identity.
    return { user: { id: payload.userId, name: payload.displayName } }
  },

  extensions: [
    new Database({
      fetch: async ({ documentName }) => {
        const result = await pool.query(
          'SELECT "State" FROM "CollabDocuments" WHERE "DocumentName" = $1',
          [documentName],
        )
        const state = result.rows[0]?.State ?? null
        try {
          return await reconcileStored(documentName, state)
        } catch (err) {
          console.error(`[collab] ${documentName} could not be reconciled; serving it as stored`, err)
          return state
        }
      },
      store: async ({ documentName, state }) => {
        // A name that is not a page id is ignored rather than treated as a
        // deleted page: there is nothing to close and nothing to warn about.
        if (!UUID.test(documentName)) return
        // Only if the page is still there. A space can be deleted (dev-plan
        // 11.3) while someone has one of its pages open; that session holds
        // the document in memory and would otherwise write it straight back,
        // resurrecting a row for a page that no longer exists.
        if (!(await persist(documentName, state))) {
          console.warn(`[collab] ${documentName} is gone; dropping its session`)
          server.hocuspocus.closeConnections(documentName)
        }
      },
    }),
  ],
})

/**
 * Ends sessions whose page has been deleted.
 *
 * The store hook above catches this too, but only when someone is still
 * typing: an open, idle editor would otherwise sit there believing it is
 * connected to a page that no longer exists. This sweep is the one that
 * reaches it. Polling rather than a push from the API keeps the sidecar's
 * only inbound surface the websocket, and covers a page purged on its own as
 * well as a whole space deleted.
 */
async function dropDeletedDocuments() {
  // `Server` wraps the Hocuspocus instance rather than being one; the open
  // documents and closeConnections both live on it.
  const open = [...server.hocuspocus.documents.keys()].filter((name) => UUID.test(name))
  if (open.length === 0) return
  const alive = await pool.query('SELECT "Id"::text FROM "Pages" WHERE "Id" = ANY($1::uuid[])', [open])
  const live = new Set(alive.rows.map((r) => r.Id))
  for (const name of open) {
    if (live.has(name)) continue
    console.warn(`[collab] ${name} no longer exists; closing its connections`)
    server.hocuspocus.closeConnections(name)
  }
}

server.listen().then(() => {
  console.log(`[collab] listening on ${PORT}`)
  const sweep = setInterval(
    () => dropDeletedDocuments().catch((err) => console.error('[collab] sweep failed', err)),
    SWEEP_MS,
  )
  sweep.unref()
})

for (const signal of ['SIGINT', 'SIGTERM']) {
  process.on(signal, async () => {
    console.log(`[collab] ${signal} received, shutting down`)
    await server.destroy()
    await pool.end()
    process.exit(0)
  })
}
