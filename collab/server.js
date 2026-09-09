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
        return result.rows[0]?.State ?? null
      },
      store: async ({ documentName, state }) => {
        await pool.query(
          `INSERT INTO "CollabDocuments" ("DocumentName", "State", "UpdatedAt")
           VALUES ($1, $2, now())
           ON CONFLICT ("DocumentName")
           DO UPDATE SET "State" = EXCLUDED."State", "UpdatedAt" = now()`,
          [documentName, state],
        )
      },
    }),
  ],
})

server.listen().then(() => console.log(`[collab] listening on ${PORT}`))

for (const signal of ['SIGINT', 'SIGTERM']) {
  process.on(signal, async () => {
    console.log(`[collab] ${signal} received, shutting down`)
    await server.destroy()
    await pool.end()
    process.exit(0)
  })
}
