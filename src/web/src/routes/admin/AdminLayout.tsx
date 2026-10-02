import { useEffect, useState } from 'react'
import { Link, Navigate, Outlet, useLocation } from 'react-router-dom'
import { useAuth } from '../../auth/AuthContext'
import { Permission, UserRole } from '../../api/client'
import { TESRIA_SITE, TESRIA_SOURCE } from '../../links'
import { OverflowTabs } from '../../components/OverflowTabs'

/**
 * The admin section's shell (dev-plan 2.1).
 *
 * The role check here is convenience, not security: every `/api/admin/*`
 * route enforces it server-side. This exists so a member who guesses the URL
 * sees a redirect rather than a page of failed requests.
 */
export function AdminLayout() {
  const { user, can } = useAuth()
  const { pathname } = useLocation()

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
        <p><Link to="/spaces">Back to Spaces</Link></p>
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

  // Only the tabs this person can actually open (dev-plan 11.1). Every one is
  // refused server-side too; this keeps the shell honest about what is there.
  // `right`: the right missing, in words, for someone who opens the tab's
  // address without it (T7-008).
  const tabs: { to: string; label: string; end?: boolean; permission: string; or?: string; right: string }[] = [
    { to: '/admin', label: 'Dashboard', end: true, permission: Permission.DashboardView, right: 'see the dashboard' },
    { to: '/admin/users', label: 'Users', permission: Permission.UsersView, right: 'see the user list' },
    { to: '/admin/spaces', label: 'Spaces', permission: Permission.SpacesManage, right: 'manage every space' },
    { to: '/admin/invites', label: 'Invites', permission: Permission.InvitesManage, or: Permission.InvitesCreate, right: 'invite people' },
    { to: '/admin/api-tokens', label: 'API Tokens', permission: Permission.UsersView, right: 'see the user list' },
    { to: '/admin/security', label: 'Security', permission: Permission.SecurityView, right: 'see security alerts' },
    { to: '/admin/backups', label: 'Backups', permission: Permission.BackupsView, right: 'see the backups' },
    { to: '/admin/roles', label: 'Roles', permission: Permission.PermissionsView, right: 'see the roles' },
    { to: '/admin/groups', label: 'Groups', permission: Permission.GroupsManage, right: 'manage groups' },
    { to: '/admin/audit', label: 'Audit', permission: Permission.AuditView, right: 'read the audit log' },
    { to: '/admin/branding', label: 'Branding', permission: Permission.SettingsBranding, right: 'change the branding' },
  ]
  const visible = tabs.filter((t) => can(t.permission) || (t.or !== undefined && can(t.or)))
  // The owner always reaches the matrix, even having taken permissions.view
  // from their own role: it is how they would undo that.
  if (!visible.some((t) => t.to === '/admin/roles') && can(Permission.PermissionsEditAdminTier)) {
    // Where it would have been: before Groups, or last.
    const at = visible.findIndex((t) => t.to === '/admin/groups')
    visible.splice(at < 0 ? visible.length : at, 0, { to: '/admin/roles', label: 'Roles', permission: Permission.PermissionsEditAdminTier, right: 'see the roles' })
  }
  // A tab opened by its address (a bookmark, a link in the docs) that this
  // role cannot open. Its page used to load and fail: "Could not load the
  // branding." for an administrator, whose role lacks Change the branding
  // by default (T7-008). Said plainly instead, with who can change it.
  const refused = tabs.find((t) => (t.end ? pathname.replace(/\/$/, '') === t.to : pathname.startsWith(t.to))
    && !visible.some((v) => v.to === t.to))
  const settingsVisible = can(Permission.SettingsInstance) || can(Permission.SettingsRegistration)
    || can(Permission.SettingsEmail) || can(Permission.SettingsPublicSpaces) || can(Permission.SecuritySettings)

  return (
    <div className="page-wrap page-wrap--admin">
      <h1>Administration</h1>
      <OverflowTabs items={[
        ...visible.map((t) => ({ key: t.to, label: t.label, to: t.to, end: t.end })),
        ...(settingsVisible ? [{ key: '/admin/settings', label: 'Settings', to: '/admin/settings' }] : []),
        // About is always last (the owner, 2026-09-28).
        ...(can(Permission.DashboardView) ? [{ key: '/admin/about', label: 'About', to: '/admin/about' }] : []),
      ]} />
      <div className="tab-panel">
        {refused ? (
          <div className="admin-refused">
            <h2>{refused.label}</h2>
            <p>
              Your role does not have the right to {refused.right}, so this tab is not open to you.
              {refused.to === '/admin/branding' && ' The branding is the owner\u2019s unless the owner gives that right to an administrator role.'}
            </p>
            <p className="muted">
              The owner decides what each role may do, in Administration, Roles. Ask them if you need it.
            </p>
          </div>
        ) : <Outlet />}
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
      <a href={TESRIA_SITE} target="_blank" rel="noopener noreferrer">Tesria</a>
      {version && <> {version.split('+')[0]}</>}
      {' · '}
      <a href={TESRIA_SOURCE} target="_blank" rel="noopener noreferrer">Source Code</a>
    </p>
  )
}
