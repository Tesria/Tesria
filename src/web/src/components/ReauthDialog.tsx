import { type FormEvent, useEffect, useState } from 'react'
import { api, ApiError } from '../api/client'
import { useAuth } from '../auth/AuthContext'
import { cancelReauth, completeReauth, subscribeReauth } from '../auth/reauth'
import { PasswordInput } from './PasswordInput'

/**
 * Asks for the password (or a one-time code) when the server says a
 * destructive action needs a fresh confirmation (dev-plan 3.5). Mounted once
 * in the layout; the API client opens it through `auth/reauth.ts`.
 */
export function ReauthDialog() {
  const { user } = useAuth()
  const [open, setOpen] = useState(false)
  const [password, setPassword] = useState('')
  const [code, setCode] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => subscribeReauth(setOpen), [])

  if (!open) return null

  async function confirm(e: FormEvent) {
    e.preventDefault()
    setBusy(true)
    setError(null)
    try {
      await api.auth.reauth({ password: password || undefined, code: code || undefined })
      setPassword('')
      setCode('')
      completeReauth()
    } catch (err) {
      setError(err instanceof ApiError && err.status === 401
        ? 'That did not match.'
        : err instanceof ApiError ? err.message : 'Could not confirm.')
    } finally {
      setBusy(false)
    }
  }

  function cancel() {
    setPassword('')
    setCode('')
    setError(null)
    cancelReauth(new ApiError(403, 'Cancelled.'))
  }

  return (
    <div className="recovery-prompt" role="dialog" aria-modal="true">
      <form className="recovery-prompt__card" onSubmit={confirm}>
        <h2>Confirm it&rsquo;s you</h2>
        <p className="muted small">
          This action is irreversible or changes who can administer the instance,
          so it needs your password again.
        </p>
        {error && <p className="alert alert--error">{error}</p>}
        <label>
          Password
          <PasswordInput value={password} onChange={(e) => setPassword(e.target.value)} autoComplete="current-password" autoFocus />
        </label>
        {user?.totpEnabled && (
          <label>
            Or a code from your authenticator
            <input value={code} onChange={(e) => setCode(e.target.value)} inputMode="numeric" autoComplete="one-time-code" placeholder="123456" />
          </label>
        )}
        <div className="row-gap">
          <button type="submit" className="btn btn--primary" disabled={busy || (!password && !code)}>
            {busy ? 'Confirming…' : 'Confirm'}
          </button>
          <button type="button" className="btn btn--ghost" onClick={cancel}>Cancel</button>
        </div>
      </form>
    </div>
  )
}
