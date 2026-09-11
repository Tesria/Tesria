import { Node, mergeAttributes } from '@tiptap/core'
import { ReactNodeViewRenderer } from '@tiptap/react'
import { TocView } from './TocView'

declare module '@tiptap/core' {
  interface Commands<ReturnType> {
    tableOfContents: {
      insertTableOfContents: () => ReturnType
    }
  }
}

/**
 * Table of contents: a block with no stored content. The node view lists the
 * page's headings live (`headingAnchors.ts`), and the export renderer builds
 * the same nested list of links at export time — so the document never
 * holds a stale copy of its own outline.
 */
export const TableOfContents = Node.create({
  name: 'tableOfContents',
  group: 'block',
  atom: true,
  selectable: true,

  parseHTML() {
    return [{ tag: 'div[data-type="table-of-contents"]' }]
  },

  renderHTML({ HTMLAttributes }) {
    return ['div', mergeAttributes(HTMLAttributes, { 'data-type': 'table-of-contents', class: 'toc' })]
  },

  addNodeView() {
    return ReactNodeViewRenderer(TocView)
  },

  addCommands() {
    return {
      insertTableOfContents:
        () =>
        ({ commands }) =>
          commands.insertContent({ type: this.name }),
    }
  },
})
