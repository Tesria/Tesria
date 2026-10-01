/**
 * A copy of a new page kept on this device until it is published (t4-025).
 *
 * A new page has no shared draft (live editing needs a page id to share), so
 * before this its text lived only in the tab: a crash, a closed laptop or a
 * killed browser lost all of it. The copy is kept per place (space and
 * parent), with the invisible draft the page was being written into, so a
 * restore keeps that draft and the pictures already uploaded to it.
 */

export interface UnpublishedPage {
  draftId: string
  title: string
  contentJson: string
  /** When it was last written, as an ISO time. */
  savedAt: string
}

const PREFIX = 'tesria-unpublished-page:'

export function unpublishedKey(spaceId: string, parentPageId: string | null | undefined): string {
  return `${PREFIX}${spaceId}:${parentPageId || 'top'}`
}

/** Whether there is anything in it worth offering back: a title, or any content. */
export function worthKeeping(title: string, contentJson: string): boolean {
  if (title.trim().length > 0) return true
  try {
    const doc = JSON.parse(contentJson) as { content?: unknown[] }
    return Array.isArray(doc.content) && doc.content.some((node) => !isEmptyParagraph(node))
  } catch {
    return false
  }
}

function isEmptyParagraph(node: unknown): boolean {
  const n = node as { type?: string; content?: unknown[] }
  return n?.type === 'paragraph' && (!Array.isArray(n.content) || n.content.length === 0)
}

/** Reads a stored copy, or null for none or anything that is not one. */
export function parseUnpublished(raw: string | null): UnpublishedPage | null {
  if (!raw) return null
  try {
    const v = JSON.parse(raw) as Partial<UnpublishedPage>
    if (typeof v.draftId !== 'string' || typeof v.title !== 'string'
      || typeof v.contentJson !== 'string' || typeof v.savedAt !== 'string') return null
    if (!worthKeeping(v.title, v.contentJson)) return null
    return { draftId: v.draftId, title: v.title, contentJson: v.contentJson, savedAt: v.savedAt }
  } catch {
    return null
  }
}

/** Storage can be missing or refuse (a private window, a full quota): never an error. */
export function readUnpublished(key: string): UnpublishedPage | null {
  try { return parseUnpublished(localStorage.getItem(key)) } catch { return null }
}

export function writeUnpublished(key: string, page: UnpublishedPage): void {
  try {
    if (worthKeeping(page.title, page.contentJson)) localStorage.setItem(key, JSON.stringify(page))
    else localStorage.removeItem(key)
  } catch { /* storage blocked or full */ }
}

export function forgetUnpublished(key: string): void {
  try { localStorage.removeItem(key) } catch { /* storage blocked */ }
}
