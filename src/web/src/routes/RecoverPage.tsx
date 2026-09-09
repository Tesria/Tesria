import { type FormEvent, useState } from 'react'
import { Link, useNavigate, useSearchParams } from 'react-router-dom'
import { api, ApiError } from '../api/client'
import { PasswordInput } from '../components/PasswordInput'

/**
 * Regaining access without email — the only recovery this instance has until
 * dev-plan 4.2 adds an emailed link.
 *
 * Two routes into the same page. `/recover` is the recovery-code form someone
 * reaches from the sign-in page. `/reset?token=…` is the link an administrator
 * hands over out of band; arriving with a token skips straight to choosing a
 * new password, because the token already is the proof of identity.
 */
export function RecoverPage() {
  const [params] = useSearchParams()
  const navigate = useNavigate()
  const token = params.get('token')

  const [email, setEmail] = useState('')
  const [code, setCode] = useState('')
  const [newPassword, setNewPassword] = useState('')
  const [confirm, setConfirm] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [done, setDone] = useState(false)

  async function submit(e: FormEvent) {
    e.preventDefault()
    if (newPassword !== confirm) {
      setError('The new passwords do not match.')
      return
    }
    setBusy(true)
    setError(null)
    try {
      if (token) await api.auth.resetWithToken({ token, newPassword })
      else await api.auth.recoverWithCode({ email, code, newPassword })
      setDone(true)
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not reset your password.')
    } finally {
      setBusy(false)
    }
  }

  if (done) {
    return (
      <div className="center">
        <div className="authcard">
          <h1>Password reset</h1>
          <p className="muted small">
            Your password has been changed, and every other device signed in to
            this account has been signed out.
          </p>
          <button type="button" className="btn btn--primary" onClick={() => navigate('/login')}>
            Sign in
          </button>
        </div>
      </div>
    )
  }

  return (
    <div className="center">
      <form className="authcard" onSubmit={submit}>
        <h1>{token ? 'Choose a new password' : 'Reset your password'}</h1>

        {!token && (
          <>
            <p className="muted small">
              Enter one of the recovery codes you saved when you created your
              account. Each code works once.
            </p>
            <label>
              Email
              <input
                type="email"
                value={email}
                onChange={(e) => setEmail(e.target.value)}
                autoComplete="username"
                required
                autoFocus
              />
            </label>
            <label>
              Recovery code
              <input
                value={code}
                onChange={(e) => setCode(e.target.value)}
                placeholder="XXXX-XXXX-XXXX"
                // Dashes and case are normalised server-side, so no need to
                // fight the user about how they type it.
                autoComplete="one-time-code"
                required
              />
            </label>
          </>
        )}

        <label>
          New password
          <PasswordInput
            value={newPassword}
            onChange={(e) => setNewPassword(e.target.value)}
            autoComplete="new-password"
            minLength={8}
            required
            autoFocus={Boolean(token)}
          />
        </label>
        <label>
          Confirm new password
          <PasswordInput
            value={confirm}
            onChange={(e) => setConfirm(e.target.value)}
            autoComplete="new-password"
            minLength={8}
            required
          />
        </label>

        {error && <p className="alert alert--error">{error}</p>}

        <button type="submit" className="btn btn--primary" disabled={busy}>
          {busy ? 'Resetting…' : 'Reset password'}
        </button>

        <p className="muted small">
          <Link to="/login">Back to sign in</Link>
          {!token && ' · Lost your codes? Ask an administrator to issue a reset link.'}
        </p>
      </form>
    </div>
  )
}
