import { type FormEvent, useState } from 'react'
import { api, ApiError } from '../api/client'
import { useAuth } from '../auth/AuthContext'
import { PasswordInput } from '../components/PasswordInput'
import { AvatarPicker } from '../components/AvatarPicker'

type Status = { kind: 'ok' | 'error'; message: string } | null

/**
 * Your own account: display name, email address and password.
 *
 * Each section submits on its own, rather than one form saving everything.
 * They have genuinely different requirements — email and password need the
 * current password, display name does not — and a single form would either
 * demand the password to rename yourself or skip the check that protects the
 * other two.
 */
export function ProfilePage() {
  const { user, refresh } = useAuth()

  const [displayName, setDisplayName] = useState(user?.displayName ?? '')
  const [nameStatus, setNameStatus] = useState<Status>(null)
  const [nameBusy, setNameBusy] = useState(false)

  const [email, setEmail] = useState(user?.email ?? '')
  const [emailPassword, setEmailPassword] = useState('')
  const [emailStatus, setEmailStatus] = useState<Status>(null)
  const [emailBusy, setEmailBusy] = useState(false)

  const [currentPassword, setCurrentPassword] = useState('')
  const [newPassword, setNewPassword] = useState('')
  const [confirmPassword, setConfirmPassword] = useState('')
  const [passwordStatus, setPasswordStatus] = useState<Status>(null)
  const [passwordBusy, setPasswordBusy] = useState(false)

  // The server owns this rule; the account is SSO-only when it has no local
  // password, and the identity provider owns its email and password.
  const ssoOnly = user?.hasPassword === false

  function message(err: unknown, fallback: string): string {
    return err instanceof ApiError ? err.message : fallback
  }

  async function saveName(e: FormEvent) {
    e.preventDefault()
    setNameBusy(true)
    setNameStatus(null)
    try {
      await api.auth.updateProfile({ displayName })
      await refresh()
      setNameStatus({ kind: 'ok', message: 'Display name updated.' })
    } catch (err) {
      setNameStatus({ kind: 'error', message: message(err, 'Could not update your display name.') })
    } finally {
      setNameBusy(false)
    }
  }

  async function saveEmail(e: FormEvent) {
    e.preventDefault()
    setEmailBusy(true)
    setEmailStatus(null)
    try {
      await api.auth.changeEmail({ currentPassword: emailPassword, email })
      await refresh()
      setEmailPassword('')
      setEmailStatus({ kind: 'ok', message: 'Email address updated.' })
    } catch (err) {
      setEmailStatus({ kind: 'error', message: message(err, 'Could not update your email address.') })
    } finally {
      setEmailBusy(false)
    }
  }

  async function savePassword(e: FormEvent) {
    e.preventDefault()
    // Checked here as well as on length server-side: a typo in the confirmation
    // should not cost a round trip, and the server never sees this field.
    if (newPassword !== confirmPassword) {
      setPasswordStatus({ kind: 'error', message: 'The new passwords do not match.' })
      return
    }
    setPasswordBusy(true)
    setPasswordStatus(null)
    try {
      await api.auth.changePassword({ currentPassword, newPassword })
      setCurrentPassword('')
      setNewPassword('')
      setConfirmPassword('')
      setPasswordStatus({
        kind: 'ok',
        message: 'Password changed. Any other devices signed in to this account have been signed out.',
      })
    } catch (err) {
      setPasswordStatus({ kind: 'error', message: message(err, 'Could not change your password.') })
    } finally {
      setPasswordBusy(false)
    }
  }

  function note(status: Status) {
    if (!status) return null
    return (
      <p className={status.kind === 'error' ? 'alert alert--error' : 'profile__ok'}>
        {status.message}
      </p>
    )
  }

  if (!user) return null

  return (
    <div className="page-wrap">
      <h1>Your profile</h1>

      <section className="profile__section">
        <h2>Avatar</h2>
        <AvatarPicker />
      </section>

      <section className="profile__section">
        <h2>Display name</h2>
        <p className="muted small">Shown on your pages, comments and version history.</p>
        <form onSubmit={saveName}>
          <label>
            Display name
            <input value={displayName} onChange={(e) => setDisplayName(e.target.value)} required />
          </label>
          {note(nameStatus)}
          <button type="submit" className="btn btn--primary" disabled={nameBusy}>
            {nameBusy ? 'Saving…' : 'Save name'}
          </button>
        </form>
      </section>

      <section className="profile__section">
        <h2>Email address</h2>
        {ssoOnly ? (
          <p className="muted small">
            This account signs in through your identity provider, which owns its email address.
          </p>
        ) : (
          <form onSubmit={saveEmail}>
            <label>
              Email address
              <input type="email" value={email} onChange={(e) => setEmail(e.target.value)} required />
            </label>
            <label>
              Current password
              <PasswordInput
                value={emailPassword}
                onChange={(e) => setEmailPassword(e.target.value)}
                autoComplete="current-password"
                required
              />
            </label>
            {note(emailStatus)}
            <button type="submit" className="btn btn--primary" disabled={emailBusy}>
              {emailBusy ? 'Saving…' : 'Change email'}
            </button>
          </form>
        )}
      </section>

      <section className="profile__section">
        <h2>Password</h2>
        {ssoOnly ? (
          <p className="muted small">
            This account signs in through your identity provider, which owns its password.
          </p>
        ) : (
          <form onSubmit={savePassword}>
            <p className="muted small">
              Changing your password signs out every other device using this account.
            </p>
            <label>
              Current password
              <PasswordInput
                value={currentPassword}
                onChange={(e) => setCurrentPassword(e.target.value)}
                autoComplete="current-password"
                required
              />
            </label>
            <label>
              New password
              <PasswordInput
                value={newPassword}
                onChange={(e) => setNewPassword(e.target.value)}
                autoComplete="new-password"
                minLength={8}
                required
              />
            </label>
            <label>
              Confirm new password
              <PasswordInput
                value={confirmPassword}
                onChange={(e) => setConfirmPassword(e.target.value)}
                autoComplete="new-password"
                minLength={8}
                required
              />
            </label>
            {note(passwordStatus)}
            <button type="submit" className="btn btn--primary" disabled={passwordBusy}>
              {passwordBusy ? 'Saving…' : 'Change password'}
            </button>
          </form>
        )}
      </section>
    </div>
  )
}
