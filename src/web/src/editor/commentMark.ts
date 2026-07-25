import { Mark, mergeAttributes } from '@tiptap/core'

/**
 * Marks a text range as having an inline comment attached (Confluence/Google
 * Docs-style "highlight and comment"). Purely a rendering/anchor concern —
 * the comment itself is a normal Comment row (already supports AnchorJson);
 * this mark is what lets the read view visually highlight the commented text
 * and is what the mark's own `commentId` attr links back to that row.
 */
export const CommentMark = Mark.create({
  name: 'comment',

  addAttributes() {
    return {
      commentId: {
        default: null,
        parseHTML: (element: HTMLElement) => element.getAttribute('data-comment-id'),
        renderHTML: (attributes: { commentId?: string | null }) =>
          attributes.commentId ? { 'data-comment-id': attributes.commentId } : {},
      },
    }
  },

  parseHTML() {
    return [{ tag: 'span[data-comment-id]' }]
  },

  renderHTML({ HTMLAttributes }) {
    return ['span', mergeAttributes(HTMLAttributes, { class: 'comment-highlight' }), 0]
  },
})
