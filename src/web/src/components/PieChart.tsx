import { useId, type CSSProperties } from 'react'
import { DONUT_RADIUS, DONUT_STROKE, DONUT_VIEW, donutFontSize, donutSegments } from './donut'

/**
 * The one pie in the product.
 *
 * Extracted from the editor's chart node (dev-plan 9.3) so that the backups
 * page can draw a disk without a second implementation or a chart library.
 * A slice is a path from the center; the whole thing is one small themed
 * SVG, which is also what lets it survive a capture-based export.
 *
 * The aria label is the numbers, not "pie chart": a chart nobody can see is
 * only worth the values it stands for.
 */
export type PieSlice = {
  label: string
  value: number
  /** A CSS color, usually a theme token, so the slice follows the theme. */
  color: string
  /** Its color in the glass style, where it is a gradient; the flat color when left out. */
  glassColor?: string
}

/**
 * The glass on a pie or a donut (0.8.1; reworked 2026-09-28 at the owner's
 * request): light on the rims, as on a bevelled glass edge. Along the
 * pie's outer edge, and along both edges of the donut's ring: a soft band
 * and a thin bright line, lit at the top-left and fading round to a faint
 * shade at the bottom-right. The ring's inner edge faces the other way, so
 * its light falls on the opposite side. Always drawn and hidden unless the
 * chart is glass (glass.css), so an export keeps what the chart asked for.
 */
function Sheen({ cx, cy, r, id, ring }: { cx: number; cy: number; r: number; id: string; ring?: number }) {
  const outer = ring ? r + ring / 2 : r
  const inner = ring ? r - ring / 2 : 0
  const band = Math.max(outer * 0.09, 0.8)
  const line = Math.max(outer * 0.022, 0.25)
  const lit = (gid: string, flip: boolean) => (
    <linearGradient id={gid} gradientUnits="userSpaceOnUse"
      x1={cx - outer} y1={cy - outer} x2={cx + outer} y2={cy + outer}>
      <stop offset="0" stopColor={flip ? '#000' : '#fff'} stopOpacity={flip ? 0.22 : 0.85} />
      <stop offset="0.5" stopColor="#fff" stopOpacity="0" />
      <stop offset="1" stopColor={flip ? '#fff' : '#000'} stopOpacity={flip ? 0.75 : 0.22} />
    </linearGradient>
  )
  const rim = (at: number, inward: boolean, gid: string) => (
    <>
      {/* The soft band, just inside the edge, then the bright line on it. */}
      <circle cx={cx} cy={cy} r={inward ? at - band / 2 : at + band / 2} fill="none" stroke={`url(#${gid})`} strokeWidth={band} opacity={0.45} />
      <circle cx={cx} cy={cy} r={inward ? at - line / 2 : at + line / 2} fill="none" stroke={`url(#${gid})`} strokeWidth={line} />
    </>
  )
  return (
    <g className="chart-sheen">
      <defs>
        {lit(`${id}-outer`, false)}
        {inner > 0 && lit(`${id}-inner`, true)}
      </defs>
      {rim(outer, true, `${id}-outer`)}
      {inner > 0 && rim(inner, false, `${id}-inner`)}
    </g>
  )
}

/**
 * Each slice's own gradient in the glass style (the owner, 2026-09-28):
 * its color lit at the top-left and deeper at the bottom-right, across the
 * whole disc so every slice shares one light. Drawn always; glass.css swaps
 * it in for the flat fill (through --glass-fill), so an export keeps what
 * the chart asked for.
 */
function SliceGradient({ id, color, cx, cy, r, turned }: { id: string; color: string; cx: number; cy: number; r: number; turned?: boolean }) {
  // Drawn inside the donut's group turned a quarter to the left, the
  // top-left of the screen is the group's top-right.
  const [x1, y1, x2, y2] = turned ? [cx + r, cy - r, cx - r, cy + r] : [cx - r, cy - r, cx + r, cy + r]
  return (
    <linearGradient id={id} gradientUnits="userSpaceOnUse" x1={x1} y1={y1} x2={x2} y2={y2}>
      <stop offset="0" style={{ stopColor: `color-mix(in srgb, ${color} 62%, #fff)` }} />
      <stop offset="0.5" style={{ stopColor: color }} />
      <stop offset="1" style={{ stopColor: `color-mix(in srgb, ${color} 68%, #000)` }} />
    </linearGradient>
  )
}
const glassFill = (id: string) => ({ '--glass-fill': `url(#${id})` }) as CSSProperties

export function PieChart({
  slices, size = 120, label,
}: {
  slices: PieSlice[]
  size?: number
  label: string
}) {
  const sheenId = useId().replace(/:/g, '')
  const total = slices.reduce((sum, s) => sum + Math.max(s.value, 0), 0)
  const r = size / 2 - 5
  const c = size / 2
  let angle = -Math.PI / 2

  return (
    <svg viewBox={`0 0 ${size} ${size}`} width={size} height={size} role="img" aria-label={label}>
      <defs>
        {slices.map((slice, i) => <SliceGradient key={slice.label} id={`pie-fill-${sheenId}-${i}`} color={slice.glassColor ?? slice.color} cx={c} cy={c} r={r} />)}
      </defs>
      {total > 0 && slices.map((slice, i) => {
        const value = Math.max(slice.value, 0)
        const sweep = (value / total) * Math.PI * 2
        // A slice that is the whole circle cannot be drawn as an arc: the
        // start and end points are the same, so the path collapses to
        // nothing. Drawn as a circle instead, which is what it is.
        if (value === total) {
          return <circle key={slice.label} className="chart-slice" style={glassFill(`pie-fill-${sheenId}-${i}`)} cx={c} cy={c} r={r} fill={slice.color} />
        }
        const [x1, y1] = [c + r * Math.cos(angle), c + r * Math.sin(angle)]
        angle += sweep
        const [x2, y2] = [c + r * Math.cos(angle), c + r * Math.sin(angle)]
        return (
          <path
            key={slice.label}
            className="chart-slice"
            style={glassFill(`pie-fill-${sheenId}-${i}`)}
            fill={slice.color}
            d={`M${c} ${c} L${x1.toFixed(2)} ${y1.toFixed(2)} A${r} ${r} 0 ${sweep > Math.PI ? 1 : 0} 1 ${x2.toFixed(2)} ${y2.toFixed(2)} Z`}
          />
        )
      })}
      {total > 0 && <Sheen cx={c} cy={c} r={r} id={`pie-sheen-${sheenId}`} />}
    </svg>
  )
}

/**
 * The pie's ring-shaped sibling (0.8.0): the same slices, drawn as a ring
 * like the donut on tesria.com's front page, with a track behind it and room
 * in the middle for one number. The backups page's disks use it, and the
 * editor's chart offers it as a type of its own beside pie, so pages that
 * chose pie keep their pies.
 *
 * Stroked circles on a ring whose circumference is 100 (see donut.ts), so
 * each segment's dash is its percentage. The text is outside the rotated
 * group, so it reads upright.
 */
export function DonutChart({
  slices, size = 120, label, center, caption,
}: {
  slices: PieSlice[]
  size?: number
  label: string
  /** One short value for the hole, such as "38%" or a total. */
  center?: string
  /** A word under it, such as "free". Left out on small donuts, where it would be unreadable. */
  caption?: string
}) {
  const segments = donutSegments(slices.map((s) => s.value))
  const mid = DONUT_VIEW / 2
  const sheenId = useId().replace(/:/g, '')
  const showCaption = caption && size >= 96
  return (
    <svg className="donut" viewBox={`0 0 ${DONUT_VIEW} ${DONUT_VIEW}`} width={size} height={size} role="img" aria-label={label}>
      <g transform={`rotate(-90 ${mid} ${mid})`} fill="none" strokeWidth={DONUT_STROKE}>
        <defs>
          {slices.map((slice, i) => (
            <SliceGradient key={slice.label} id={`donut-fill-${sheenId}-${i}`} color={slice.glassColor ?? slice.color}
              cx={mid} cy={mid} r={DONUT_RADIUS + DONUT_STROKE / 2} turned />
          ))}
        </defs>
        <circle className="donut__track" cx={mid} cy={mid} r={DONUT_RADIUS} />
        {segments.map((segment, i) => segment.length > 0 && (
          <circle
            key={slices[i].label}
            className="chart-slice chart-slice--ring"
            style={glassFill(`donut-fill-${sheenId}-${i}`)}
            cx={mid}
            cy={mid}
            r={DONUT_RADIUS}
            stroke={slices[i].color}
            strokeDasharray={`${segment.length.toFixed(3)} ${(100 - segment.length).toFixed(3)}`}
            strokeDashoffset={(-segment.start).toFixed(3)}
          />
        ))}
      </g>
      {/* Outside the rotated group, so the light still falls from the top-left. */}
      <Sheen cx={mid} cy={mid} r={DONUT_RADIUS} id={`donut-sheen-${sheenId}`} ring={DONUT_STROKE} />
      {center && (
        <text
          className="donut__center"
          x={mid}
          y={showCaption ? mid - 1.2 : mid}
          textAnchor="middle"
          dominantBaseline="central"
          fontSize={donutFontSize(center)}
        >
          {center}
        </text>
      )}
      {center && showCaption && (
        <text className="donut__caption" x={mid} y={mid + 5.2} textAnchor="middle" dominantBaseline="central" fontSize={3.4}>
          {caption}
        </text>
      )}
    </svg>
  )
}
