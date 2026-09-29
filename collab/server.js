// Real-time collaboration sidecar (PLAN §1: the editor engine is JS-only, so
// co-editing is isolated in a small Node service rather than reshaping the
// .NET stack).
//
// Responsibilities:
//   - accept Yjs/Hocuspocus websocket connections from the TipTap editor
//   - authorize them using short-lived HMAC tokens issued by the .NET API
//   - persist document state into the main Postgres database, so live edits
//     survive restarts and are covered by the existing backups
import crypto from 'node:crypto'
import { readFileSync } from 'node:fs'
import { Server } from '@hocuspocus/server'
import { Database } from '@hocuspocus/extension-database'
import pg from 'pg'
import * as Y from 'yjs'
// The editor's own schema and reconciliation, built from src/web at image
// build time (dev-plan 8.6). Not a copy: a node declared in extensions.ts
// and missing here would be dropped from every document this touched.
import { reconcile, reset } from './vendor/collab-schema.js'

/**
 * A secret from the environment, or else from the file the init service
 * wrote (dev-plan 25.1). Under Compose it is the file: the variables are no
 * longer set, so `docker inspect` shows none of them.
 */
function secret(envName, name) {
  if (process.env[envName]) return process.env[envName]
  try {
    return readFileSync(`/run/tesria/${name}/value`, 'utf8').replace(/[\r\n]+$/, '') || undefined
  } catch {
    return undefined
  }
}

const PORT = Number(process.env.COLLAB_PORT ?? 8090)
const SECRET = secret('COLLAB_SHARED_SECRET', 'collab-secret')
// The least-privilege role (dev-plan 3.1), and only it: the owner's password
// is no longer given to this service (dev-plan 14.3). DATABASE_URL if set;
// otherwise node-postgres takes the host, database and user from PGHOST,
// PGDATABASE and PGUSER, and the password is the app role's file.
const DATABASE_URL = process.env.DATABASE_URL
const APP_DB_PASSWORD = DATABASE_URL ? undefined : secret('APP_DB_PASSWORD', 'app-db-password')

if (!SECRET) {
  console.error('[collab] no shared secret: set COLLAB_SHARED_SECRET or start the stack with its init service')
  process.exit(1)
}
if (!DATABASE_URL && !APP_DB_PASSWORD) {
  console.error('[collab] no database password: set DATABASE_URL or start the stack with its init service')
  process.exit(1)
}

const pool = new pg.Pool(DATABASE_URL ? { connectionString: DATABASE_URL } : { password: APP_DB_PASSWORD })

// A document name is a page id. Checked before it is ever cast to uuid in
// SQL, so a malformed name is ignored rather than throwing.
const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
const SWEEP_MS = Number(process.env.COLLAB_SWEEP_MS ?? 15000)

// The app, on the compose network, for asking whether a connection may stand
// (dev-plan 14.4, the review's SEC-02). Never the public address.
const APP_URL = (process.env.APP_URL ?? 'http://app:8080').replace(/\/$/, '')
const RECHECK_MS = Number(process.env.COLLAB_RECHECK_MS ?? 60000)
// How long a new connection waits for an app that does not answer (it is
// restarting, most likely) before it is turned away.
const AUTHORIZE_PATIENCE_MS = Number(process.env.COLLAB_AUTHORIZE_PATIENCE_MS ?? 15000)

// When this process started. Compared with SiteSettings.LastRestoredAt by the
// sweep: a restore that finished after this moment means every document in
// memory may hold content from after the backup, and the only reliable way to
// forget all of it is to exit and let compose start this clean (dev-plan 9.4).
const STARTED_AT = new Date()

/**
 * Restore maintenance (dev-plan 9.4). While this is on, every connection is
 * closed, every document is dropped, and new connections are refused: an open
 * editor writes its in-memory document back on the next keystroke, which
 * after a restore would put content from after the backup into the restored
 * wiki.
 *
 * The deadline is a safety valve, not a schedule. A restore that ends without
 * the app saying so (it crashed, the network dropped the call) must not leave
 * co-editing switched off for everyone until somebody notices, so the flag
 * expires on its own; the ordinary end is the app posting `false` at startup.
 */
const MAINTENANCE_MAX_MS = Number(process.env.COLLAB_MAINTENANCE_MAX_MS ?? 30 * 60 * 1000)
let maintenanceUntil = 0

const inMaintenance = () => maintenanceUntil > Date.now()

function setMaintenance(on) {
  if (on) {
    maintenanceUntil = Date.now() + MAINTENANCE_MAX_MS
    console.warn('[collab] restore in progress: closing every document and refusing connections')
    for (const name of [...server.hocuspocus.documents.keys()]) {
      server.hocuspocus.closeConnections(name)
      server.hocuspocus.documents.delete(name)
    }
  } else if (maintenanceUntil !== 0) {
    maintenanceUntil = 0
    console.log('[collab] restore finished: accepting connections again')
  }
}

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
 * What one published version of a page said: the base of a three-way merge
 * (0.8.2). The draft's `meta.version` names the version it was last brought
 * up to date with, so comparing the draft with *that* is what separates the
 * human's unpublished typing from what a later write changed. Null when the
 * version cannot be found, which makes the merge fall back to the plain
 * two-way comparison.
 */
async function versionContent(documentName, version) {
  if (!UUID.test(documentName) || typeof version !== 'number') return null
  try {
    const result = await pool.query(
      `SELECT "ContentJson"::text AS content FROM "PageVersions"
       WHERE "PageId" = $1::uuid AND "VersionNumber" = $2`,
      [documentName, version],
    )
    const row = result.rows[0]
    return row ? JSON.parse(row.content) : null
  } catch (err) {
    console.warn(`[collab] ${documentName}: could not read version ${version} as a base`, err.message)
    return null
  }
}

/** Whether a page has a stored draft at all. A page nobody has opened has none. */
async function hasStoredDocument(documentName) {
  const result = await pool.query('SELECT 1 FROM "CollabDocuments" WHERE "DocumentName" = $1', [documentName])
  return result.rowCount > 0
}

/**
 * Who has typed in a draft since it was last published or discarded (0.8.2):
 * user id to display name, written by each editor on its first change. The
 * editor names them in a banner to the next person who opens a draft holding
 * someone else's unpublished work. Emptied here whenever the draft stops
 * holding unpublished work.
 */
function clearDrafters(doc) {
  const drafters = doc.getMap('drafters')
  for (const key of [...drafters.keys()]) drafters.delete(key)
}

/**
 * When each document last had a person connected, so a write that lands a
 * moment after someone's connection dropped is still treated as a write to a
 * draft being edited (merged, never reset). A network blip unloads the
 * document; without this grace, an API publish during it would reset the
 * draft under the person about to reconnect.
 */
const EDIT_GRACE_MS = Number(process.env.COLLAB_EDIT_GRACE_MS ?? 120000)
const lastEdited = new Map()

/** Whether somebody is editing this document now, or was a moment ago. */
function isBeingEdited(documentName) {
  const document = server.hocuspocus.documents.get(documentName)
  if (document && document.getConnections().length > 0) return true
  const at = lastEdited.get(documentName)
  return at !== undefined && Date.now() - at < EDIT_GRACE_MS
}

/**
 * Brings a document that somebody has open up to the page's current version,
 * as tracked changes against its base (0.8.2). Asked for by an editor whose
 * Update was refused because the page moved on (the 409), which used to
 * reconcile in the browser instead: done offline, that and the load-time
 * reconcile on reconnect both inserted the same change, and the page showed
 * it twice (QA T5-015). Here there is one reconciler, and the version check
 * inside the transaction makes a second request a no-op.
 */
async function reconcileOpenDocument(documentName) {
  const page = await currentVersion(documentName)
  if (!page) return
  const connection = await server.hocuspocus.openDirectConnection(documentName, { write: true })
  try {
    const seen = connection.document?.getMap('meta').get('version')
    if (typeof seen === 'number' && seen >= page.version) return
    const base = await versionContent(documentName, seen)
    await connection.transact((doc) => {
      const meta = doc.getMap('meta')
      const now = meta.get('version')
      if (typeof now === 'number' && now >= page.version) return
      const changed = reconcile(doc, JSON.parse(page.contentJson), { source: 'page', actor: page.author ?? null }, base)
      meta.set('version', page.version)
      if (changed) console.log(`[collab] ${documentName} reconciled to version ${page.version} on an editor's request`)
    })
  } finally {
    await connection.disconnect()
  }
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
 * **A three-way merge against the draft's base** (0.8.2): the version
 * `meta.version` names is fetched and compared too, so unpublished typing in
 * the stored draft stays the human's and only what the page changed is
 * highlighted. This path is what runs after the sidecar was down for a write
 * (the app could not tell it), and a person may be about to reconnect with
 * offline edits, so it always merges rather than resetting.
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

  const base = await versionContent(documentName, seen)
  const changed = reconcile(ydoc, JSON.parse(page.contentJson), {
    source: 'page',
    actor: page.author ?? null,
  }, base)
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
  // What the token was issued under, for asking the app (14.4). A token from
  // before that has none and is refused; the editor asks for a new one.
  if (typeof payload.sk !== 'string' || !UUID.test(payload.sid ?? '') || typeof payload.st !== 'string') return null
  if (!UUID.test(payload.userId ?? '')) return null
  return payload
}

/**
 * Asks the app whether connections may stand (dev-plan 14.4, the review's
 * SEC-02). A valid signature proves the app issued the token; only the app
 * knows whether the account, its session and its right to edit the page
 * still hold, so it is asked at every connection and once a minute for every
 * open one. This service never evaluates a permission itself.
 *
 * Takes `{ key, userId, pageId, sk, sid, st }` items; returns a Map of key to
 * yes or no, or null when the app could not be asked.
 */
async function authorize(connections) {
  try {
    const response = await fetch(`${APP_URL}/internal/collab/authorize`, {
      method: 'POST',
      headers: { 'content-type': 'application/json', 'x-collab-secret': SECRET },
      body: JSON.stringify({ connections }),
      signal: AbortSignal.timeout(5000),
    })
    if (!response.ok) {
      console.warn(`[collab] the app answered ${response.status} to an authorization check`)
      return null
    }
    const { allowed } = await response.json()
    return new Map(Object.entries(allowed ?? {}))
  } catch (err) {
    console.warn(`[collab] could not ask the app about connections: ${err.message}`)
    return null
  }
}

const claimsOf = (payload) => ({
  userId: payload.userId, pageId: payload.pageId, sk: payload.sk, sid: payload.sid, st: payload.st,
})

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
 * Applies a page write to the page's shared draft (dev-plan 8.6). The app
 * posts here after it commits.
 *
 * What happens depends on whether anybody is editing (0.8.2, the owner's
 * decision after the QA run):
 *
 * - **Somebody is editing** (connected now, or within the last two minutes):
 *   the write is merged in as tracked changes against the draft's base, so
 *   only what the write changed is highlighted and their typing is untouched.
 * - **Nobody is**: the draft is reset to what was just published. Unpublished
 *   changes left behind with Close used to survive an API or MCP publish and
 *   come back, struck through, to the next person, who could publish them by
 *   pressing Update (QA cal-001, T3-001, T5-008, T5-031). The next editor now
 *   starts from the page.
 *
 * A page nobody has ever opened has no draft, and nothing is loaded for it.
 * Otherwise the document is opened with a direct connection, which loads it
 * if nobody has it open and unloads it again afterwards; the load is told
 * this is a write (`context.write`), so it does not reconcile on its own
 * account first.
 *
 * A write from the editor itself carries no content worth showing: the
 * document already *is* that content, the human made it. Only the version is
 * recorded, and the list of people with unpublished changes is emptied,
 * since Update publishes everyone's. Reconciling those too would look safer
 * and is worse: publishing and then carrying on typing is ordinary, and a
 * diff would strike through the words the human is still writing.
 */
async function applyWrite(documentName, { contentJson, source, version }) {
  if (!UUID.test(documentName)) return { status: 404, body: 'unknown document' }
  if (!server.hocuspocus.documents.has(documentName) && !(await hasStoredDocument(documentName))) {
    return { status: 202, body: 'no draft' }
  }

  const editing = isBeingEdited(documentName)
  const published = JSON.parse(contentJson)
  const connection = await server.hocuspocus.openDirectConnection(documentName, { write: true })
  try {
    const seen = connection.document?.getMap('meta').get('version')
    const base = source !== 'editor' && editing ? await versionContent(documentName, seen) : null
    await connection.transact((doc) => {
      const meta = doc.getMap('meta')
      const current = meta.get('version')
      // Already there: a request for the same write arrived twice, or an
      // editor's own reconcile request got in first.
      if (typeof version === 'number' && typeof current === 'number' && current >= version) return
      if (source === 'editor') {
        clearDrafters(doc)
      } else if (editing) {
        const changed = reconcile(doc, published, { source, actor: null }, base)
        if (changed) console.log(`[collab] ${documentName} took a live ${source} write (version ${version})`)
      } else {
        const changed = reset(doc, published)
        clearDrafters(doc)
        if (changed) console.log(`[collab] ${documentName}: draft reset to the ${source} write (version ${version})`)
      }
      if (typeof version === 'number') meta.set('version', version)
    })
  } finally {
    await connection.disconnect()
  }
  return { status: 200, body: 'applied' }
}

/**
 * Discard (0.8.2): makes the shared draft exactly the published page, for
 * everyone with it open, and forgets who had unpublished changes in it. The
 * app checks the caller may edit the page before asking.
 *
 * In place, never by starting a new document: an editor that is offline
 * still holds the old one, and reconnecting has to merge with this rather
 * than put every block on the page twice.
 */
async function resetDraft(documentName, { contentJson, version }) {
  if (!UUID.test(documentName)) return { status: 404, body: 'unknown document' }
  if (!server.hocuspocus.documents.has(documentName) && !(await hasStoredDocument(documentName))) {
    return { status: 202, body: 'no draft' }
  }
  const published = JSON.parse(contentJson)
  const connection = await server.hocuspocus.openDirectConnection(documentName, { write: true })
  try {
    await connection.transact((doc) => {
      reset(doc, published)
      clearDrafters(doc)
      if (typeof version === 'number') doc.getMap('meta').set('version', version)
    })
  } finally {
    await connection.disconnect()
  }
  console.log(`[collab] ${documentName}: draft discarded (back to version ${version})`)
  return { status: 200, body: 'reset' }
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
    const path = (request.url ?? '').split('?')[0]
    const match = /^\/pages\/([^/]+)\/reconcile$/.exec(path)
    const discard = /^\/pages\/([^/]+)\/reset$/.exec(path)
    const maintenance = path === '/maintenance'
    const revoke = path === '/revoke'
    if (request.method !== 'POST' || (!match && !discard && !maintenance && !revoke)) return

    const provided = request.headers['x-collab-secret']
    // Constant-time, and length-checked first, as timingSafeEqual requires.
    const expected = Buffer.from(SECRET)
    const given = Buffer.from(typeof provided === 'string' ? provided : '')
    if (given.length !== expected.length || !crypto.timingSafeEqual(given, expected)) {
      response.writeHead(403).end('forbidden')
      return handled()
    }

    // The app saying someone's access changed (dev-plan 14.3).
    if (revoke) {
      try {
        const closed = await closeFor(await readJson(request, 64 * 1024))
        response.writeHead(200).end(String(closed))
      } catch (err) {
        console.error('[collab] revoke request failed', err)
        response.writeHead(500).end('failed')
      }
      return handled()
    }

    // The app going into or out of a restore (dev-plan 9.4).
    if (maintenance) {
      try {
        const body = await readJson(request)
        setMaintenance(body.maintenance === true)
        response.writeHead(200).end('ok')
      } catch (err) {
        console.error('[collab] maintenance request failed', err)
        response.writeHead(500).end('failed')
      }
      return handled()
    }

    // Discarding a page's shared draft (0.8.2).
    if (discard) {
      try {
        const result = await resetDraft(discard[1], await readJson(request))
        response.writeHead(result.status).end(result.body)
      } catch (err) {
        console.error(`[collab] discard request failed for ${discard[1]}`, err)
        response.writeHead(500).end('failed')
      }
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
    // Nothing joins a document while the database underneath it is being
    // replaced. Refused rather than queued: the editor shows its
    // disconnected state, which is the truth.
    if (inMaintenance()) {
      console.warn(`[collab] refusing ${documentName}: a restore is in progress`)
      throw new Error('Unavailable')
    }
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
    // The signature says the app issued this token, not that it would still
    // (14.4): a token outlives a revocation by up to its ten minutes, and a
    // client need not ask for a fresh one to reconnect. So the app is asked.
    // Closed by default: an app that cannot be asked admits nobody, after a
    // short wait, since the usual reason is an app restarting.
    const claims = claimsOf(payload)
    let answer = await authorize([{ key: 'c', ...claims }])
    for (let waited = 0; answer === null && waited < AUTHORIZE_PATIENCE_MS; waited += 2000) {
      await new Promise((resolve) => setTimeout(resolve, 2000))
      answer = await authorize([{ key: 'c', ...claims }])
    }
    if (answer === null) {
      console.warn(`[collab] refusing ${documentName}: the app could not be asked`)
      throw new Error('Unavailable')
    }
    if (answer.get('c') !== true) {
      console.warn(`[collab] refusing ${documentName}: the app says no`)
      throw new Error('Unauthorized')
    }
    // Surfaced to other clients as the collaborator's identity; the expiry is
    // kept so the sweep can end the connection when the token does, and the
    // claims so the recheck can ask about it again.
    return { user: { id: payload.userId, name: payload.displayName }, exp: payload.exp, claims }
  },

  /** Remembers when a person was last connected; see isBeingEdited. */
  async onDisconnect({ documentName, context }) {
    if (context?.user) lastEdited.set(documentName, Date.now())
  },

  /**
   * An editor asking for its document to be brought up to date with the
   * page (0.8.2): sent after its Update was refused because the page moved
   * on. Only an authenticated editor of this document can send one, and it
   * carries no content: the sidecar reads the page itself.
   */
  async onStateless({ documentName, payload }) {
    let message
    try {
      message = JSON.parse(payload)
    } catch {
      return
    }
    if (message?.type !== 'reconcile') return
    try {
      await reconcileOpenDocument(documentName)
    } catch (err) {
      console.error(`[collab] ${documentName}: an editor's reconcile request failed`, err)
    }
  },

  extensions: [
    new Database({
      fetch: async ({ documentName, context }) => {
        const result = await pool.query(
          'SELECT "State" FROM "CollabDocuments" WHERE "DocumentName" = $1',
          [documentName],
        )
        const state = result.rows[0]?.State ?? null
        // Loaded for a write, a discard or an editor's request, each of which
        // decides for itself what happens to the draft (see applyWrite).
        if (context?.write) return state
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
/**
 * The backstop for a restore this sidecar was never told about (dev-plan
 * 9.4): the app could not reach it, or the restore was run from the runbook
 * by hand. `LastRestoredAt` moving past this process's start means the
 * documents in memory may predate a swap, and exiting is the only way to
 * forget all of them at once. Compose restarts this container.
 */
async function exitIfRestored() {
  const result = await pool.query('SELECT "LastRestoredAt" FROM "SiteSettings" LIMIT 1')
  const at = result.rows[0]?.LastRestoredAt
  if (at && new Date(at) > STARTED_AT) {
    console.warn('[collab] the wiki was restored after this process started; restarting to drop every document')
    await server.destroy()
    await pool.end()
    process.exit(0)
  }
}

async function dropDeletedDocuments() {
  // Documents are dropped wholesale during a restore; there is nothing to
  // sweep, and the database is very likely mid-swap.
  if (inMaintenance()) return
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

/**
 * Ends connections when access to them may have changed (dev-plan 14.3). The
 * app says whose, or which space's, or everyone's; this sidecar never decides
 * who lost access. It closes the connections, the editors reconnect with a
 * fresh token, and the app's /collab-token check lets back in exactly the
 * people who may still edit. Returns how many were closed.
 */
async function closeFor({ userId, spaceId, pageId, all }) {
  const open = [...server.hocuspocus.documents.entries()]
  let inScope = null
  if (spaceId || pageId) {
    const names = open.map(([name]) => name).filter((name) => UUID.test(name))
    if (names.length === 0) return 0
    const result = spaceId
      ? await pool.query('SELECT "Id"::text FROM "Pages" WHERE "Id" = ANY($1::uuid[]) AND "SpaceId" = $2::uuid', [names, spaceId])
      : await pool.query(
          'SELECT "Id"::text FROM "Pages" WHERE "Id" = ANY($1::uuid[]) AND "SpaceId" = (SELECT "SpaceId" FROM "Pages" WHERE "Id" = $2::uuid)',
          [names, pageId])
    inScope = new Set(result.rows.map((r) => r.Id))
  }
  let closed = 0
  for (const [name, document] of open) {
    if (inScope && !inScope.has(name)) continue
    for (const connection of document.getConnections()) {
      if (!all && !inScope && connection.context?.user?.id !== userId) continue
      endConnection(connection, 4403, 'access-changed')
      closed++
    }
  }
  if (closed) console.log(`[collab] closed ${closed} connection(s): access changed`)
  return closed
}

/**
 * Hocuspocus's own close only tells the browser this one document is closed,
 * over a socket that stays open: the provider then marks itself signed out
 * but neither reconnects nor asks for a token again, and the editor goes on
 * saying "Live" while nothing it sends is accepted (found verifying 14.3).
 * Closing the socket as well makes the provider reconnect, which calls the
 * editor's token function and so asks the app again. The editor opens one
 * socket per page, so this ends nothing else.
 */
function endConnection(connection, code, reason) {
  connection.close({ code, reason })
  try { connection.webSocket.close(code, reason) } catch { /* already closing */ }
}

/**
 * Asks the app about every open connection, in one request, and ends those it
 * says no to (dev-plan 14.4). The backstop for a revocation notice that never
 * arrived: with it, access that has gone ends within a minute whatever
 * happened to the notice. An app that cannot be asked ends nothing; the
 * token's expiry still does, within ten minutes.
 */
async function recheckConnections() {
  if (inMaintenance()) return
  const open = []
  for (const [, document] of server.hocuspocus.documents) {
    for (const connection of document.getConnections()) {
      const claims = connection.context?.claims
      if (claims) open.push({ connection, claims })
    }
  }
  if (open.length === 0) return
  const answer = await authorize(open.map(({ claims }, i) => ({ key: String(i), ...claims })))
  if (answer === null) return
  let closed = 0
  open.forEach(({ connection }, i) => {
    if (answer.get(String(i)) === true) return
    endConnection(connection, 4403, 'access-changed')
    closed++
  })
  if (closed) console.log(`[collab] closed ${closed} connection(s): the app no longer allows them`)
}

/** Keeps the last-connected times to the grace period they are read for. */
function forgetOldConnections() {
  const now = Date.now()
  for (const [name, at] of lastEdited) if (now - at >= EDIT_GRACE_MS) lastEdited.delete(name)
}

/** Ends connections whose token has expired; the editor reconnects with a fresh one (dev-plan 14.3). */
function closeExpired() {
  const now = Date.now() / 1000
  for (const [, document] of server.hocuspocus.documents) {
    for (const connection of document.getConnections()) {
      const exp = connection.context?.exp
      if (typeof exp === 'number' && exp < now) endConnection(connection, 4401, 'token-expired')
    }
  }
}

server.listen().then(() => {
  console.log(`[collab] listening on ${PORT}`)
  const sweep = setInterval(
    () =>
      exitIfRestored()
        .then(dropDeletedDocuments)
        .then(closeExpired)
        .then(forgetOldConnections)
        .catch((err) => console.error('[collab] sweep failed', err)),
    SWEEP_MS,
  )
  sweep.unref()
  const recheck = setInterval(
    () => recheckConnections().catch((err) => console.error('[collab] recheck failed', err)),
    RECHECK_MS,
  )
  recheck.unref()
})

for (const signal of ['SIGINT', 'SIGTERM']) {
  process.on(signal, async () => {
    console.log(`[collab] ${signal} received, shutting down`)
    await server.destroy()
    await pool.end()
    process.exit(0)
  })
}
