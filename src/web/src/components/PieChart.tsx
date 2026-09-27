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
}

export function PieChart({
  slices, size = 120, label,
}: {
  slices: PieSlice[]
  size?: number
  label: string
}) {
  const total = slices.reduce((sum, s) => sum + Math.max(s.value, 0), 0)
  const r = size / 2 - 5
  const c = size / 2
  let angle = -Math.PI / 2

  return (
    <svg viewBox={`0 0 ${size} ${size}`} width={size} height={size} role="img" aria-label={label}>
      {total > 0 && slices.map((slice) => {
        const value = Math.max(slice.value, 0)
        const sweep = (value / total) * Math.PI * 2
        // A slice that is the whole circle cannot be drawn as an arc: the
        // start and end points are the same, so the path collapses to
        // nothing. Drawn as a circle instead, which is what it is.
        if (value === total) {
          return <circle key={slice.label} cx={c} cy={c} r={r} fill={slice.color} />
        }
        const [x1, y1] = [c + r * Math.cos(angle), c + r * Math.sin(angle)]
        angle += sweep
        const [x2, y2] = [c + r * Math.cos(angle), c + r * Math.sin(angle)]
        return (
          <path
            key={slice.label}
            fill={slice.color}
            d={`M${c} ${c} L${x1.toFixed(2)} ${y1.toFixed(2)} A${r} ${r} 0 ${sweep > Math.PI ? 1 : 0} 1 ${x2.toFixed(2)} ${y2.toFixed(2)} Z`}
          />
        )
      })}
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
  const showCaption = caption && size >= 96
  return (
    <svg className="donut" viewBox={`0 0 ${DONUT_VIEW} ${DONUT_VIEW}`} width={size} height={size} role="img" aria-label={label}>
      <g transform={`rotate(-90 ${mid} ${mid})`} fill="none" strokeWidth={DONUT_STROKE}>
        <circle className="donut__track" cx={mid} cy={mid} r={DONUT_RADIUS} />
        {segments.map((segment, i) => segment.length > 0 && (
          <circle
            key={slices[i].label}
            cx={mid}
            cy={mid}
            r={DONUT_RADIUS}
            stroke={slices[i].color}
            strokeDasharray={`${segment.length.toFixed(3)} ${(100 - segment.length).toFixed(3)}`}
            strokeDashoffset={(-segment.start).toFixed(3)}
          />
        ))}
      </g>
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
