import { type FormEvent, useEffect, useState } from 'react'
import { api, ApiError } from '../api/client'
import { PasswordInput } from './PasswordInput'
import { RecoveryCodes } from './RecoveryCodes'

/**
 * The recovery-codes part of the profile page: how many are left, and a way to
 * mint a fresh set.
 *
 * It also covers the accounts that predate the feature. Those have no codes at
 * all: a silent zero would leave them with no recovery path and no idea, so
 * the empty case is a warning rather than a neutral count.
 */
export function RecoveryCodesSection() {
  const [remaining, setRemaining] = useState<number | null>(null)
  const [codes, setCodes] = useState<string[] | null>(null)
  const [password, setPassword] = useState('')
  const [confirming, setConfirming] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let cancelled = false
    api.auth
      .recoveryStatus()
      .then((s) => !cancelled && setRemaining(s.remaining))
      .catch(() => !cancelled && setRemaining(null))
    return () => {
      cancelled = true
    }
  }, [])

  async function regenerate(e: FormEvent) {
    e.preventDefault()
    setBusy(true)
    setError(null)
    try {
      const result = await api.auth.regenerateRecoveryCodes({ currentPassword: password })
      setCodes(result.codes)
      setRemaining(result.codes.length)
      setPassword('')
      setConfirming(false)
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not generate new codes.')
    } finally {
      setBusy(false)
    }
  }

  if (codes) {
    return (
      <>
        <RecoveryCodes codes={codes} onDone={() => setCodes(null)} doneLabel="Done" />
      </>
    )
  }

  const none = remaining === 0
  const low = remaining !== null && remaining > 0 && remaining <= 2

  return (
    <>
      <p className="muted small">
        Single-use codes that reset your password if you are locked out. They
        work with no email server configured.
      </p>

      {remaining !== null && (
        <p className={none || low ? 'alert alert--error' : 'muted small'}>
          {none
            ? 'You have no recovery codes. Generate a set so you can regain access if you forget your password.'
            : low
              ? `Only ${remaining} code${remaining === 1 ? '' : 's'} left. Generate a new set.`
              : `${remaining} unused codes remaining.`}
        </p>
      )}

      {confirming ? (
        <form onSubmit={regenerate}>
          <p className="muted small">
            Generating a new set immediately invalidates your existing codes.
          </p>
          <label>
            Current password
            <PasswordInput
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              autoComplete="current-password"
              required
              autoFocus
            />
          </label>
          {error && <p className="alert alert--error">{error}</p>}
          <div className="row-gap">
            <button type="submit" className="btn btn--primary" disabled={busy}>
              {busy ? 'Generating…' : 'Generate new codes'}
            </button>
            <button
              type="button"
              className="btn btn--ghost"
              onClick={() => {
                setConfirming(false)
                setError(null)
                setPassword('')
              }}
            >
              Cancel
            </button>
          </div>
        </form>
      ) : (
        <button type="button" className="btn btn--ghost" onClick={() => setConfirming(true)}>
          {none ? 'Generate recovery codes' : 'Generate new codes'}
        </button>
      )}
    </>
  )
}
