import { NavLink, Navigate, Outlet } from 'react-router-dom'
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
  if (!user || user.role !== UserRole.Admin) return <Navigate to="/spaces" replace />

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
        <NavLink to="/admin/settings" className={tab}>Settings</NavLink>
      </nav>
      <div className="tab-panel">
        <Outlet />
      </div>
    </div>
  )
}
