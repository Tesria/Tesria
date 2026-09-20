import { type FormEvent, useEffect, useState } from 'react'
import { Link, Navigate, useLocation, useNavigate, useSearchParams } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'
import { api, ApiError } from '../api/client'
import { PasswordInput } from '../components/PasswordInput'
import { useInstance } from '../InstanceContext'

export function LoginPage() {
  const { user, login, completeTotp } = useAuth()
  const navigate = useNavigate()
  const location = useLocation()
  // Back to the public page the reader was on, if that is where they came from.
  const destination = (location.state as { from?: string } | null)?.from ?? '/spaces'
  const [searchParams] = useSearchParams()
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [challenge, setChallenge] = useState<string | null>(null)
  const [code, setCode] = useState('')
  const [error, setError] = useState<string | null>(searchParams.get('ssoError'))
  const [busy, setBusy] = useState(false)
  const [oidc, setOidc] = useState<{ enabled: boolean; displayName: string } | null>(null)
  const instance = useInstance()
  // An invite link is its own authorisation, so it shows the sign-up route
  // even on an instance that has closed public registration.
  const invited = searchParams.get('invite') !== null
  const mayRegister = invited || (instance?.allowPublicRegistration ?? false)

  // Hooks must run unconditionally, so this is fetched before the `user`
  // early-return below rather than after it.
  useEffect(() => {
    api.auth.oidcStatus()
      .then(setOidc)
      .catch(() => setOidc({ enabled: false, displayName: '' }))
  }, [])

  if (user) return <Navigate to={destination} replace />

  async function onSubmit(e: FormEvent) {
    e.preventDefault()
    setBusy(true)
    setError(null)
    try {
      const pending = await login(email, password)
      if (pending) {
        setChallenge(pending.challenge)
        return
      }
      navigate(destination)
    } catch (err) {
      setError(err instanceof ApiError && err.status === 401 ? 'Incorrect email or password.'
        : err instanceof Error ? err.message : 'Sign in failed.')
    } finally {
      setBusy(false)
    }
  }

  async function onSubmitCode(e: FormEvent) {
    e.preventDefault()
    setBusy(true)
    setError(null)
    try {
      await completeTotp(challenge!, code)
      navigate(destination)
    } catch (err) {
      setError(err instanceof ApiError && err.status === 401
        ? 'That code is not right. Codes change every 30 seconds; a recovery code also works.'
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

  if (challenge) {
    return (
      <div className="center">
        <form className="authcard" onSubmit={onSubmitCode}>
          <h1>One more step</h1>
          <p className="muted small">Enter the six-digit code from your authenticator app, or one of your recovery codes.</p>
          {error && <p className="alert alert--error">{error}</p>}
          <label>
            Code
            <input value={code} onChange={(e) => setCode(e.target.value)} inputMode="numeric" autoComplete="one-time-code" required autoFocus />
          </label>
          <button type="submit" className="btn btn--primary" disabled={busy}>
            {busy ? 'Checking…' : 'Sign in'}
          </button>
          <p className="muted small">
            <button type="button" className="link-btn" onClick={() => { setChallenge(null); setCode(''); setError(null) }}>Start over</button>
          </p>
        </form>
      </div>
    )
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
          <PasswordInput value={password} onChange={(e) => setPassword(e.target.value)} autoComplete="current-password" required />
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
          <Link to="/recover">Forgot your password?</Link>
        </p>
        {/* Somebody who reached sign-in on an instance nobody has claimed
            yet (dev-plan 10.2). */}
        {instance?.needsOwner && (
          <p className="alert alert--error">
            This instance has no owner yet. <Link to="/setup">Set it up.</Link>
          </p>
        )}
        {mayRegister && (
          <p className="muted small">
            No account? <Link to="/register">Create one</Link>
          </p>
        )}
        {/* Somebody who reached sign-in out of habit on an instance that does
            publish something should not be stranded here (dev-plan 5.5). */}
        {instance?.publicReading && (
          <p className="muted small">
            <Link to="/spaces">Browse what is public</Link>
          </p>
        )}
      </form>
    </div>
  )
}
