import { type FormEvent, useEffect, useState } from 'react'
import { api, ApiError, type SiteSettings, Permission } from '../../api/client'
import { useAuth } from '../../auth/AuthContext'
import { PasswordInput } from '../../components/PasswordInput'

/** Admin → Settings (dev-plan 2.3), the UI over the SiteSettings row. */
export function AdminSettingsPage() {
  const { can } = useAuth()
  const [settings, setSettings] = useState<SiteSettings | null>(null)
  const [instanceName, setInstanceName] = useState('')
  const [baseUrl, setBaseUrl] = useState('')
  const [embedAllowlist, setEmbedAllowlist] = useState('')
  const [testResult, setTestResult] = useState<string | null>(null)
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
        setBaseUrl(s.baseUrl ?? '')
        setEmbedAllowlist(s.embedAllowlist ?? '')
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

  // Each section is gated by its own right (dev-plan 11.1); a role holding
  // none of them would otherwise be shown a blank page.
  const anySection = can(Permission.SettingsInstance) || can(Permission.SecuritySettings)
    || can(Permission.SettingsRegistration) || can(Permission.SettingsPublicSpaces)
    || can(Permission.SettingsEmail)

  return (
    <>
      {error && <p className="alert alert--error">{error}</p>}
      {status && <p className="profile__ok">{status}</p>}
      {!anySection && (
        <p className="muted">Your role does not allow changing any of this instance's settings.</p>
      )}

      {can(Permission.SettingsInstance) && (
      <section className="profile__section">
        <h2>Instance</h2>
        <label>
          Name
          <input value={instanceName} onChange={(e) => setInstanceName(e.target.value)} />
        </label>
        <label>
          Public address
          <input value={baseUrl} onChange={(e) => setBaseUrl(e.target.value)} placeholder={settings.effectiveBaseUrl} />
          <span className="muted small">
            Where links in email point. Blank uses the deploy-time value ({settings.effectiveBaseUrl}).
          </span>
        </label>
        <button
          type="button"
          className="btn btn--primary"
          disabled={busy}
          onClick={() => patch({ instanceName, baseUrl }, 'Instance settings saved.')}
        >
          Save
        </button>
      </section>
      )}

      {can(Permission.SecuritySettings) && (
      <section className="profile__section">
        <h2>Embeds</h2>
        <p className="muted small">
          Which sites a page may show in a frame. This is the whole of that
          decision: an address on no line here is refused when the page is
          written <em>and</em> blocked by the browser, so an embed can never
          reach an unlisted site. One host per line; a leading dot
          (<code>.youtube.com</code>) also matches its subdomains. Leave it
          empty to turn embeds off entirely.
        </p>
        <label>
          Allowed embed hosts
          <textarea
            rows={6}
            value={embedAllowlist}
            onChange={(e) => setEmbedAllowlist(e.target.value)}
            spellCheck={false}
            placeholder=".youtube.com"
          />
        </label>
        <button
          type="button"
          className="btn btn--primary"
          disabled={busy}
          onClick={() => patch({ embedAllowlist }, 'Embed allowlist saved.')}
        >
          Save
        </button>
      </section>
      )}

      {(can(Permission.SettingsRegistration) || can(Permission.SettingsPublicSpaces)) && (
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
              Instance-wide switch for anonymous read access. With it on, publish
              individual spaces from Admin → Spaces. Before turning it on for an
              instance reachable from the internet, work through the readiness
              checklist in <code>docs/security.md</code>. Turning it off hides every
              public space at once and keeps their settings.
            </span>
          </span>
        </label>
      </section>
      )}

      {can(Permission.SettingsEmail) && (
      <section className="profile__section">
        <h2>Email</h2>
        <label className="admin__toggle">
          <input
            type="checkbox"
            checked={settings.emailEnabled}
            disabled={busy}
            onChange={(e) => patch(
              { emailEnabled: e.target.checked },
              e.target.checked ? 'Outbound email is on.' : 'Outbound email is off.',
            )}
          />
          <span>
            <strong>Send email</strong>
            <br />
            <span className="muted small">
              Password-reset links, security alerts to administrators, and
              notifications for people who opt in. Off means none are attempted.
            </span>
          </span>
        </label>
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
          <div className="row-gap">
            <button type="submit" className="btn btn--primary" disabled={busy}>
              Save mail settings
            </button>
            <button
              type="button"
              className="btn btn--ghost"
              disabled={busy || !settings.emailEnabled}
              onClick={async () => {
                setTestResult(null)
                try {
                  const r = await api.admin.settings.sendTestEmail()
                  setTestResult(r.sent ? 'Sent: check your inbox.' : `Not sent: ${r.error ?? 'unknown error'}`)
                } catch (err) {
                  setTestResult(err instanceof ApiError ? err.message : 'Could not send.')
                }
              }}
            >
              Send test email to me
            </button>
          </div>
          {testResult && <p className={testResult.startsWith('Sent') ? 'profile__ok' : 'alert alert--error'}>{testResult}</p>}
        </form>
      </section>
      )}
    </>
  )
}
