import { useState } from 'react'
import type { DailyPoint } from '../../api/client'

/**
 * A single-series sparkline.
 *
 * One series, so there is no legend and no categorical palette: the tile's
 * own title names what it is. Color is a single token: `--primary` for
 * ordinary activity, `--danger` for failed logins, which is a status signal
 * rather than "another series".
 *
 * The axis is deliberately just a baseline. At this size a grid would be more
 * ink than data, and the numbers that matter are the hero figure above it and
 * the hovered value.
 */
export function Sparkline({
  points,
  tone = 'primary',
  label,
}: {
  points: DailyPoint[]
  tone?: 'primary' | 'danger'
  label: string
}) {
  const [hover, setHover] = useState<number | null>(null)

  const width = 240
  const height = 40
  const max = Math.max(1, ...points.map((p) => p.count))

  const x = (i: number) => (points.length <= 1 ? 0 : (i / (points.length - 1)) * width)
  const y = (count: number) => height - (count / max) * (height - 4) - 2

  const line = points.map((p, i) => `${i === 0 ? 'M' : 'L'}${x(i).toFixed(1)},${y(p.count).toFixed(1)}`).join(' ')
  const area = `${line} L${width},${height} L0,${height} Z`
  const stroke = tone === 'danger' ? 'var(--danger)' : 'var(--primary)'

  const active = hover !== null ? points[hover] : null

  return (
    <div className="spark">
      <svg
        viewBox={`0 0 ${width} ${height}`}
        preserveAspectRatio="none"
        className="spark__svg"
        role="img"
        aria-label={`${label}: ${points.reduce((sum, p) => sum + p.count, 0)} over ${points.length} days`}
        onMouseLeave={() => setHover(null)}
        onMouseMove={(e) => {
          const rect = e.currentTarget.getBoundingClientRect()
          const ratio = (e.clientX - rect.left) / rect.width
          setHover(Math.max(0, Math.min(points.length - 1, Math.round(ratio * (points.length - 1)))))
        }}
      >
        <path d={area} fill={stroke} opacity="0.12" />
        <path d={line} fill="none" stroke={stroke} strokeWidth="2" strokeLinejoin="round" strokeLinecap="round" />
        {active && (
          <>
            <line x1={x(hover!)} y1="0" x2={x(hover!)} y2={height} stroke="var(--border)" strokeWidth="1" />
            {/* A 2px surface ring so the marker reads against the area fill. */}
            <circle cx={x(hover!)} cy={y(active.count)} r="4" fill={stroke} stroke="var(--surface)" strokeWidth="2" />
          </>
        )}
      </svg>
      <p className="spark__hint muted small">
        {active
          ? // A bare "2026-09-16" parses as midnight UTC, which is the previous
            // evening anywhere west of Greenwich: the label showed the wrong
            // day. With a time and no offset it parses as local midnight.
            `${new Date(`${active.date}T00:00:00`).toLocaleDateString()} · ${active.count}`
          : `Last ${points.length} days`}
      </p>
    </div>
  )
}

/** A hero number. Not a chart, one value has no shape to plot. */
