import { useEffect, useState } from 'react'
import { Link, NavLink, Navigate, Outlet } from 'react-router-dom'
import { useAuth } from '../../auth/AuthContext'
import { Permission, UserRole } from '../../api/client'

/**
 * The admin section's shell (dev-plan 2.1).
 *
 * The role check here is convenience, not security: every `/api/admin/*`
 * route enforces it server-side. This exists so a member who guesses the URL
 * sees a redirect rather than a page of failed requests.
 */
export function AdminLayout() {
  const { user, can } = useAuth()

  if (user === undefined) return <p className="muted page-wrap">Loading…</p>
  if (!user) return <Navigate to="/login" replace />

  // The server refuses every /api/admin route to members; this is the human
  // version of that refusal, in place of a silent bounce to the spaces list.
  if (user.role < UserRole.Admin) {
    return (
      <div className="page-wrap">
        <h1>Administration</h1>
        <p className="alert alert--error">
          This area (users, groups, the audit log, security and instance settings)
          is for administrators. If you need to manage groups or see the audit log,
          ask an administrator of this instance to make the change or to grant you
          the administrator role.
        </p>
        <p><Link to="/spaces">Back to spaces</Link></p>
      </div>
    )
  }

  // The server refuses every admin route until enrollment (dev-plan 3.5);
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

  // Only the tabs this person can actually open (dev-plan 11.1). Every one is
  // refused server-side too; this keeps the shell honest about what is there.
  const tabs: { to: string; label: string; end?: boolean; permission: string; or?: string }[] = [
    { to: '/admin', label: 'Dashboard', end: true, permission: Permission.DashboardView },
    { to: '/admin/users', label: 'Users', permission: Permission.UsersView },
    { to: '/admin/spaces', label: 'Spaces', permission: Permission.SpacesManage },
    { to: '/admin/invites', label: 'Invites', permission: Permission.InvitesManage, or: Permission.InvitesCreate },
    { to: '/admin/api-tokens', label: 'API tokens', permission: Permission.UsersView },
    { to: '/admin/security', label: 'Security', permission: Permission.SecurityView },
    { to: '/admin/backups', label: 'Backups', permission: Permission.BackupsView },
    { to: '/admin/roles', label: 'Roles', permission: Permission.PermissionsView },
    { to: '/admin/groups', label: 'Groups', permission: Permission.GroupsManage },
    { to: '/admin/audit', label: 'Audit', permission: Permission.AuditView },
    { to: '/admin/branding', label: 'Branding', permission: Permission.SettingsBranding },
  ]
  const visible = tabs.filter((t) => can(t.permission) || (t.or !== undefined && can(t.or)))
  // The owner always reaches the matrix, even having taken permissions.view
  // from their own role: it is how they would undo that.
  if (!visible.some((t) => t.to === '/admin/roles') && can(Permission.PermissionsEditAdminTier)) {
    // Where it would have been: before Groups, or last.
    const at = visible.findIndex((t) => t.to === '/admin/groups')
    visible.splice(at < 0 ? visible.length : at, 0, { to: '/admin/roles', label: 'Roles', permission: Permission.PermissionsEditAdminTier })
  }
  const settingsVisible = can(Permission.SettingsInstance) || can(Permission.SettingsRegistration)
    || can(Permission.SettingsEmail) || can(Permission.SettingsPublicSpaces) || can(Permission.SecuritySettings)

  return (
    <div className="page-wrap page-wrap--admin">
      <h1>Administration</h1>
      <nav className="tabs">
        {visible.map((t) => (
          <NavLink key={t.to} to={t.to} end={t.end} className={tab}>{t.label}</NavLink>
        ))}
        {settingsVisible && <NavLink to="/admin/settings" className={tab}>Settings</NavLink>}
      </nav>
      <div className="tab-panel">
        <Outlet />
      </div>
      <VersionLine />
    </div>
  )
}

/**
 * "Tesria" and the version, at the foot of every Administration tab
 * (dev-plan 13.1, decision F). The one place the product names itself on a
 * branded instance besides the sign-in page, and only administrators see it.
 * Worth having anyway: it is the first thing to quote when asking for help.
 */
function VersionLine() {
  const [version, setVersion] = useState<string | null>(null)
  useEffect(() => {
    let canceled = false
    fetch('/api/health', { credentials: 'include' })
      .then((r) => r.json() as Promise<{ version?: string }>)
      .then((h) => { if (!canceled && h.version) setVersion(h.version) })
      .catch(() => { /* the line simply shows no number */ })
    return () => { canceled = true }
  }, [])
  return (
    <p className="admin-version">
      <a href="https://brianintheloop.com/tesria" target="_blank" rel="noopener noreferrer">Tesria</a>
      {version && <> {version.split('+')[0]}</>}
    </p>
  )
}
