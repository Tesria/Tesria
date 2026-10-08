import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { api, ApiError, UserRole, type AdminPersonAccess } from '../api/client'
import { levelWord, reasonShort, restrictionNote } from './adminAccess'

/**
 * What one person can do in every space, and why (dev-plan 21.5), opened from
 * Admin, Users: Check Access turned the other way. Each level is the real
 * check made as them; the reasons are the ones Check Access gives.
 */
export function PersonAccessPanel({ userId, onClose }: { userId: string; onClose: () => void }) {
  const [access, setAccess] = useState<AdminPersonAccess | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [showNone, setShowNone] = useState(false)

  useEffect(() => {
    let live = true
    api.admin.users.access(userId)
      .then((a) => { if (live) setAccess(a) })
      .catch((err: unknown) => { if (live) setError(err instanceof ApiError ? err.message : 'Could not load what they can see.') })
    return () => { live = false }
  }, [userId])

  const reachable = access?.spaces.filter((s) => s.level != null) ?? []
  const closed = access?.spaces.filter((s) => s.level == null) ?? []
  const name = access?.person.displayName ?? 'this person'

  return (
    <section className="card access-panel" aria-label={`What ${name} can see`}>
      <div className="access-panel__head">
        <h2>What {name} Can See</h2>
        <button type="button" className="btn btn--ghost btn--sm" onClick={onClose}>Close</button>
      </div>
      {error && <p className="alert alert--error">{error}</p>}
      {!access && !error && <p className="muted">Loading…</p>}
      {access && (
        <>
          {!access.person.active && (
            <p className="alert alert--warning">This account is suspended: it cannot sign in, so none of this gives it anything until it is reactivated.</p>
          )}
          <p className="muted small">
            {access.role === UserRole.Member
              ? `${name} is a user.`
              : `${name} is ${access.role === UserRole.Owner ? 'the owner' : 'an administrator'} of Tesria, which lets them run it but opens no space by itself.`}
            {access.globalGroups.length > 0 && ` They are in ${access.globalGroups.join(' and ')}, so they can read every space.`}
          </p>

          <p><strong>{reachable.length}</strong> of {access.spaces.length} space{access.spaces.length === 1 ? '' : 's'}</p>
          {reachable.length > 0 && (
            <table className="admin-table person-access">
              <thead>
                <tr><th>Space</th><th>Can</th><th>Why</th></tr>
              </thead>
              <tbody>
                {reachable.map((s) => {
                  const note = restrictionNote(s)
                  return (
                    <tr key={s.spaceId}>
                      <td>
                        <Link to={`/spaces/${encodeURIComponent(s.key)}`}><strong>{s.name}</strong></Link>{' '}
                        <span className="badge">{s.key}</span>
                        {s.archived && <span className="badge">archived</span>}
                      </td>
                      <td className={s.level === 2 && !s.explicitAdmin ? 'person-access__warn' : undefined}>
                        {levelWord(s.level)}
                      </td>
                      <td>
                        {s.reasons.map((r, i) => <div key={i} className="small">{reasonShort(r)}</div>)}
                        {note && <div className="muted small">{note}</div>}
                      </td>
                    </tr>
                  )
                })}
              </tbody>
            </table>
          )}
          {closed.length > 0 && (
            <p className="small">
              <button type="button" className="link-btn" aria-expanded={showNone} onClick={() => setShowNone(!showNone)}>
                {showNone ? 'Hide' : 'Show'} the {closed.length} space{closed.length === 1 ? '' : 's'} they cannot open
              </button>
              {showNone && <span className="person-access__closed">{closed.map((s) => s.name).join(', ')}</span>}
            </p>
          )}
          <p className="muted small">
            Page restrictions can hide particular pages inside a space they can open. To check one page, use Check
            Access on the Groups tab.
          </p>
        </>
      )}
    </section>
  )
}
