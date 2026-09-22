import { type FormEvent, useState } from 'react'
import { Link, Navigate, useNavigate, useSearchParams } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'
import { PasswordInput } from '../components/PasswordInput'
import { RecoveryCodes } from '../components/RecoveryCodes'

export function RegisterPage() {
  const { user, register } = useAuth()
  const navigate = useNavigate()
  const [params] = useSearchParams()
  // An invite link carries its token in the query string, so the person
  // following it never has to know it exists.
  const inviteToken = params.get('invite') ?? undefined
  const [email, setEmail] = useState('')
  const [displayName, setDisplayName] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [codes, setCodes] = useState<string[] | null>(null)

  // Registration signs the user straight in, so this guard would fire the
  // moment the account exists and redirect past the recovery codes, which are
  // shown exactly once. Hold the redirect until they have been acknowledged.
  if (user && !codes) return <Navigate to="/spaces" replace />

  if (codes) {
    return (
      <div className="center">
        <div className="authcard authcard--wide">
          <h1>Save your recovery codes</h1>
          <RecoveryCodes
            codes={codes}
            onDone={() => navigate('/spaces')}
            doneLabel="Continue to Tesria"
          />
        </div>
      </div>
    )
  }

  async function onSubmit(e: FormEvent) {
    e.preventDefault()
    setBusy(true)
    setError(null)
    try {
      setCodes(await register(email, displayName, password, inviteToken))
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Registration failed.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="center">
      <form className="authcard" onSubmit={onSubmit}>
        <h1>Create account</h1>
        {inviteToken && (
          <p className="muted small">You were invited to this instance.</p>
        )}
        {error && <p className="alert alert--error">{error}</p>}
        <label>
          Display name
          <input value={displayName} onChange={(e) => setDisplayName(e.target.value)} required autoFocus />
        </label>
        <label>
          Email
          <input type="email" value={email} onChange={(e) => setEmail(e.target.value)} required />
        </label>
        <label>
          Password
          <PasswordInput value={password} onChange={(e) => setPassword(e.target.value)} autoComplete="new-password" required minLength={8} />
        </label>
        <button type="submit" className="btn btn--primary" disabled={busy}>
          {busy ? 'Creating…' : 'Create account'}
        </button>
        <p className="muted">
          Already have an account? <Link to="/login">Sign in</Link>
        </p>
      </form>
    </div>
  )
}
