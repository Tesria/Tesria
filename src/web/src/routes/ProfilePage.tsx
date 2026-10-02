import { type FormEvent, useEffect, useId, useState } from 'react'
import { api, ApiError, DISPLAY_NAME_MAX, Permission } from '../api/client'
import { useNavigate } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'
import { PasswordInput } from '../components/PasswordInput'
import { PASSWORD_HINT, PASSWORD_MAX, PASSWORD_MIN, passwordProblem } from '../auth/passwordRule'
import { AvatarPicker } from '../components/AvatarPicker'
import { RecoveryCodesSection } from '../components/RecoveryCodesSection'
import { SessionsSection } from '../components/SessionsSection'
import { ApiTokensSection } from '../components/ApiTokensSection'
import { NotificationPreferences } from '../components/NotificationPreferences'
import { TourAndTipsSection } from '../components/TourAndTipsSection'
import { TotpSection } from '../components/TotpSection'
import { noteProfileVisit } from '../onboarding/signals'
import { useInstance } from '../InstanceContext'

type Status = { kind: 'ok' | 'error'; message: string } | null

/**
 * Your own account: display name, email address and password.
 *
 * Each section submits on its own, rather than one form saving everything.
 * They have genuinely different requirements (email and password need the
 * current password, display name does not) and a single form would either
 * demand the password to rename yourself or skip the check that protects the
 * other two.
 */
export function ProfilePage() {
  const { user, refresh, can, logout } = useAuth()
  const navigate = useNavigate()
  const instance = useInstance()
  useEffect(() => { noteProfileVisit() }, [])

  const [displayName, setDisplayName] = useState(user?.displayName ?? '')
  const [nameStatus, setNameStatus] = useState<Status>(null)
  const [nameBusy, setNameBusy] = useState(false)

  const [email, setEmail] = useState(user?.email ?? '')
  const [emailPassword, setEmailPassword] = useState('')
  const [emailStatus, setEmailStatus] = useState<Status>(null)
  const [emailBusy, setEmailBusy] = useState(false)
  // Whether this instance sends email decides how a change works (t2-009):
  // with email, a link to the new address confirms it; without, it is at once.
  const [sendsEmail, setSendsEmail] = useState<boolean | null>(null)
  useEffect(() => {
    api.auth.recoveryOptions().then((o) => setSendsEmail(o.emailEnabled)).catch(() => setSendsEmail(null))
  }, [])
  const passwordHintId = useId()

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
      const updated = await api.auth.changeEmail({ currentPassword: emailPassword, email })
      await refresh()
      setEmailPassword('')
      if (updated.pendingEmail) {
        // Nothing has changed yet: the field goes back to the address that
        // still signs in, and the waiting change is shown above it.
        setEmail(updated.email)
        setEmailStatus({ kind: 'ok', message: `We sent a link to ${updated.pendingEmail}. Open it to finish the change.` })
      } else {
        setEmailStatus({ kind: 'ok', message: 'Email address updated.' })
      }
    } catch (err) {
      setEmailStatus({ kind: 'error', message: message(err, 'Could not update your email address.') })
    } finally {
      setEmailBusy(false)
    }
  }

  async function resendEmail() {
    setEmailBusy(true)
    setEmailStatus(null)
    try {
      const updated = await api.auth.resendEmailChange()
      await refresh()
      setEmailStatus({ kind: 'ok', message: `We sent a new link to ${updated.pendingEmail}. Earlier links no longer work.` })
    } catch (err) {
      setEmailStatus({ kind: 'error', message: message(err, 'Could not send the link again.') })
    } finally {
      setEmailBusy(false)
    }
  }

  async function cancelEmail() {
    setEmailBusy(true)
    setEmailStatus(null)
    try {
      await api.auth.cancelEmailChange()
      await refresh()
      setEmailStatus({ kind: 'ok', message: 'Email change canceled. The link no longer works.' })
    } catch (err) {
      setEmailStatus({ kind: 'error', message: message(err, 'Could not cancel the change.') })
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
    const weak = passwordProblem(newPassword)
    if (weak) {
      setPasswordStatus({ kind: 'error', message: weak })
      return
    }
    setPasswordBusy(true)
    setPasswordStatus(null)
    try {
      await api.auth.changePassword({ currentPassword, newPassword })
      // A waiting email change stops with a new password (t2-009).
      await refresh()
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
    <div className="page-wrap page-wrap--admin">
      {/* Sign out, opposite the heading (the owner, 2026-09-27): it left the
          top bar, and this is where people look for their own account. */}
      <div className="profile__heading">
        <h1>Your Profile</h1>
        <button type="button" className="btn btn--ghost" onClick={async () => { await logout(); navigate('/login') }}>
          Sign Out
        </button>
      </div>
      {/* Full-width cards, one to a row, like the admin pages (the owner,
          2026-09-23): 480px cards in a 900px column were one long narrow
          strip, and a grid of them read as a jumble. */}
      <div className="profile-grid">
        <section className="profile__section">
          <h2>Avatar</h2>
          <AvatarPicker />
        </section>

        <section className="profile__section">
          <h2>Display Name</h2>
          <p className="muted small">Shown on your pages, comments and version history.</p>
          <form onSubmit={saveName}>
            <label>
              Display Name
              <input value={displayName} onChange={(e) => setDisplayName(e.target.value)} required maxLength={DISPLAY_NAME_MAX} />
            </label>
            {note(nameStatus)}
            <button type="submit" className="btn btn--primary" disabled={nameBusy}>
              {nameBusy ? 'Saving…' : 'Save Name'}
            </button>
          </form>
        </section>

        <section className="profile__section">
          <h2>Email Address</h2>
          {ssoOnly ? (
            <p className="muted small">
              This account signs in through your identity provider, which owns its email address.
            </p>
          ) : (
            <form onSubmit={saveEmail}>
              {user.pendingEmail && (
                <div className="alert alert--warning profile__pending">
                  <p className="small">
                    <strong>Waiting for confirmation:</strong> {user.pendingEmail}.{' '}
                    {user.pendingEmailExpiresAt && new Date(user.pendingEmailExpiresAt) < new Date()
                      ? 'The link has expired: send a new one, or cancel the change.'
                      : `Open the link we sent there to make it your sign-in email. Until then you sign in with ${user.email}.`}
                  </p>
                  <div className="row-gap">
                    <button type="button" className="btn btn--ghost" onClick={resendEmail} disabled={emailBusy}>Resend Link</button>
                    <button type="button" className="btn btn--ghost" onClick={cancelEmail} disabled={emailBusy}>Cancel Change</button>
                  </div>
                </div>
              )}
              <label>
                Email Address
                <input type="email" value={email} onChange={(e) => setEmail(e.target.value)} required />
                <span className="muted small">
                  {sendsEmail === false
                    ? 'This instance does not send email, so a change takes effect at once. Check the new address carefully: you sign in with it.'
                    : 'We send a link to the new address, and the change takes effect when it is opened. Your current address is told too.'}
                </span>
              </label>
              <label>
                Current Password
                <PasswordInput
                  value={emailPassword}
                  onChange={(e) => setEmailPassword(e.target.value)}
                  autoComplete="current-password"
                  required
                />
              </label>
              {note(emailStatus)}
              <button type="submit" className="btn btn--primary" disabled={emailBusy}>
                {emailBusy ? 'Saving…' : 'Change Email'}
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
                Current Password
                <PasswordInput
                  value={currentPassword}
                  onChange={(e) => setCurrentPassword(e.target.value)}
                  autoComplete="current-password"
                  required
                />
              </label>
              <label>
                New Password
                <PasswordInput
                  value={newPassword}
                  onChange={(e) => setNewPassword(e.target.value)}
                  autoComplete="new-password"
                  minLength={PASSWORD_MIN}
                  maxLength={PASSWORD_MAX}
                  required
                  describedBy={passwordHintId}
                />
                <span className="muted small" id={passwordHintId}>{PASSWORD_HINT}</span>
              </label>
              <label>
                Confirm New Password
                <PasswordInput
                  value={confirmPassword}
                  onChange={(e) => setConfirmPassword(e.target.value)}
                  autoComplete="new-password"
                  minLength={PASSWORD_MIN}
                  maxLength={PASSWORD_MAX}
                  required
                />
              </label>
              {note(passwordStatus)}
              <button type="submit" className="btn btn--primary" disabled={passwordBusy}>
                {passwordBusy ? 'Saving…' : 'Change Password'}
              </button>
            </form>
          )}
        </section>

        <section className="profile__section" id="two-factor">
          <h2>Two-Factor Sign-In</h2>
          <TotpSection />
        </section>

        {!ssoOnly && (
          <section className="profile__section">
            <h2>Recovery Codes</h2>
            <RecoveryCodesSection />
          </section>
        )}

        {/* Trusting the server's own certificate (dev-plan 15.5). The wizard

            is a page of its own at /trust, also served over plain HTTP for a

            device that cannot get past the warning at all. From here the

            connection already works, so the link stays on it: plain HTTP was

            unreachable from a Windows machine in testing (2026-09-23). */}
        {instance?.ownCertificate && (
          <section className="profile__section" id="trust-this-device">
            <h2>Trust This Device</h2>
            <p className="muted">
              This server makes its own security certificate, so each browser warns about it until the device is told to
              trust it. If you see "Not secure" beside the address, or had to click past a warning to get here, a short
              guide sets this device up: it takes about three minutes, once per device.
            </p>
            <p>
              <a className="btn" href="/trust">Set Up This Device</a>
            </p>
          </section>
        )}

        <section className="profile__section" id="notifications">
          <h2>Email Notifications</h2>
          <NotificationPreferences />
        </section>

        <section className="profile__section" id="tour-and-tips">
          <h2>Tour and Tips</h2>
          <TourAndTipsSection />
        </section>

        <section className="profile__section profile__section--wide">
          <h2>Sessions</h2>
          <SessionsSection />
        </section>

        {can(Permission.TokensUse) ? (
          <section className="profile__section profile__section--wide" id="api-tokens">
            <h2>API Tokens</h2>
            <ApiTokensSection />
          </section>
        ) : (
          <section className="profile__section profile__section--wide" id="api-tokens">
            <h2>API Tokens</h2>
            <p className="muted small">
              Your role does not allow API tokens. An administrator can grant it under
              Administration, Roles.
            </p>
          </section>
        )}
      </div>
    </div>
  )
}
