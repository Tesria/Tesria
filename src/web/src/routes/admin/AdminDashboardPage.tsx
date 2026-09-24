import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { api, ApiError, type BackupHealth, type Dashboard } from '../../api/client'
import { bytes, relative } from './format'
import { Sparkline } from './Sparkline'

const RANGES = [7, 30, 90] as const


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
            <Stat label="Users" value={data.people.total} hint={`${data.people.admins} ${data.people.admins === 1 ? 'administrator' : 'administrators'}`} />
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
            {data.version && (
              <div className="stat">
                <p className="stat__label">Tesria version</p>
                <p className="stat__value">{data.version.current}</p>
                {data.version.previous && data.version.changedAt && (
                  <p className="muted small">
                    Upgraded from {data.version.previous} on{' '}
                    {new Date(data.version.changedAt).toLocaleDateString(undefined, { month: 'long', day: 'numeric', year: 'numeric' })}
                  </p>
                )}
              </div>
            )}
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
                          {/* A page deleted since it was viewed, or one this
                              administrator may not see, keeps its views but has
                              nowhere to link to and no space to name; the
                              server's title says which. */}
                          {p.spaceKey ? (
                            <>
                              <Link to={`/spaces/${p.spaceKey}/pages/${p.pageId}`}>{p.title}</Link>{' '}
                              <span className="badge">{p.spaceKey}</span>
                            </>
                          ) : (
                            <span className="muted">{p.title === '(deleted)' ? 'Deleted page' : p.title}</span>
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
