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
import { reconcileYDoc, type ExternalEditOrigin } from '../src/editor/externalEdits'

/** The one schema, from the one place it is declared. */
export const schema = getSchema(getSharedExtensions({ collaborative: true }))

/**
 * Reconciles a shared document against what has been published, marking the
 * difference as tracked changes. See `externalEdits.ts` for the rules; this
 * only binds the schema so the caller does not have to know about it.
 */
export function reconcile(
  ydoc: Parameters<typeof reconcileYDoc>[0],
  published: JSONContent,
  origin: ExternalEditOrigin,
): boolean {
  return reconcileYDoc(ydoc, schema, published, origin)
}

