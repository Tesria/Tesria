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

const server = new Server({
  port: PORT,
  address: '0.0.0.0',

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
