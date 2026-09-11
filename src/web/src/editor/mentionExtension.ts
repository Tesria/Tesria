import { Node, mergeAttributes } from '@tiptap/core'

declare module '@tiptap/core' {
  interface Commands<ReturnType> {
    mention: {
      insertMention: (range: { from: number; to: number }, user: { id: string; label: string }) => ReturnType
    }
  }
}

/**
 * An @mention of a user.
 *
 * The node stores the user's id *and* a snapshot of their display name. The
 * id is what the server diffs to decide who to notify
 * (`Infrastructure/Mentions`); the label is what makes the mention still
 * read as a name in an exported file, in a page version from last year, and
 * after the account is deleted — none of which can look the name up. The
 * live editor and reading view have the id, so they could re-resolve it, but
 * a stored document that only renders correctly while the app is running is
 * not a document.
 */
export const Mention = Node.create({
  name: 'mention',
  group: 'inline',
  inline: true,
  atom: true,
  selectable: true,

  addAttributes() {
    return {
      userId: {
        default: null as string | null,
        parseHTML: (element: HTMLElement) => element.getAttribute('data-user-id'),
        renderHTML: (attributes: { userId?: string | null }) =>
          attributes.userId ? { 'data-user-id': attributes.userId } : {},
      },
      label: {
        default: '',
        parseHTML: (element: HTMLElement) => element.getAttribute('data-label') ?? element.textContent ?? '',
        renderHTML: (attributes: { label?: string }) => ({ 'data-label': attributes.label ?? '' }),
      },
    }
  },

  parseHTML() {
    return [{ tag: 'span[data-type="mention"]' }]
  },

  renderHTML({ node, HTMLAttributes }) {
    const label = String(node.attrs.label ?? '').trim() || 'Unknown user'
    return ['span', mergeAttributes(HTMLAttributes, { 'data-type': 'mention', class: 'mention' }), `@${label}`]
  },

  renderText({ node }) {
    return `@${String(node.attrs.label ?? '').trim() || 'Unknown user'}`
  },

  addCommands() {
    return {
      insertMention:
        (range, user) =>
        ({ chain }) =>
          chain()
            .focus()
            .insertContentAt(range, [
              { type: this.name, attrs: { userId: user.id, label: user.label } },
              // A trailing space, so typing carries on after the mention
              // instead of landing inside it.
              { type: 'text', text: ' ' },
            ])
            .run(),
    }
  },
})
