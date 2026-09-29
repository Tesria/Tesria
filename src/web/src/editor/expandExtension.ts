import { Node, mergeAttributes } from '@tiptap/core'
import { ReactNodeViewRenderer } from '@tiptap/react'
import { ExpandView } from './ExpandView'
import { markNewExpand } from './expandTitleFocus'

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
 * it is open is view state, not content: the author opening it to edit
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
        ({ commands, tr, editor, dispatch }) => {
          if (!commands.wrapIn(this.name, { title: '' })) return false
          if (dispatch) {
            // The new expand is the innermost one around the cursor.
            const { $from } = tr.selection
            for (let d = $from.depth; d > 0; d--) {
              if ($from.node(d).type.name === this.name) {
                markNewExpand(editor, $from.before(d))
                break
              }
            }
          }
          return true
        },
      unsetExpand:
        () =>
        ({ commands }) =>
          commands.lift(this.name),
    }
  },
})
