/**
 * The donut's geometry (0.8.0), kept apart from the component so it can be
 * tested: where each segment starts and how long it is, on a ring whose
 * circumference is exactly 100, the same drawing as tesria.com's front page.
 * With that radius a segment's length is its percentage; each segment is
 * drawn as its own arc (donutArcPath).
 */

/** A radius whose circumference is 100 (100 / 2π). */
export const DONUT_RADIUS = 15.915
/** The canvas: 42 across, room for the ring and its stroke. */
export const DONUT_VIEW = 42
export const DONUT_STROKE = 7

export type DonutSegment = { length: number; start: number }

/**
 * Each value's segment, in order, clockwise from the top. Negative values
 * count as nothing, as they do in the pie. All zero means no segments: the
 * track alone is drawn, which reads as "nothing" rather than as a full ring.
 *
 * `minLength` gives every nonzero value at least that much of the ring,
 * taken from the others in proportion, so a slice of a fraction of a
 * percent is a thin visible sliver rather than a sub-pixel speck that
 * blurs the edge of its neighbor (the Backups page's disk donut, where the
 * wiki and its backups are each well under 1% of a disk, 2026-09-29). The
 * legend still gives the true numbers.
 */
export function donutSegments(values: number[], minLength = 0): DonutSegment[] {
  const clean = values.map((v) => (Number.isFinite(v) && v > 0 ? v : 0))
  const total = clean.reduce((sum, v) => sum + v, 0)
  if (total <= 0) return clean.map(() => ({ length: 0, start: 0 }))
  let lengths = clean.map((v) => (v / total) * 100)
  const small = lengths.filter((l) => l > 0 && l < minLength).length
  if (small > 0 && small * minLength < 100) {
    const bigTotal = lengths.filter((l) => l >= minLength).reduce((sum, l) => sum + l, 0)
    const room = 100 - small * minLength
    lengths = lengths.map((l) => (l === 0 ? 0 : l < minLength ? minLength : (l / bigTotal) * room))
  }
  let start = 0
  return lengths.map((length) => {
    const segment = { length, start }
    start += length
    return segment
  })
}

/**
 * The SVG path of one segment as an arc of the ring, for a stroke. The
 * segment runs from `start` to `start + length` (in hundredths of the ring,
 * clockwise from the positive x axis; the component turns the group so 0 is
 * the top). Each segment is drawn as its own arc rather than as a dash of a
 * whole circle: a dash that is tiny, or that ends where the circle's path
 * begins, is drawn with a stepped edge in Chrome.
 */
export function donutArcPath(cx: number, cy: number, r: number, start: number, length: number): string {
  const a0 = (start / 100) * Math.PI * 2
  const a1 = ((start + length) / 100) * Math.PI * 2
  const x0 = cx + r * Math.cos(a0), y0 = cy + r * Math.sin(a0)
  const x1 = cx + r * Math.cos(a1), y1 = cy + r * Math.sin(a1)
  const large = length > 50 ? 1 : 0
  const f = (n: number) => n.toFixed(4)
  return `M ${f(x0)} ${f(y0)} A ${f(r)} ${f(r)} 0 ${large} 1 ${f(x1)} ${f(y1)}`
}

/**
 * The size of the text in the hole, in canvas units: as large as fits. The
 * hole is about 25 across; a character is about 0.6 of the font size wide,
 * and 20 of the 25 are used so the text never touches the ring.
 */
export function donutFontSize(text: string, largest = 7.5): number {
  const length = Math.max(text.length, 1)
  return Math.min(largest, 20 / (0.6 * length))
}
