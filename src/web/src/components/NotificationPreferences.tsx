import { useEffect, useState } from 'react'
import { api, ApiError, EmailNotificationMode } from '../api/client'
import { useAuth } from '../auth/AuthContext'

const OPTIONS: Array<{ value: EmailNotificationMode; label: string; hint: string }> = [
  { value: EmailNotificationMode.Off, label: 'Off', hint: 'Only the bell in the app.' },
  { value: EmailNotificationMode.Immediate, label: 'Immediately', hint: 'An email for each update, within a minute.' },
  { value: EmailNotificationMode.DailyDigest, label: 'Daily digest', hint: 'One email a day, when something changed.' },
]

/** Profile → Email notifications (dev-plan 4.3). */
export function NotificationPreferences() {
  const { user, refresh } = useAuth()
  const [instanceSends, setInstanceSends] = useState<boolean | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    api.auth.recoveryOptions().then((o) => setInstanceSends(o.emailEnabled)).catch(() => setInstanceSends(null))
  }, [])

  async function choose(value: EmailNotificationMode) {
    setBusy(true)
    setError(null)
    try {
      await api.auth.setNotificationPreference(value)
      await refresh()
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not save.')
    } finally {
      setBusy(false)
    }
  }

  if (!user) return null
  return (
    <>
      <p className="muted small">
        Updates to pages and spaces you watch. Security alerts to administrators are
        always emailed.
        {instanceSends === false && ' This instance is not sending email yet; your choice is kept for when it does.'}
      </p>
      {error && <p className="alert alert--error">{error}</p>}
      {OPTIONS.map((o) => (
        <label key={o.value} className="admin__toggle">
          <input type="radio" name="emailNotifications" checked={user.emailNotifications === o.value}
            disabled={busy} onChange={() => choose(o.value)} />
          <span><strong>{o.label}</strong><br /><span className="muted small">{o.hint}</span></span>
        </label>
      ))}
    </>
  )
}
