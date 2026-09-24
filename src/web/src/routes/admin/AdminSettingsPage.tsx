import { useEffect, useState } from 'react'
import { api, ApiError, type SiteSettings, Permission } from '../../api/client'
import { useAuth } from '../../auth/AuthContext'
import { EmailSettingsSection } from './EmailSettingsSection'

/** Admin → Settings (dev-plan 2.3), the UI over the SiteSettings row. */
export function AdminSettingsPage() {
  const { can } = useAuth()
  const [settings, setSettings] = useState<SiteSettings | null>(null)
  const [instanceName, setInstanceName] = useState('')
  const [baseUrl, setBaseUrl] = useState('')
  const [embedAllowlist, setEmbedAllowlist] = useState('')
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

      {/* A grid, so these independent forms use the width of a large display
          instead of stacking in one narrow column beside empty space. */}
      <div className="admin-settings">

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
        <EmailSettingsSection settings={settings} onSaved={setSettings} />
      )}
      </div>
    </>
  )
}
