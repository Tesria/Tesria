import { useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { api, type LabelledPage } from '../api/client'

/** Browse every page carrying a given label. */
export function LabelPage() {
  const { name = '' } = useParams()
  const [pages, setPages] = useState<LabelledPage[] | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let cancelled = false
    setPages(null)
    setError(null)
    api.labels
      .pages(name)
      .then((p) => !cancelled && setPages(p))
      .catch((err: unknown) => !cancelled && setError(err instanceof Error ? err.message : 'Failed to load.'))
    return () => {
      cancelled = true
    }
  }, [name])

  return (
    <div className="page-wrap">
      <h1>
        Label: <span className="badge">{name}</span>
      </h1>
      {error && <p className="alert alert--error">{error}</p>}
      {!pages && !error && <p className="muted">Loading…</p>}
      {pages && pages.length === 0 && <p className="muted">No pages carry this label.</p>}
      <ul className="search-results">
        {pages?.map((p) => (
          <li key={p.pageId} className="search-result">
            <Link to={`/spaces/${p.spaceKey}/pages/${p.pageId}`} className="search-result__title">
              {p.title}
            </Link>
            <span className="badge">{p.spaceKey}</span>
          </li>
        ))}
      </ul>
    </div>
  )
}
