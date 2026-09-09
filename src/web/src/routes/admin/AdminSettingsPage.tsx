import { type FormEvent, useEffect, useState } from 'react'
import { api, ApiError, type SiteSettings } from '../../api/client'
import { PasswordInput } from '../../components/PasswordInput'

/** Admin → Settings (dev-plan 2.3), the UI over the SiteSettings row. */
export function AdminSettingsPage() {
  const [settings, setSettings] = useState<SiteSettings | null>(null)
  const [instanceName, setInstanceName] = useState('')
  const [smtpPassword, setSmtpPassword] = useState('')
  const [status, setStatus] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    api.admin.settings
      .get()
      .then((s) => {
        setSettings(s)
        setInstanceName(s.instanceName)
      })
      .catch(() => setError('Could not load settings.'))
  }, [])

  /** Sends one field. Omitted fields keep their stored value server-side. */
  async function patch(input: Record<string, unknown>, message: string) {
    setBusy(true)
    setError(null)
    setStatus(null)
    try {
      const updated = await api.admin.settings.update(input as never)
      setSettings(updated)
      setStatus(message)
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not save.')
    } finally {
      setBusy(false)
    }
  }

  async function saveSmtp(e: FormEvent) {
    e.preventDefault()
    const form = new FormData(e.currentTarget as HTMLFormElement)
    await patch(
      {
        smtpHost: String(form.get('smtpHost') ?? ''),
        smtpPort: Number(form.get('smtpPort') ?? 587),
        smtpUsername: String(form.get('smtpUsername') ?? ''),
        smtpFromAddress: String(form.get('smtpFromAddress') ?? ''),
        smtpTls: Number(form.get('smtpTls') ?? 1),
        // Only sent when typed: an empty string would clear the stored one,
        // and null (omitted) means "leave it alone".
        ...(smtpPassword ? { smtpPassword } : {}),
      },
      'Mail settings saved.',
    )
    setSmtpPassword('')
  }

  if (!settings) return <p className="muted">{error ?? 'Loading…'}</p>

  return (
    <>
      {error && <p className="alert alert--error">{error}</p>}
      {status && <p className="profile__ok">{status}</p>}

      <section className="profile__section">
        <h2>Instance</h2>
        <label>
          Name
          <input value={instanceName} onChange={(e) => setInstanceName(e.target.value)} />
        </label>
        <button
          type="button"
          className="btn btn--primary"
          disabled={busy}
          onClick={() => patch({ instanceName }, 'Instance name saved.')}
        >
          Save
        </button>
      </section>

      <section className="profile__section">
        <h2>Access</h2>
        <label className="admin__toggle">
          <input
            type="checkbox"
            checked={settings.allowPublicRegistration}
            disabled={busy}
            onChange={(e) => patch(
              { allowPublicRegistration: e.target.checked },
              e.target.checked ? 'Registration is open.' : 'Registration is now by invitation.',
            )}
          />
          <span>
            <strong>Allow public registration</strong>
            <br />
            <span className="muted small">
              When off, new accounts need an invite link. The very first account
              on an empty instance can always register, so this cannot lock you out.
            </span>
          </span>
        </label>

        <label className="admin__toggle">
          <input
            type="checkbox"
            checked={settings.allowPublicSpaces}
            disabled={busy}
            onChange={(e) => patch(
              { allowPublicSpaces: e.target.checked },
              e.target.checked ? 'Public spaces enabled.' : 'Public spaces disabled.',
            )}
          />
          <span>
            <strong>Allow public spaces</strong>
            <br />
            <span className="muted small">
              Instance-wide switch for anonymous read access. Per-space publishing
              is not built yet (dev-plan Phase 5); until the security hardening in
              Phase 3 ships, leave this off on anything reachable from the internet.
            </span>
          </span>
        </label>
      </section>

      <section className="profile__section">
        <h2>Email</h2>
        <p className="muted small">
          Outbound mail is not wired up yet (dev-plan Phase 4). These settings
          are stored now so they are ready when it is.
        </p>
        <form onSubmit={saveSmtp}>
          <label>
            SMTP host
            <input name="smtpHost" defaultValue={settings.smtpHost ?? ''} />
          </label>
          <label>
            Port
            <input name="smtpPort" type="number" min={1} max={65535} defaultValue={settings.smtpPort} />
          </label>
          <label>
            Username
            <input name="smtpUsername" defaultValue={settings.smtpUsername ?? ''} />
          </label>
          <label>
            Password
            <PasswordInput
              value={smtpPassword}
              onChange={(e) => setSmtpPassword(e.target.value)}
              autoComplete="off"
            />
          </label>
          <p className="muted small">
            {settings.smtpPasswordSet
              ? 'A password is stored. Leave blank to keep it.'
              : 'No password stored.'}
          </p>
          <label>
            From address
            <input name="smtpFromAddress" type="email" defaultValue={settings.smtpFromAddress ?? ''} />
          </label>
          <label>
            Encryption
            <select name="smtpTls" defaultValue={settings.smtpTls}>
              <option value={0}>None</option>
              <option value={1}>STARTTLS</option>
              <option value={2}>SSL on connect</option>
            </select>
          </label>
          <button type="submit" className="btn btn--primary" disabled={busy}>
            Save mail settings
          </button>
        </form>
      </section>
    </>
  )
}
