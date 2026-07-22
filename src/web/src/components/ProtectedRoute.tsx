import { Navigate, Outlet, useLocation } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'

/** Gates child routes behind authentication; redirects to /login otherwise. */
export function ProtectedRoute() {
  const { user } = useAuth()
  const location = useLocation()

  if (user === undefined) return <div className="center muted">Loading…</div>
  if (user === null) return <Navigate to="/login" replace state={{ from: location.pathname }} />
  return <Outlet />
}
