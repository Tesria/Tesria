import { useEffect, useState } from 'react'
import { api, ApiError, UserRole, UserStatus, type AdminUser } from '../../api/client'
import { useAuth } from '../../auth/AuthContext'
import { Avatar } from '../../components/Avatar'

/** Admin → Users (dev-plan 2.2). */
export function AdminUsersPage() {
  const { user: me } = useAuth()
  const [users, setUsers] = useState<AdminUser[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [busyId, setBusyId] = useState<string | null>(null)
  const [resetLink, setResetLink] = useState<{ name: string; url: string } | null>(null)

  async function load() {
    try {
      setUsers(await api.admin.users.list())
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not load users.')
    }
  }

  useEffect(() => {
    void load()
  }, [])

  async function act(id: string, work: () => Promise<unknown>, fallback: string) {
    setBusyId(id)
    setError(null)
    try {
      await work()
      await load()
    } catch (err) {
      setError(err instanceof ApiError ? err.message : fallback)
    } finally {
      setBusyId(null)
    }
  }

  async function issueReset(u: AdminUser) {
    await act(u.id, async () => {
      const issued = await api.admin.users.issueReset(u.id)
      // Built from the address the admin is already on: the server sits behind
      // a proxy and does not reliably know its own public origin.
      setResetLink({ name: u.displayName, url: `${window.location.origin}${issued.path}` })
    }, 'Could not issue a reset link.')
  }

  if (!users) return <p className="muted">Loading…</p>

  return (
    <>
      {error && <p className="alert alert--error">{error}</p>}

      {resetLink && (
        <div className="admin__notice">
          <p>
            One-time reset link for <strong>{resetLink.name}</strong>, valid for one hour.
            Give it to them directly — it is shown once.
          </p>
          <code className="admin__link">{resetLink.url}</code>
          <div className="row-gap">
            <button
              type="button"
              className="btn btn--ghost btn--sm"
              onClick={() => navigator.clipboard.writeText(resetLink.url).catch(() => {})}
            >
              Copy
            </button>
            <button type="button" className="btn btn--ghost btn--sm" onClick={() => setResetLink(null)}>
              Dismiss
            </button>
          </div>
        </div>
      )}

      <table className="admin-table">
        <thead>
          <tr>
            <th>User</th>
            <th>Role</th>
            <th>Status</th>
            <th>Codes</th>
            <th>Last seen</th>
            <th>Actions</th>
          </tr>
        </thead>
        <tbody>
          {users.map((u) => {
            const isSelf = u.id === me?.id
            const busy = busyId === u.id
            return (
              <tr key={u.id}>
                <td>
                  <span className="admin-table__user">
                    <Avatar subject={u} size={28} />
                    <span>
                      <strong>{u.displayName}</strong>
                      <br />
                      <span className="muted small">{u.email}</span>
                    </span>
                  </span>
                </td>
                <td>
                  {u.role === UserRole.Admin ? <span className="badge">admin</span> : 'Member'}
                  {u.isSso && <span className="badge">sso</span>}
                </td>
                <td>
                  {u.status === UserStatus.Suspended
                    ? <span className="badge badge--danger">suspended</span>
                    : 'Active'}
                </td>
                <td>
                  {/* Accounts predating recovery codes have none, and would
                      otherwise have no recovery path and no warning. */}
                  {u.recoveryCodesRemaining === 0 && u.hasPassword
                    ? <span className="badge badge--danger">none</span>
                    : u.recoveryCodesRemaining}
                </td>
                <td className="muted small">
                  {u.lastSeenAt ? new Date(u.lastSeenAt).toLocaleDateString() : 'Never'}
                </td>
                <td className="admin-table__actions">
                  <button
                    type="button"
                    className="link-btn"
                    disabled={busy}
                    onClick={() => act(u.id, () => api.admin.users.setRole(
                      u.id, u.role === UserRole.Admin ? UserRole.Member : UserRole.Admin,
                    ), 'Could not change the role.')}
                  >
                    {u.role === UserRole.Admin ? 'Demote' : 'Make admin'}
                  </button>
                  {!isSelf && (
                    <button
                      type="button"
                      className="link-btn"
                      disabled={busy}
                      onClick={() => act(u.id, () => api.admin.users.setStatus(
                        u.id, u.status === UserStatus.Suspended ? UserStatus.Active : UserStatus.Suspended,
                      ), 'Could not change the status.')}
                    >
                      {u.status === UserStatus.Suspended ? 'Reactivate' : 'Suspend'}
                    </button>
                  )}
                  <button
                    type="button"
                    className="link-btn"
                    disabled={busy}
                    onClick={() => act(u.id, () => api.admin.users.revokeSessions(u.id),
                      'Could not revoke sessions.')}
                  >
                    Sign out
                  </button>
                  <button
                    type="button"
                    className="link-btn"
                    disabled={busy}
                    onClick={() => act(u.id, () => api.admin.users.revokeTokens(u.id),
                      'Could not revoke tokens.')}
                  >
                    Revoke tokens
                  </button>
                  {u.hasPassword && (
                    <button type="button" className="link-btn" disabled={busy} onClick={() => issueReset(u)}>
                      Reset password
                    </button>
                  )}
                </td>
              </tr>
            )
          })}
        </tbody>
      </table>
    </>
  )
}
