import { type FormEvent, useState } from 'react'
import { api, ApiError, type Space } from '../api/client'
import { SpaceIconPicker } from '../components/SpaceIconPicker'
import { useSpaceContext } from './SpacePage'

/**
 * A space's own settings: what it is called, and how it looks (dev-plan 6).
 *
 * The name and description endpoint existed since Phase 2 with nothing in the
 * UI reaching it; the icon needed somewhere to live, so this page finally
 * gives both a home. It is the Details tab of `SpaceSettingsLayout`, which
 * owns the heading and the tab row — permissions, webhooks and trash are
 * the other three tabs.
 */
export function SpaceSettingsPage() {
  const { space, onSpaceChanged } = useSpaceContext()
  const [name, setName] = useState(space.name)
  const [description, setDescription] = useState(space.description ?? '')
  const [busy, setBusy] = useState(false)
  const [status, setStatus] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  function applied(updated: Space, message: string) {
    onSpaceChanged(updated)
    setName(updated.name)
    setDescription(updated.description ?? '')
    setStatus(message)
    setError(null)
  }

  async function saveDetails(e: FormEvent) {
    e.preventDefault()
    setBusy(true)
    setStatus(null)
    setError(null)
    try {
      applied(await api.spaces.update(space.key, { name, description: description || null }), 'Saved.')
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not save.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <>
      {error && <p className="alert alert--error">{error}</p>}
      {status && <p className="profile__ok">{status}</p>}

      <section className="profile__section profile__section--wide">
        <h2>Icon</h2>
        <p className="muted small">
          Shown wherever this space appears — the spaces list, the sidebar and the
          breadcrumb.
        </p>
        <SpaceIconPicker space={space} onChanged={(updated) => applied(updated, 'Icon updated.')} />
      </section>

      <section className="profile__section profile__section--wide">
        <h2>Details</h2>
        <form onSubmit={saveDetails}>
          <label>
            Name
            <input value={name} onChange={(e) => setName(e.target.value)} required />
          </label>
          <label>
            Description
            <input
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              placeholder="What this space is for"
            />
          </label>
          <p className="muted small">
            The key <code>{space.key}</code> is part of every page&rsquo;s address and cannot change.
          </p>
          <button type="submit" className="btn btn--primary" disabled={busy}>
            {busy ? 'Saving…' : 'Save'}
          </button>
        </form>
      </section>
    </>
  )
}
