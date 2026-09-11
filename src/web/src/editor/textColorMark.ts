import { Mark, mergeAttributes } from '@tiptap/core'

/**
 * Text colour, stored as a colour *name* out of a fixed set rather than a
 * hex value.
 *
 * The highlight mark stores a hex because a highlight is a background: the
 * text on top of it stays legible in dark mode by pinning the ink (see
 * index.css's `[data-theme="dark"] … mark[style*="background-color"]`).
 * Coloured *text* has no such escape — a hex dark enough to read on white
 * is invisible on this app's dark background, and no CSS rule can lighten a
 * colour it cannot see. A name can be re-pointed per theme, so that is what
 * the document carries; `index.css` maps it (`--text-color-*`) and the
 * export renderer inlines the light-theme hex for a standalone file.
 *
 * The cost is that the palette is fixed. Confluence's own picker is
 * effectively fixed too (dev-plan Phase 7 Wave B says "palette-limited"),
 * and a name can never carry CSS into an exported `style` attribute.
 */
export const TEXT_COLORS = ['grey', 'blue', 'teal', 'green', 'yellow', 'orange', 'red', 'purple'] as const
export type TextColor = (typeof TEXT_COLORS)[number]

export const TEXT_COLOR_LABELS: Record<TextColor, string> = {
  grey: 'Grey',
  blue: 'Blue',
  teal: 'Teal',
  green: 'Green',
  yellow: 'Yellow',
  orange: 'Orange',
  red: 'Red',
  purple: 'Purple',
}

export function isTextColor(value: unknown): value is TextColor {
  return typeof value === 'string' && (TEXT_COLORS as readonly string[]).includes(value)
}

declare module '@tiptap/core' {
  interface Commands<ReturnType> {
    textColor: {
      setTextColor: (color: TextColor) => ReturnType
      unsetTextColor: () => ReturnType
    }
  }
}

export const TextColorMark = Mark.create({
  name: 'textColor',

  addAttributes() {
    return {
      color: {
        default: 'grey' as TextColor,
        parseHTML: (element: HTMLElement) => {
          const raw = element.getAttribute('data-text-color')
          return isTextColor(raw) ? raw : 'grey'
        },
        renderHTML: (attributes: { color?: string }) => ({
          'data-text-color': isTextColor(attributes.color) ? attributes.color : 'grey',
        }),
      },
    }
  },

  parseHTML() {
    return [{ tag: 'span[data-text-color]' }]
  },

  renderHTML({ HTMLAttributes }) {
    return ['span', mergeAttributes(HTMLAttributes, { class: 'text-color' }), 0]
  },

  addCommands() {
    return {
      setTextColor:
        (color) =>
        ({ commands }) =>
          commands.setMark(this.name, { color }),
      unsetTextColor:
        () =>
        ({ commands }) =>
          commands.unsetMark(this.name),
    }
  },
})
