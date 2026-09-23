import { type FormEvent, useCallback, useEffect, useState } from 'react'
import { api, ApiError, type PageTemplate } from '../api/client'
import { useConfirm } from '../components/ConfirmDialog'
import { useSpaceContext } from './SpacePage'

/**
 * Space settings → Templates (dev-plan 10.5 step 1).
 *
 * Templates could be made ("Save as template" on a page) and chosen (when
 * creating one), but never seen in one place, renamed or removed: the
 * delete endpoint existed with no screen. This lists what someone creating a
 * page in this space is offered: the space's own templates and the
 * instance-wide ones. Rename and Delete appear only where the server says
 * the viewer may use them.
 */
export function SpaceTemplatesPage() {
  const { space } = useSpaceContext()
  const { ask, dialog } = useConfirm()
  const [templates, setTemplates] = useState<PageTemplate[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [editing, setEditing] = useState<{ id: string; name: string; description: string } | null>(null)

  const load = useCallback(() => {
    api.templates.list(space.id)
      .then(setTemplates)
      .catch((err: unknown) => setError(err instanceof ApiError ? err.message : 'Could not load templates.'))
  }, [space.id])
  useEffect(load, [load])

  async function save(e: FormEvent) {
    e.preventDefault()
    if (!editing || !editing.name.trim()) return
    setError(null)
    try {
      await api.templates.update(editing.id, { name: editing.name.trim(), description: editing.description.trim() || null })
      setEditing(null)
      load()
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not rename the template.')
    }
  }

  async function remove(t: PageTemplate) {
    const ok = await ask({
      title: `Delete the template ${t.name}?`,
      danger: true,
      confirmLabel: 'Delete the template',
      body: t.spaceId
        ? <p>It stops being offered when a page is created in this space. Pages already made from it are not changed.</p>
        : <p>It is instance-wide, so it stops being offered in every space. Pages already made from it are not changed.</p>,
    })
    if (!ok) return
    setError(null)
    try {
      await api.templates.remove(t.id)
      load()
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not delete the template.')
    }
  }

  const own = templates?.filter((t) => t.spaceId === space.id) ?? []
  const shared = templates?.filter((t) => t.spaceId === null) ?? []

  const list = (items: PageTemplate[], empty: string) => items.length === 0 ? (
    <p className="muted small">{empty}</p>
  ) : (
    <ul className="version-list">
      {items.map((t) => editing?.id === t.id ? (
        <li key={t.id} className="version">
          <form className="form-inline group-edit" onSubmit={save}>
            <label>
              Name
              <input value={editing.name} onChange={(e) => setEditing({ ...editing, name: e.target.value })} required autoFocus />
            </label>
            <label>
              Description
              <input value={editing.description} onChange={(e) => setEditing({ ...editing, description: e.target.value })} placeholder="Optional" />
            </label>
            <button type="submit" className="btn btn--primary btn--sm">Save</button>
            <button type="button" className="btn btn--ghost btn--sm" onClick={() => setEditing(null)}>Cancel</button>
          </form>
        </li>
      ) : (
        <li key={t.id} className="version">
          <span className="version__num">{t.name}</span>
          <span className="muted small">
            {t.createdByName ? `by ${t.createdByName}, ` : ''}{new Date(t.createdAt).toLocaleDateString()}
          </span>
          {t.description && <span className="version__comment">{t.description}</span>}
          {t.canManage && (
            <span className="version__actions">
              <button type="button" className="link-btn"
                onClick={() => setEditing({ id: t.id, name: t.name, description: t.description ?? '' })}>
                Rename
              </button>
              <button type="button" className="link-btn link-btn--danger" onClick={() => remove(t)}>Delete</button>
            </span>
          )}
        </li>
      ))}
    </ul>
  )

  return (
    <>
      <p className="muted small">
        What is offered when someone creates a page here. To make one, open a page and choose
        Save as template from its menu.
      </p>
      {error && <p className="alert alert--error">{error}</p>}
      {templates === null ? <p className="muted">Loading…</p> : (
        <>
          <h2 className="settings-subhead">This space</h2>
          {list(own, 'No templates of this space\'s own yet.')}
          <h2 className="settings-subhead">Instance-wide</h2>
          {list(shared, 'No instance-wide templates.')}
        </>
      )}
      {dialog}
    </>
  )
}
