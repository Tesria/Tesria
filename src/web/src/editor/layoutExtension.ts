import { Node, findParentNode, mergeAttributes } from '@tiptap/core'
import { Fragment, type Node as PMNode, type Schema } from '@tiptap/pm/model'
import { TextSelection, type Transaction } from '@tiptap/pm/state'

/**
 * Layouts: a `layoutSection` holding two or three `layoutColumn`s, each a
 * normal block container. Confluence's set of presets, and Confluence's
 * rule that sections stack but never nest: `layoutSection` is not in the
 * `block` group, so a column's `block+` (and a panel's, and an expand's)
 * cannot hold one — only the document itself can, via `extensions.ts`'s
 * Document override.
 *
 * Column widths are stored per column as a percentage and applied as
 * flex-grow weights (`--column-width` in index.css), so the browser divides
 * the gap between them and the numbers need not sum to exactly 100.
 */
export const LAYOUT_PRESETS = {
  'two-equal': { label: 'Two columns', widths: [50, 50] },
  'three-equal': { label: 'Three columns', widths: [33.33, 33.34, 33.33] },
  'left-sidebar': { label: 'Left sidebar', widths: [33.33, 66.67] },
  'right-sidebar': { label: 'Right sidebar', widths: [66.67, 33.33] },
  'three-sidebars': { label: 'Three with sidebars', widths: [25, 50, 25] },
} as const satisfies Record<string, { label: string; widths: readonly number[] }>

export type LayoutPreset = keyof typeof LAYOUT_PRESETS
export const LAYOUT_PRESET_KEYS = Object.keys(LAYOUT_PRESETS) as LayoutPreset[]

/** Section width, on the page's own centred / wide / full scale (index.css `.layout-section--*`). */
export const LAYOUT_WIDTHS = ['default', 'wide', 'full'] as const
export type LayoutWidth = (typeof LAYOUT_WIDTHS)[number]
export const LAYOUT_WIDTH_LABELS: Record<LayoutWidth, string> = {
  default: 'Centred',
  wide: 'Wide',
  full: 'Full width',
}

function isLayoutWidth(value: unknown): value is LayoutWidth {
  return typeof value === 'string' && (LAYOUT_WIDTHS as readonly string[]).includes(value)
}

/** The preset whose widths match this section's columns, if any (after a manual edit there may be none). */
export function presetOf(section: PMNode): LayoutPreset | null {
  const widths: number[] = []
  section.forEach((col) => widths.push(Math.round(Number(col.attrs.width))))
  return (
    LAYOUT_PRESET_KEYS.find((key) => {
      const p = LAYOUT_PRESETS[key].widths
      return p.length === widths.length && p.every((w, i) => Math.round(w) === widths[i])
    }) ?? null
  )
}

const findSection = findParentNode((n) => n.type.name === 'layoutSection')

function buildSection(schema: Schema, widths: readonly number[], contents: Fragment[], attrs: Record<string, unknown>) {
  const columns = widths.map((width, i) => {
    const content = contents[i] && contents[i].size > 0 ? contents[i] : Fragment.from(schema.nodes.paragraph.create())
    return schema.nodes.layoutColumn.create({ width }, content)
  })
  return schema.nodes.layoutSection.create(attrs, columns)
}

/** Redistribute existing column content across `count` columns: extra columns' content folds into the last. */
function redistribute(section: PMNode, count: number): Fragment[] {
  const existing: Fragment[] = []
  section.forEach((col) => existing.push(col.content))
  return Array.from({ length: count }, (_, i) =>
    i < count - 1
      ? (existing[i] ?? Fragment.empty)
      : existing.slice(i).reduce((acc, f) => acc.append(f), Fragment.empty),
  )
}

/** Place the cursor at the start of the first column's first block. */
function selectFirstColumn(tr: Transaction, sectionPos: number) {
  tr.setSelection(TextSelection.near(tr.doc.resolve(sectionPos + 3)))
}

declare module '@tiptap/core' {
  interface Commands<ReturnType> {
    layout: {
      /**
       * Insert a new section below the current top-level block (replacing it
       * when it is an empty paragraph), after deleting `range` if given.
       */
      insertLayout: (preset: LayoutPreset, range?: { from: number; to: number }) => ReturnType
      /** Re-shape the section around the cursor; content is kept, folding surplus columns into the last. */
      setLayoutPreset: (preset: LayoutPreset) => ReturnType
      setLayoutWidth: (width: LayoutWidth) => ReturnType
      /** Unwrap the section: its columns' content stacks in order where it stood. */
      removeLayout: () => ReturnType
    }
  }
}

export const LayoutColumn = Node.create({
  name: 'layoutColumn',
  content: 'block+',
  isolating: true,
  defining: true,

  addAttributes() {
    return {
      width: {
        default: 50,
        parseHTML: (element: HTMLElement) => parseFloat(element.getAttribute('data-width') ?? '') || 50,
        renderHTML: (attributes: { width?: number }) => {
          const width = Number(attributes.width) > 0 ? Number(attributes.width) : 50
          return { 'data-width': String(width), style: `--column-width: ${width}` }
        },
      },
    }
  },

  parseHTML() {
    return [{ tag: 'div[data-type="layout-column"]' }]
  },

  renderHTML({ HTMLAttributes }) {
    return ['div', mergeAttributes(HTMLAttributes, { 'data-type': 'layout-column', class: 'layout-column' }), 0]
  },
})

export const LayoutSection = Node.create({
  name: 'layoutSection',
  content: 'layoutColumn{2,3}',
  isolating: true,
  defining: true,
  selectable: false,

  addAttributes() {
    return {
      width: {
        default: 'default' as LayoutWidth,
        parseHTML: (element: HTMLElement) => {
          const raw = element.getAttribute('data-width')
          return isLayoutWidth(raw) ? raw : 'default'
        },
        renderHTML: (attributes: { width?: string }) => ({
          'data-width': isLayoutWidth(attributes.width) ? attributes.width : 'default',
        }),
      },
    }
  },

  parseHTML() {
    return [{ tag: 'div[data-type="layout-section"]' }]
  },

  renderHTML({ node, HTMLAttributes }) {
    const width = isLayoutWidth(node.attrs.width) ? node.attrs.width : 'default'
    return [
      'div',
      mergeAttributes(HTMLAttributes, { 'data-type': 'layout-section', class: `layout-section layout-section--${width}` }),
      0,
    ]
  },

  addCommands() {
    return {
      insertLayout:
        (preset, range) =>
        ({ tr, state, dispatch }) => {
          if (range) tr.delete(range.from, range.to)
          const $from = tr.selection.$from
          let from: number
          let to: number
          if ($from.depth === 0) {
            from = to = $from.pos
          } else {
            const block = $from.node(1)
            from = block.type.name === 'paragraph' && block.content.size === 0 ? $from.before(1) : $from.after(1)
            to = block.type.name === 'paragraph' && block.content.size === 0 ? $from.after(1) : from
          }
          if (dispatch) {
            const section = buildSection(state.schema, LAYOUT_PRESETS[preset].widths, [], { width: 'default' })
            tr.replaceWith(from, to, section)
            selectFirstColumn(tr, from)
            tr.scrollIntoView()
          }
          return true
        },
      setLayoutPreset:
        (preset) =>
        ({ tr, state, dispatch }) => {
          const found = findSection(state.selection)
          if (!found) return false
          if (dispatch) {
            const widths = LAYOUT_PRESETS[preset].widths
            const section = buildSection(state.schema, widths, redistribute(found.node, widths.length), found.node.attrs)
            tr.replaceWith(found.pos, found.pos + found.node.nodeSize, section)
            selectFirstColumn(tr, found.pos)
          }
          return true
        },
      setLayoutWidth:
        (width) =>
        ({ commands }) =>
          commands.updateAttributes('layoutSection', { width }),
      removeLayout:
        () =>
        ({ tr, state, dispatch }) => {
          const found = findSection(state.selection)
          if (!found) return false
          if (dispatch) {
            const content = redistribute(found.node, 1)[0]
            tr.replaceWith(found.pos, found.pos + found.node.nodeSize, content)
            tr.setSelection(TextSelection.near(tr.doc.resolve(found.pos + 1)))
          }
          return true
        },
    }
  },
})
