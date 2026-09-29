import { describe, expect, it } from 'vitest'
import type { JSONContent } from '@tiptap/core'
import { Schema } from '@tiptap/pm/model'
import { EditorState, NodeSelection, TextSelection } from '@tiptap/pm/state'
import { placeImages } from './imagePlacement'

/**
 * Where uploaded pictures land (QA t4-012, t4-013). A gallery held one
 * picture, because each new one replaced the one inserted before it.
 */
const schema = new Schema({
  nodes: {
    doc: { content: 'block+' },
    paragraph: { group: 'block', content: 'inline*', toDOM: () => ['p', 0] },
    gallery: { group: 'block', content: 'block+', toDOM: () => ['div', 0] },
    image: { group: 'block', attrs: { src: {}, alt: { default: null } }, toDOM: () => ['img'] },
    text: { group: 'inline' },
  },
})

const p = (text?: string): JSONContent => (text ? { type: 'paragraph', content: [{ type: 'text', text }] } : { type: 'paragraph' })
const img = (n: number) => ({ src: `/a/${n}`, alt: `pic${n}.png` })

function place(json: JSONContent, select: (doc: import('@tiptap/pm/model').Node) => import('@tiptap/pm/state').Selection, images: ReturnType<typeof img>[], at?: number) {
  const doc = schema.nodeFromJSON(json)
  const state = EditorState.create({ doc, selection: select(doc) })
  const tr = state.tr
  placeImages(tr, images, at)
  return tr
}

/** The document as a list of block types, with galleries opened up: "gallery(image,image)". */
function shape(node: import('@tiptap/pm/model').Node): string[] {
  const out: string[] = []
  node.forEach((child) => {
    if (child.type.name === 'gallery') {
      const inner: string[] = []
      child.forEach((c) => inner.push(c.type.name === 'image' ? `image ${c.attrs.alt}` : c.type.name))
      out.push(`gallery(${inner.join(', ')})`)
    } else out.push(child.type.name === 'image' ? `image ${child.attrs.alt}` : child.type.name)
  })
  return out
}

describe('placing uploaded pictures', () => {
  const emptyGallery: JSONContent = { type: 'doc', content: [{ type: 'gallery', content: [p()] }, p('after')] }

  it('puts several pictures into an empty gallery as one tile each, in order', () => {
    const tr = place(emptyGallery, (doc) => TextSelection.create(doc, 2), [img(1), img(2), img(3)])

    expect(shape(tr.doc)).toEqual(['gallery(image pic1.png, image pic2.png, image pic3.png)', 'paragraph'])
  })

  it('leaves the last one selected, so the next picture goes after it rather than replacing it', () => {
    let tr = place(emptyGallery, (doc) => TextSelection.create(doc, 2), [img(1)])
    expect(tr.selection).toBeInstanceOf(NodeSelection)

    // The next paste or drop, with that tile still selected.
    const state = EditorState.create({ doc: tr.doc, selection: tr.selection })
    tr = state.tr
    placeImages(tr, [img(2)])

    expect(shape(tr.doc)).toEqual(['gallery(image pic1.png, image pic2.png)', 'paragraph'])
  })

  it('puts several pictures dropped on a paragraph after one another, not one over the other', () => {
    const tr = place({ type: 'doc', content: [p('Drop below this line')] }, (doc) => TextSelection.create(doc, 1), [img(1), img(2)])

    expect(shape(tr.doc).filter((s) => s.startsWith('image'))).toEqual(['image pic1.png', 'image pic2.png'])
  })

  it('puts dropped pictures where they were dropped, not where the cursor is', () => {
    // Cursor in the paragraph after the gallery; the drop is inside the gallery.
    const json: JSONContent = { type: 'doc', content: [{ type: 'gallery', content: [{ type: 'image', attrs: img(1) }] }, p('after')] }
    const doc = schema.nodeFromJSON(json)
    const insideGallery = 1 + doc.firstChild!.firstChild!.nodeSize
    const tr = place(json, (d) => TextSelection.create(d, d.content.size - 1), [img(2)], insideGallery)

    expect(shape(tr.doc)).toEqual(['gallery(image pic1.png, image pic2.png)', 'paragraph'])
  })
})
