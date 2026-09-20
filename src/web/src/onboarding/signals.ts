/**
 * The small facts a tip's trigger needs (dev-plan 10.3).
 *
 * Counters live in `localStorage`, keyed by user id so two people sharing a
 * browser do not inherit each other's. They are not worth a column: losing
 * them costs one repeated tip, and sending them to the server would mean
 * telling it how many times somebody opened the editor, which is nobody's
 * business.
 *
 * Signals are recorded where the thing happens; `TipHost` reads them where a
 * tip is judged. Everything is wrapped against a browser with storage
 * blocked, where every count reads as zero and tips simply do not fire.
 */

const PREFIX = 'tesria-onboarding'

function key(userId: string, name: string) {
  return `${PREFIX}:${userId}:${name}`
}

function read(userId: string, name: string): number {
  try { return Number(localStorage.getItem(key(userId, name))) || 0 } catch { return 0 }
}

function bump(userId: string, name: string, by = 1): number {
  const next = read(userId, name) + by
  try { localStorage.setItem(key(userId, name), String(next)) } catch { /* nothing to do */ }
  return next
}

/** Who the counters belong to. Set once the session is known. */
let currentUser: string | null = null
export function setSignalUser(userId: string | null) { currentUser = userId }

const forUser = <T,>(fn: (id: string) => T, fallback: T): T =>
  currentUser ? fn(currentUser) : fallback

// --- Recording.

export const noteEditorSession = () => forUser((id) => bump(id, 'editor-sessions'), 0)
export const notePageCreated = () => forUser((id) => bump(id, 'pages-created'), 0)
export const noteSearch = () => forUser((id) => bump(id, 'searches'), 0)
export const noteProfileVisit = () => forUser((id) => bump(id, 'profile-visits'), 0)
export const notePageVisit = (pageId: string) => forUser((id) => bump(id, `page:${pageId}`), 0)

/** Set while an editing session is open; cleared when it closes. */
let pasted = false
export const notePastedFormatting = () => { pasted = true }
export const clearPasted = () => { pasted = false }
export const hasPastedFormatting = () => pasted

/** The page currently open, for the triggers that ask about its author. */
type OpenPage = { id: string; createdById: string }
let openPage: OpenPage | null = null
export const noteOpenPage = (page: OpenPage | null) => { openPage = page }
export const currentPage = () => openPage

// --- Reading.

export const counts = () => forUser((id) => ({
  editorSessions: read(id, 'editor-sessions'),
  pagesCreated: read(id, 'pages-created'),
  searches: read(id, 'searches'),
  profileVisits: read(id, 'profile-visits'),
}), { editorSessions: 0, pagesCreated: 0, searches: 0, profileVisits: 0 })

export const visitsTo = (pageId: string) => forUser((id) => read(id, `page:${pageId}`), 0)

// --- The daily cap.

/** Local date, not UTC: "three a day" means the person's day. */
const today = () => new Date().toLocaleDateString('en-CA')

export function tipsShownToday(): number {
  return forUser((id) => {
    try {
      const raw = localStorage.getItem(key(id, 'shown'))
      if (!raw) return 0
      const [day, count] = raw.split('|')
      return day === today() ? Number(count) || 0 : 0
    } catch { return 0 }
  }, 0)
}

export function noteTipShown() {
  forUser((id) => {
    try { localStorage.setItem(key(id, 'shown'), `${today()}|${tipsShownToday() + 1}`) } catch { /* nothing */ }
    return 0
  }, 0)
}
