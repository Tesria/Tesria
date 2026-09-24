import { useCallback, useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { api, ApiError, type AdminSpace } from '../../api/client'
import { useConfirm } from '../../components/ConfirmDialog'

/** Formats bytes for humans; storage figures are the point of this page. */
function bytes(value: number): string {
  if (value === 0) return '–'
  const units = ['B', 'KB', 'MB', 'GB', 'TB']
  const i = Math.min(Math.floor(Math.log(value) / Math.log(1024)), units.length - 1)
  return `${(value / 1024 ** i).toFixed(i === 0 ? 0 : 1)} ${units[i]}`
}

/**
 * Admin → Spaces (dev-plan 2.4).
 *
 * Metadata only, deliberately. Admins do not bypass space permissions, so this
 * shows ownership, size and counts, never content. To read a space they hold
 * no grant for, an admin uses recover-access, which is audited.
 */
export function AdminSpacesPage() {
  const [spaces, setSpaces] = useState<AdminSpace[] | null>(null)
  const [allowPublic, setAllowPublic] = useState<boolean | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const [busy, setBusy] = useState<string | null>(null)
  const { ask, dialog } = useConfirm()

  const load = useCallback(() => {
    // Settings are read only for the public-reading switch, and a role that
    // manages spaces may hold no settings right at all: that refusal used to
    // take the whole tab down (found 2026-09-23). Without it, Publish stays
    // offered and the server says if the switch is off.
    Promise.all([api.admin.spaces.list(), api.admin.settings.get().catch(() => null)])
      .then(([rows, settings]) => {
        setSpaces(rows)
        setAllowPublic(settings?.allowPublicSpaces ?? true)
      })
      .catch((err: unknown) =>
        setError(err instanceof ApiError ? err.message : 'Could not load spaces.'))
  }, [])
  useEffect(load, [load])

  /** Publishing names exactly what becomes visible before asking (dev-plan 5.4). */
  async function setPublic(s: AdminSpace, isPublic: boolean) {
    const ok = await ask(isPublic
      ? {
        title: `Publish "${s.name}" to the internet?`,
        danger: true,
        confirmLabel: 'Publish the space',
        body: (
          <>
            <p>
              <strong>{s.pageCount} page{s.pageCount === 1 ? '' : 's'}</strong> and{' '}
              <strong>{s.attachmentCount} attachment{s.attachmentCount === 1 ? '' : 's'}</strong> in{' '}
              <strong>{s.key}</strong> become readable by anyone, with no account.
            </p>
            <p>Restricted pages stay hidden. Comments stay private unless you allow them.</p>
          </>
        ),
      }
      : {
        title: `Withdraw "${s.name}" from public reading?`,
        confirmLabel: 'Withdraw the space',
        body: <p>Anonymous readers lose access to <strong>{s.key}</strong> within a minute.</p>,
      })
    if (!ok) return
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

  /**
   * Administrators do not bypass space permissions, so reaching a private
   * space they hold no grant for is a deliberate, audited act that leaves an
   * ordinary grant behind, which anyone with the space's admin can revoke.
   */
  async function recoverAccess(s: AdminSpace) {
    const ok = await ask({
      title: `Give yourself access to "${s.name}"?`,
      confirmLabel: 'Give me access',
      body: (
        <>
          <p>You will be added to the space as an administrator, so you can read it and manage its permissions.</p>
          <p>This is recorded in the audit log. The grant is an ordinary one: remove it from the space's Permissions tab when you are done.</p>
          <p>A space that is open to everyone already lets you in, and is left exactly as it is.</p>
        </>
      ),
    })
    if (!ok) return
    setBusy(s.id)
    setError(null)
    setNotice(null)
    try {
      const r = await api.admin.spaces.recoverAccess(s.key)
      setNotice(r.alreadyHadAccess
        ? `You already have access to ${s.name}; nothing was changed.`
        : `You now administer ${s.name}. The grant is recorded in the audit log.`)
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not give you access.')
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
      {notice && <p className="profile__ok" role="status">{notice}</p>}
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
            <th>Access</th>
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
              <td>
                <div className="admin-table__actions">
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
                </div>
              </td>
              <td className="muted small">{new Date(s.createdAt).toLocaleDateString()}</td>
              <td>
                <div className="admin-table__actions">
                  <button type="button" className="link-btn" disabled={busy === s.id} onClick={() => recoverAccess(s)}>
                    Get access
                  </button>
                </div>
              </td>
            </tr>
          ))}
        </tbody>
      </table>

      {dialog}
    </>
  )
}
