import { type FormEvent, useEffect, useState } from 'react'
import { Link, useLocation } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'
import { SpaceIcon } from '../components/SpaceIcon'
import { api, ApiError, type Space, Permission } from '../api/client'

export function SpacesPage() {
  const { user, can } = useAuth()
  const [spaces, setSpaces] = useState<Space[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [creating, setCreating] = useState(false)
  // Left behind by a deletion (dev-plan 11.3): the space it happened on is
  // gone, so the confirmation has to land somewhere else.
  const notice = (useLocation().state as { notice?: string } | null)?.notice ?? null

  useEffect(() => {
    api.spaces
      .list()
      .then(setSpaces)
      .catch((err: unknown) => setError(err instanceof Error ? err.message : 'Failed to load spaces.'))
  }, [])

  return (
    <div className="page-wrap">
      <div className="row-between">
        <h1>Spaces</h1>
        {user && can(Permission.SpacesCreate) && (
          <button type="button" className="btn btn--primary" onClick={() => setCreating((v) => !v)}>
            {creating ? 'Cancel' : 'New space'}
          </button>
        )}
      </div>

      {creating && (
        <CreateSpaceForm
          onCreated={(s) => {
            setSpaces((prev) => [...(prev ?? []), s].sort((a, b) => a.name.localeCompare(b.name)))
            setCreating(false)
          }}
        />
      )}

      {notice && <p className="profile__ok">{notice}</p>}
      {error && <p className="alert alert--error">{error}</p>}
      {!spaces && !error && <p className="muted">Loading…</p>}
      {spaces && spaces.length === 0 && (
        <p className="muted">
          {user ? 'No spaces yet. Create your first one.' : 'Nothing is published for public reading. Sign in to see more.'}
        </p>
      )}

      <ul className="space-grid">
        {spaces?.map((s) => (
          <li key={s.id} className="space-card">
            <Link to={`/spaces/${s.key}`}>
              <span className="space-card__head">
                <SpaceIcon space={s} size={32} />
                <span className="space-card__key">
                  {s.key}{s.isPublic && <span className="badge badge--public">public</span>}
                </span>
              </span>
              <span className="space-card__name">{s.name}</span>
              {s.description && <span className="space-card__desc">{s.description}</span>}
            </Link>
          </li>
        ))}
      </ul>
    </div>
  )
}

function CreateSpaceForm({ onCreated }: { onCreated: (space: Space) => void }) {
  const [key, setKey] = useState('')
  const [name, setName] = useState('')
  const [description, setDescription] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  async function onSubmit(e: FormEvent) {
    e.preventDefault()
    setBusy(true)
    setError(null)
    try {
      const space = await api.spaces.create({ key, name, description: description || null })
      onCreated(space)
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not create the space.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <form className="card form-inline" onSubmit={onSubmit}>
      {error && <p className="alert alert--error">{error}</p>}
      <label>
        Key
        <input
          value={key}
          onChange={(e) => setKey(e.target.value.toUpperCase())}
          placeholder="ENG"
          required
        />
      </label>
      <label>
        Name
        <input value={name} onChange={(e) => setName(e.target.value)} placeholder="Engineering" required />
      </label>
      <label>
        Description
        <input value={description} onChange={(e) => setDescription(e.target.value)} placeholder="Optional" />
      </label>
      <button type="submit" className="btn btn--primary" disabled={busy}>
        {busy ? 'Creating…' : 'Create'}
      </button>
    </form>
  )
}
