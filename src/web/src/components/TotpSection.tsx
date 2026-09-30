import { type FormEvent, useEffect, useState } from 'react'
import QRCode from 'qrcode'
import { api, ApiError, type TotpSetup } from '../api/client'
import { useAuth } from '../auth/AuthContext'
import { PasswordInput } from './PasswordInput'

/**
 * Profile → Two-factor sign-in (dev-plan 3.5). Enrollment is scan → type a
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
    }, 'Two-factor sign-in is off. Other devices have been signed out.')
  }

  if (!user) return null
  // An account with no Tesria password signs in through its identity
  // provider, which handles two-factor for it. Tesria's own is offered only
  // when this instance requires it of administrators (t2-020): single
  // sign-on then asks for its code too. Before, the rule held such an
  // administrator's rights back and this card had no way to meet it.
  const ssoOnly = !user.hasPassword
  if (ssoOnly && !user.totpEnabled && !user.totpRequired) {
    return <p className="muted small">Your identity provider handles two-factor sign-in for this account.</p>
  }

  return (
    <>
      {error && <p className="alert alert--error">{error}</p>}
      {status && <p className="profile__ok">{status}</p>}

      {user.totpEnabled ? (
        <form onSubmit={disable}>
          <p className="muted small">
            <strong>On.</strong> Signing in asks for a code from your authenticator app.{' '}
            {ssoOnly
              ? 'This account has no recovery codes: if you lose the device, an administrator can turn two-factor off for you.'
              : 'Your recovery codes work in its place if you lose the device.'}
          </p>
          {user.totpMandatory ? (
            <p className="muted small">Administrators on this instance must keep two-factor sign-in on.</p>
          ) : (
            <>
              {!ssoOnly && (
                <label>
                  Current Password
                  <PasswordInput value={password} onChange={(e) => setPassword(e.target.value)} autoComplete="current-password" />
                </label>
              )}
              <label>
                {ssoOnly ? 'A Code From the App' : 'Or a Code From the App'}
                <input value={code} onChange={(e) => setCode(e.target.value)} inputMode="numeric" autoComplete="one-time-code" />
              </label>
              <button type="submit" className="btn btn--ghost" disabled={busy || (!password && !code)}>Turn Off</button>
            </>
          )}
        </form>
      ) : setup ? (
        <form onSubmit={enable} className="totp-enroll">
          {qr ? <img src={qr} alt="QR code for your authenticator app" width={192} height={192} /> : null}
          <p className="muted small">
            Can&rsquo;t scan? Enter this key by hand: <code className="totp-secret">{setup.secret}</code>
          </p>
          <label>
            Six-Digit Code
            <input value={code} onChange={(e) => setCode(e.target.value)} inputMode="numeric" autoComplete="one-time-code" required autoFocus />
          </label>
          <div className="row-gap">
            <button type="submit" className="btn btn--primary" disabled={busy}>Turn On</button>
            <button type="button" className="btn btn--ghost" onClick={() => setSetup(null)}>Cancel</button>
          </div>
        </form>
      ) : (
        <form onSubmit={begin}>
          <p className="muted small">
            {user.totpRequired
              ? ssoOnly
                ? 'Administrators on this instance must also use Tesria’s own two-factor sign-in, even with single sign-on. Set it up within a few minutes of signing in to continue administering; single sign-on then asks for its code too.'
                : 'Administrators on this instance must use two-factor sign-in. Set it up to continue administering.'
              : 'Add a second step to sign-in: a code from an authenticator app such as Aegis, 1Password, Google Authenticator or Authy.'}
          </p>
          {!ssoOnly && (
            <label>
              Current Password
              <PasswordInput value={password} onChange={(e) => setPassword(e.target.value)} autoComplete="current-password" />
              <span className="muted small">Not needed within a few minutes of signing in.</span>
            </label>
          )}
          <button type="submit" className="btn btn--primary" disabled={busy}>Set Up Two-Factor</button>
        </form>
      )}
    </>
  )
}
