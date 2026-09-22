import { Node, mergeAttributes } from '@tiptap/core'
import { ReactNodeViewRenderer } from '@tiptap/react'
import { ChartView } from './ChartView'

export const CHART_TYPES = ['bar', 'column', 'line', 'pie'] as const
export type ChartType = (typeof CHART_TYPES)[number]

export const CHART_TYPE_LABELS: Record<ChartType, string> = {
  bar: 'Bar (horizontal)',
  column: 'Column (vertical)',
  line: 'Line',
  pie: 'Pie',
}

export function isChartType(value: unknown): value is ChartType {
  return typeof value === 'string' && (CHART_TYPES as readonly string[]).includes(value)
}

declare module '@tiptap/core' {
  interface Commands<ReturnType> {
    chart: {
      insertChart: (type?: ChartType) => ReturnType
    }
  }
}

/**
 * A chart of a table that is already on the page.
 *
 * `source` is the table's *ordinal* on the page (1 = the first table), not
 * an id: ProseMirror nodes have no stable identity, so an id would have to
 * be minted, stored and kept unique through copy-paste, and an author
 * thinks in "the second table" anyway. The data is never copied into the
 * chart: editing the table redraws it, and there is only ever one set of
 * numbers on the page.
 *
 * Deliberately *not* a Wave D dynamic block: the table is right here in the
 * document, so a server round trip would be slower, would not see unsaved
 * edits, and would need a fourth result shape the contract does not have.
 */
export const Chart = Node.create({
  name: 'chart',
  group: 'block',
  atom: true,
  selectable: true,

  addAttributes() {
    return {
      source: {
        default: 1,
        parseHTML: (element: HTMLElement) => parseInt(element.getAttribute('data-source') ?? '1', 10) || 1,
        renderHTML: (attributes: { source?: number }) => ({ 'data-source': String(attributes.source ?? 1) }),
      },
      chartType: {
        default: 'column' as ChartType,
        parseHTML: (element: HTMLElement) => {
          const raw = element.getAttribute('data-chart-type')
          return isChartType(raw) ? raw : 'column'
        },
        renderHTML: (attributes: { chartType?: string }) => ({
          'data-chart-type': isChartType(attributes.chartType) ? attributes.chartType : 'column',
        }),
      },
      title: {
        default: '',
        parseHTML: (element: HTMLElement) => element.getAttribute('data-title') ?? '',
        renderHTML: (attributes: { title?: string }) => ({ 'data-title': attributes.title ?? '' }),
      },
    }
  },

  parseHTML() {
    return [{ tag: 'div[data-type="chart"]' }]
  },

  renderHTML({ HTMLAttributes }) {
    return ['div', mergeAttributes(HTMLAttributes, { 'data-type': 'chart', class: 'chart' })]
  },

  addNodeView() {
    return ReactNodeViewRenderer(ChartView)
  },

  addCommands() {
    return {
      insertChart:
        (type = 'column') =>
        ({ commands }) =>
          commands.insertContent({ type: this.name, attrs: { chartType: type, source: 1, title: '' } }),
    }
  },
})
