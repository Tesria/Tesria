import { useCallback, useEffect, useState } from 'react'
import { Link, useLocation } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'
import { SpaceIcon } from '../components/SpaceIcon'
import { api, type Space, Permission } from '../api/client'
import { ImportPackForm } from '../components/ImportPackForm'
import { NewSpaceWizard } from '../components/NewSpaceWizard'

export function SpacesPage() {
  const { user, can } = useAuth()
  const [spaces, setSpaces] = useState<Space[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [creating, setCreating] = useState(false)
  const [importing, setImporting] = useState(false)
  // Left behind by a deletion (dev-plan 11.3): the space it happened on is
  // gone, so the confirmation has to land somewhere else.
  const notice = (useLocation().state as { notice?: string } | null)?.notice ?? null

  const reload = useCallback(
    () =>
      api.spaces
        .list()
        .then(setSpaces)
        .catch((err: unknown) => setError(err instanceof Error ? err.message : 'Failed to load spaces.')),
    [],
  )

  useEffect(() => {
    void reload()
  }, [reload])

  return (
    <div className="page-wrap">
      <div className="row-between">
        <h1>Spaces</h1>
        {user && can(Permission.SpacesCreate) && (
          <div className="row-gap">
            <button type="button" className="btn" onClick={() => { setImporting((v) => !v); setCreating(false) }}>
              {importing ? 'Cancel' : 'Import a Pack'}
            </button>
            <button type="button" className="btn btn--primary" onClick={() => { setCreating((v) => !v); setImporting(false) }}>
              {creating ? 'Cancel' : 'New Space'}
            </button>
          </div>
        )}
      </div>

      {importing && <ImportPackForm onImported={() => { void reload() }} />}

      {/* The wizard (dev-plan 21.2) lands on the new space when it is done. */}
      {creating && <NewSpaceWizard onCancel={() => setCreating(false)} />}

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
                  {/* A 50-letter key ends in "…" (QA cal-005); the whole of it in the tooltip. */}
                  <span className="space-card__key-text" title={s.key}>{s.key}</span>
                  {s.isPublic && <span className="badge badge--public">public</span>}
                </span>
              </span>
              <span className="space-card__name" title={s.name.length > 60 ? s.name : undefined}>{s.name}</span>
              {s.description && <span className="space-card__desc">{s.description}</span>}
            </Link>
          </li>
        ))}
      </ul>
    </div>
  )
}
