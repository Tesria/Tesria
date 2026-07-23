import { useEffect, useState } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { api, type SearchResult } from '../api/client'

export function SearchPage() {
  const [params] = useSearchParams()
  const query = params.get('q') ?? ''
  const [results, setResults] = useState<SearchResult[] | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (!query) {
      setResults([])
      return
    }
    let cancelled = false
    setResults(null)
    setError(null)
    api
      .search(query)
      .then((r) => !cancelled && setResults(r))
      .catch((err: unknown) => !cancelled && setError(err instanceof Error ? err.message : 'Search failed.'))
    return () => {
      cancelled = true
    }
  }, [query])

  return (
    <div className="page-wrap">
      <h1>Search</h1>
      {query ? (
        <p className="muted small">
          Results for <strong>“{query}”</strong>
        </p>
      ) : (
        <p className="muted">Type a query in the search box above.</p>
      )}
      {error && <p className="alert alert--error">{error}</p>}
      {query && !results && !error && <p className="muted">Searching…</p>}
      {results && results.length === 0 && query && <p className="muted">No pages matched.</p>}

      <ul className="search-results">
        {results?.map((r) => (
          <li key={r.pageId} className="search-result">
            <Link to={`/spaces/${r.spaceKey}/pages/${r.pageId}`} className="search-result__title">
              {r.title}
            </Link>
            <span className="badge">{r.spaceKey}</span>
            {r.snippet && <p className="search-result__snippet">{r.snippet}</p>}
          </li>
        ))}
      </ul>
    </div>
  )
}
