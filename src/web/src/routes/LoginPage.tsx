import { type FormEvent, useEffect, useState } from 'react'
import { Link, Navigate, useNavigate, useSearchParams } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'
import { api, ApiError } from '../api/client'

export function LoginPage() {
  const { user, login } = useAuth()
  const navigate = useNavigate()
  const [searchParams] = useSearchParams()
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState<string | null>(searchParams.get('ssoError'))
  const [busy, setBusy] = useState(false)
  const [oidc, setOidc] = useState<{ enabled: boolean; displayName: string } | null>(null)

  // Hooks must run unconditionally, so this is fetched before the `user`
  // early-return below rather than after it.
  useEffect(() => {
    api.auth.oidcStatus()
      .then(setOidc)
      .catch(() => setOidc({ enabled: false, displayName: '' }))
  }, [])

  if (user) return <Navigate to="/spaces" replace />

  async function onSubmit(e: FormEvent) {
    e.preventDefault()
    setBusy(true)
    setError(null)
    try {
      await login(email, password)
      navigate('/spaces')
    } catch (err) {
      setError(err instanceof ApiError && err.status === 401 ? 'Incorrect email or password.'
        : err instanceof Error ? err.message : 'Sign in failed.')
    } finally {
      setBusy(false)
    }
  }

  function ssoLogin() {
    // A full-page navigation, not a fetch — the identity provider needs to
    // take over the browser's own address bar for its login page.
    window.location.href = `/api/auth/oidc/login?returnUrl=${encodeURIComponent('/spaces')}`
  }

  return (
    <div className="center">
      <form className="authcard" onSubmit={onSubmit}>
        <h1>Sign in</h1>
        {error && <p className="alert alert--error">{error}</p>}
        <label>
          Email
          <input type="email" value={email} onChange={(e) => setEmail(e.target.value)} required autoFocus />
        </label>
        <label>
          Password
          <input type="password" value={password} onChange={(e) => setPassword(e.target.value)} required />
        </label>
        <button type="submit" className="btn btn--primary" disabled={busy}>
          {busy ? 'Signing in…' : 'Sign in'}
        </button>
        {oidc?.enabled && (
          <>
            <p className="muted small" style={{ textAlign: 'center', margin: '0.75rem 0 0' }}>or</p>
            <button type="button" className="btn btn--ghost" onClick={ssoLogin}>
              Sign in with {oidc.displayName}
            </button>
          </>
        )}
        <p className="muted">
          No account? <Link to="/register">Create one</Link>
        </p>
      </form>
    </div>
  )
}
