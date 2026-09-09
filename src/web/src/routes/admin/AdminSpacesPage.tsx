import { useCallback, useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { api, ApiError, type AdminSpace } from '../../api/client'

/** Formats bytes for humans; storage figures are the point of this page. */
function bytes(value: number): string {
  if (value === 0) return '—'
  const units = ['B', 'KB', 'MB', 'GB', 'TB']
  const i = Math.min(Math.floor(Math.log(value) / Math.log(1024)), units.length - 1)
  return `${(value / 1024 ** i).toFixed(i === 0 ? 0 : 1)} ${units[i]}`
}

/**
 * Admin → Spaces (dev-plan 2.4).
 *
 * Metadata only, deliberately. Admins do not bypass space permissions, so this
 * shows ownership, size and counts — never content. To read a space they hold
 * no grant for, an admin uses recover-access, which is audited.
 */
export function AdminSpacesPage() {
  const [spaces, setSpaces] = useState<AdminSpace[] | null>(null)
  const [allowPublic, setAllowPublic] = useState<boolean | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState<string | null>(null)

  const load = useCallback(() => {
    Promise.all([api.admin.spaces.list(), api.admin.settings.get()])
      .then(([rows, settings]) => {
        setSpaces(rows)
        setAllowPublic(settings.allowPublicSpaces)
      })
      .catch((err: unknown) =>
        setError(err instanceof ApiError ? err.message : 'Could not load spaces.'))
  }, [])
  useEffect(load, [load])

  /** Publishing names exactly what becomes visible before asking (dev-plan 5.4). */
  async function setPublic(s: AdminSpace, isPublic: boolean) {
    const what = isPublic
      ? `Publish "${s.name}" (${s.key}) to the internet?\n\n${s.pageCount} page${s.pageCount === 1 ? '' : 's'} and ${s.attachmentCount} attachment${s.attachmentCount === 1 ? '' : 's'} become readable by anyone, no account needed. Restricted pages stay hidden. Comments stay private unless you allow them.`
      : `Withdraw "${s.name}" (${s.key}) from public reading? Anonymous readers lose access within a minute.`
    if (!window.confirm(what)) return
    setBusy(s.id)
    setError(null)
    try {
      await api.admin.spaces.setPublic(s.key, { isPublic })
      load()
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not change the setting.')
    } finally {
      setBusy(null)
    }
  }

  async function setComments(s: AdminSpace, publicComments: boolean) {
    setBusy(s.id)
    try {
      await api.admin.spaces.setPublic(s.key, { isPublic: s.isPublic, publicComments })
      load()
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not change the setting.')
    } finally {
      setBusy(null)
    }
  }

  if (error && !spaces) return <p className="alert alert--error">{error}</p>
  if (!spaces) return <p className="muted">Loading…</p>

  return (
    <>
      {error && <p className="alert alert--error">{error}</p>}
      {allowPublic === false && (
        <p className="muted small">
          Public reading is switched off for the whole instance (Settings → Allow public spaces).
          Spaces marked public below stay private until it is on.
        </p>
      )}
      <table className="admin-table">
        <thead>
          <tr>
            <th>Space</th>
            <th>Owner</th>
            <th>Pages</th>
            <th>Storage</th>
            <th>Public</th>
            <th>Created</th>
          </tr>
        </thead>
        <tbody>
          {spaces.map((s) => (
            <tr key={s.id}>
              <td>
                <Link to={`/spaces/${s.key}`}>
                  <strong>{s.name}</strong>
                </Link>{' '}
                <span className="badge">{s.key}</span>
                {s.archived && <span className="badge">archived</span>}
                {s.isPublic && <span className="badge badge--public">public</span>}
              </td>
              <td className="muted small">{s.createdByName}</td>
              <td>{s.pageCount}</td>
              <td>{bytes(s.storageBytes)}</td>
              <td className="admin-table__actions">
                <button type="button" className="link-btn" disabled={busy === s.id || (!s.isPublic && allowPublic === false)}
                  onClick={() => setPublic(s, !s.isPublic)}>
                  {s.isPublic ? 'Withdraw' : 'Publish'}
                </button>
                {s.isPublic && (
                  <label className="small nowrap">
                    <input type="checkbox" checked={s.publicComments} disabled={busy === s.id}
                      onChange={(e) => setComments(s, e.target.checked)} />{' '}
                    comments
                  </label>
                )}
              </td>
              <td className="muted small">{new Date(s.createdAt).toLocaleDateString()}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </>
  )
}
