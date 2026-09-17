import { Node, mergeAttributes } from '@tiptap/core'
import { ReactNodeViewRenderer } from '@tiptap/react'
import { TocView } from './TocView'
import { TOC_DEFAULTS } from './tocOptions'

/** One attribute per option, round-tripped through data-* for pasted HTML. */
function tocAttribute<K extends keyof typeof TOC_DEFAULTS>(key: K) {
  const dataName = 'data-' + key.replace(/[A-Z]/g, (c) => '-' + c.toLowerCase())
  const fallback = TOC_DEFAULTS[key]
  return {
    default: fallback,
    parseHTML: (el: HTMLElement) => {
      const raw = el.getAttribute(dataName)
      if (raw === null) return fallback
      if (typeof fallback === 'boolean') return raw === 'true'
      if (typeof fallback === 'number') return parseInt(raw, 10) || fallback
      return raw
    },
    renderHTML: (attrs: Record<string, unknown>) =>
      attrs[key] === fallback || attrs[key] === undefined ? {} : { [dataName]: String(attrs[key]) },
  }
}

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

  addAttributes() {
    return {
      display: tocAttribute('display'),
      bulletStyle: tocAttribute('bulletStyle'),
      minLevel: tocAttribute('minLevel'),
      maxLevel: tocAttribute('maxLevel'),
      sectionNumbers: tocAttribute('sectionNumbers'),
      indent: tocAttribute('indent'),
      include: tocAttribute('include'),
      exclude: tocAttribute('exclude'),
      cssClass: tocAttribute('cssClass'),
      excludeInPdf: tocAttribute('excludeInPdf'),
    }
  },

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
