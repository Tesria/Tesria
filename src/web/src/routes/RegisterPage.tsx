import { type FormEvent, useEffect, useId, useState } from 'react'
import { Link, Navigate, useNavigate, useSearchParams } from 'react-router-dom'
import { api, DISPLAY_NAME_MAX } from '../api/client'
import { useAuth } from '../auth/AuthContext'
import { PasswordInput } from '../components/PasswordInput'
import { PASSWORD_HINT, PASSWORD_MAX, PASSWORD_MIN, passwordProblem } from '../auth/passwordRule'
import { RecoveryCodes } from '../components/RecoveryCodes'
import { AuthPage } from '../components/Brand'
import { useInstance } from '../InstanceContext'

export function RegisterPage() {
  const { user, register, refresh } = useAuth()
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
  const hintId = useId()
  const instance = useInstance()
  // Why the link cannot be used, asked as it opens (t2-005): a used,
  // expired or revoked invite greeted people with "You were invited" and
  // said otherwise only after the whole form was filled in. With open
  // registration it does not matter, since anyone may register anyway.
  const [inviteProblem, setInviteProblem] = useState<string | null>(null)
  useEffect(() => {
    if (!inviteToken) return
    let active = true
    api.auth.inviteStatus(inviteToken)
      .then((s) => { if (active) setInviteProblem(s.message) })
      .catch(() => { /* the form still says why when it is sent */ })
    return () => { active = false }
  }, [inviteToken])
  const invitationOnly = instance ? !instance.allowPublicRegistration : false

  // Registration signs the user straight in, so this guard would fire the
  // moment the account exists and redirect past the recovery codes, which are
  // shown exactly once. Hold the redirect until they have been acknowledged.
  if (user && !codes) return <Navigate to="/spaces" replace />

  if (codes) {
    return (
      <AuthPage>
        <div className="authcard authcard--wide">
          <h1>Save Your Recovery Codes</h1>
          <RecoveryCodes
            codes={codes}
            onDone={() => {
              // Recorded, so the account is not asked again whether it has
              // codes. Best effort: the codes are saved either way.
              void api.auth.acknowledgeRecoveryCodes().catch(() => {}).then(() => refresh()).catch(() => {})
              navigate('/spaces')
            }}
            doneLabel="Continue to Tesria"
          />
        </div>
      </AuthPage>
    )
  }

  async function onSubmit(e: FormEvent) {
    e.preventDefault()
    const weak = passwordProblem(password)
    if (weak) {
      setError(weak)
      return
    }
    setBusy(true)
    setError(null)
    try {
      await register(email, displayName, password, inviteToken, setCodes)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Registration failed.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <AuthPage>
      <form className="authcard" onSubmit={onSubmit}>
        <h1>Create Account</h1>
        {inviteToken && !inviteProblem && (
          <p className="muted small">You were invited to this instance.</p>
        )}
        {inviteProblem && invitationOnly && !error && <p className="alert alert--error">{inviteProblem}</p>}
        {error && <p className="alert alert--error">{error}</p>}
        <label>
          Display Name
          <input value={displayName} onChange={(e) => setDisplayName(e.target.value)} required autoFocus
            maxLength={DISPLAY_NAME_MAX} />
        </label>
        <label>
          Email
          <input type="email" value={email} onChange={(e) => setEmail(e.target.value)} required />
        </label>
        <label>
          Password
          <PasswordInput value={password} onChange={(e) => setPassword(e.target.value)} autoComplete="new-password" required
            minLength={PASSWORD_MIN} maxLength={PASSWORD_MAX} describedBy={hintId} />
          <span className="muted small" id={hintId}>{PASSWORD_HINT}</span>
        </label>
        <button type="submit" className="btn btn--primary" disabled={busy}>
          {busy ? 'Creating…' : 'Create Account'}
        </button>
        <p className="muted">
          Already have an account? <Link to="/login">Sign In</Link>
        </p>
      </form>
    </AuthPage>
  )
}
