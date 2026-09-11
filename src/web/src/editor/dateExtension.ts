import { Node, mergeAttributes } from '@tiptap/core'

const ISO_DATE = /^(\d{4})-(\d{2})-(\d{2})$/

/** Today as `yyyy-mm-dd` in the viewer's own calendar, not UTC's. */
export function todayIso(): string {
  const d = new Date()
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`
}

export function isIsoDate(value: unknown): value is string {
  if (typeof value !== 'string') return false
  const m = ISO_DATE.exec(value)
  if (!m) return false
  const [, y, mo, d] = m.map(Number)
  const date = new Date(y, mo - 1, d)
  return date.getFullYear() === y && date.getMonth() === mo - 1 && date.getDate() === d
}

/**
 * "10 Sept 2026" in the viewer's locale. The parts are parsed by hand rather
 * than handed to `new Date(iso)`, which treats a bare date as UTC midnight
 * and shows the day before to anyone west of Greenwich.
 */
export function formatDate(iso: string): string {
  if (!isIsoDate(iso)) return iso
  const [y, m, d] = iso.split('-').map(Number)
  return new Date(y, m - 1, d).toLocaleDateString(undefined, { day: 'numeric', month: 'short', year: 'numeric' })
}

declare module '@tiptap/core' {
  interface Commands<ReturnType> {
    date: {
      /** Insert a date (today, unless given) at `range` or the selection, and select it so the picker opens. */
      insertDate: (range?: { from: number; to: number }, date?: string) => ReturnType
    }
  }
}

/** An inline date chip. Stored as an ISO calendar date; rendered in the viewer's locale. */
export const DateChip = Node.create({
  name: 'date',
  group: 'inline',
  inline: true,
  atom: true,
  selectable: true,

  addAttributes() {
    return {
      date: {
        default: null as string | null,
        parseHTML: (element: HTMLElement) => {
          const raw = element.getAttribute('data-date')
          return isIsoDate(raw) ? raw : todayIso()
        },
        renderHTML: (attributes: { date?: string | null }) => ({
          'data-date': isIsoDate(attributes.date) ? attributes.date : '',
        }),
      },
    }
  },

  parseHTML() {
    return [{ tag: 'span[data-type="date"]' }]
  },

  renderHTML({ node, HTMLAttributes }) {
    const iso = isIsoDate(node.attrs.date) ? node.attrs.date : ''
    return [
      'span',
      mergeAttributes(HTMLAttributes, { 'data-type': 'date', class: 'date-chip' }),
      iso ? formatDate(iso) : 'Pick a date',
    ]
  },

  addCommands() {
    return {
      insertDate:
        (range, date) =>
        ({ chain, state }) => {
          const at = range ?? { from: state.selection.from, to: state.selection.to }
          return chain()
            .insertContentAt(at, { type: this.name, attrs: { date: isIsoDate(date) ? date : todayIso() } })
            .setNodeSelection(at.from)
            .run()
        },
    }
  },
})
