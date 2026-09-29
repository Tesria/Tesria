import { describe, expect, it } from 'vitest'
import {
  categoryLabelLayout, describeChart, detectFormat, donutCenter, formatValue, legendValue, linePath,
  niceScale, parseNumber, percentText, pieSlices, resolveFormat, stackSeries, stepDecimals, tableToChart,
  truncate, valueRange, type NumberFormat,
} from './chartData'

const PLAIN: NumberFormat = { kind: 'plain', symbol: '', decimals: 0 }
const L = 'en-US'

describe('parseNumber', () => {
  it('reads numbers the way people write them', () => {
    expect(parseNumber('1,234')).toBe(1234)
    expect(parseNumber('45%')).toBe(45)
    expect(parseNumber('£9.50')).toBe(9.5)
    expect(parseNumber('-3')).toBe(-3)
  })
  it('is null for text', () => {
    expect(parseNumber('n/a')).toBeNull()
    expect(parseNumber('')).toBeNull()
  })
})

describe('tableToChart', () => {
  const rows = [
    ['Month', 'Free', 'Team'],
    ['July', '120', '18'],
    ['August', '180', '26'],
  ]

  it('makes each column after the first a series and each row a category', () => {
    expect(tableToChart(rows)).toEqual({
      categories: ['July', 'August'],
      series: [{ name: 'Free', values: [120, 180] }, { name: 'Team', values: [18, 26] }],
    })
  })

  it('swaps rows and columns', () => {
    expect(tableToChart(rows, true)).toEqual({
      categories: ['Free', 'Team'],
      series: [{ name: 'July', values: [120, 18] }, { name: 'August', values: [180, 26] }],
    })
  })

  it('skips a row with no numbers and a column with no numbers', () => {
    const data = tableToChart([
      ['Month', 'Sales', 'Notes'],
      ['Jan', '10', 'quiet'],
      ['', '', ''],
      ['Feb', 'n/a', 'busy'],
      ['Mar', '7', ''],
    ])
    expect(data).toEqual({ categories: ['Jan', 'Mar'], series: [{ name: 'Sales', values: [10, 7] }] })
  })

  it('counts a stray text cell in a numeric column as zero', () => {
    expect(tableToChart([['A', 'B', 'C'], ['x', '5', '1'], ['y', 'soon', '2']])?.series[0].values).toEqual([5, 0])
  })

  it('is null without a header and a row of numbers', () => {
    expect(tableToChart([['A', 'B']])).toBeNull()
    expect(tableToChart([['A', 'B'], ['x', 'y']])).toBeNull()
    expect(tableToChart([['Only'], ['a'], ['b']])).toBeNull()
  })

  it('names an unnamed column or row', () => {
    const data = tableToChart([['', ''], ['', '4']], true)
    expect(data?.categories).toEqual(['Column 2'])
    expect(data?.series[0].name).toBe('Row 1')
  })

  it('reads a ragged row as zero where it has no cell', () => {
    expect(tableToChart([['A', 'B', 'C'], ['x', '1', '2'], ['y', '3']])?.series[1].values).toEqual([2, 0])
  })
})

describe('detectFormat and resolveFormat', () => {
  it('finds percentages', () => {
    expect(detectFormat([['Q', 'Share'], ['a', '45%'], ['b', '55%']])).toEqual({ kind: 'percent', symbol: '', decimals: 0 })
  })
  it('finds money and its commonest symbol, and keeps its cents', () => {
    expect(detectFormat([['Item', 'Cost'], ['a', '£9.50'], ['b', '£12'], ['c', '$3']])).toEqual({ kind: 'currency', symbol: '£', decimals: 2 })
  })
  it('is plain when marks are a minority, ignoring the header and label column', () => {
    expect(detectFormat([['$', '%'], ['50%', '1'], ['b', '2'], ['c', '3%']]).kind).toBe('plain')
  })
  it('lets the author override, with dollars for money the table does not mark', () => {
    const detected = detectFormat([['A', 'B'], ['x', '1.5']])
    expect(resolveFormat('auto', detected)).toEqual({ kind: 'plain', symbol: '', decimals: 1 })
    expect(resolveFormat('currency', detected)).toEqual({ kind: 'currency', symbol: '$', decimals: 1 })
    expect(resolveFormat('percent', detected).kind).toBe('percent')
    expect(resolveFormat('plain', { kind: 'currency', symbol: '€', decimals: 0 }).kind).toBe('plain')
    expect(resolveFormat('nonsense', detected)).toEqual(detected)
  })
})

describe('formatValue', () => {
  it('writes each kind', () => {
    expect(formatValue(1234, PLAIN, { locale: L })).toBe('1,234')
    expect(formatValue(45, { ...PLAIN, kind: 'percent' }, { locale: L })).toBe('45%')
    expect(formatValue(9.5, { kind: 'currency', symbol: '£', decimals: 2 }, { locale: L })).toBe('£9.50')
    expect(formatValue(-5, { kind: 'currency', symbol: '$', decimals: 0 }, { locale: L })).toBe('-$5')
  })
  it('shortens only a thousand and over when compact', () => {
    expect(formatValue(1234, PLAIN, { compact: true, locale: L })).toBe('1.2K')
    expect(formatValue(40, PLAIN, { compact: true, locale: L })).toBe('40')
    expect(formatValue(2500000, { kind: 'currency', symbol: '$', decimals: 2 }, { compact: true, locale: L })).toBe('$2.5M')
  })
  it('takes a tick step decimals over the table', () => {
    expect(formatValue(0.5, PLAIN, { decimals: 1, locale: L })).toBe('0.5')
    expect(formatValue(10, { kind: 'currency', symbol: '$', decimals: 2 }, { decimals: 0, locale: L })).toBe('$10')
  })
  it('never writes minus zero', () => {
    expect(formatValue(-0.0001, PLAIN, { locale: L })).toBe('0')
  })
})

describe('niceScale', () => {
  it('rounds the ends and steps by 1, 2 or 5', () => {
    expect(niceScale(0, 260, 5)).toEqual({ min: 0, max: 300, step: 50, ticks: [0, 50, 100, 150, 200, 250, 300] })
    expect(niceScale(0, 9, 5).ticks).toEqual([0, 2, 4, 6, 8, 10])
  })
  it('covers negative values', () => {
    const s = niceScale(-40, 90, 5)
    expect(s.min).toBeLessThanOrEqual(-40)
    expect(s.max).toBeGreaterThanOrEqual(90)
    expect(s.ticks).toContain(0)
  })
  it('handles fractions without floating-point noise', () => {
    expect(niceScale(0, 1.2, 5).ticks).toEqual([0, 0.2, 0.4, 0.6, 0.8, 1, 1.2])
  })
  it('gives nothing, and a single value, some room', () => {
    expect(niceScale(0, 0)).toMatchObject({ min: 0, max: 1 })
    const one = niceScale(50, 50)
    expect(one.min).toBeLessThan(50)
    expect(one.max).toBeGreaterThan(50)
  })
  it('fits a range well away from zero', () => {
    const s = niceScale(410, 490, 5)
    expect(s.min).toBe(400)
    expect(s.max).toBe(500)
  })
})

describe('valueRange', () => {
  it('starts at zero, or fits the data', () => {
    expect(valueRange([410, 490], true)).toEqual([0, 490])
    expect(valueRange([410, 490], false)).toEqual([410, 490])
    expect(valueRange([-5, 10], true)).toEqual([-5, 10])
    expect(valueRange([], true)).toEqual([0, 1])
  })
})

describe('stepDecimals', () => {
  it('counts the places a step needs', () => {
    expect(stepDecimals(50)).toBe(0)
    expect(stepDecimals(0.5)).toBe(1)
    expect(stepDecimals(0.25)).toBe(2)
    expect(stepDecimals(0.2)).toBe(1)
  })
})

describe('stackSeries', () => {
  it('piles positives up and negatives down from zero', () => {
    const { segments, min, max } = stackSeries([
      { name: 'a', values: [10, -5] },
      { name: 'b', values: [5, 3] },
      { name: 'c', values: [-2, -4] },
    ])
    expect(segments).toEqual([
      [[0, 10], [-5, 0]],
      [[10, 15], [0, 3]],
      [[-2, 0], [-9, -5]],
    ])
    expect(min).toBe(-9)
    expect(max).toBe(15)
  })
  it('is zero to zero for nothing', () => {
    expect(stackSeries([])).toEqual({ segments: [], min: 0, max: 0 })
  })
})

describe('pieSlices', () => {
  const data = {
    categories: ['Building', 'Meetings', 'Reviews', 'Refunds'],
    series: [{ name: 'Hours', values: [18, 8, 20, -3] }, { name: 'Days', values: [3, 1, 2, 0] }],
  }
  it('cuts the first column by default, negatives as nothing', () => {
    expect(pieSlices(data, 1).map((s) => s.value)).toEqual([18, 8, 20, 0])
  })
  it('cuts the chosen column, and falls back to the first when it is gone', () => {
    expect(pieSlices(data, 2).map((s) => s.value)).toEqual([3, 1, 2, 0])
    expect(pieSlices(data, 7).map((s) => s.value)).toEqual([18, 8, 20, 0])
  })
  it('sorts largest first, keeping each slice its place for its color', () => {
    expect(pieSlices(data, 1, true).map((s) => [s.label, s.index])).toEqual([
      ['Reviews', 2], ['Building', 0], ['Meetings', 1], ['Refunds', 3],
    ])
  })
  it('keeps the table order among equal slices', () => {
    const tie = { categories: ['a', 'b', 'c'], series: [{ name: 'v', values: [1, 2, 2] }] }
    expect(pieSlices(tie, 1, true).map((s) => s.label)).toEqual(['b', 'c', 'a'])
  })
})

describe('legend values and the donut center', () => {
  it('writes a share, with <1% and >99% at the edges', () => {
    expect(percentText(18, 40)).toBe('45%')
    expect(percentText(1, 400)).toBe('<1%')
    expect(percentText(399, 400)).toBe('>99%')
    expect(percentText(0, 40)).toBe('0%')
    expect(percentText(5, 0)).toBe('0%')
  })
  it('writes a legend value in the chart format', () => {
    expect(legendValue(18, 40, PLAIN, L)).toBe('18 (45%)')
    expect(legendValue(9.5, 19, { kind: 'currency', symbol: '€', decimals: 2 }, L)).toBe('€9.50 (50%)')
    expect(legendValue(45, 100, { ...PLAIN, kind: 'percent' }, L)).toBe('45%')
    expect(legendValue(45, 90, { ...PLAIN, kind: 'percent' }, L)).toBe('45% (50%)')
  })
  const slices = [{ label: 'Building', value: 18, index: 0 }, { label: 'Meetings and more', value: 22, index: 1 }]
  it('shows the total by default, shortened', () => {
    expect(donutCenter('total', slices, PLAIN, '', L)).toEqual({ text: '40', caption: 'total' })
    expect(donutCenter(undefined, [{ label: 'a', value: 1234, index: 0 }], PLAIN, '', L)).toEqual({ text: '1.2K', caption: 'total' })
  })
  it('shows the largest slice as a share, named underneath', () => {
    expect(donutCenter('largest', slices, PLAIN, '', L)).toEqual({ text: '55%', caption: 'Meetings an…' })
    expect(donutCenter('largest', [], PLAIN, '', L)).toEqual({ text: '0%', caption: '' })
  })
  it('shows custom text as written', () => {
    expect(donutCenter('custom', slices, PLAIN, '  Q3  ', L)).toEqual({ text: 'Q3', caption: '' })
  })
})

describe('category labels', () => {
  it('stay level when they fit', () => {
    expect(categoryLabelLayout(['Jan', 'Feb'], 100)).toEqual({ rotate: false, every: 1, maxChars: 3 })
  })
  it('turn when they do not, and thin out when even turned they would overlap', () => {
    expect(categoryLabelLayout(['September', 'October'], 40)).toEqual({ rotate: true, every: 1, maxChars: 9 })
    expect(categoryLabelLayout(['September', 'October'], 5).every).toBe(5)
    expect(categoryLabelLayout(['September', 'October'], 21).every).toBe(1)
  })
  it('cut long labels', () => {
    expect(categoryLabelLayout(['A very long label for one row indeed'], 40).maxChars).toBe(16)
    expect(truncate('A very long label', 8)).toBe('A very …')
    expect(truncate('Short', 8)).toBe('Short')
  })
})

describe('linePath', () => {
  it('joins points with straight lines', () => {
    expect(linePath([[0, 10], [5, 0], [10, 10]])).toBe('M0,10L5,0L10,10')
  })
  it('curves smoothly through every point without overshooting', () => {
    const d = linePath([[0, 10], [5, 0], [10, 10]], true)
    expect(d.startsWith('M0,10C')).toBe(true)
    expect(d.endsWith(' 10,10')).toBe(true)
    // The peak is a turning point, so the curve is level there: both
    // control points around it sit at its height.
    expect(d).toContain('3.33,0 5,0C6.67,0')
  })
  it('stays flat along a flat run', () => {
    expect(linePath([[0, 5], [5, 5], [10, 5]], true)).toBe('M0,5C1.67,5 3.33,5 5,5C6.67,5 8.33,5 10,5')
  })
  it('is a straight line for two points, and nothing for none', () => {
    expect(linePath([[0, 0], [1, 1]], true)).toBe('M0,0L1,1')
    expect(linePath([])).toBe('')
  })
})

describe('describeChart', () => {
  const data = { categories: ['July', 'August', 'September'], series: [{ name: 'Free', values: [120, 180, 260] }, { name: 'Team', values: [18, 26, 41] }] }
  it('summarizes series, categories and range', () => {
    expect(describeChart('column', 'Sign-ups', data, PLAIN, { locale: L })).toBe(
      'Column chart of Sign-ups: 2 series, Free, Team; 3 categories from July to September; values from 18 to 260.')
  })
  it('lists every slice of a pie', () => {
    const slices = [{ label: 'A', value: 3, index: 0 }, { label: 'B', value: 1, index: 1 }]
    expect(describeChart('pie', '', data, PLAIN, { slices, locale: L })).toBe('Pie chart, total 4: A 3 (75%), B 1 (25%).')
  })
})

describe('series colors', () => {
  it('reads only names to six-digit hex colors', async () => {
    const { parseSeriesColors } = await import('./chartData')
    expect(parseSeriesColors('{"Free":"#FF0000","Team":"red","X":"#12345"}')).toEqual({ Free: '#ff0000' })
    expect(parseSeriesColors('not json')).toEqual({})
    expect(parseSeriesColors('')).toEqual({})
    expect(parseSeriesColors('["#ff0000"]')).toEqual({})
  })

  it('stores nothing when no color is chosen, and sorted keys otherwise', async () => {
    const { serializeSeriesColors } = await import('./chartData')
    expect(serializeSeriesColors({})).toBe('')
    expect(serializeSeriesColors({ b: '#00FF00', a: '#0000ff' })).toBe('{"a":"#0000ff","b":"#00ff00"}')
  })

  it('uses the chosen color, else the palette by place', async () => {
    const { seriesColor } = await import('./chartData')
    const palette = ['#111111', '#222222']
    expect(seriesColor({ Team: '#abcdef' }, 'Team', 0, palette)).toBe('#abcdef')
    expect(seriesColor({}, 'Free', 3, palette)).toBe('#222222')
  })
})

describe('alreadyLargestFirst', () => {
  it('knows when sorting would change nothing', async () => {
    const { alreadyLargestFirst } = await import('./chartData')
    const data = { categories: ['a', 'b', 'c'], series: [{ name: 'x', values: [18, 8, 6] }, { name: 'y', values: [1, 5, 2] }] }
    expect(alreadyLargestFirst(data, 1)).toBe(true)
    expect(alreadyLargestFirst(data, 2)).toBe(false)
  })
})
