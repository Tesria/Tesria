import { useId, useLayoutEffect, useRef, useState, type CSSProperties, type ReactNode } from 'react'
import { DonutChart, PieChart } from '../components/PieChart'
import {
  categoryLabelLayout, describeChart, donutCenter, formatValue, legendValue, linePath, niceScale, pieSlices,
  stackSeries, stepDecimals, truncate, valueRange, type ChartData, type NumberFormat, type Point, COLORS, GLASS_SLICE_COLORS, seriesColor } from './chartData'
import type { ChartSize, ChartType, LegendPosition } from './chartExtension'

/**
 * The drawing of a chart (chart options, 2026-09-29). Plain SVG rather than
 * a charting library: five chart types over one table is a few hundred
 * lines, and the alternative is another ~150KB in the bundle for a feature
 * most pages never use.
 *
 * Column, bar and line charts are drawn at the width they are given, in
 * real pixels, rather than scaled from a fixed canvas: a scaled canvas
 * shrinks its text with it, and on a phone the labels would be too small to
 * read. So the width is measured, and the ticks, the category labels (level,
 * turned, or thinned) and the margins are worked out for it.
 */


export type PlotOptions = {
  legend: LegendPosition
  size: ChartSize
  axes: boolean
  categoryTitle: string
  valueTitle: string
  valueLabels: boolean
  points: boolean
  smooth: boolean
  area: boolean
  fromZero: boolean
  stacked: boolean
  legendValues: boolean
  dataColumn: number
  largestFirst: boolean
  donutCenter: string
  centerText: string
  /** Chosen colors by series (or slice) name; the palette fills the rest. */
  colors: Record<string, string>
}

/**
 * Each size's drawing. Medium is the size charts had before the options:
 * the full width with bars about 180px tall, a 320px pie. `bar` is the
 * thickness of one bar in a horizontal chart.
 */
const SIZES: Record<ChartSize, { maxWidth: number; height: number; pie: number; bar: number }> = {
  small: { maxWidth: 420, height: 180, pie: 200, bar: 10 },
  medium: { maxWidth: 10000, height: 240, pie: 320, bar: 14 },
  large: { maxWidth: 10000, height: 360, pie: 440, bar: 20 },
}

const FONT = 11
/** The average width of a character at FONT in the UI font, for fitting labels. */
const CHAR = 6.3
const textWidth = (s: string) => s.length * CHAR

export function ChartPlot({ data, type, options, format, title }: {
  data: ChartData
  type: ChartType
  options: PlotOptions
  format: NumberFormat
  title: string
}) {
  // A chosen color is used in both styles; Glass derives its shading from it.
  const colorOf = (i: number, name: string) => seriesColor(options.colors, name, i, COLORS)
  const glassOf = (i: number, name: string) => seriesColor(options.colors, name, i, GLASS_SLICE_COLORS)
  const size = SIZES[options.size] ?? SIZES.medium

  if (type === 'pie' || type === 'donut') {
    // A pie charts one series: the first, which is what people mean, or the one chosen.
    // The drawing itself lives in components/PieChart (dev-plan 9.3), so the
    // editor and the backups page share one pie rather than two that drift.
    const slices = pieSlices(data, options.dataColumn, options.largestFirst)
    const drawn = slices.map((s) => ({ label: s.label, value: s.value, color: colorOf(s.index, s.label), glassColor: glassOf(s.index, s.label) }))
    const total = slices.reduce((sum, s) => sum + s.value, 0)
    const label = describeChart(type, title, data, format, { slices })
    const center = donutCenter(options.donutCenter, slices, format, options.centerText)
    return (
      <Frame legend={options.legend} items={slices.map((s) => ({
        label: s.label, color: colorOf(s.index, s.label), glassColor: glassOf(s.index, s.label),
        value: options.legendValues ? legendValue(s.value, total, format) : undefined,
      }))}>
        <div className="chart__pie" style={{ maxWidth: size.pie }}>
          {type === 'pie'
            ? <PieChart slices={drawn} size={size.pie} label={label} />
            // The same slices as a ring, with the total (or the chosen text) in the middle (0.8.0).
            : <DonutChart slices={drawn} size={size.pie} label={label} center={center.text || undefined} caption={center.caption || undefined} />}
        </div>
      </Frame>
    )
  }

  return (
    <Frame legend={options.legend} items={data.series.map((s, i) => ({ label: s.name, color: colorOf(i, s.name), line: type === 'line' }))}>
      <Measured maxWidth={size.maxWidth}>
        {(width) => type === 'bar'
          ? <BarChart data={data} options={options} format={format} width={width} thickness={size.bar} label={describeChart(type, title, data, format)} />
          : <ColumnOrLine data={data} type={type} options={options} format={format} width={width} height={size.height} label={describeChart(type, title, data, format)} />}
      </Measured>
    </Frame>
  )
}

/** The plot with its legend below it, beside it (wrapping below on a narrow page), or none. */
function Frame({ legend, items, children }: { legend: LegendPosition; items: LegendItem[]; children: ReactNode }) {
  return (
    <div className={`chart__plot chart__plot--legend-${legend}`}>
      {children}
      {legend !== 'none' && <Legend items={items} />}
    </div>
  )
}

/** Measures the room a chart has, and draws it at that width. */
function Measured({ maxWidth, children }: { maxWidth: number; children: (width: number) => ReactNode }) {
  const ref = useRef<HTMLDivElement>(null)
  const [width, setWidth] = useState(0)
  useLayoutEffect(() => {
    const el = ref.current
    if (!el) return
    const measure = () => setWidth(Math.floor(el.getBoundingClientRect().width))
    measure()
    const observer = new ResizeObserver(measure)
    observer.observe(el)
    return () => observer.disconnect()
  }, [])
  return (
    <div ref={ref} className="chart__canvas" style={{ maxWidth }}>
      {width > 0 && children(Math.max(width, 160))}
    </div>
  )
}

type LegendItem = { label: string; color: string; glassColor?: string; value?: string; line?: boolean }

function Legend({ items }: { items: LegendItem[] }) {
  if (items.length === 0) return null
  return (
    <ul className="chart__legend">
      {items.map((item, i) => (
        <li key={`${i}-${item.label}`}>
          <span className={item.line ? 'chart__swatch chart__swatch--line' : 'chart__swatch'}
            style={{ background: item.color, ...(item.glassColor ? { '--glass-swatch': item.glassColor } : {}) } as CSSProperties} />
          <span className="chart__legend-name">{item.label}</span>
          {item.value && <span className="chart__legend-value">{item.value}</span>}
        </li>
      ))}
    </ul>
  )
}

/**
 * A bar as a path, rounded at the end away from its baseline (`end`), or
 * square (a segment inside a stack).
 */
function barPath(x: number, y: number, w: number, h: number, end: 'top' | 'bottom' | 'right' | 'left' | null): string {
  const f = (n: number) => Number(n.toFixed(2))
  const r = end ? Math.min(3, w / 2, h / 2) : 0
  if (r <= 0) return `M${f(x)},${f(y)}h${f(w)}v${f(h)}h${f(-w)}Z`
  switch (end) {
    case 'top': return `M${f(x)},${f(y + h)}V${f(y + r)}Q${f(x)},${f(y)} ${f(x + r)},${f(y)}H${f(x + w - r)}Q${f(x + w)},${f(y)} ${f(x + w)},${f(y + r)}V${f(y + h)}Z`
    case 'bottom': return `M${f(x)},${f(y)}V${f(y + h - r)}Q${f(x)},${f(y + h)} ${f(x + r)},${f(y + h)}H${f(x + w - r)}Q${f(x + w)},${f(y + h)} ${f(x + w)},${f(y + h - r)}V${f(y)}Z`
    case 'right': return `M${f(x)},${f(y)}H${f(x + w - r)}Q${f(x + w)},${f(y)} ${f(x + w)},${f(y + r)}V${f(y + h - r)}Q${f(x + w)},${f(y + h)} ${f(x + w - r)},${f(y + h)}H${f(x)}Z`
    default: return `M${f(x + w)},${f(y)}H${f(x + r)}Q${f(x)},${f(y)} ${f(x)},${f(y + r)}V${f(y + h - r)}Q${f(x)},${f(y + h)} ${f(x + r)},${f(y + h)}H${f(x + w)}Z`
  }
}

/** The light on a glass bar: white at its top fading out, as the bars had before (glass.css shows it). */
function BarSheenDefs({ id }: { id: string }) {
  return (
    <linearGradient id={id} x1="0" y1="0" x2="0" y2="1">
      <stop offset="0" stopColor="#fff" stopOpacity="0.5" />
      <stop offset="0.65" stopColor="#fff" stopOpacity="0" />
    </linearGradient>
  )
}

/** Which series is outermost in each category, up and down, so only that end of a stack is rounded. */
function stackEnds(data: ChartData): { up: number[]; down: number[] } {
  const up: number[] = [], down: number[] = []
  data.categories.forEach((_, i) => {
    up[i] = -1; down[i] = -1
    data.series.forEach((s, j) => {
      const v = s.values[i] ?? 0
      if (v > 0) up[i] = j
      if (v < 0) down[i] = j
    })
  })
  return { up, down }
}

type Scaled = { scale: ReturnType<typeof niceScale>; decimals: number }

/**
 * The value axis. With axes shown it runs between round numbers; hidden,
 * nobody reads a tick, so it runs from the data's own least to its most
 * and the bars use all the room.
 */
function scaleFor(data: ChartData, stacked: boolean, fromZero: boolean, length: number, axes: boolean): Scaled {
  const [lo, hi] = stacked
    ? (() => { const s = stackSeries(data.series); return [s.min, s.max] as [number, number] })()
    : valueRange(data.series.flatMap((s) => s.values), fromZero)
  const scale = niceScale(lo, hi, Math.max(2, Math.min(8, Math.floor(length / 48))))
  const fitted = !axes && hi > lo ? { ...scale, min: lo, max: hi } : scale
  return { scale: fitted, decimals: stepDecimals(scale.step) }
}

function ColumnOrLine({ data, type, options, format, width, height, label }: {
  data: ChartData; type: ChartType; options: PlotOptions; format: NumberFormat; width: number; height: number; label: string
}) {
  const uid = useId().replace(/:/g, '')
  const line = type === 'line'
  const stacked = !line && options.stacked
  const { axes, valueLabels } = options
  const n = data.categories.length

  // Bars always start at zero: a bar's length is its value, and a bar cut
  // short at a fitted floor would say something the numbers do not.
  const { scale, decimals } = scaleFor(data, stacked, line ? options.fromZero : true, height - 50, axes)
  const tick = (v: number) => formatValue(v, format, { compact: true, decimals })
  const tickWidth = axes ? Math.max(...scale.ticks.map((t) => textWidth(tick(t)))) : 0

  const right = line ? 12 : 6
  let left = (axes ? tickWidth + 10 : 4) + (options.valueTitle ? 18 : 0)
  let slot = Math.max(width - left - right, 20) / Math.max(n, 1)
  let layout = categoryLabelLayout(data.categories, slot)
  if (layout.rotate) {
    // A turned label reaches left of its category; the first one must not
    // run off the chart's edge.
    const reach = (layout.maxChars * CHAR + 10) * 0.71 - slot / 2 + 4
    if (reach > left) {
      left = reach
      slot = Math.max(width - left - right, 20) / Math.max(n, 1)
      layout = categoryLabelLayout(data.categories, slot)
    }
  }
  const plotW = Math.max(width - left - right, 20)
  const labelBand = layout.rotate ? layout.maxChars * CHAR * 0.72 + 12 : 18
  const top = valueLabels ? 16 : 8
  const bottom = labelBand + (options.categoryTitle ? 18 : 0) + 2
  const plotH = Math.max(height - top - bottom, 40)
  const x0 = left, y0 = top, x1 = left + plotW, y1 = top + plotH
  const y = (v: number) => y1 - ((v - scale.min) / (scale.max - scale.min || 1)) * plotH
  const zero = y(Math.min(Math.max(0, scale.min), scale.max))
  const color = (j: number) => seriesColor(options.colors, data.series[j]?.name ?? '', j, COLORS)
  const value = (v: number) => formatValue(v, format)

  const sheen = `chart-sheen-${uid}`
  const grid = (
    <g className="chart-grid">
      {axes && scale.ticks.map((t) => <line key={t} x1={x0} x2={x1} y1={y(t)} y2={y(t)} />)}
    </g>
  )
  const ticks = axes && (
    <g className="chart-tick" textAnchor="end">
      {scale.ticks.map((t) => <text key={t} x={x0 - 6} y={y(t)} dy="0.32em">{tick(t)}</text>)}
    </g>
  )
  const categoryLabels = (
    <g className="chart-cat">
      {data.categories.map((c, i) => {
        if (i % layout.every !== 0) return null
        const cx = x0 + slot * (i + 0.5)
        const shown = truncate(c, layout.maxChars)
        return layout.rotate
          ? <text key={i} x={cx} y={y1 + 10} textAnchor="end" transform={`rotate(-45 ${cx} ${y1 + 10})`}>{shown}{shown !== c && <title>{c}</title>}</text>
          : <text key={i} x={cx} y={y1 + 14} textAnchor="middle">{shown}{shown !== c && <title>{c}</title>}</text>
      })}
    </g>
  )
  const titles = (
    <>
      {options.valueTitle && (
        <text className="chart-axis-title" x={11} y={(y0 + y1) / 2} textAnchor="middle" transform={`rotate(-90 11 ${(y0 + y1) / 2})`}>{options.valueTitle}</text>
      )}
      {options.categoryTitle && (
        <text className="chart-axis-title" x={(x0 + x1) / 2} y={height - 5} textAnchor="middle">{options.categoryTitle}</text>
      )}
    </>
  )

  let plot: ReactNode
  if (line) {
    const pointsOf = (j: number): Point[] => data.series[j].values.map((v, i) => [x0 + slot * (i + 0.5), y(v)])
    // One point cannot make a line, so it is always drawn as a point.
    const showPoints = options.points || n === 1
    plot = (
      <>
        <defs>
          {data.series.map((_, j) => (
            <linearGradient key={j} id={`chart-area-${uid}-${j}`} x1="0" y1="0" x2="0" y2="1">
              <stop offset="0" stopColor={color(j)} stopOpacity="0.45" />
              <stop offset="1" stopColor={color(j)} stopOpacity="0.04" />
            </linearGradient>
          ))}
        </defs>
        {options.area && data.series.map((_, j) => {
          const pts = pointsOf(j)
          if (pts.length < 2) return null
          const d = `${linePath(pts, options.smooth)}L${pts[pts.length - 1][0].toFixed(2)},${zero.toFixed(2)}L${pts[0][0].toFixed(2)},${zero.toFixed(2)}Z`
          return <path key={j} className="chart-area" d={d} fill={color(j)} style={{ '--glass-fill': `url(#chart-area-${uid}-${j})` } as CSSProperties} />
        })}
        <g className="chart-lines">
          {data.series.map((_, j) => (
            <path key={j} className="chart-line" d={linePath(pointsOf(j), options.smooth)} fill="none" stroke={color(j)} strokeWidth="2.25" strokeLinejoin="round" strokeLinecap="round" />
          ))}
          {showPoints && data.series.map((_, j) => pointsOf(j).map(([px, py], i) => (
            <circle key={`${j}-${i}`} className="chart-point" cx={px} cy={py} r={3.5} fill={color(j)} />
          )))}
        </g>
        {/* Invisible targets over every point, so pointing at one names its value. */}
        <g className="chart-hits">
          {data.series.map((s, j) => pointsOf(j).map(([px, py], i) => (
            <circle key={`${j}-${i}`} cx={px} cy={py} r={Math.min(8, Math.max(slot / 2, 3))} fill="transparent">
              <title>{`${s.name}, ${data.categories[i]}: ${value(s.values[i])}`}</title>
            </circle>
          )))}
        </g>
        {valueLabels && (
          <g className="chart-value" textAnchor="middle">
            {data.series.map((s, j) => pointsOf(j).map(([px, py], i) => i % layout.every === 0 && (
              <text key={`${j}-${i}`} x={px} y={s.values[i] < 0 ? py + 13 : py - 7}>{value(s.values[i])}</text>
            )))}
          </g>
        )}
      </>
    )
  } else {
    const count = data.series.length
    const groupPad = Math.max(slot * (stacked || count === 1 ? 0.2 : 0.12), 1)
    const inner = slot - groupPad * 2
    const gap = count > 1 && !stacked ? Math.min(2, inner / count / 4) : 0
    const barW = stacked ? inner : (inner - gap * (count - 1)) / count
    const segments = stacked ? stackSeries(data.series).segments : null
    const ends = stacked ? stackEnds(data) : null
    const bars: ReactNode[] = []
    const labels: ReactNode[] = []
    data.series.forEach((s, j) => s.values.forEach((v, i) => {
      if (v === 0) return
      const [a, b] = segments ? segments[j][i] : v >= 0 ? [0, v] : [v, 0]
      const top = y(b), h = y(a) - y(b)
      if (h <= 0) return
      const x = x0 + slot * i + groupPad + (stacked ? 0 : j * (barW + gap))
      const end = !ends ? (v >= 0 ? 'top' : 'bottom') : (v > 0 && ends.up[i] === j) ? 'top' : (v < 0 && ends.down[i] === j) ? 'bottom' : null
      const d = barPath(x, top, barW, h, end)
      bars.push(
        <g key={`${j}-${i}`}>
          <path className="chart-bar" d={d} fill={color(j)}><title>{`${s.name}, ${data.categories[i]}: ${value(v)}`}</title></path>
          <path className="chart-bar-sheen" d={d} fill={`url(#${sheen})`} />
        </g>,
      )
      if (!valueLabels) return
      const text = value(v)
      if (stacked) {
        // Inside its segment, when it fits there.
        if (h >= 14 && textWidth(text) <= barW + 2) {
          labels.push(<text key={`${j}-${i}`} className="chart-value--inside" x={x + barW / 2} y={top + h / 2} dy="0.35em">{text}</text>)
        }
      } else if (textWidth(text) <= (count === 1 ? slot : barW + 8)) {
        labels.push(<text key={`${j}-${i}`} x={x + barW / 2} y={v >= 0 ? top - 4 : top + h + 11}>{text}</text>)
      }
    }))
    plot = (
      <>
        <defs><BarSheenDefs id={sheen} /></defs>
        <g className="chart-bars">{bars}</g>
        {valueLabels && <g className="chart-value" textAnchor="middle">{labels}</g>}
      </>
    )
  }

  return (
    <svg className="chart__svg" width={width} height={height} viewBox={`0 0 ${width} ${height}`} role="img" aria-label={label} fontSize={FONT}>
      {grid}
      {axes && <line className="chart-axis" x1={x0} x2={x0} y1={y0} y2={y1} />}
      {plot}
      {/* The zero line over the bars, where negative values meet positive ones. */}
      {(axes || !line) && <line className="chart-axis" x1={x0} x2={x1} y1={axes ? y1 : zero} y2={axes ? y1 : zero} />}
      {axes && scale.min < 0 && scale.max > 0 && <line className="chart-zero" x1={x0} x2={x1} y1={zero} y2={zero} />}
      {ticks}
      {categoryLabels}
      {titles}
    </svg>
  )
}

function BarChart({ data, options, format, width, thickness, label }: {
  data: ChartData; options: PlotOptions; format: NumberFormat; width: number; thickness: number; label: string
}) {
  const uid = useId().replace(/:/g, '')
  const { axes, valueLabels, stacked } = options
  const n = data.categories.length
  const count = data.series.length
  const gap = count > 1 && !stacked ? 2 : 0
  const group = stacked ? thickness * 1.4 : count * thickness + gap * (count - 1)
  const band = group + Math.max(8, thickness * 0.9)

  const value = (v: number) => formatValue(v, format)
  const longest = Math.max(0, ...data.categories.map((c) => c.length))
  // Labels have up to a third of the width; longer ones are cut, with the whole in a tooltip.
  const labelChars = Math.max(3, Math.min(longest, Math.floor((width * 0.34) / CHAR)))
  const left = labelChars * CHAR + 10 + (options.categoryTitle ? 18 : 0)
  const valueRoom = valueLabels ? Math.max(...data.series.flatMap((s) => s.values.map((v) => textWidth(value(v))))) + 8 : 0
  const { scale, decimals } = scaleFor(data, stacked, true, width - left - 20, axes)
  const tick = (v: number) => formatValue(v, format, { compact: true, decimals })
  const lastTick = axes ? textWidth(tick(scale.max)) / 2 + 4 : 6
  const right = Math.max(lastTick, valueRoom)
  // A negative bar's value is written past its end, on the left. Usually
  // the axis runs far enough below zero to hold it; when it does not, the
  // plot moves right to make room.
  const span = scale.max - scale.min || 1
  const room = width - left - right
  const leftRoom = !valueLabels || stacked ? 0 : Math.max(0, ...data.series.flatMap((s) => s.values
    .filter((v) => v < 0)
    .map((v) => textWidth(value(v)) + 6 - ((v - scale.min) / span) * room)))
  const top = 6
  const bottom = (axes ? 20 : 4) + (options.valueTitle ? 18 : 0)
  const plotH = n * band
  const height = top + plotH + bottom
  const x0 = left + leftRoom, x1 = Math.max(width - right, x0 + 20), y0 = top, y1 = top + plotH
  const plotW = x1 - x0
  const x = (v: number) => x0 + ((v - scale.min) / (scale.max - scale.min || 1)) * plotW
  const zero = x(Math.min(Math.max(0, scale.min), scale.max))
  const color = (j: number) => seriesColor(options.colors, data.series[j]?.name ?? '', j, COLORS)
  const sheen = `chart-sheen-${uid}`

  const segments = stacked ? stackSeries(data.series).segments : null
  const ends = stacked ? stackEnds(data) : null
  const bars: ReactNode[] = []
  const labels: ReactNode[] = []
  data.series.forEach((s, j) => s.values.forEach((v, i) => {
    if (v === 0) return
    const [a, b] = segments ? segments[j][i] : v >= 0 ? [0, v] : [v, 0]
    const bx = x(a), w = x(b) - x(a)
    if (w <= 0) return
    const by = y0 + band * i + (band - group) / 2 + (stacked ? 0 : j * (thickness + gap))
    const h = stacked ? group : thickness
    const end = !ends ? (v >= 0 ? 'right' : 'left') : (v > 0 && ends.up[i] === j) ? 'right' : (v < 0 && ends.down[i] === j) ? 'left' : null
    const d = barPath(bx, by, w, h, end)
    bars.push(
      <g key={`${j}-${i}`}>
        <path className="chart-bar" d={d} fill={color(j)}><title>{`${s.name}, ${data.categories[i]}: ${value(v)}`}</title></path>
        <path className="chart-bar-sheen" d={d} fill={`url(#${sheen})`} />
      </g>,
    )
    if (!valueLabels) return
    const text = value(v)
    if (stacked) {
      if (textWidth(text) + 6 <= w && h >= 11) {
        labels.push(<text key={`${j}-${i}`} className="chart-value--inside" x={bx + w / 2} y={by + h / 2} dy="0.35em" textAnchor="middle">{text}</text>)
      }
    } else {
      labels.push(<text key={`${j}-${i}`} x={v >= 0 ? bx + w + 4 : bx - 4} y={by + h / 2} dy="0.35em" textAnchor={v >= 0 ? 'start' : 'end'}>{text}</text>)
    }
  }))

  return (
    <svg className="chart__svg" width={width} height={height} viewBox={`0 0 ${width} ${height}`} role="img" aria-label={label} fontSize={FONT}>
      <defs><BarSheenDefs id={sheen} /></defs>
      <g className="chart-grid">
        {axes && scale.ticks.map((t) => <line key={t} x1={x(t)} x2={x(t)} y1={y0} y2={y1} />)}
      </g>
      {axes && <line className="chart-axis" x1={x0} x2={x1} y1={y1} y2={y1} />}
      <g className="chart-bars">{bars}</g>
      <line className="chart-axis" x1={zero} x2={zero} y1={y0} y2={y1} />
      {valueLabels && <g className="chart-value">{labels}</g>}
      {axes && (
        <g className="chart-tick" textAnchor="middle">
          {scale.ticks.map((t) => <text key={t} x={x(t)} y={y1 + 14}>{tick(t)}</text>)}
        </g>
      )}
      <g className="chart-cat" textAnchor="end">
        {data.categories.map((c, i) => {
          const shown = truncate(c, labelChars)
          return <text key={i} x={left - 8} y={y0 + band * (i + 0.5)} dy="0.35em">{shown}{shown !== c && <title>{c}</title>}</text>
        })}
      </g>
      {options.categoryTitle && (
        <text className="chart-axis-title" x={11} y={(y0 + y1) / 2} textAnchor="middle" transform={`rotate(-90 11 ${(y0 + y1) / 2})`}>{options.categoryTitle}</text>
      )}
      {options.valueTitle && (
        <text className="chart-axis-title" x={(x0 + x1) / 2} y={height - 5} textAnchor="middle">{options.valueTitle}</text>
      )}
    </svg>
  )
}
