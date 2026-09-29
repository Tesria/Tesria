import { Fragment, Slice, type Node as PMNode } from '@tiptap/pm/model'
import { NodeSelection, type Transaction } from '@tiptap/pm/state'
import { dropPoint, insertPoint } from '@tiptap/pm/transform'

export type ImageAttrs = { src: string; alt: string }

/** The image node the transaction's last step just inserted, found by its address. */
function findInserted(tr: Transaction, src: string): { pos: number; node: PMNode } | null {
  const map = tr.steps[tr.steps.length - 1]?.getMap()
  if (!map) return null
  let from = 0
  let to = tr.doc.content.size
  map.forEach((_oldStart, _oldEnd, newStart, newEnd) => {
    from = newStart
    to = newEnd
  })
  let found: { pos: number; node: PMNode } | null = null
  tr.doc.nodesBetween(Math.max(0, from - 1), Math.min(tr.doc.content.size, to + 1), (node, pos) => {
    if (!found && node.type.name === 'image' && node.attrs.src === src) found = { pos, node }
    return !found
  })
  return found
}

/**
 * Puts images into a transaction's document, in order, and leaves the last
 * one selected (QA t4-012, t4-013). Returns whether anything was placed.
 *
 * Each picture used to be inserted "at the selection" on its own, and the
 * selection after inserting a picture is that picture, so the next one
 * *replaced* it: a gallery could hold one picture, several files dropped at
 * once made one image, and every replaced upload was left as an unused
 * attachment. Now each goes after the one before. The first goes where it
 * was dropped (`at`), or after a selected picture (a gallery tile), or at
 * the cursor.
 */
export function placeImages(tr: Transaction, images: ImageAttrs[], at?: number): boolean {
  const type = tr.doc.type.schema.nodes.image
  if (!type || images.length === 0) return false
  let last: { pos: number; node: PMNode } | null = null

  for (const attrs of images) {
    const node = type.create(attrs)
    if (last) {
      const after = last.pos + last.node.nodeSize
      tr.insert(insertPoint(tr.doc, after, type) ?? after, node)
    } else if (at !== undefined) {
      const bounded = Math.max(0, Math.min(at, tr.doc.content.size))
      const pos = dropPoint(tr.doc, bounded, new Slice(Fragment.from(node), 0, 0)) ?? bounded
      tr.replaceRangeWith(pos, pos, node)
    } else if (tr.selection instanceof NodeSelection && tr.selection.node.isBlock) {
      const after = tr.selection.to
      tr.insert(insertPoint(tr.doc, after, type) ?? after, node)
    } else if (tr.selection.empty && tr.selection.$from.parent.isTextblock && tr.selection.$from.parent.childCount === 0) {
      // An empty line (a new gallery's, say) becomes the picture, as
      // TipTap's own insert does. ProseMirror's replaceSelectionWith would
      // lift the picture out of the gallery instead.
      const $from = tr.selection.$from
      tr.replaceWith($from.before(), $from.after(), node)
    } else {
      tr.replaceSelectionWith(node)
    }
    last = findInserted(tr, attrs.src)
  }

  if (!last) return false
  tr.setSelection(NodeSelection.create(tr.doc, last.pos))
  return true
}
