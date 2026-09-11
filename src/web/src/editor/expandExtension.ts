import { Node, mergeAttributes } from '@tiptap/core'
import { ReactNodeViewRenderer } from '@tiptap/react'
import { ExpandView } from './ExpandView'

declare module '@tiptap/core' {
  interface Commands<ReturnType> {
    expand: {
      /** Wrap the current block(s) in a collapsible section. */
      setExpand: () => ReturnType
      /** Unwrap the expand around the cursor, leaving its content behind. */
      unsetExpand: () => ReturnType
    }
  }
}

/**
 * Expand: Confluence's collapsible section. Only the title is stored; whether
 * it is open is view state, not content — the author opening it to edit
 * must not publish it open for every reader.
 */
export const Expand = Node.create({
  name: 'expand',
  group: 'block',
  content: 'block+',
  defining: true,
  isolating: true,

  addAttributes() {
    return {
      title: {
        default: '',
        parseHTML: (element: HTMLElement) => element.getAttribute('data-title') ?? '',
        renderHTML: (attributes: { title?: string }) => ({ 'data-title': attributes.title ?? '' }),
      },
    }
  },

  parseHTML() {
    return [{ tag: 'div[data-type="expand"]' }]
  },

  renderHTML({ HTMLAttributes }) {
    return ['div', mergeAttributes(HTMLAttributes, { 'data-type': 'expand', class: 'expand' }), 0]
  },

  addNodeView() {
    return ReactNodeViewRenderer(ExpandView)
  },

  addCommands() {
    return {
      setExpand:
        () =>
        ({ commands }) =>
          commands.wrapIn(this.name, { title: '' }),
      unsetExpand:
        () =>
        ({ commands }) =>
          commands.lift(this.name),
    }
  },
})
