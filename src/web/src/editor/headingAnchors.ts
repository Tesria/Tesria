import { Extension } from '@tiptap/core'
import type { Node as PMNode } from '@tiptap/pm/model'
import { Plugin, PluginKey } from '@tiptap/pm/state'
import { Decoration, DecorationSet } from '@tiptap/pm/view'

/**
 * Heading anchors: every heading gets an `id` derived from its text, so
 * links can point at it (`#setup`) and a table of contents can list it.
 *
 * The id is *derived*, never stored: the same choice Confluence makes.
 * Storing one would keep a link alive across a rewording, but it also means
 * duplicates on paste, drift between collaborators, a migration for every
 * existing page and a hidden value the author cannot see. Deriving it keeps
 * one algorithm, run in two places that must agree exactly: here, for the
 * editor and the reading view, and `HeadingAnchors.cs` for export. Change
 * one, change both, and keep `HeadingAnchorTests` green.
 */
export type HeadingAnchor = {
  level: number
  text: string
  id: string
  /** Position of the heading node in the document. */
  pos: number
  nodeSize: number
}

/**
 * Lower-case, letters and digits kept (any script), every other run of
 * characters collapsed to one hyphen, hyphens trimmed. Empty → "heading".
 */
export function slugify(text: string): string {
  const slug = text
    .toLowerCase()
    .replace(/[^\p{L}\p{Nd}]+/gu, '-')
    .replace(/^-+|-+$/g, '')
  return slug || 'heading'
}

/** Headings in document order, with ids de-duplicated by a numeric suffix (`setup`, `setup-2`, …). */
export function collectHeadingAnchors(doc: PMNode): HeadingAnchor[] {
  const used = new Set<string>()
  const anchors: HeadingAnchor[] = []
  doc.descendants((node, pos) => {
    if (node.type.name !== 'heading') return
    const base = slugify(node.textContent)
    let id = base
    for (let n = 2; used.has(id); n++) id = `${base}-${n}`
    used.add(id)
    anchors.push({ level: Number(node.attrs.level) || 1, text: node.textContent, id, pos, nodeSize: node.nodeSize })
  })
  return anchors
}

/** The element a `#fragment` link points at, looked up inside `root` only: a heading called "Root" must not resolve to the app's own `#root`. */
export function findAnchorTarget(root: ParentNode, id: string): HTMLElement | null {
  return root.querySelector<HTMLElement>(`[id="${CSS.escape(id)}"]`)
}

export function scrollToAnchor(root: ParentNode, id: string): boolean {
  const target = findAnchorTarget(root, id)
  if (!target) return false
  target.scrollIntoView({ block: 'start', behavior: 'smooth' })
  return true
}

const key = new PluginKey('headingAnchors')

/**
 * Puts the derived ids on the rendered headings as node decorations rather
 * than attributes: a decoration is recomputed from the document on every
 * change and never serialized, which is exactly the "derived, not stored"
 * rule above. Works in the read-only view too, which uses the same
 * extension list.
 */
export const HeadingAnchors = Extension.create({
  name: 'headingAnchors',

  addProseMirrorPlugins() {
    const build = (doc: PMNode) =>
      DecorationSet.create(
        doc,
        collectHeadingAnchors(doc).map((h) => Decoration.node(h.pos, h.pos + h.nodeSize, { id: h.id })),
      )
    return [
      new Plugin({
        key,
        state: {
          init: (_, state) => build(state.doc),
          apply: (tr, old) => (tr.docChanged ? build(tr.doc) : old),
        },
        props: {
          decorations(state) {
            return this.getState(state)
          },
        },
      }),
    ]
  },
})
