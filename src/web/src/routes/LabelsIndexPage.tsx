import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { api, type LabelUsage } from '../api/client'

/**
 * Every label in use, with how many pages you can see carry it (dev-plan
 * 15.3). Before this, a label could only be reached from a page that had it.
 */
export function LabelsIndexPage() {
  const [labels, setLabels] = useState<LabelUsage[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [filter, setFilter] = useState('')

  useEffect(() => {
    api.labels.all().then(setLabels).catch((err: unknown) => setError(err instanceof Error ? err.message : 'Failed to load.'))
  }, [])

  const shown = (labels ?? []).filter((l) => l.name.includes(filter.trim().toLowerCase()))

  return (
    <div className="page-wrap">
      <h1>Labels</h1>
      {error && <p className="alert alert--error">{error}</p>}
      {!labels && !error && <p className="muted">Loading…</p>}
      {labels && labels.length === 0 && <p className="muted">No labels are in use yet. Add one under any page’s title.</p>}
      {labels && labels.length > 0 && (
        <>
          <input className="labels-index__filter" placeholder="Filter labels" aria-label="Filter labels"
            value={filter} onChange={(e) => setFilter(e.target.value)} />
          <ul className="labels-index">
            {shown.map((l) => (
              <li key={l.name}>
                <Link to={`/labels/${encodeURIComponent(l.name)}`} className="label-chip">{l.name}</Link>
                <span className="muted small">{l.pageCount} page{l.pageCount === 1 ? '' : 's'}</span>
              </li>
            ))}
          </ul>
        </>
      )}
    </div>
  )
}
