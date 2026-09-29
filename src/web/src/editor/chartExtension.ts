import { Node, mergeAttributes } from '@tiptap/core'
import { ReactNodeViewRenderer } from '@tiptap/react'
import { ChartView } from './ChartView'
import { appearanceAttribute } from './appearance'
import { DONUT_CENTERS, NUMBER_FORMATS } from './chartData'

// Donut added in 0.8.0 as a type of its own: a new stored value, so pages
// that chose pie keep their pies.
export const CHART_TYPES = ['bar', 'column', 'line', 'pie', 'donut'] as const
export type ChartType = (typeof CHART_TYPES)[number]

export const CHART_TYPE_LABELS: Record<ChartType, string> = {
  bar: 'Bar (Horizontal)',
  column: 'Column (Vertical)',
  line: 'Line',
  pie: 'Pie',
  donut: 'Donut',
}

export function isChartType(value: unknown): value is ChartType {
  return typeof value === 'string' && (CHART_TYPES as readonly string[]).includes(value)
}

/** Where a chart's legend goes (chart options, 2026-09-29). */
export const LEGEND_POSITIONS = ['below', 'right', 'none'] as const
export type LegendPosition = (typeof LEGEND_POSITIONS)[number]
export const LEGEND_LABELS: Record<LegendPosition, string> = { below: 'Below', right: 'Beside', none: 'Hidden' }

/** How large a chart is drawn; medium is the size every chart had before. */
export const CHART_SIZES = ['small', 'medium', 'large'] as const
export type ChartSize = (typeof CHART_SIZES)[number]
export const CHART_SIZE_LABELS: Record<ChartSize, string> = { small: 'Small', medium: 'Medium', large: 'Large' }

/*
 * The chart options (2026-09-29) are all plain attributes with defaults, so a
 * chart saved before them reads exactly as before apart from the two new
 * defaults (axes on, legend values on). Each is written to the HTML as a
 * data-* attribute only when it differs from its default, so a chart that
 * uses none of them is written as it always was.
 */
function flag(key: string, name: string, fallback: boolean) {
  return {
    default: fallback,
    parseHTML: (element: HTMLElement) => {
      const raw = element.getAttribute(name)
      return raw === null ? fallback : raw !== 'false'
    },
    renderHTML: (attributes: Record<string, unknown>) =>
      Boolean(attributes[key] ?? fallback) === fallback ? {} : { [name]: String(Boolean(attributes[key])) },
  }
}

function choice<T extends string>(key: string, name: string, values: readonly T[], fallback: T) {
  const valid = (v: unknown): v is T => typeof v === 'string' && (values as readonly string[]).includes(v)
  return {
    default: fallback,
    parseHTML: (element: HTMLElement) => {
      const raw = element.getAttribute(name)
      return valid(raw) ? raw : fallback
    },
    renderHTML: (attributes: Record<string, unknown>) =>
      valid(attributes[key]) && attributes[key] !== fallback ? { [name]: attributes[key] as string } : {},
  }
}

function words(key: string, name: string) {
  return {
    default: '',
    parseHTML: (element: HTMLElement) => element.getAttribute(name) ?? '',
    renderHTML: (attributes: Record<string, unknown>) => {
      const value = String(attributes[key] ?? '')
      return value ? { [name]: value } : {}
    },
  }
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
      // Flat or glass for this chart alone (0.8.1).
      appearance: appearanceAttribute,

      // ---- Chart options (2026-09-29). Every chart:
      /** Legend below the chart, beside it, or hidden. */
      legend: choice('legend', 'data-legend', LEGEND_POSITIONS, 'below'),
      /** Chart the table's rows as the series instead of its columns. */
      transpose: flag('transpose', 'data-transpose', false),
      chartSize: choice('chartSize', 'data-size', CHART_SIZES, 'medium'),
      /** Auto finds %, or a currency symbol, in the table's cells. */
      numberFormat: choice('numberFormat', 'data-number-format', NUMBER_FORMATS, 'auto'),
      // Line, bar and column:
      /** Axes with tick values and gridlines. On by default, so older charts gain them. */
      axes: flag('axes', 'data-axes', true),
      /** The title of the axis the categories run along (the first column), and of the value axis. */
      categoryTitle: words('categoryTitle', 'data-category-title'),
      valueTitle: words('valueTitle', 'data-value-title'),
      /** Each bar's or point's value written on the chart. */
      valueLabels: flag('valueLabels', 'data-value-labels', false),
      // Line only:
      points: flag('points', 'data-points', false),
      smooth: flag('smooth', 'data-smooth', false),
      area: flag('area', 'data-area', false),
      /** The value axis from zero, or fitted to the data. Bars always start at zero. */
      fromZero: flag('fromZero', 'data-from-zero', true),
      // Bar and column only:
      stacked: flag('stacked', 'data-stacked', false),
      // Pie and donut:
      /** Each slice's number and share beside its name. */
      legendValues: flag('legendValues', 'data-legend-values', true),
      /** Which series a pie is cut from, counting from 1: the first column, as before. */
      dataColumn: {
        default: 1,
        parseHTML: (element: HTMLElement) => Math.max(1, parseInt(element.getAttribute('data-column') ?? '1', 10) || 1),
        renderHTML: (attributes: { dataColumn?: number }) =>
          Number(attributes.dataColumn ?? 1) > 1 ? { 'data-column': String(attributes.dataColumn) } : {},
      },
      largestFirst: flag('largestFirst', 'data-largest-first', false),
      /** The middle of a donut: the total (as before), the largest slice's share, or custom text. */
      donutCenter: choice('donutCenter', 'data-donut-center', DONUT_CENTERS, 'total'),
      centerText: words('centerText', 'data-center-text'),
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
