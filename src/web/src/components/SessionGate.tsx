import { Navigate, Outlet, useLocation } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'
import { useInstance } from '../InstanceContext'

/**
 * Waits for the initial session check, then decides who may see the shell.
 *
 * Public read mode (dev-plan 5.3) let the shell and the space/page routes
 * render for anonymous readers. 5.5 narrows that to instances which actually
 * publish something: anonymous reading exists only when the instance-wide
 * switch is on *and* some space is public. Otherwise a visitor with no
 * session is sent to sign in, as they were before Phase 5, because an
 * instance that publishes nothing should look like one rather than offering
 * an empty public shell.
 *
 * A deep link to a page therefore lands on the sign-in page, carrying `from`
 * so the reader arrives where they meant to once they are in.
 */
export function SessionGate() {
  const { user } = useAuth()
  const instance = useInstance()
  const location = useLocation()

  if (user === undefined || instance === undefined) return <div className="center muted">Loading…</div>
  if (user === null && !instance.publicReading) {
    return <Navigate to="/login" replace state={{ from: location.pathname + location.search }} />
  }
  return <Outlet />
}
