import { useId, useMemo, useState, type ReactNode } from 'react'
import { AppearancePicker } from './AppearancePicker'
import { appearanceData } from './appearance'
import { NodeViewWrapper, useEditorState, type ReactNodeViewProps } from '@tiptap/react'
import type { Node as PMNode } from '@tiptap/pm/model'
import {
  CHART_SIZES, CHART_SIZE_LABELS, CHART_TYPES, CHART_TYPE_LABELS, LEGEND_LABELS, LEGEND_POSITIONS, isChartType,
  type ChartSize, type ChartType, type LegendPosition,
} from './chartExtension'
import {
  DONUT_CENTERS, DONUT_CENTER_LABELS, NUMBER_FORMATS, NUMBER_FORMAT_LABELS, detectFormat, isDonutCenter,
  isNumberFormatChoice, parseSeriesColors, resolveFormat, serializeSeriesColors, seriesColor, tableToChart, COLORS,
} from './chartData'
import { ChartPlot, type PlotOptions } from './ChartPlot'

/**
 * The cells of the nth table on the page, as text, row by row. The chart
 * works from these (chartData.ts), so the table stays the one place the
 * numbers live.
 */
function readTableCells(doc: PMNode, ordinal: number): string[][] | null {
  let found: PMNode | null = null
  let seen = 0
  doc.descendants((node) => {
    if (node.type.name !== 'table') return
    seen += 1
    if (seen === ordinal) found = node
  })
  if (!found) return null
  const rows: string[][] = []
  ;(found as PMNode).forEach((row) => {
    const cells: string[] = []
    row.forEach((cell) => cells.push(cell.textContent.trim()))
    if (cells.length > 0) rows.push(cells)
  })
  return rows
}

const oneOf = <T extends string>(values: readonly T[], value: unknown, fallback: T): T =>
  typeof value === 'string' && (values as readonly string[]).includes(value) ? (value as T) : fallback

export function ChartView({ node, editor, selected, updateAttributes }: ReactNodeViewProps) {
  const attrs = node.attrs
  const source = Number(attrs.source) || 1
  const type: ChartType = isChartType(attrs.chartType) ? attrs.chartType : 'column'
  const title = String(attrs.title ?? '')
  const transpose = Boolean(attrs.transpose)
  const numberFormat = isNumberFormatChoice(attrs.numberFormat) ? attrs.numberFormat : 'auto'
  const [moreOpen, setMoreOpen] = useState(false)
  const moreId = useId()

  // Re-read whenever the document changes, so editing the table redraws the
  // chart: the whole reason the data is not copied in.
  const cells = useEditorState({
    editor,
    selector: ({ editor }) => readTableCells(editor.state.doc, source),
    equalityFn: (a, b) => b !== null && JSON.stringify(a) === JSON.stringify(b),
  })
  const tableCount = useEditorState({
    editor,
    selector: ({ editor }) => {
      let n = 0
      editor.state.doc.descendants((node) => { if (node.type.name === 'table') n += 1 })
      return n
    },
  })
  const data = useMemo(() => (cells ? tableToChart(cells, transpose) : null), [cells, transpose])
  const format = useMemo(() => resolveFormat(numberFormat, detectFormat(cells ?? [])), [cells, numberFormat])

  const options: PlotOptions = {
    legend: oneOf<LegendPosition>(LEGEND_POSITIONS, attrs.legend, 'below'),
    size: oneOf<ChartSize>(CHART_SIZES, attrs.chartSize, 'medium'),
    axes: attrs.axes !== false,
    categoryTitle: String(attrs.categoryTitle ?? ''),
    valueTitle: String(attrs.valueTitle ?? ''),
    valueLabels: Boolean(attrs.valueLabels),
    points: Boolean(attrs.points),
    smooth: Boolean(attrs.smooth),
    area: Boolean(attrs.area),
    fromZero: attrs.fromZero !== false,
    stacked: Boolean(attrs.stacked),
    legendValues: attrs.legendValues !== false,
    dataColumn: Math.max(1, Number(attrs.dataColumn) || 1),
    largestFirst: Boolean(attrs.largestFirst),
    donutCenter: isDonutCenter(attrs.donutCenter) ? attrs.donutCenter : 'total',
    centerText: String(attrs.centerText ?? ''),
    colors: parseSeriesColors(attrs.seriesColors),
  }

  return (
    <NodeViewWrapper
      className={selected ? 'chart is-selected' : 'chart'}
      contentEditable={false}
      {...appearanceData(attrs.appearance)}
    >
      {editor.isEditable && (
        <>
          <div className="chart__controls">
            <label>
              <span>Table</span>
              <select value={String(source)} onChange={(e) => updateAttributes({ source: Number(e.target.value) })}>
                {Array.from({ length: Math.max(tableCount, 1) }, (_, i) => (
                  <option key={i + 1} value={i + 1}>{`Table ${i + 1}`}</option>
                ))}
              </select>
            </label>
            <label>
              <span>Type</span>
              <select value={type} onChange={(e) => updateAttributes({ chartType: e.target.value })}>
                {CHART_TYPES.map((t) => <option key={t} value={t}>{CHART_TYPE_LABELS[t]}</option>)}
              </select>
            </label>
            <AppearancePicker value={attrs.appearance} onChange={(appearance) => updateAttributes({ appearance })} />
            <input
              className="chart__title-input"
              value={title}
              placeholder="Chart Title (Optional)"
              aria-label="Chart Title"
              onChange={(e) => updateAttributes({ title: e.target.value })}
            />
            <button
              type="button"
              className={moreOpen ? 'chart__more is-open' : 'chart__more'}
              aria-expanded={moreOpen}
              aria-controls={moreId}
              onClick={() => setMoreOpen((open) => !open)}
            >
              More Options
            </button>
          </div>
          {moreOpen && (
            <div className="chart__options" id={moreId} role="group" aria-label="More Chart Options">
              <MoreOptions type={type} options={options} numberFormat={numberFormat} transpose={transpose}
                seriesNames={data?.series.map((s) => s.name) ?? []} categories={data?.categories ?? []} update={updateAttributes} />
            </div>
          )}
        </>
      )}
      {title && <p className="chart__title">{title}</p>}
      {!data && (
        <p className="chart__note">
          {tableCount === 0
            ? 'Add a table to this page, then point this chart at it.'
            : `Table ${source} has no numbers to chart.`}
        </p>
      )}
      {data && <ChartPlot data={data} type={type} options={options} format={format} title={title} />}
    </NodeViewWrapper>
  )
}

/**
 * The less-used options, behind "More Options" so the row above stays one
 * tidy line. Only the options that apply to the chart's type are shown;
 * the others keep their values, so switching type and back loses nothing.
 */
function MoreOptions({ type, options, numberFormat, transpose, seriesNames, categories, update }: {
  type: ChartType
  options: PlotOptions
  numberFormat: string
  transpose: boolean
  seriesNames: string[]
  categories: string[]
  update: (attrs: Record<string, unknown>) => void
}) {
  const round = type === 'pie' || type === 'donut'
  const line = type === 'line'
  // The axis along the bottom is the categories' on a column or line chart,
  // and the values' on a horizontal bar chart. Stored by what each axis
  // holds, so switching between column and bar keeps each title with its axis.
  const xKey = type === 'bar' ? 'valueTitle' : 'categoryTitle'
  const yKey = type === 'bar' ? 'categoryTitle' : 'valueTitle'
  return (
    <>
      <Field label="Legend">
        {(id) => (
          <select id={id} value={options.legend} onChange={(e) => update({ legend: e.target.value })}>
            {LEGEND_POSITIONS.map((p) => <option key={p} value={p}>{LEGEND_LABELS[p]}</option>)}
          </select>
        )}
      </Field>
      <Field label="Size">
        {(id) => (
          <select id={id} value={options.size} onChange={(e) => update({ chartSize: e.target.value })}>
            {CHART_SIZES.map((s) => <option key={s} value={s}>{CHART_SIZE_LABELS[s]}</option>)}
          </select>
        )}
      </Field>
      <Field label="Number Format">
        {(id) => (
          <select id={id} value={numberFormat} onChange={(e) => update({ numberFormat: e.target.value })}
            title="Auto uses a % or a currency symbol when the table's numbers have one">
            {NUMBER_FORMATS.map((f) => <option key={f} value={f}>{NUMBER_FORMAT_LABELS[f]}</option>)}
          </select>
        )}
      </Field>
      <Check label="Swap Rows and Columns" checked={transpose} onChange={(v) => update({ transpose: v })} />

      {round && seriesNames.length > 1 && (
        <Field label={transpose ? 'Row' : 'Column'}>
          {(id) => (
            <select id={id} value={String(Math.min(options.dataColumn, seriesNames.length))}
              onChange={(e) => update({ dataColumn: Number(e.target.value) })}>
              {seriesNames.map((name, i) => <option key={i} value={i + 1}>{name}</option>)}
            </select>
          )}
        </Field>
      )}
      {round && <Check label="Values in Legend" checked={options.legendValues} onChange={(v) => update({ legendValues: v })} />}
      {round && <Check label="Largest Slice First" checked={options.largestFirst} onChange={(v) => update({ largestFirst: v })} />}
      {type === 'donut' && (
        <Field label="Center">
          {(id) => (
            <select id={id} value={options.donutCenter} onChange={(e) => update({ donutCenter: e.target.value })}>
              {DONUT_CENTERS.map((c) => <option key={c} value={c}>{DONUT_CENTER_LABELS[c]}</option>)}
            </select>
          )}
        </Field>
      )}
      {type === 'donut' && options.donutCenter === 'custom' && (
        <Field label="Center Text">
          {(id) => (
            <input id={id} className="chart__short-input" value={options.centerText} maxLength={16} placeholder="Such as Q3"
              onChange={(e) => update({ centerText: e.target.value })} />
          )}
        </Field>
      )}

      {!round && <Check label="Axes and Gridlines" checked={options.axes} onChange={(v) => update({ axes: v })} />}
      {!round && <Check label="Value Labels" checked={options.valueLabels} onChange={(v) => update({ valueLabels: v })} />}
      {!round && (
        <Field label="X Axis Title">
          {(id) => (
            <input id={id} className="chart__short-input" value={options[xKey]} placeholder="Optional"
              onChange={(e) => update({ [xKey]: e.target.value })} />
          )}
        </Field>
      )}
      {!round && (
        <Field label="Y Axis Title">
          {(id) => (
            <input id={id} className="chart__short-input" value={options[yKey]} placeholder="Optional"
              onChange={(e) => update({ [yKey]: e.target.value })} />
          )}
        </Field>
      )}
      {line && <Check label="Points" checked={options.points} onChange={(v) => update({ points: v })} />}
      {line && <Check label="Smooth Curve" checked={options.smooth} onChange={(v) => update({ smooth: v })} />}
      {line && <Check label="Filled Area" checked={options.area} onChange={(v) => update({ area: v })} />}
      {line && (
        <Field label="Y Axis">
          {(id) => (
            <select id={id} value={options.fromZero ? 'zero' : 'fit'} onChange={(e) => update({ fromZero: e.target.value === 'zero' })}>
              <option value="zero">From Zero</option>
              <option value="fit">Fit to Data</option>
            </select>
          )}
        </Field>
      )}
      {(type === 'bar' || type === 'column') && (
        <Check label="Stacked" checked={options.stacked} onChange={(v) => update({ stacked: v })} />
      )}
      {(type === 'bar' || type === 'column') && <span className="chart__hint">Bars always start at zero.</span>}

      {/* A color per series, or per slice on a pie or a donut (the owner,
          2026-09-29). Keyed by name, so a color stays with its series when
          rows move; Reset goes back to the palette. */}
      <SeriesColors names={round ? categories : seriesNames} chosen={options.colors}
        onChange={(colors) => update({ seriesColors: serializeSeriesColors(colors) })} />
    </>
  )
}

function Field({ label, children }: { label: string; children: (id: string) => ReactNode }) {
  const id = useId()
  return (
    <span className="chart__field">
      <label htmlFor={id}>{label}</label>
      {children(id)}
    </span>
  )
}

function Check({ label, checked, onChange }: { label: string; checked: boolean; onChange: (value: boolean) => void }) {
  return (
    <label className="chart__check">
      <input type="checkbox" checked={checked} onChange={(e) => onChange(e.target.checked)} />
      <span>{label}</span>
    </label>
  )
}

function SeriesColors({ names, chosen, onChange }: {
  names: string[]
  chosen: Record<string, string>
  onChange: (colors: Record<string, string>) => void
}) {
  if (names.length === 0) return null
  return (
    <div className="chart__colors" role="group" aria-label="Colors">
      <span className="chart__colors-title">Colors</span>
      {names.map((name, i) => (
        <SeriesColor key={`${i}-${name}`} name={name} value={seriesColor(chosen, name, i, COLORS)} custom={name in chosen}
          onChange={(color) => onChange({ ...chosen, [name]: color })}
          onReset={() => { const next = { ...chosen }; delete next[name]; onChange(next) }} />
      ))}
    </div>
  )
}

function SeriesColor({ name, value, custom, onChange, onReset }: {
  name: string
  value: string
  custom: boolean
  onChange: (color: string) => void
  onReset: () => void
}) {
  const id = useId()
  const label = name || 'Unnamed'
  return (
    <span className="chart__color">
      <input id={id} type="color" value={value} onChange={(e) => onChange(e.target.value)} aria-label={`Color for ${label}`} />
      <label htmlFor={id}>{label}</label>
      {custom && (
        <button type="button" className="chart__color-reset" onClick={onReset} aria-label={`Reset the color for ${label}`} title="Back to the palette color">
          Reset
        </button>
      )}
    </span>
  )
}
