import { type FormEvent, useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { api, ApiError, type Label } from '../../api/client'

/** Label chips for a page, with inline add/remove. */
export function PageLabels({ pageId }: { pageId: string }) {
  const [labels, setLabels] = useState<Label[]>([])
  const [name, setName] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [adding, setAdding] = useState(false)

  useEffect(() => {
    let cancelled = false
    api.labels
      .forPage(pageId)
      .then((l) => !cancelled && setLabels(l))
      .catch(() => {})
    return () => {
      cancelled = true
    }
  }, [pageId])

  async function add(e: FormEvent) {
    e.preventDefault()
    const value = name.trim()
    if (!value) return
    setError(null)
    try {
      await api.labels.add(pageId, value)
      setLabels(await api.labels.forPage(pageId))
      setName('')
      setAdding(false)
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not add the label.')
    }
  }

  async function remove(labelName: string) {
    try {
      await api.labels.remove(pageId, labelName)
      setLabels((prev) => prev.filter((l) => l.name !== labelName))
    } catch {
      /* ignore — the list refreshes on next load */
    }
  }

  return (
    <div className="labels">
      {labels.map((l) => (
        <span key={l.id} className="label-chip">
          <Link to={`/labels/${encodeURIComponent(l.name)}`}>{l.name}</Link>
          <button type="button" onClick={() => remove(l.name)} aria-label={`Remove label ${l.name}`}>
            ×
          </button>
        </span>
      ))}
      {adding ? (
        <form className="label-add" onSubmit={add}>
          <input
            value={name}
            onChange={(e) => setName(e.target.value)}
            placeholder="label-name"
            autoFocus
            onBlur={() => !name && setAdding(false)}
          />
          <button type="submit" className="btn btn--primary btn--sm">Add</button>
        </form>
      ) : (
        <button type="button" className="link-btn" onClick={() => setAdding(true)}>
          + Add label
        </button>
      )}
      {error && <span className="small" style={{ color: 'var(--danger)' }}>{error}</span>}
    </div>
  )
}
