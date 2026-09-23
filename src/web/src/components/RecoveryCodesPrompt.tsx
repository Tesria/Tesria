import { type FormEvent, useState } from 'react'
import { api, ApiError } from '../api/client'
import { useAuth } from '../auth/AuthContext'
import { PasswordInput } from './PasswordInput'
import { RecoveryCodes } from './RecoveryCodes'

/**
 * Dismissals last for the tab, so the prompt returns at the next sign-in
 * rather than nagging on every navigation within one session.
 *
 * Keyed by user: one person clicking "Not now" must not silence the prompt
 * for whoever signs in next in the same tab.
 */
const dismissedKey = (userId: string) => `tesria-recovery-prompt-dismissed:${userId}`

function dismissedThisSession(userId: string): boolean {
  try {
    return sessionStorage.getItem(dismissedKey(userId)) === '1'
  } catch {
    return false
  }
}

/**
 * Offers to generate recovery codes to an account that has none.
 *
 * Accounts created before recovery codes existed have no way back in if their
 * password is lost, and a banner on a settings page they may never open is not
 * a fix. Showing this right after a sign-in works because the sign-in *is* the
 * re-authentication: the server accepts the generation without a second
 * password prompt inside a short window, the same way GitHub and Google
 * handle backup codes.
 *
 * Outside that window the server asks for the password, and the prompt asks
 * for it here rather than sending someone off to the profile page: a dialog
 * whose only button fails and redirects is worse than no dialog.
 */
export function RecoveryCodesPrompt() {
  const { user, refresh } = useAuth()
  const [codes, setCodes] = useState<string[] | null>(null)
  const [dismissedFor, setDismissedFor] = useState<string | null>(null)
  const [password, setPassword] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  // SSO accounts have no local password, so recovery codes would reset nothing.
  const none = !!user && user.hasPassword && user.recoveryCodesRemaining === 0
  // Codes that exist but were never confirmed saved: registration creates
  // them, and until 2026-09-22 a race could skip the screen that shows them,
  // so an account can hold eight codes its owner has never seen.
  const unsaved = !!user && user.hasPassword && user.recoveryCodesRemaining > 0 && !user.recoveryCodesSaved
  const needed = none || unsaved
  const dismissed = user != null && (dismissedFor === user.id || dismissedThisSession(user.id))

  // Codes, once generated, keep the dialog open no matter what: refreshing the
  // profile takes `recoveryCodesRemaining` off zero, and closing on that would
  // destroy the one and only copy of the codes the person still has to save.
  if (!codes && (!needed || dismissed)) return null

  async function generate(e?: FormEvent) {
    e?.preventDefault()
    setBusy(true)
    setError(null)
    try {
      // Omitted while the sign-in is recent; supplied once the server has told
      // us that window has passed.
      const result = await api.auth.regenerateRecoveryCodes(
        password === null ? {} : { currentPassword: password },
      )
      setCodes(result.codes)
      setPassword(null)
      await refresh()
    } catch (err) {
      const message = err instanceof ApiError ? err.message : 'Could not generate recovery codes.'
      // The first rejection is the expected one on a session that has been
      // open a while: ask for the password instead of giving up.
      if (password === null && err instanceof ApiError) setPassword('')
      setError(message)
    } finally {
      setBusy(false)
    }
  }

  /** "I have my codes": the person saved them at the time; take their word. */
  async function confirmSaved() {
    setBusy(true)
    try {
      await api.auth.acknowledgeRecoveryCodes()
      await refresh()
    } catch {
      setError('Could not record that. Try again, or choose Not now.')
    } finally {
      setBusy(false)
    }
  }

  /** Done on freshly generated codes: they are saved now. */
  function doneWithCodes() {
    void api.auth.acknowledgeRecoveryCodes().catch(() => {}).then(() => refresh()).catch(() => {})
    dismiss()
  }

  function dismiss() {
    if (!user) return
    try {
      sessionStorage.setItem(dismissedKey(user.id), '1')
    } catch {
      // Storage can be blocked; the prompt simply reappears on navigation.
    }
    setDismissedFor(user.id)
    setCodes(null)
  }

  return (
    <div className="recovery-prompt">
      <div className="recovery-prompt__card">
        {codes ? (
          <>
            <h2>Save your recovery codes</h2>
            <RecoveryCodes codes={codes} onDone={doneWithCodes} doneLabel="Done" />
          </>
        ) : (
          <form onSubmit={generate}>
            {unsaved ? (
              <>
                <h2>Do you have your recovery codes?</h2>
                <p className="muted small">
                  This account has recovery codes, but they were never confirmed as saved,
                  so they may never have been shown to you. If you have them somewhere safe,
                  say so. If not, make a new set: it replaces the old one, and takes a moment.
                </p>
              </>
            ) : (
              <>
                <h2>Set up account recovery</h2>
                <p className="muted small">
                  This account has no recovery codes. Without them, losing your
                  password means an administrator has to let you back in. Generating
                  a set takes a moment and needs no email.
                </p>
              </>
            )}
            {error && <p className="alert alert--error">{error}</p>}
            {password !== null && (
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
            )}
            <div className="row-gap">
              <button type="submit" className="btn btn--primary" disabled={busy}>
                {busy ? 'Generating…' : unsaved ? 'Make new codes' : 'Generate codes'}
              </button>
              {unsaved && (
                <button type="button" className="btn btn--ghost" disabled={busy} onClick={() => void confirmSaved()}>
                  I have my codes
                </button>
              )}
              <button type="button" className="btn btn--ghost" onClick={dismiss}>
                Not now
              </button>
            </div>
          </form>
        )}
      </div>
    </div>
  )
}
