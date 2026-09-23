import { Navigate } from 'react-router-dom'
import { Permission } from '../api/client'
import { useAuth } from '../auth/AuthContext'
import { AdminInvitesPage } from './admin/AdminInvitesPage'

/**
 * Invite people, for anyone holding "Create invite links" (dev-plan 10.5
 * step 1). The right can be given to people who are not administrators, and
 * the admin area turns those away, so without this page the right did
 * nothing for them. The form is the Invites tab's own.
 */
export function InvitePage() {
  const { user, can } = useAuth()
  if (user === undefined) return <p className="muted page-wrap">Loading…</p>
  if (!can(Permission.InvitesCreate)) return <Navigate to="/spaces" replace />
  return (
    <div className="page-wrap">
      <h1>Invite people</h1>
      <AdminInvitesPage />
    </div>
  )
}
