import { useEffect, useRef } from 'react'
import { useLocation, useNavigate } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'

/** Pages someone signed out is already meant to be on. */
const SIGNED_OUT_PAGES = new Set(['/login', '/register', '/recover', '/reset'])

/**
 * When the auth provider finds this tab's session ended elsewhere (t2-024),
 * goes to the sign-in page once, carrying the page the person was on so
 * signing in returns them to it. Once only: on an instance with public
 * reading, someone who then chooses to read signed out may.
 */
export function SessionEndedRedirect() {
  const { sessionEndedAt } = useAuth()
  const location = useLocation()
  const navigate = useNavigate()
  const handled = useRef<number | null>(null)

  useEffect(() => {
    if (sessionEndedAt === null || handled.current === sessionEndedAt) return
    handled.current = sessionEndedAt
    if (SIGNED_OUT_PAGES.has(location.pathname)) return
    navigate('/login', { replace: true, state: { from: location.pathname + location.search } })
  }, [sessionEndedAt, location, navigate])

  return null
}
