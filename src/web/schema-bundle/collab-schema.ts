/**
 * Everything the collaboration sidecar needs in order to touch a document,
 * built into one headless ESM file by `vite.schema.config.ts` (dev-plan 8.6).
 *
 * **Why a bundle rather than a copy.** The sidecar has to turn a page's stored
 * ProseMirror JSON into Yjs, which needs the schema, and a schema is exactly
 * the kind of thing that must not exist twice: a node added in
 * `extensions.ts` and forgotten here would make the sidecar quietly drop it
 * from every document it reconciled. Building from the same source means a
 * schema change reaches the sidecar by rebuilding, and cannot drift.
 *
 * It bundles the React node views along with the schema, which is dead weight
 * in Node. That is the price of having one definition, and it is paid at
 * build time rather than in anyone's browser.
 */
import { getSchema } from '@tiptap/core'
import type { JSONContent } from '@tiptap/core'
import { getSharedExtensions } from '../src/editor/extensions'
import { reconcileYDoc, resetYDoc, type ExternalEditOrigin } from '../src/editor/externalEdits'
import { placeComment, removeComment, type PlaceResult } from '../src/editor/commentAnchor'

/** The one schema, from the one place it is declared. */
export const schema = getSchema(getSharedExtensions({ collaborative: true }))

/**
 * Reconciles a shared document against what has been published, marking what
 * the write changed as tracked changes. `base` is the published version the
 * draft was last brought up to date with, which makes it a three-way merge
 * (0.8.2); null falls back to the plain comparison. See `externalEdits.ts`
 * for the rules; this only binds the schema so the caller does not have to
 * know about it.
 */
export function reconcile(
  ydoc: Parameters<typeof reconcileYDoc>[0],
  published: JSONContent,
  origin: ExternalEditOrigin,
  base: JSONContent | null = null,
): boolean {
  return reconcileYDoc(ydoc, schema, published, origin, { base })
}

/**
 * Makes a shared document exactly the published page, in place (0.8.2:
 * Discard, and an API or MCP publish while nobody is editing).
 */
export function reset(ydoc: Parameters<typeof resetYDoc>[0], published: JSONContent): boolean {
  return resetYDoc(ydoc, schema, published)
}


/**
 * Puts an inline comment's highlight on a quoted passage of the shared
 * document (dev-plan 22.2): the search and the format in one transaction,
 * nothing else in the document touched. See `commentAnchor.ts`.
 */
export function placeInlineComment(
  ydoc: Parameters<typeof placeComment>[0],
  request: { commentId: string; quote: string; occurrence?: number | null },
): PlaceResult {
  return placeComment(ydoc, schema, request)
}

/** Takes a comment's highlight off the shared document again (a comment row that failed to save). */
export function removeInlineComment(ydoc: Parameters<typeof removeComment>[0], commentId: string): boolean {
  return removeComment(ydoc, schema, commentId)
}
