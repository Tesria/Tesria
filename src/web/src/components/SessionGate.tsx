import { Outlet } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'

/**
 * Waits for the initial session check, then renders — signed in or not.
 * Public read mode (dev-plan 5.3): the shell and the space/page routes work
 * for anonymous readers; routes that need an account nest a ProtectedRoute
 * underneath this one.
 */
export function SessionGate() {
  const { user } = useAuth()
  if (user === undefined) return <div className="center muted">Loading…</div>
  return <Outlet />
}
