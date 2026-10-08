import { useCallback, useEffect, useRef, useState } from 'react'
import { Link } from 'react-router-dom'
import { api, ApiError, Permission, type AdminSpace } from '../../api/client'
import { useAuth } from '../../auth/AuthContext'
import { accessDetail, accessHeadline } from '../../components/adminAccess'
import { useConfirm } from '../../components/ConfirmDialog'
import { OpenSpaceReview } from '../../components/OpenSpaceReview'
import { SpaceAccessPanel } from '../../components/SpaceAccessPanel'

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
 * shows who created each space, its size and counts, never content. To read a
 * space they hold no grant for, an admin uses recover-access, which is audited.
 *
 * Since 21.5 it also says who can get in: each row's Access in counts, the
 * full answer in a panel (who, at what level, and why), and at the top the
 * review of spaces everyone may administer that nobody chose to leave so.
 */
export function AdminSpacesPage() {
  const [spaces, setSpaces] = useState<AdminSpace[] | null>(null)
  const [allowPublic, setAllowPublic] = useState<boolean | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const [busy, setBusy] = useState<string | null>(null)
  const { ask, dialog } = useConfirm()
  const { can } = useAuth()
  // The panel names people, so it needs See the user list as well.
  const mayDetail = can(Permission.UsersView)
  const [detail, setDetail] = useState<string | null>(null)
  // Bumped on every reload, so an open panel reads the space again.
  const [loads, setLoads] = useState(0)
  const panel = useRef<HTMLDivElement>(null)

  function showDetail(key: string) {
    setDetail(key)
    // The panel sits under the table; bring it into view rather than leave
    // the click looking like it did nothing.
    requestAnimationFrame(() => panel.current?.scrollIntoView({ behavior: 'smooth', block: 'start' }))
  }

  const load = useCallback(() => {
    // Settings are read only for the public-reading switch, and a role that
    // manages spaces may hold no settings right at all: that refusal used to
    // take the whole tab down (found 2026-09-23). Without it, Publish stays
    // offered and the server says if the switch is off.
    Promise.all([api.admin.spaces.list(), api.admin.settings.get().catch(() => null)])
      .then(([rows, settings]) => {
        setSpaces(rows)
        setLoads((n) => n + 1)
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
        confirmLabel: 'Publish the Space',
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
        confirmLabel: 'Withdraw the Space',
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
   * ordinary membership of its Admins group behind (dev-plan 21.1), which the
   * space's administrators can remove.
   */
  async function recoverAccess(key: string, name: string) {
    const ok = await ask({
      title: `Give yourself access to "${name}"?`,
      confirmLabel: 'Give Me Access',
      body: (
        <>
          <p>You will be added to the space as an administrator, so you can read it and manage its permissions.</p>
          <p>This is recorded in the audit log. You join the space's Admins group: remove yourself from it in the space's Permissions tab when you are done.</p>
          <p>A space that is open to everyone and has administrators of its own already lets you in, and is left exactly as it is.</p>
        </>
      ),
    })
    if (!ok) return
    setBusy(key)
    setError(null)
    setNotice(null)
    try {
      const r = await api.admin.spaces.recoverAccess(key)
      setNotice(r.alreadyHadAccess
        ? `You already have access to ${name}; nothing was changed.`
        : `You now administer ${name}, as a member of its Admins group. This is recorded in the audit log.`)
      load()
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
      <OpenSpaceReview spaces={spaces} signedIn={null} onChanged={load} onDetails={(s) => showDetail(s.key)} />
      <table className="admin-table">
        <thead>
          <tr>
            <th>Space</th>
            <th>Access</th>
            <th>Created By</th>
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
                  <strong className="admin-table__clip admin-table__clip--inline" title={s.name.length > 30 ? s.name : undefined}>{s.name}</strong>
                </Link>{' '}
                <span className="badge admin-table__clip admin-table__clip--inline" title={s.key}>{s.key}</span>
                {s.archived && <span className="badge">archived</span>}
                {s.isPublic && <span className="badge badge--public">public</span>}
              </td>
              <td className="admin-spaces__access">
                {s.access && (() => {
                  const head = accessHeadline(s.access)
                  return (
                    <>
                      <span className={`admin-spaces__headline admin-spaces__headline--${head.tone}`}>{head.text}</span>
                      <span className="muted small">{accessDetail(s.access)}</span>
                    </>
                  )
                })()}
                <div className="admin-table__actions">
                  {mayDetail ? (
                    <button type="button" className="link-btn" aria-expanded={detail === s.key} onClick={() => showDetail(s.key)}>
                      Who Has Access
                    </button>
                  ) : (
                    <button type="button" className="link-btn" disabled={busy === s.key} onClick={() => void recoverAccess(s.key, s.name)}>
                      Get Access
                    </button>
                  )}
                </div>
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
                      Comments
                    </label>
                  )}
                </div>
              </td>
              <td className="muted small">{new Date(s.createdAt).toLocaleDateString()}</td>
            </tr>
          ))}
        </tbody>
      </table>

      <div ref={panel}>
        {detail && mayDetail && (
          <SpaceAccessPanel key={detail} spaceKey={detail} refresh={loads} onClose={() => setDetail(null)} onRecover={recoverAccess} />
        )}
      </div>

      {dialog}
    </>
  )
}
