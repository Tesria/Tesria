/**
 * The one pie in the product.
 *
 * Extracted from the editor's chart node (dev-plan 9.3) so that the backups
 * page can draw a disk without a second implementation or a chart library.
 * A slice is a path from the centre; the whole thing is one small themed
 * SVG, which is also what lets it survive a capture-based export.
 *
 * The aria label is the numbers, not "pie chart": a chart nobody can see is
 * only worth the values it stands for.
 */
export type PieSlice = {
  label: string
  value: number
  /** A CSS colour, usually a theme token, so the slice follows the theme. */
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
