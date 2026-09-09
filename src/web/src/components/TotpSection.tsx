import { type FormEvent, useEffect, useState } from 'react'
import QRCode from 'qrcode'
import { api, ApiError, type TotpSetup } from '../api/client'
import { useAuth } from '../auth/AuthContext'
import { PasswordInput } from './PasswordInput'

/**
 * Profile → Two-factor sign-in (dev-plan 3.5). Enrolment is scan → type a
 * code → on; the recovery codes from registration are the backup, so nothing
 * new to save.
 */
export function TotpSection() {
  const { user, refresh } = useAuth()
  const [setup, setSetup] = useState<TotpSetup | null>(null)
  const [qr, setQr] = useState<string | null>(null)
  const [password, setPassword] = useState('')
  const [code, setCode] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [status, setStatus] = useState<string | null>(null)

  useEffect(() => {
    if (!setup) {
      setQr(null)
      return
    }
    QRCode.toDataURL(setup.otpauthUri, { margin: 1, width: 192 }).then(setQr).catch(() => setQr(null))
  }, [setup])

  async function run(work: () => Promise<unknown>, done: string) {
    setBusy(true)
    setError(null)
    setStatus(null)
    try {
      await work()
      setStatus(done)
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Something went wrong.')
    } finally {
      setBusy(false)
    }
  }

  function begin(e: FormEvent) {
    e.preventDefault()
    return run(async () => {
      setSetup(await api.auth.totp.setup({ currentPassword: password || undefined }))
      setPassword('')
    }, 'Scan the code with your authenticator, then enter the six digits it shows.')
  }

  function enable(e: FormEvent) {
    e.preventDefault()
    return run(async () => {
      await api.auth.totp.enable(code)
      setSetup(null)
      setCode('')
      await refresh()
    }, 'Two-factor sign-in is on. Other devices have been signed out.')
  }

  function disable(e: FormEvent) {
    e.preventDefault()
    return run(async () => {
      await api.auth.totp.disable({ currentPassword: password || undefined, code: code || undefined })
      setPassword('')
      setCode('')
      await refresh()
    }, 'Two-factor sign-in is off.')
  }

  if (!user?.hasPassword) {
    return <p className="muted small">Your identity provider handles two-factor sign-in for this account.</p>
  }

  return (
    <>
      {error && <p className="alert alert--error">{error}</p>}
      {status && <p className="profile__ok">{status}</p>}

      {user.totpEnabled ? (
        <form onSubmit={disable}>
          <p className="muted small">
            <strong>On.</strong> Signing in asks for a code from your authenticator app.
            Your recovery codes work in its place if you lose the device.
          </p>
          {user.totpRequired === false && (
            <>
              <label>
                Current password
                <PasswordInput value={password} onChange={(e) => setPassword(e.target.value)} autoComplete="current-password" />
              </label>
              <label>
                Or a code from the app
                <input value={code} onChange={(e) => setCode(e.target.value)} inputMode="numeric" autoComplete="one-time-code" />
              </label>
              <button type="submit" className="btn btn--ghost" disabled={busy || (!password && !code)}>Turn off</button>
            </>
          )}
        </form>
      ) : setup ? (
        <form onSubmit={enable} className="totp-enrol">
          {qr ? <img src={qr} alt="QR code for your authenticator app" width={192} height={192} /> : null}
          <p className="muted small">
            Can&rsquo;t scan? Enter this key by hand: <code className="totp-secret">{setup.secret}</code>
          </p>
          <label>
            Six-digit code
            <input value={code} onChange={(e) => setCode(e.target.value)} inputMode="numeric" autoComplete="one-time-code" required autoFocus />
          </label>
          <div className="row-gap">
            <button type="submit" className="btn btn--primary" disabled={busy}>Turn on</button>
            <button type="button" className="btn btn--ghost" onClick={() => setSetup(null)}>Cancel</button>
          </div>
        </form>
      ) : (
        <form onSubmit={begin}>
          <p className="muted small">
            {user.totpRequired
              ? 'Administrators on this instance must use two-factor sign-in. Set it up to continue administering.'
              : 'Add a second step to sign-in: a code from an authenticator app such as Aegis, 1Password, Google Authenticator or Authy.'}
          </p>
          <label>
            Current password
            <PasswordInput value={password} onChange={(e) => setPassword(e.target.value)} autoComplete="current-password" />
            <span className="muted small">Not needed within a few minutes of signing in.</span>
          </label>
          <button type="submit" className="btn btn--primary" disabled={busy}>Set up two-factor</button>
        </form>
      )}
    </>
  )
}
