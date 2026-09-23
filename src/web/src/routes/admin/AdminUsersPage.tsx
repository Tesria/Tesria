import { useCallback, useEffect, useState } from 'react'
import { api, ApiError, Permission, UserRole, UserStatus, type AdminUser, type InstanceRole } from '../../api/client'
import { useAuth } from '../../auth/AuthContext'
import { Avatar } from '../../components/Avatar'
import { useConfirm } from '../../components/ConfirmDialog'

/** Admin → Users (dev-plan 2.2). */
export function AdminUsersPage() {
  const { user: me, can } = useAuth()
  const iAmOwner = me?.role === UserRole.Owner
  // An administrator may be allowed to promote (dev-plan 11.1); demoting an
  // administrator stays with the owner, so two of them cannot unmake each
  // other. Both are refused server-side as well.
  const mayPromote = iAmOwner || can(Permission.UsersPromoteAdmins)
  const [users, setUsers] = useState<AdminUser[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [busyId, setBusyId] = useState<string | null>(null)
  const { ask, dialog } = useConfirm()
  const [resetLink, setResetLink] = useState<{ name: string; url: string } | null>(null)
  // The roles a person could be moved to, when the viewer may see them.
  const [roles, setRoles] = useState<InstanceRole[]>([])

  const load = useCallback(async () => {
    try {
      setUsers(await api.admin.users.list())
      if (can(Permission.PermissionsView)) {
        try {
          setRoles((await api.admin.roles.matrix()).roles)
        } catch {
          // Seeing roles is its own right; the page works without it.
        }
      }
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not load users.')
    }
  }, [can])

  useEffect(() => {
    void load()
  }, [load])

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
            Give it to them directly: it is shown once.
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
            // Signing the owner out, revoking their tokens or resetting their
            // password are all ways for an administrator to take the instance
            // or keep its owner out of it; the server refuses them too.
            const othersOwnerRow = u.role === UserRole.Owner && !isSelf
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
                  {u.role === UserRole.Owner
                    ? <span className="badge badge--owner">owner</span>
                    : u.role === UserRole.Admin ? <span className="badge">admin</span> : 'Member'}
                  {u.isSso && <span className="badge">sso</span>}
                  {/* The role within the tier (dev-plan 11.2). A picker only
                      where there is a choice to make and the right to make it. */}
                  {(() => {
                    const inTier = roles.filter((r) => r.tier === u.role)
                    const mayAssign = u.role === UserRole.Owner ? false
                      : u.role === UserRole.Admin ? iAmOwner : can(Permission.UsersAssignRoles)
                    if (inTier.length < 2) {
                      return u.roleName && !inTier.some((r) => r.builtIn && r.name === u.roleName)
                        ? <div className="muted small">{u.roleName}</div>
                        : null
                    }
                    return mayAssign ? (
                      <select
                        className="users__role"
                        aria-label={`Role for ${u.displayName}`}
                        value={u.roleId ?? ''}
                        disabled={busy}
                        onChange={(e) => act(u.id, () => api.admin.users.assignRole(u.id, e.target.value),
                          'Could not change the role.')}
                      >
                        {inTier.map((r) => <option key={r.id} value={r.id}>{r.name}</option>)}
                      </select>
                    ) : (
                      <div className="muted small">{u.roleName}</div>
                    )
                  })()}
                </td>
                <td>
                  {u.status === UserStatus.Suspended
                    ? <span className="badge badge--danger">suspended</span>
                    : 'Active'}
                  {u.lockedUntil && new Date(u.lockedUntil) > new Date() && (
                    <span className="badge badge--danger" title={`${u.failedLoginCount} failed sign-ins`}>
                      locked
                    </span>
                  )}
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
                <td>
                  <div className="admin-table__actions">
                    {/* The owner's own row carries nothing that could unseat or
                        lock out the instance's last way back in. */}
                    {u.role !== UserRole.Owner && (
                      <button
                        type="button"
                        className="link-btn"
                        disabled={busy || (u.role === UserRole.Admin ? !iAmOwner : !mayPromote)}
                        title={
                          u.role === UserRole.Admin
                            ? (iAmOwner ? undefined : 'Only the owner demotes an administrator.')
                            : (mayPromote ? undefined : 'Your role does not allow promoting people to administrator.')
                        }
                        onClick={() => act(u.id, () => api.admin.users.setRole(
                          u.id, u.role === UserRole.Admin ? UserRole.Member : UserRole.Admin,
                        ), 'Could not change the role.')}
                      >
                        {u.role === UserRole.Admin ? 'Demote' : 'Make admin'}
                      </button>
                    )}
                    {iAmOwner && !isSelf && u.status === UserStatus.Active && (
                      <button
                        type="button"
                        className="link-btn link-btn--danger"
                        disabled={busy}
                        onClick={() => {
                          void (async () => {
                            const ok = await ask({
                              title: `Make ${u.displayName} the owner of this instance?`,
                              danger: true,
                              confirmLabel: 'Transfer ownership',
                              body: (
                                <>
                                  <p>You become an administrator.</p>
                                  <p>
                                    Only <strong>{u.displayName}</strong> will be able to change roles,
                                    or hand ownership back to you.
                                  </p>
                                </>
                              ),
                            })
                            if (!ok) return
                            await act(u.id, () => api.admin.users.transferOwnership(u.id),
                              'Could not transfer ownership.')
                          })()
                        }}
                      >
                        Transfer ownership
                      </button>
                    )}
                    {!isSelf && u.role !== UserRole.Owner && (
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
                    {!othersOwnerRow && (
                      <button
                        type="button"
                        className="link-btn"
                        disabled={busy}
                        onClick={() => act(u.id, () => api.admin.users.revokeSessions(u.id),
                          'Could not revoke sessions.')}
                      >
                        Sign out
                      </button>
                    )}
                    {!othersOwnerRow && (
                      <button
                        type="button"
                        className="link-btn"
                        disabled={busy}
                        onClick={() => act(u.id, () => api.admin.users.revokeTokens(u.id),
                          'Could not revoke tokens.')}
                      >
                        Revoke tokens
                      </button>
                    )}
                    {u.hasPassword && !othersOwnerRow && (
                      <button type="button" className="link-btn" disabled={busy} onClick={() => issueReset(u)}>
                        Reset password
                      </button>
                    )}
                    {u.lockedUntil && new Date(u.lockedUntil) > new Date() && (
                      <button
                        type="button"
                        className="link-btn"
                        disabled={busy}
                        onClick={() => act(u.id, () => api.admin.users.unlock(u.id), 'Could not unlock.')}
                      >
                        Unlock
                      </button>
                    )}
                  </div>
                </td>
              </tr>
            )
          })}
        </tbody>
      </table>

      {dialog}
    </>
  )
}
