import { useState } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { api, ApiError } from '../api/client'
import { useAuth } from '../auth/AuthContext'
import { AuthPage } from '../components/Brand'

/**
 * The link in "Confirm your new email address" (t2-009): `/confirm-email?token=…`.
 * Works signed in or not, as a reset link does, since it may be opened on
 * another device. It asks for a click rather than confirming on load, so a
 * mail scanner that follows links does not make the change for anyone.
 */
export function ConfirmEmailPage() {
  const [params] = useSearchParams()
  const token = params.get('token') ?? ''
  const { user, refresh } = useAuth()
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [confirmed, setConfirmed] = useState<string | null>(null)

  async function confirm() {
    setBusy(true)
    setError(null)
    try {
      const { email } = await api.auth.confirmEmail(token)
      setConfirmed(email)
      if (user) await refresh()
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not confirm the new address.')
    } finally {
      setBusy(false)
    }
  }

  const onward = user
    ? <Link className="btn btn--primary" to="/profile">Go to Your Profile</Link>
    : <Link className="btn btn--primary" to="/login">Sign In</Link>

  if (confirmed) {
    return (
      <AuthPage>
        <div className="authcard">
          <h1>Email Address Confirmed</h1>
          <p className="muted small" style={{ overflowWrap: 'anywhere' }}>
            From now on you sign in with <strong>{confirmed}</strong>, and email from this site goes there.
          </p>
          {onward}
        </div>
      </AuthPage>
    )
  }

  return (
    <AuthPage>
      <div className="authcard">
        <h1>Confirm Your New Email Address</h1>
        {token ? (
          <>
            <p className="muted small">
              Someone, probably you, asked to sign in with this address from now on. Choose Confirm to make the change.
            </p>
            {error && <p className="alert alert--error">{error}</p>}
            <button type="button" className="btn btn--primary" onClick={confirm} disabled={busy}>
              {busy ? 'Confirming…' : 'Confirm'}
            </button>
          </>
        ) : (
          <p className="alert alert--error">This link is incomplete. Open it again from the email, or ask for a new one from your profile.</p>
        )}
        <p className="muted small">
          {user ? <Link to="/profile">Back to Your Profile</Link> : <Link to="/login">Back to Sign In</Link>}
        </p>
      </div>
    </AuthPage>
  )
}
