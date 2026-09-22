import { useMemo } from 'react'
import { NodeViewWrapper, useEditorState, type ReactNodeViewProps } from '@tiptap/react'
import type { Node as PMNode } from '@tiptap/pm/model'
import { CHART_TYPES, CHART_TYPE_LABELS, isChartType, type ChartType } from './chartExtension'

type Series = { label: string; values: number[] }
type TableData = { headers: string[]; series: Series[] }

/** A number as a person would write one in a table: "1,234", "45%", "£9.50". */
function parseNumber(text: string): number | null {
  const cleaned = text.replace(/[^0-9.,\-+eE]/g, '').replace(/,/g, '')
  if (!cleaned || !/\d/.test(cleaned)) return null
  const value = Number(cleaned)
  return Number.isFinite(value) ? value : null
}

/**
 * Reads the nth table on the page into rows of numbers. The first column is
 * the label; every other column is a series. A row with no numbers at all is
 * skipped rather than charted as zero: a spacer row is not data.
 */
function readTable(doc: PMNode, ordinal: number): TableData | null {
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
  if (rows.length < 2) return null

  const [head, ...body] = rows
  const headers = head.slice(1)
  const series: Series[] = body
    .map((cells) => ({ label: cells[0] ?? '', values: cells.slice(1).map((c) => parseNumber(c) ?? 0) }))
    // A spacer row with no numbers at all is not data, so it is dropped
    // rather than charted as a run of zeroes.
    .filter((_, i) => body[i].slice(1).some((c) => parseNumber(c) !== null))
  return series.length === 0 ? null : { headers, series }
}

// The chart palette: readable on both themes, and distinguishable without
// relying on hue alone (the legend names every series).
const COLORS = ['#0c66e4', '#00875a', '#a54800', '#5e4db2', '#ae4787', '#206a83', '#946f00', '#bf2600']

export function ChartView({ node, editor, selected, updateAttributes }: ReactNodeViewProps) {
  const source = Number(node.attrs.source) || 1
  const type: ChartType = isChartType(node.attrs.chartType) ? node.attrs.chartType : 'column'
  const title = String(node.attrs.title ?? '')

  // Re-read whenever the document changes, so editing the table redraws the
  // chart: the whole reason the data is not copied in.
  const data = useEditorState({
    editor,
    selector: ({ editor }) => readTable(editor.state.doc, source),
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

  return (
    <NodeViewWrapper className={selected ? 'chart is-selected' : 'chart'} contentEditable={false}>
      {editor.isEditable && (
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
          <input
            className="chart__title-input"
            value={title}
            placeholder="Chart title (optional)"
            onChange={(e) => updateAttributes({ title: e.target.value })}
          />
        </div>
      )}
      {title && <p className="chart__title">{title}</p>}
      {!data && (
        <p className="chart__note">
          {tableCount === 0
            ? 'Add a table to this page, then point this chart at it.'
            : `Table ${source} has no numbers to chart.`}
        </p>
      )}
      {data && <Plot data={data} type={type} />}
    </NodeViewWrapper>
  )
}

/**
 * Plain SVG rather than a charting library: four chart types over one table
 * is a few dozen lines, and the alternative is another ~150KB in the bundle
 * for a feature most pages never use.
 */
function Plot({ data, type }: { data: TableData; type: ChartType }) {
  const flat = useMemo(() => data.series.flatMap((s) => s.values), [data])
  const max = Math.max(...flat, 0)
  const min = Math.min(...flat, 0)

  if (type === 'pie') {
    // A pie charts one column: the first, which is what people mean.
    const slices = data.series.map((s, i) => ({ label: s.label, value: Math.max(s.values[0] ?? 0, 0), color: COLORS[i % COLORS.length] }))
    const total = slices.reduce((sum, s) => sum + s.value, 0)
    let angle = -Math.PI / 2
    return (
      <div className="chart__plot">
        <svg viewBox="0 0 120 120" role="img" aria-label="Pie chart">
          {total > 0 && slices.map((slice) => {
            const sweep = (slice.value / total) * Math.PI * 2
            const [x1, y1] = [60 + 55 * Math.cos(angle), 60 + 55 * Math.sin(angle)]
            angle += sweep
            const [x2, y2] = [60 + 55 * Math.cos(angle), 60 + 55 * Math.sin(angle)]
            return (
              <path key={slice.label} fill={slice.color}
                d={`M60 60 L${x1.toFixed(2)} ${y1.toFixed(2)} A55 55 0 ${sweep > Math.PI ? 1 : 0} 1 ${x2.toFixed(2)} ${y2.toFixed(2)} Z`} />
            )
          })}
        </svg>
        <Legend items={slices.map((s) => ({ label: s.label, color: s.color }))} />
      </div>
    )
  }

  if (type === 'line') {
    const width = 320, height = 160, pad = 4
    const span = max - min || 1
    return (
      <div className="chart__plot">
        <svg viewBox={`0 0 ${width} ${height}`} role="img" aria-label="Line chart">
          {data.headers.map((_, column) => (
            <polyline
              key={column}
              fill="none"
              stroke={COLORS[column % COLORS.length]}
              strokeWidth="2"
              points={data.series.map((s, i) => {
                const x = pad + (i * (width - pad * 2)) / Math.max(data.series.length - 1, 1)
                const y = height - pad - (((s.values[column] ?? 0) - min) / span) * (height - pad * 2)
                return `${x.toFixed(1)},${y.toFixed(1)}`
              }).join(' ')}
            />
          ))}
        </svg>
        <Legend items={data.headers.map((h, i) => ({ label: h, color: COLORS[i % COLORS.length] }))} />
      </div>
    )
  }

  // Bars: one group per row, one bar per column. Horizontal or vertical.
  const horizontal = type === 'bar'
  return (
    <div className="chart__plot">
      <div className={horizontal ? 'chart__bars chart__bars--h' : 'chart__bars'}>
        {data.series.map((s) => (
          <div className="chart__group" key={s.label}>
            <span className="chart__group-label">{s.label}</span>
            <div className="chart__group-bars">
              {s.values.map((v, column) => (
                <div
                  key={column}
                  className="chart__bar"
                  style={{
                    [horizontal ? 'width' : 'height']: `${max > 0 ? (Math.max(v, 0) / max) * 100 : 0}%`,
                    background: COLORS[column % COLORS.length],
                  }}
                  title={`${data.headers[column] ?? ''}: ${v}`}
                />
              ))}
            </div>
          </div>
        ))}
      </div>
      <Legend items={data.headers.map((h, i) => ({ label: h, color: COLORS[i % COLORS.length] }))} />
    </div>
  )
}

function Legend({ items }: { items: { label: string; color: string }[] }) {
  if (items.length === 0) return null
  return (
    <ul className="chart__legend">
      {items.map((item) => (
        <li key={item.label}>
          <span className="chart__swatch" style={{ background: item.color }} />
          {item.label}
        </li>
      ))}
    </ul>
  )
}
