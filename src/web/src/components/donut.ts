/**
 * The donut's geometry (0.8.0), kept apart from the component so it can be
 * tested: where each segment starts and how long it is, on a ring whose
 * circumference is exactly 100, the same drawing as tesria.com's front page.
 * With that radius a segment's length is its percentage, and one
 * stroke-dasharray per segment draws it.
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
 */
export function donutSegments(values: number[]): DonutSegment[] {
  const clean = values.map((v) => (Number.isFinite(v) && v > 0 ? v : 0))
  const total = clean.reduce((sum, v) => sum + v, 0)
  if (total <= 0) return clean.map(() => ({ length: 0, start: 0 }))
  let start = 0
  return clean.map((v) => {
    const length = (v / total) * 100
    const segment = { length, start }
    start += length
    return segment
  })
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
