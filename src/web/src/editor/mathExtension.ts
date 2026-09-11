import { Node, mergeAttributes } from '@tiptap/core'
import { ReactNodeViewRenderer } from '@tiptap/react'
import { MathView } from './MathView'

declare module '@tiptap/core' {
  interface Commands<ReturnType> {
    math: {
      /** Insert inline math at the selection, or a display block on its own line. */
      insertMath: (display?: boolean, latex?: string) => ReturnType
    }
  }
}

/**
 * LaTeX maths, inline or display.
 *
 * One node type with a `display` flag rather than two: the content, the
 * editing affordance and the export are identical, and only the rendering
 * differs by a KaTeX option. The document stores the LaTeX source, so it is
 * still readable, still diffable, and still exportable when KaTeX is not
 * available.
 */
export const Math = Node.create({
  name: 'math',
  // Inline by default; `display` lifts it visually without changing where it
  // may appear, so a display equation can still sit inside a paragraph the
  // way an author typed it.
  group: 'inline',
  inline: true,
  atom: true,
  selectable: true,

  addAttributes() {
    return {
      latex: {
        default: '',
        parseHTML: (element: HTMLElement) => element.getAttribute('data-latex') ?? element.textContent ?? '',
        renderHTML: (attributes: { latex?: string }) => ({ 'data-latex': attributes.latex ?? '' }),
      },
      display: {
        default: false,
        parseHTML: (element: HTMLElement) => element.getAttribute('data-display') === 'true',
        renderHTML: (attributes: { display?: boolean }) =>
          attributes.display ? { 'data-display': 'true' } : {},
      },
    }
  },

  parseHTML() {
    return [{ tag: 'span[data-type="math"]' }]
  },

  renderHTML({ node, HTMLAttributes }) {
    // Read-only rendering with no node view still shows the source, which is
    // what an export does too.
    const latex = String(node.attrs.latex ?? '')
    return ['span', mergeAttributes(HTMLAttributes, { 'data-type': 'math', class: 'math' }),
      node.attrs.display ? `$$${latex}$$` : `$${latex}$`]
  },

  addNodeView() {
    return ReactNodeViewRenderer(MathView)
  },

  addCommands() {
    return {
      insertMath:
        (display = false, latex = '') =>
        ({ chain, state }) => {
          const { from, to } = state.selection
          return chain()
            .focus()
            .insertContentAt({ from, to }, { type: this.name, attrs: { latex, display } })
            .setNodeSelection(from)
            .run()
        },
    }
  },
})
