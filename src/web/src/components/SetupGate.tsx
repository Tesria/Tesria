import { Navigate, Outlet, useLocation } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'
import { useInstance } from '../InstanceContext'

/** Once per tab: the tour should not reappear on every navigation. */
const OFFERED = 'tesria-tour-offered'
const tourOfferedThisSession = () => {
  try { return sessionStorage.getItem(OFFERED) === '1' } catch { return false }
}
const markTourOffered = () => {
  try { sessionStorage.setItem(OFFERED, '1') } catch { /* nothing to do */ }
}

/** Paths the gate never redirects away from. */
// /register because a new account is signed in before its recovery codes are
// shown, and redirecting it to the tour then skipped them for good.
const ALLOWED = ['/setup', '/welcome', '/export', '/logout', '/register']

/**
 * Sends first-run traffic to the wizard (dev-plan 10.2).
 *
 * Two cases. An instance with no accounts at all sends every visitor to
 * `/setup`, because there is nothing else to do there yet. An instance whose
 * owner has not finished setup sends *that owner* there, and nobody else: an
 * administrator or a member arriving meanwhile carries on as normal.
 *
 * This is convenience, not enforcement. The API is not blocked while setup is
 * unfinished, and the docs say so: the wizard is a friendlier door to rooms
 * that are already open, so a redirect is as far as it should go.
 *
 * It wraps the outlet rather than sitting beside it. As a sibling its
 * <Navigate> raced SessionGate's, which renders deeper and won, and a fresh
 * instance landed on /login instead of the wizard.
 */
export function SetupGate() {
  const { user } = useAuth()
  const instance = useInstance()
  const location = useLocation()

  if (instance === undefined || user === undefined) return null
  if (ALLOWED.some((p) => location.pathname.startsWith(p))) return <Outlet />

  // A signed-in account answers for itself. `needsOwner` is read once at
  // load and never refetched, so after the owner is created it still says
  // true; trusting it here left the new owner bounced back to /setup
  // forever, including from the wizard's own last step.
  if (user) {
    if (user.setupRequired) return <Navigate to="/setup" replace />
    // The welcome tour (dev-plan 10.3), once per session: a person who
    // leaves it is not asked again until they ask for it, and one who
    // navigates away mid-tour has skipped it as far as the server knows.
    if (user.onboarding?.tourDue && !tourOfferedThisSession()) {
      markTourOffered()
      return <Navigate to="/welcome" replace />
    }
    return <Outlet />
  }

  // Nobody signed in. An instance with no accounts has nothing else to show,
  // except /register, which still works and still makes the first account the
  // owner: the wizard is the friendlier door to the same room.
  if (instance.needsOwner && location.pathname !== '/register') {
    return <Navigate to="/setup" replace />
  }
  return <Outlet />
}
