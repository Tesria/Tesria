import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { api, ApiError, type BackupHealth, type DailyPoint, type Dashboard } from '../../api/client'
import { bytes, relative } from './format'

const RANGES = [7, 30, 90] as const


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
function Sparkline({
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
function Stat({ label, value, hint }: { label: string; value: string | number; hint?: string }) {
  return (
    <div className="stat">
      <p className="stat__label">{label}</p>
      <p className="stat__value">{value}</p>
      {hint && <p className="muted small">{hint}</p>}
    </div>
  )
}

const BACKUP_TILE: Record<string, string> = {
  logical: 'Last database dump',
  physical: 'Last physical backup',
}

/**
 * One backup agent's health (dev-plan 2.5's Health row, which needed 9.1).
 * Failure is the only thing drawn in the danger color; "no backup yet" on
 * a fresh instance is not a failure.
 */
function BackupTile({ health }: { health: BackupHealth }) {
  const problem = health.lastRunFailed ? 'Last run failed'
    : health.overdue ? 'Overdue'
      : !health.reporting ? 'Not reporting'
        : !health.online ? 'Agent offline'
          : null
  return (
    <Link to="/admin/backups" className="stat stat--link">
      <p className="stat__label">{BACKUP_TILE[health.agent] ?? health.agent}</p>
      <p className="stat__value">{health.lastBackupAt ? relative(health.lastBackupAt, Date.now(), 'short') : 'None yet'}</p>
      <p className={problem && (health.lastRunFailed || health.overdue) ? 'small backup-text--bad' : 'muted small'}>
        {problem ?? (health.lastBackupBytes != null ? `OK · ${bytes(health.lastBackupBytes)}` : 'OK')}
      </p>
    </Link>
  )
}

/** Admin → Dashboard (dev-plan 2.5). */
export function AdminDashboardPage() {
  const [range, setRange] = useState<number>(30)
  const [data, setData] = useState<Dashboard | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let canceled = false
    setData(null)
    api.admin
      .dashboard(range)
      .then((d) => !canceled && setData(d))
      .catch((err: unknown) =>
        !canceled && setError(err instanceof ApiError ? err.message : 'Could not load the dashboard.'))
    return () => {
      canceled = true
    }
  }, [range])

  if (error) return <p className="alert alert--error">{error}</p>

  return (
    <>
      {/* Filters in one row above the charts. */}
      <div className="dash__filters">
        {RANGES.map((r) => (
          <button
            key={r}
            type="button"
            className={r === range ? 'cell-menu__scope is-active' : 'cell-menu__scope'}
            onClick={() => setRange(r)}
          >
            {r} days
          </button>
        ))}
      </div>

      {!data ? (
        <p className="muted">Loading…</p>
      ) : (
        <>
          <h2 className="dash__heading">People</h2>
          <div className="dash__grid">
            <Stat label="Users" value={data.people.total} hint={`${data.people.admins} admin`} />
            <Stat label="Active (7 days)" value={data.people.activeLast7Days} />
            <Stat label="Active (30 days)" value={data.people.activeLast30Days} />
            <Stat
              label="New in range"
              value={data.people.newInRange}
              hint={data.people.suspended > 0 ? `${data.people.suspended} suspended` : undefined}
            />
            <div className="stat stat--wide">
              <p className="stat__label">Sign-ins</p>
              <Sparkline points={data.people.loginsPerDay} label="Sign-ins" />
            </div>
            <div className="stat stat--wide">
              <p className="stat__label">Failed sign-ins</p>
              {/* On the front page on purpose: a spike here is the first sign
                  of a brute-force attempt, and dev-plan 3.3 turns it into an
                  alert. Until then, someone has to be able to see it. */}
              <Sparkline points={data.people.failedLoginsPerDay} tone="danger" label="Failed sign-ins" />
            </div>
          </div>

          <h2 className="dash__heading">Content</h2>
          <div className="dash__grid">
            <Stat label="Spaces" value={data.content.spaces} />
            <Stat label="Pages" value={data.content.pages} hint={`${data.content.versions} versions`} />
            <Stat label="Comments" value={data.content.comments} />
            <Stat
              label="Attachments"
              value={data.content.attachments}
              hint={bytes(data.content.storageBytes)}
            />
            <div className="stat stat--wide">
              <p className="stat__label">Pages created</p>
              <Sparkline points={data.content.pagesCreatedPerDay} label="Pages created" />
            </div>
          </div>

          <h2 className="dash__heading">Usage</h2>
          <div className="dash__grid">
            <Stat label="Page views" value={data.usage.viewsInRange} hint={`in ${range} days`} />
            <div className="stat stat--wide">
              <p className="stat__label">Views per day</p>
              <Sparkline points={data.usage.viewsPerDay} label="Views per day" />
            </div>
          </div>

          <h2 className="dash__heading">Health</h2>
          <div className="dash__grid">
            {data.health.backups.map((h) => <BackupTile key={h.agent} health={h} />)}
          </div>

          <div className="dash__tables">
            <section className="dash__panel">
              <h3 className="stat__label">Most viewed</h3>
              {data.usage.topPages.length === 0 ? (
                <p className="muted small">No page views recorded yet.</p>
              ) : (
                <table className="admin-table dash__table">
                  <thead>
                    <tr><th>Page</th><th>Views</th></tr>
                  </thead>
                  <tbody>
                    {data.usage.topPages.map((p) => (
                      <tr key={p.pageId}>
                        <td>
                          {/* A page deleted since it was viewed keeps its views
                              but has nowhere to link to and no space to name. */}
                          {p.spaceKey ? (
                            <>
                              <Link to={`/spaces/${p.spaceKey}/pages/${p.pageId}`}>{p.title}</Link>{' '}
                              <span className="badge">{p.spaceKey}</span>
                            </>
                          ) : (
                            <span className="muted">Deleted page</span>
                          )}
                        </td>
                        <td>{p.views}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              )}
            </section>

            <section className="dash__panel">
              <h3 className="stat__label">Most active editors</h3>
              {data.usage.topEditors.length === 0 ? (
                <p className="muted small">No edits in this range.</p>
              ) : (
                <table className="admin-table dash__table">
                  <thead>
                    <tr><th>Editor</th><th>Versions</th></tr>
                  </thead>
                  <tbody>
                    {data.usage.topEditors.map((e) => (
                      <tr key={e.userId}>
                        <td>{e.displayName}</td>
                        <td>{e.versions}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              )}
            </section>
          </div>
        </>
      )}
    </>
  )
}
