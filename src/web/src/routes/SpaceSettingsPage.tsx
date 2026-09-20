import { type FormEvent, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { api, ApiError, Permission, type Space } from '../api/client'
import { useAuth } from '../auth/AuthContext'
import { SpaceIconPicker } from '../components/SpaceIconPicker'
import { DeleteSpaceDialog } from './DeleteSpaceDialog'
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
  const { can } = useAuth()
  const navigate = useNavigate()
  const [deleting, setDeleting] = useState(false)
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

  /** The reversible alternative to deleting, and the only one a space's own
   *  administrator has. The endpoint has existed since Phase 2 with nothing
   *  in the UI reaching it; 11.3's delete dialog offers it, so it needs to be
   *  somewhere real to offer. */
  async function setArchived(archived: boolean) {
    setBusy(true)
    setStatus(null)
    setError(null)
    try {
      const updated = archived
        ? await api.spaces.archive(space.key)
        : await api.spaces.unarchive(space.key)
      applied(updated, archived ? 'Space archived.' : 'Space unarchived.')
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not change the setting.')
    } finally {
      setBusy(false)
    }
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

      <section className="profile__section profile__section--wide" id="archive">
        <h2>Archive</h2>
        <p className="muted small">
          {space.archived
            ? 'This space is archived. It stays out of the spaces list until you bring it back, and nothing in it has been touched.'
            : 'Keeps the space and everything in it, out of the way: archived spaces are hidden from the spaces list unless you ask for them. Reversible at any time.'}
        </p>
        <button type="button" className="btn" disabled={busy} onClick={() => setArchived(!space.archived)}>
          {space.archived ? 'Unarchive this space' : 'Archive this space'}
        </button>
      </section>

      {/* Last on the page and visually apart, because nothing else here is
          irreversible (dev-plan 11.3). The right is an instance one, so a
          space's own administrator sees only archiving. */}
      {can(Permission.SpacesDelete) && (
        <section className="profile__section profile__section--wide danger-zone">
          <h2>Danger zone</h2>
          <p className="muted small">
            Deleting <code>{space.key}</code> destroys every page in it, with all
            versions, comments and attachments. It cannot be undone from inside
            Tesria: only a backup taken beforehand would still hold the content.
          </p>
          <button type="button" className="btn btn--danger" onClick={() => setDeleting(true)}>
            Delete this space
          </button>
        </section>
      )}

      {deleting && (
        <DeleteSpaceDialog
          space={space}
          onCancel={() => setDeleting(false)}
          onDeleted={() => navigate('/spaces', {
            replace: true,
            state: { notice: `The space ${space.key} was deleted.` },
          })}
        />
      )}
    </>
  )
}
