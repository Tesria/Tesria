import { Node, mergeAttributes } from '@tiptap/core'
import type { Editor } from '@tiptap/core'

declare module '@tiptap/core' {
  interface Commands<ReturnType> {
    excerpt: {
      /** Mark the current block(s) as this page's excerpt. */
      setExcerpt: () => ReturnType
      unsetExcerpt: () => ReturnType
    }
  }
}

/**
 * Excerpt: the piece of a page that other pages include (the
 * `excerpt-include` dynamic block takes the *first* one). A plain static
 * container (no query, no node view) so it costs the schema one node and
 * the export renderer nothing: an exported page shows its excerpt as
 * ordinary content, because that is what it is.
 */
export const Excerpt = Node.create({
  name: 'excerpt',
  group: 'block',
  content: 'block+',
  defining: true,

  parseHTML() {
    return [{ tag: 'div[data-type="excerpt"]' }]
  },

  renderHTML({ HTMLAttributes }) {
    return ['div', mergeAttributes(HTMLAttributes, { 'data-type': 'excerpt', class: 'excerpt' }), 0]
  },

  addCommands() {
    return {
      setExcerpt: () => ({ commands }) => commands.wrapIn(this.name),
      unsetExcerpt: () => ({ commands }) => commands.lift(this.name),
    }
  },
})

/**
 * Page properties: a two-column table (key, value) that the
 * `page-properties-report` block reads across pages. Also a static
 * container: the table inside is a normal table, so it is edited with the
 * table controls everyone already knows.
 */
export const PageProperties = Node.create({
  name: 'pageProperties',
  group: 'block',
  content: 'block+',
  defining: true,

  parseHTML() {
    return [{ tag: 'div[data-type="page-properties"]' }]
  },

  renderHTML({ HTMLAttributes }) {
    return ['div', mergeAttributes(HTMLAttributes, { 'data-type': 'page-properties', class: 'page-properties' }), 0]
  },
})

/**
 * Inserts a page-properties block seeded with a two-column table, because
 * an empty one is useless and the report only reads two-column rows.
 */
export function insertPageProperties(editor: Editor): boolean {
  const row = (key: string, value: string) => ({
    type: 'tableRow',
    content: [
      { type: 'tableCell', content: [{ type: 'paragraph', content: key ? [{ type: 'text', text: key }] : [] }] },
      { type: 'tableCell', content: [{ type: 'paragraph', content: value ? [{ type: 'text', text: value }] : [] }] },
    ],
  })
  return editor.chain().focus().insertContent({
    type: 'pageProperties',
    content: [{ type: 'table', content: [row('Status', ''), row('Owner', '')] }],
  }).run()
}
