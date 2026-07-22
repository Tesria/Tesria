import { useCallback, useEffect, useState } from 'react'
import { api, ApiError, type TrashedPage } from '../api/client'
import { useSpaceContext } from './SpacePage'

/** Lists trashed pages for the space, with restore and permanent-delete. */
export function TrashPage() {
  const { space, reloadTree } = useSpaceContext()
  const [items, setItems] = useState<TrashedPage[] | null>(null)
  const [error, setError] = useState<string | null>(null)

  const load = useCallback(() => {
    setError(null)
    api.pages
      .trash(space.id)
      .then(setItems)
      .catch((err: unknown) => setError(err instanceof Error ? err.message : 'Failed to load trash.'))
  }, [space.id])

  useEffect(() => {
    setItems(null)
    load()
  }, [load])

  async function restore(id: string) {
    try {
      await api.pages.untrash(id)
      reloadTree()
      load()
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Restore failed.')
    }
  }

  async function purge(id: string, title: string) {
    if (!confirm(`Permanently delete "${title}" and its sub-pages? This cannot be undone.`)) return
    try {
      await api.pages.purge(id)
      load()
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Delete failed.')
    }
  }

  return (
    <div className="page-wrap">
      <h1>Trash</h1>
      <p className="muted small">Deleted pages in {space.name}. Restoring brings back the page and its sub-pages.</p>
      {error && <p className="alert alert--error">{error}</p>}
      {items && items.length === 0 && <p className="muted">Trash is empty.</p>}
      <ul className="version-list">
        {items?.map((t) => (
          <li key={t.id} className="version">
            <span className="version__num">{t.title}</span>
            <span className="muted small">deleted {new Date(t.deletedAt).toLocaleString()}</span>
            <span className="version__actions">
              <button type="button" className="link-btn" onClick={() => restore(t.id)}>Restore</button>
              <button type="button" className="link-btn link-btn--danger" onClick={() => purge(t.id, t.title)}>
                Delete permanently
              </button>
            </span>
          </li>
        ))}
      </ul>
    </div>
  )
}
