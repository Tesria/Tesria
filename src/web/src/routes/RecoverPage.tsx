import { type FormEvent, useEffect, useState } from 'react'
import { Link, useNavigate, useSearchParams } from 'react-router-dom'
import { api, ApiError } from '../api/client'
import { PasswordInput } from '../components/PasswordInput'

/**
 * Regaining access.
 *
 * Three ways in, one page. `/recover` offers a recovery code, and, when the
 * instance sends email (dev-plan 4.2), an emailed link instead. `/reset?token=…`
 * is that link, or the one an administrator hands over out of band; arriving
 * with a token skips straight to choosing a new password, because the token
 * already is the proof of identity.
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
  const [emailOffered, setEmailOffered] = useState(false)
  const [method, setMethod] = useState<'code' | 'email'>('code')
  const [emailSent, setEmailSent] = useState<string | null>(null)

  useEffect(() => {
    if (token) return
    api.auth.recoveryOptions()
      .then((o) => {
        setEmailOffered(o.emailEnabled)
        if (o.emailEnabled) setMethod('email')
      })
      .catch(() => setEmailOffered(false))
  }, [token])

  async function requestLink(e: FormEvent) {
    e.preventDefault()
    setBusy(true)
    setError(null)
    try {
      setEmailSent((await api.auth.recoverByEmail(email)).message)
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not request a reset link.')
    } finally {
      setBusy(false)
    }
  }

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

  if (!token && method === 'email') {
    return (
      <div className="center">
        <form className="authcard" onSubmit={requestLink}>
          <h1>Reset your password</h1>
          {emailSent ? (
            <p className="profile__ok">{emailSent}</p>
          ) : (
            <>
              <p className="muted small">We will email you a link that works once and expires in an hour.</p>
              <label>
                Email
                <input type="email" value={email} onChange={(e) => setEmail(e.target.value)} autoComplete="username" required autoFocus />
              </label>
              {error && <p className="alert alert--error">{error}</p>}
              <button type="submit" className="btn btn--primary" disabled={busy}>
                {busy ? 'Sending…' : 'Email me a reset link'}
              </button>
            </>
          )}
          <p className="muted small">
            <button type="button" className="link-btn" onClick={() => setMethod('code')}>Use a recovery code instead</button>
            {' · '}<Link to="/login">Back to sign in</Link>
          </p>
        </form>
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
          {!token && emailOffered && (
            <><button type="button" className="link-btn" onClick={() => setMethod('email')}>Email me a link instead</button>{' · '}</>
          )}
          <Link to="/login">Back to sign in</Link>
          {!token && !emailOffered && ' · Lost your codes? Ask an administrator to issue a reset link.'}
        </p>
      </form>
    </div>
  )
}
