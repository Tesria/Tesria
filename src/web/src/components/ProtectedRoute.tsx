import { Navigate, Outlet, useLocation, useOutletContext } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'

/**
 * Gates child routes behind authentication; redirects to /login otherwise.
 *
 * The outlet context is forwarded deliberately. Since dev-plan 5.3 this sits
 * *inside* a space's route (`/spaces/:key/*`), where `SpacePage` supplies the
 * space and its page tree through `<Outlet context={…}>`. A bare `<Outlet />`
 * here starts a fresh context of `null`, so every gated child — the editor,
 * trash, permissions, webhooks, settings — would find no space and throw on
 * `useSpaceContext()`. Passing it through makes this component transparent,
 * which is what a gate should be.
 */
export function ProtectedRoute() {
  const { user } = useAuth()
  const location = useLocation()
  const context = useOutletContext<unknown>()

  if (user === undefined) return <div className="center muted">Loading…</div>
  if (user === null) return <Navigate to="/login" replace state={{ from: location.pathname }} />
  return <Outlet context={context} />
}
