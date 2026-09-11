import { Node, mergeAttributes } from '@tiptap/core'

/**
 * Status lozenge: Confluence's six colours, by name. Stored as a name and
 * mapped to the palette in index.css (`.status--*`) and in the export
 * renderer, so a theme change re-tints every status and no colour value
 * from the document ever reaches a style attribute.
 */
export const STATUS_COLORS = ['grey', 'red', 'yellow', 'green', 'blue', 'purple'] as const
export type StatusColor = (typeof STATUS_COLORS)[number]

export const STATUS_LABELS: Record<StatusColor, string> = {
  grey: 'Grey',
  red: 'Red',
  yellow: 'Yellow',
  green: 'Green',
  blue: 'Blue',
  purple: 'Purple',
}

export function isStatusColor(value: unknown): value is StatusColor {
  return typeof value === 'string' && (STATUS_COLORS as readonly string[]).includes(value)
}

declare module '@tiptap/core' {
  interface Commands<ReturnType> {
    status: {
      /** Insert a status at `range` (or the selection) and select it, so the edit bubble opens. */
      insertStatus: (range?: { from: number; to: number }, attrs?: { text?: string; color?: StatusColor }) => ReturnType
    }
  }
}

export const Status = Node.create({
  name: 'status',
  group: 'inline',
  inline: true,
  atom: true,
  selectable: true,

  addAttributes() {
    return {
      text: {
        default: 'STATUS',
        parseHTML: (element: HTMLElement) => element.getAttribute('data-text') ?? element.textContent ?? '',
        renderHTML: (attributes: { text?: string }) => ({ 'data-text': attributes.text ?? '' }),
      },
      color: {
        default: 'grey' as StatusColor,
        parseHTML: (element: HTMLElement) => {
          const raw = element.getAttribute('data-color')
          return isStatusColor(raw) ? raw : 'grey'
        },
        renderHTML: (attributes: { color?: string }) => ({
          'data-color': isStatusColor(attributes.color) ? attributes.color : 'grey',
        }),
      },
    }
  },

  parseHTML() {
    return [{ tag: 'span[data-type="status"]' }]
  },

  renderHTML({ node, HTMLAttributes }) {
    const color = isStatusColor(node.attrs.color) ? node.attrs.color : 'grey'
    const text = String(node.attrs.text ?? '').trim() || 'STATUS'
    return ['span', mergeAttributes(HTMLAttributes, { 'data-type': 'status', class: `status status--${color}` }), text]
  },

  addCommands() {
    return {
      insertStatus:
        (range, attrs) =>
        ({ chain, state }) => {
          const at = range ?? { from: state.selection.from, to: state.selection.to }
          return chain()
            .insertContentAt(at, { type: this.name, attrs: { text: attrs?.text ?? 'STATUS', color: attrs?.color ?? 'grey' } })
            .setNodeSelection(at.from)
            .run()
        },
    }
  },
})
