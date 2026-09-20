import { Link, NavLink, Navigate, Outlet } from 'react-router-dom'
import { useAuth } from '../../auth/AuthContext'
import { UserRole } from '../../api/client'

/**
 * The admin section's shell (dev-plan 2.1).
 *
 * The role check here is convenience, not security — every `/api/admin/*`
 * route enforces it server-side. This exists so a member who guesses the URL
 * sees a redirect rather than a page of failed requests.
 */
export function AdminLayout() {
  const { user } = useAuth()

  if (user === undefined) return <p className="muted page-wrap">Loading…</p>
  if (!user) return <Navigate to="/login" replace />

  // The server refuses every /api/admin route to members; this is the human
  // version of that refusal, in place of a silent bounce to the spaces list.
  if (user.role !== UserRole.Admin) {
    return (
      <div className="page-wrap">
        <h1>Administration</h1>
        <p className="alert alert--error">
          This area — users, groups, the audit log, security and instance settings —
          is for administrators. If you need to manage groups or see the audit log,
          ask an administrator of this instance to make the change or to grant you
          the administrator role.
        </p>
        <p><Link to="/spaces">Back to spaces</Link></p>
      </div>
    )
  }

  // The server refuses every admin route until enrolment (dev-plan 3.5);
  // say so instead of rendering a page of failed requests.
  if (user.totpRequired) {
    return (
      <div className="page-wrap">
        <h1>Administration</h1>
        <p className="alert alert--error">
          This instance requires two-factor sign-in for administrators.{' '}
          <Link to="/profile#two-factor">Set it up on your profile</Link> to continue.
        </p>
      </div>
    )
  }

  const tab = ({ isActive }: { isActive: boolean }) => (isActive ? 'tab is-active' : 'tab')

  return (
    <div className="page-wrap">
      <h1>Administration</h1>
      <nav className="tabs">
        <NavLink to="/admin" end className={tab}>Dashboard</NavLink>
        <NavLink to="/admin/users" className={tab}>Users</NavLink>
        <NavLink to="/admin/spaces" className={tab}>Spaces</NavLink>
        <NavLink to="/admin/invites" className={tab}>Invites</NavLink>
        <NavLink to="/admin/security" className={tab}>Security</NavLink>
        <NavLink to="/admin/backups" className={tab}>Backups</NavLink>
        <NavLink to="/admin/groups" className={tab}>Groups</NavLink>
        <NavLink to="/admin/audit" className={tab}>Audit</NavLink>
        <NavLink to="/admin/settings" className={tab}>Settings</NavLink>
      </nav>
      <div className="tab-panel">
        <Outlet />
      </div>
    </div>
  )
}
