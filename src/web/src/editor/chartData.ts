/**
 * The chart's arithmetic (chart options, 2026-09-29), kept apart from the drawing
 * so it can be tested: reading a table's cells into series, turning it on
 * its side, the number format, a readable scale, stacking, slices, the
 * legend's values and the summary a screen reader hears.
 *
 * Nothing here knows about ProseMirror or React. ChartView reads the cells
 * out of the document and ChartPlot draws what these functions return.
 */

/** One colored thing in a chart: a line, one color of bars, or the column a pie is cut from. */
export type Series = { name: string; values: number[] }
/** A table read for charting: the labels along the category axis, and the series drawn against them. */
export type ChartData = { categories: string[]; series: Series[] }

/** A number as a person would write one in a table: "1,234", "45%", "£9.50". */
export function parseNumber(text: string): number | null {
  const cleaned = text.replace(/[^0-9.,\-+eE]/g, '').replace(/,/g, '')
  if (!cleaned || !/\d/.test(cleaned)) return null
  const value = Number(cleaned)
  return Number.isFinite(value) ? value : null
}

/**
 * Rows of cell text (the first row the header, the first column the labels)
 * as chart data. Each column after the first is a series, each row a
 * category; `transpose` turns that round, so each row is a series and each
 * column a category ("Swap Rows and Columns").
 *
 * A row with no numbers at all is skipped rather than charted as zero: a
 * spacer row is not data. A column with no numbers (a Notes column) is
 * skipped for the same reason. Any other cell that is not a number counts
 * as zero, as it always has.
 */
export function tableToChart(rows: string[][], transpose = false): ChartData | null {
  if (rows.length < 2) return null
  const [head, ...body] = rows
  const kept = body.filter((cells) => cells.slice(1).some((c) => parseNumber(c) !== null))
  if (kept.length === 0) return null
  const width = Math.max(head.length, ...kept.map((r) => r.length))
  const series: Series[] = []
  for (let col = 1; col < width; col++) {
    if (!kept.some((r) => parseNumber(r[col] ?? '') !== null)) continue
    series.push({
      name: head[col] || `Column ${col + 1}`,
      values: kept.map((r) => parseNumber(r[col] ?? '') ?? 0),
    })
  }
  if (series.length === 0) return null
  const categories = kept.map((r) => r[0] ?? '')
  if (!transpose) return { categories, series }
  return {
    categories: series.map((s) => s.name),
    series: categories.map((name, i) => ({ name: name || `Row ${i + 1}`, values: series.map((s) => s.values[i]) })),
  }
}

// ------------------------------------------------------------ number format

export const NUMBER_FORMATS = ['auto', 'plain', 'percent', 'currency'] as const
export type NumberFormatChoice = (typeof NUMBER_FORMATS)[number]
export const NUMBER_FORMAT_LABELS: Record<NumberFormatChoice, string> = {
  auto: 'Auto',
  plain: 'Plain',
  percent: 'Percent',
  currency: 'Currency',
}

/** How numbers are written on a chart: the kind, the currency symbol, and the decimals the table uses. */
export type NumberFormat = { kind: 'plain' | 'percent' | 'currency'; symbol: string; decimals: number }

const CURRENCY = /[$£€¥₹₩₽₺₪₫฿₱¢]/

/**
 * What the table's own numbers look like: percentages when most of its
 * numbers carry a %, money when most carry a currency symbol (the commonest
 * one), otherwise plain. `decimals` is the most places after the point any
 * number is written with (at most 4), so £9.50 stays £9.50 on the chart.
 * The first row and the first column are names, so they are not looked at.
 */
export function detectFormat(rows: string[][]): NumberFormat {
  let numbers = 0, percent = 0, money = 0, decimals = 0
  const symbols = new Map<string, number>()
  for (const row of rows.slice(1)) {
    for (const cell of row.slice(1)) {
      if (parseNumber(cell) === null) continue
      numbers += 1
      if (cell.includes('%')) percent += 1
      const sym = cell.match(CURRENCY)?.[0]
      if (sym) { money += 1; symbols.set(sym, (symbols.get(sym) ?? 0) + 1) }
      const places = cell.replace(/[^0-9.]/g, '').split('.')[1]?.length ?? 0
      decimals = Math.max(decimals, Math.min(places, 4))
    }
  }
  const symbol = [...symbols].sort((a, b) => b[1] - a[1])[0]?.[0] ?? ''
  const kind = numbers > 0 && percent * 2 > numbers ? 'percent' : numbers > 0 && money * 2 > numbers ? 'currency' : 'plain'
  return { kind, symbol, decimals }
}

/** The format a chart uses: what it detected, or what the author chose. Currency without a symbol in the table is dollars. */
export function resolveFormat(choice: unknown, detected: NumberFormat): NumberFormat {
  switch (choice) {
    case 'plain': return { ...detected, kind: 'plain' }
    case 'percent': return { ...detected, kind: 'percent' }
    case 'currency': return { ...detected, kind: 'currency', symbol: detected.symbol || '$' }
    default: return detected.kind === 'currency' && !detected.symbol ? { ...detected, symbol: '$' } : detected
  }
}

export function isNumberFormatChoice(value: unknown): value is NumberFormatChoice {
  return typeof value === 'string' && (NUMBER_FORMATS as readonly string[]).includes(value)
}

/**
 * A number written in a chart's format. `compact` shortens a thousand and
 * over (1.2K), as a tick or the middle of a donut has room for; `decimals`
 * overrides the table's (a tick's step decides its own). Money keeps its
 * cents: $9.50, not $9.5.
 */
export function formatValue(
  value: number,
  format: NumberFormat,
  { compact = false, decimals, locale }: { compact?: boolean; decimals?: number; locale?: string } = {},
): string {
  const abs = Math.abs(value)
  const places = decimals ?? format.decimals
  const options: Intl.NumberFormatOptions = compact && abs >= 1000
    ? { notation: 'compact', maximumFractionDigits: 1 }
    : {
        maximumFractionDigits: places,
        // A currency's cents show whenever the table writes them, even on a round number.
        minimumFractionDigits: format.kind === 'currency' && decimals === undefined ? places : 0,
      }
  const body = new Intl.NumberFormat(locale, options).format(abs)
  // A value that rounds to zero is not negative: no "-0".
  const sign = value < 0 && body.replace(/[^1-9]/g, '') !== '' ? '-' : ''
  if (format.kind === 'percent') return `${sign}${body}%`
  if (format.kind === 'currency') return `${sign}${format.symbol}${body}`
  return `${sign}${body}`
}

/** The decimals a tick step needs: 0.25 needs 2, 5 needs none. */
export function stepDecimals(step: number): number {
  for (let d = 0; d <= 6; d++) {
    const scaled = step * 10 ** d
    if (Math.abs(Math.round(scaled) - scaled) < 1e-6 * Math.max(1, scaled)) return d
  }
  return 6
}

// ------------------------------------------------------------------- scales

export type Scale = { min: number; max: number; step: number; ticks: number[] }

/** A step of 1, 2 or 5 times a power of ten near `rough` (the thresholds d3 uses: √2, √10, √50). */
function niceStep(rough: number): number {
  const power = Math.floor(Math.log10(rough))
  const error = rough / 10 ** power
  const factor = error >= Math.sqrt(50) ? 10 : error >= Math.sqrt(10) ? 5 : error >= Math.SQRT2 ? 2 : 1
  return factor * 10 ** power
}

/**
 * A readable value axis over [lo, hi]: round ends and a step of 1, 2 or 5
 * times a power of ten, with about `target` ticks. Nothing to show (all
 * zero) is 0 to 1; one value on its own gets a little room either side.
 */
export function niceScale(lo: number, hi: number, target = 5): Scale {
  if (!Number.isFinite(lo) || !Number.isFinite(hi)) { lo = 0; hi = 1 }
  if (lo > hi) [lo, hi] = [hi, lo]
  if (lo === hi) {
    if (lo === 0) hi = 1
    else { const pad = Math.abs(lo) * 0.1; lo -= pad; hi += pad }
  }
  const step = niceStep((hi - lo) / Math.max(1, Math.round(target)))
  const min = Math.floor(lo / step + 1e-9) * step
  const max = Math.ceil(hi / step - 1e-9) * step
  const ticks: number[] = []
  for (let v = min; v <= max + step / 2; v += step) ticks.push(Number(v.toFixed(10)))
  return { min: Number(min.toFixed(10)), max: Number(max.toFixed(10)), step, ticks }
}

/** The range a value axis covers: from zero (bars always; lines by default) or fitted to the data. */
export function valueRange(values: number[], fromZero: boolean): [number, number] {
  const finite = values.filter(Number.isFinite)
  if (finite.length === 0) return [0, 1]
  const lo = Math.min(...finite), hi = Math.max(...finite)
  return fromZero ? [Math.min(lo, 0), Math.max(hi, 0)] : [lo, hi]
}

// ----------------------------------------------------------------- stacking

/**
 * Stacked bars: each series' [start, end] in every category, positives
 * piled up from zero and negatives down from it, so a negative value
 * never hides behind a positive one. Also the lowest and highest the
 * stacks reach, for the scale.
 */
export function stackSeries(series: Series[]): { segments: [number, number][][]; min: number; max: number } {
  const n = Math.max(0, ...series.map((s) => s.values.length))
  const up = new Array<number>(n).fill(0), down = new Array<number>(n).fill(0)
  const segments = series.map((s) => Array.from({ length: n }, (_, i): [number, number] => {
    const v = s.values[i] ?? 0
    if (v >= 0) { const seg: [number, number] = [up[i], up[i] + v]; up[i] += v; return seg }
    const seg: [number, number] = [down[i] + v, down[i]]; down[i] += v; return seg
  }))
  return { segments, min: Math.min(0, ...down), max: Math.max(0, ...up) }
}

// ------------------------------------------------------------- pie and donut

export type Slice = { label: string; value: number; /** The category's place in the table, which keeps its color when sorted. */ index: number }

/**
 * A pie's or a donut's slices: one per category, cut from one series (the
 * `column`th, counting from 1; the first when there is no such column).
 * Negative values count as nothing. `largestFirst` sorts them from the
 * largest down, keeping the table's order among equals.
 */
export function pieSlices(data: ChartData, column: number, largestFirst = false): Slice[] {
  const s = data.series[column - 1] ?? data.series[0]
  if (!s) return []
  const slices = data.categories.map((label, index) => ({ label, value: Math.max(s.values[index] ?? 0, 0), index }))
  return largestFirst ? [...slices].sort((a, b) => b.value - a.value || a.index - b.index) : slices
}

/** A share of a whole as a legend writes it: 45%, and <1% rather than a misleading 0%. */
export function percentText(value: number, total: number): string {
  if (!(total > 0) || !(value > 0)) return '0%'
  const pct = (value / total) * 100
  if (pct < 1) return '<1%'
  if (pct > 99 && pct < 100) return '>99%'
  return `${Math.round(pct)}%`
}

/**
 * A slice's value and share for the legend: "18 (45%)". Percentages that
 * already add up to 100 are their own share, so "45%" is not followed by
 * "(45%)".
 */
export function legendValue(value: number, total: number, format: NumberFormat, locale?: string): string {
  const shown = formatValue(value, format, { locale })
  if (format.kind === 'percent' && Math.abs(total - 100) < 0.5) return shown
  return `${shown} (${percentText(value, total)})`
}

export const DONUT_CENTERS = ['total', 'largest', 'custom'] as const
export type DonutCenter = (typeof DONUT_CENTERS)[number]
export const DONUT_CENTER_LABELS: Record<DonutCenter, string> = {
  total: 'Total',
  largest: 'Largest Slice',
  custom: 'Custom Text',
}
export function isDonutCenter(value: unknown): value is DonutCenter {
  return typeof value === 'string' && (DONUT_CENTERS as readonly string[]).includes(value)
}

/** What the hole of a donut says, and the word under it. */
export function donutCenter(
  mode: unknown,
  slices: Slice[],
  format: NumberFormat,
  custom = '',
  locale?: string,
): { text: string; caption: string } {
  const total = slices.reduce((sum, s) => sum + s.value, 0)
  if (mode === 'custom') return { text: custom.trim(), caption: '' }
  if (mode === 'largest') {
    const top = slices.reduce<Slice | null>((best, s) => (!best || s.value > best.value ? s : best), null)
    if (!top || total <= 0) return { text: '0%', caption: '' }
    return { text: percentText(top.value, total), caption: truncate(top.label, 12) }
  }
  return { text: formatValue(total, format, { compact: true, locale }), caption: 'total' }
}

// ------------------------------------------------------------------- labels

/** A label cut to `max` characters with an ellipsis. */
export function truncate(label: string, max: number): string {
  if (max < 1) return ''
  return label.length <= max ? label : `${label.slice(0, Math.max(max - 1, 1))}…`
}

/**
 * How category labels fit under a chart when each has `slot` pixels:
 * level when the longest fits, otherwise turned 45 degrees, and when even
 * turned they would overlap, only every `every`th label is written.
 * `charWidth` is the average width of a character at the label's size.
 */
export function categoryLabelLayout(
  labels: string[],
  slot: number,
  { charWidth = 6.2, lineHeight = 13, maxChars = 16 }: { charWidth?: number; lineHeight?: number; maxChars?: number } = {},
): { rotate: boolean; every: number; maxChars: number } {
  const longest = Math.max(0, ...labels.map((l) => l.length))
  if (longest * charWidth <= slot - 6) return { rotate: false, every: 1, maxChars: longest }
  // Turned 45 degrees, a label takes about its line height times √2 along
  // the axis, and a little more so neighbors do not touch.
  const every = Math.max(1, Math.ceil((lineHeight * 1.6) / Math.max(slot, 0.1)))
  return { rotate: true, every, maxChars: Math.min(longest, maxChars) }
}

// ------------------------------------------------------------------- lines

export type Point = [number, number]

/**
 * The SVG path through a line's points: straight segments, or a smooth
 * curve that never overshoots (monotone cubic, Fritsch and Carlson), so a
 * smoothed line does not invent a peak or a dip the data does not have.
 */
export function linePath(points: Point[], smooth = false): string {
  const f = (n: number) => Number(n.toFixed(2))
  if (points.length === 0) return ''
  const [x0, y0] = points[0]
  if (!smooth || points.length < 3) return `M${f(x0)},${f(y0)}` + points.slice(1).map(([x, y]) => `L${f(x)},${f(y)}`).join('')
  const n = points.length
  const dx: number[] = [], slope: number[] = []
  for (let i = 0; i < n - 1; i++) {
    dx.push(points[i + 1][0] - points[i][0])
    slope.push(dx[i] === 0 ? 0 : (points[i + 1][1] - points[i][1]) / dx[i])
  }
  const m: number[] = [slope[0]]
  for (let i = 1; i < n - 1; i++) m.push(slope[i - 1] * slope[i] <= 0 ? 0 : (slope[i - 1] + slope[i]) / 2)
  m.push(slope[n - 2])
  for (let i = 0; i < n - 1; i++) {
    if (slope[i] === 0) { m[i] = 0; m[i + 1] = 0; continue }
    const a = m[i] / slope[i], b = m[i + 1] / slope[i]
    const h = a * a + b * b
    if (h > 9) { const t = 3 / Math.sqrt(h); m[i] = t * a * slope[i]; m[i + 1] = t * b * slope[i] }
  }
  let d = `M${f(x0)},${f(y0)}`
  for (let i = 0; i < n - 1; i++) {
    const [xa, ya] = points[i], [xb, yb] = points[i + 1]
    const third = dx[i] / 3
    d += `C${f(xa + third)},${f(ya + m[i] * third)} ${f(xb - third)},${f(yb - m[i + 1] * third)} ${f(xb)},${f(yb)}`
  }
  return d
}

// ------------------------------------------------------------ accessibility

const KIND_NAMES: Record<string, string> = { column: 'Column', bar: 'Bar', line: 'Line', pie: 'Pie', donut: 'Donut' }

/**
 * What a screen reader hears for a chart: its kind and title, then either
 * every slice with its share (a pie or a donut), or the series, the span
 * of categories and the range of values.
 */
export function describeChart(
  type: string,
  title: string,
  data: ChartData,
  format: NumberFormat,
  { slices, locale }: { slices?: Slice[]; locale?: string } = {},
): string {
  const head = `${KIND_NAMES[type] ?? 'A'} chart${title ? ` of ${title}` : ''}`
  if (slices) {
    const total = slices.reduce((sum, s) => sum + s.value, 0)
    const parts = slices.map((s) => `${s.label} ${legendValue(s.value, total, format, locale)}`)
    return `${head}, total ${formatValue(total, format, { locale })}: ${parts.join(', ')}.`
  }
  const values = data.series.flatMap((s) => s.values)
  const lo = Math.min(...values), hi = Math.max(...values)
  const names = data.series.map((s) => s.name).join(', ')
  const cats = data.categories
  const span = cats.length === 1 ? `1 category, ${cats[0]}` : `${cats.length} categories from ${cats[0]} to ${cats[cats.length - 1]}`
  const series = data.series.length === 1 ? `1 series, ${names}` : `${data.series.length} series, ${names}`
  return `${head}: ${series}; ${span}; values from ${formatValue(lo, format, { locale })} to ${formatValue(hi, format, { locale })}.`
}

/**
 * The colors chosen per series (or per slice, on a pie or a donut), keyed by
 * the series' name so a color stays with its series when rows move. Stored
 * as JSON; anything that is not a name to a six-digit hex color is ignored.
 */
export function parseSeriesColors(raw: unknown): Record<string, string> {
  if (typeof raw !== 'string' || !raw) return {}
  try {
    const parsed = JSON.parse(raw) as unknown
    if (!parsed || typeof parsed !== 'object' || Array.isArray(parsed)) return {}
    const out: Record<string, string> = {}
    for (const [name, color] of Object.entries(parsed as Record<string, unknown>)) {
      if (typeof color === 'string' && /^#[0-9a-f]{6}$/i.test(color)) out[name] = color.toLowerCase()
    }
    return out
  } catch {
    return {}
  }
}

/** The stored form: sorted keys, and nothing at all when no color is chosen. */
export function serializeSeriesColors(colors: Record<string, string>): string {
  const keys = Object.keys(colors).filter((k) => /^#[0-9a-f]{6}$/i.test(colors[k])).sort()
  return keys.length ? JSON.stringify(Object.fromEntries(keys.map((k) => [k, colors[k].toLowerCase()]))) : ''
}

/** The color a series is drawn in: the chosen one, else the palette's for its place. */
export function seriesColor(chosen: Record<string, string>, name: string, index: number, palette: readonly string[]): string {
  return chosen[name] ?? palette[index % palette.length]
}

// The chart palette: readable on both themes, and distinguishable without
// relying on hue alone (the legend names every series).
export const COLORS = ['#0c66e4', '#00875a', '#a54800', '#5e4db2', '#ae4787', '#206a83', '#946f00', '#bf2600']
/** A pie's and a donut's colors in the glass style: brighter and more saturated (the owner, 2026-09-28). */
export const GLASS_SLICE_COLORS = ['#1f7bff', '#00b86b', '#ff7a1a', '#8b5cf6', '#ec4899', '#06b6d4', '#f5b800', '#ef4444']
