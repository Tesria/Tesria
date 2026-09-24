import { type FormEvent, useEffect, useState } from 'react'
import {
  api, ApiError, MailSignIn, type MailProvider, type SiteSettings,
} from '../../api/client'
import { MailProviderHint, MailProviderPicker } from '../../components/MailProviderPicker'
import { PasswordInput } from '../../components/PasswordInput'

/**
 * Administration → Settings → Email (dev-plan 4.1, Phase 18): the provider
 * presets, and signing in with Microsoft or Google instead of a password.
 *
 * With a sign-in, the server, username and From address are the provider's
 * and the signed-in mailbox's, so the password form is replaced by the app
 * registration and the sign-in itself.
 */
export function EmailSettingsSection({
  settings, onSaved,
}: {
  settings: SiteSettings
  onSaved: (s: SiteSettings) => void
}) {
  const mail = settings.mail
  const [providers, setProviders] = useState<MailProvider[]>([])
  const [providerId, setProviderId] = useState(mail.provider ?? '')
  const [host, setHost] = useState(settings.smtpHost ?? '')
  const [port, setPort] = useState(settings.smtpPort)
  const [tls, setTls] = useState(settings.smtpTls)
  const [username, setUsername] = useState(settings.smtpUsername ?? '')
  const [from, setFrom] = useState(settings.smtpFromAddress ?? '')
  const [password, setPassword] = useState('')
  // For a provider with its own sign-in, which way Tesria signs in. It
  // starts as whatever is in use.
  const [useSignIn, setUseSignIn] = useState(mail.signIn !== MailSignIn.Password || !mail.provider)
  const [clientId, setClientId] = useState('')
  const [clientSecret, setClientSecret] = useState('')
  const [tenant, setTenant] = useState(mail.microsoftTenant ?? '')
  const [pasteBack, setPasteBack] = useState(false)
  const [pasted, setPasted] = useState('')
  const [testResult, setTestResult] = useState<string | null>(null)
  const [status, setStatus] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    api.admin.settings.mailProviders().then(setProviders).catch(() => setProviders([]))
  }, [])

  // Back from Microsoft or Google: the callback put the outcome in the address.
  useEffect(() => {
    const q = new URLSearchParams(window.location.search)
    const outcome = q.get('mail')
    if (!outcome) return
    const detail = q.get('detail') ?? ''
    if (outcome === 'connected') setStatus(`Signed in. Email now goes out from ${detail}.`)
    else setError(detail || 'The sign-in did not finish.')
    window.history.replaceState(null, '', window.location.pathname + window.location.hash)
  }, [])

  const provider = providers.find((p) => p.id === providerId) ?? null
  const signInKind = provider?.signIn ?? null
  const signInName = signInKind === MailSignIn.Microsoft ? 'Microsoft' : 'Google'
  const signedIn = mail.signIn !== MailSignIn.Password
  const showSignIn = signInKind !== null && signInKind !== MailSignIn.Password && useSignIn

  // Keep the registration fields in step with what the server has.
  useEffect(() => {
    if (signInKind === MailSignIn.Microsoft) setClientId(mail.microsoftClientId ?? '')
    if (signInKind === MailSignIn.Google) setClientId(mail.googleClientId ?? '')
    setClientSecret('')
  }, [signInKind, mail.microsoftClientId, mail.googleClientId])

  function choose(p: MailProvider | null) {
    setProviderId(p?.id ?? '')
    if (p) {
      setHost(p.host)
      setPort(p.port)
      setTls(p.tls)
      setUseSignIn(p.signIn !== null)
    }
    setStatus(null)
    setError(null)
  }

  async function run(action: () => Promise<void>) {
    setBusy(true)
    setError(null)
    setStatus(null)
    try {
      await action()
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Something went wrong.')
    } finally {
      setBusy(false)
    }
  }

  async function savePasswordSettings(e: FormEvent) {
    e.preventDefault()
    await run(async () => {
      const updated = await api.admin.settings.update({
        smtpProvider: providerId,
        smtpHost: host,
        smtpPort: port,
        smtpUsername: username,
        smtpFromAddress: from,
        smtpTls: tls,
        // Only sent when typed: an empty string would clear the stored one.
        ...(password ? { smtpPassword: password } : {}),
      })
      // Choosing a password for a provider signed in another way is a
      // decision to stop using that sign-in.
      if (updated.mail.signIn !== MailSignIn.Password) {
        updated.mail = await api.admin.settings.mailSignOut()
      }
      onSaved(updated)
      setPassword('')
      setStatus('Mail settings saved.')
    })
  }

  /** Saves the app registration, then goes to the provider to sign in. */
  async function signIn() {
    await run(async () => {
      const which = signInKind === MailSignIn.Microsoft ? 'microsoft' : 'google'
      const updated = await api.admin.settings.update({
        smtpProvider: providerId,
        ...(which === 'microsoft'
          ? { microsoftClientId: clientId, microsoftTenant: tenant, ...(clientSecret ? { microsoftClientSecret: clientSecret } : {}) }
          : { googleClientId: clientId, ...(clientSecret ? { googleClientSecret: clientSecret } : {}) }),
      })
      onSaved(updated)
      setClientSecret('')
      const start = await api.admin.settings.mailSignInStart(which)
      if (start.pasteBack) {
        // Google cannot come back to this address: the sign-in opens in a
        // new tab, ends on a page that does not load, and its address is
        // pasted here.
        window.open(start.url, '_blank', 'noopener')
        setPasteBack(true)
      } else {
        window.location.assign(start.url)
      }
    })
  }

  async function finishPasted(e: FormEvent) {
    e.preventDefault()
    await run(async () => {
      const info = await api.admin.settings.mailSignInComplete(pasted.trim())
      onSaved({ ...settings, mail: info, smtpFromAddress: info.account, smtpUsername: info.account })
      setPasteBack(false)
      setPasted('')
      setStatus(`Signed in. Email now goes out from ${info.account}.`)
    })
  }

  async function signOut() {
    await run(async () => {
      const info = await api.admin.settings.mailSignOut()
      onSaved({ ...settings, mail: info })
      setUseSignIn(false)
      setStatus('Signed out. Tesria will use the password below to send email.')
    })
  }

  const redirect = signInKind === MailSignIn.Microsoft ? mail.microsoftRedirectUri : mail.googleRedirectUri
  const secretSet = signInKind === MailSignIn.Microsoft ? mail.microsoftClientSecretSet : mail.googleClientSecretSet

  return (
    <section className="profile__section" id="email">
      <h2>Email</h2>
      <label className="admin__toggle">
        <input
          type="checkbox"
          checked={settings.emailEnabled}
          disabled={busy}
          onChange={(e) => run(async () => {
            onSaved(await api.admin.settings.update({ emailEnabled: e.target.checked }))
            setStatus(e.target.checked ? 'Outbound email is on.' : 'Outbound email is off.')
          })}
        />
        <span>
          <strong>Send email</strong>
          <br />
          <span className="muted small">
            Password-reset links, invitations, security alerts to administrators, and
            notifications for people who opt in. Off means none are attempted.
          </span>
        </span>
      </label>

      {error && <p className="alert alert--error">{error}</p>}
      {status && <p className="profile__ok">{status}</p>}
      {signedIn && mail.error && (
        <p className="alert alert--error">
          {mail.signIn === MailSignIn.Microsoft ? 'Microsoft' : 'Google'} stopped accepting the sign-in, so no
          email is going out: {mail.error}
        </p>
      )}

      <MailProviderPicker providers={providers} value={providerId} onChange={choose} disabled={busy} />
      {provider && <MailProviderHint provider={provider} signingIn={showSignIn} />}

      {signInKind !== null && signInKind !== MailSignIn.Password && (
        <fieldset className="mail-choice">
          <legend>How Tesria signs in</legend>
          <label className="admin__toggle admin__toggle--inline">
            <input type="radio" name="mail-signin" checked={useSignIn} onChange={() => setUseSignIn(true)} />
            <span>
              <strong>Sign in with {signInName}</strong> (recommended)
              <br />
              <span className="muted small">
                {signInKind === MailSignIn.Microsoft
                  ? 'The only way for a personal Outlook.com account, and the lasting one for Microsoft 365.'
                  : 'No app password to make or lose. Needs a small Google Cloud project, once.'}
              </span>
            </span>
          </label>
          <label className="admin__toggle admin__toggle--inline">
            <input type="radio" name="mail-signin" checked={!useSignIn} onChange={() => setUseSignIn(false)} />
            <span>
              <strong>{signInKind === MailSignIn.Microsoft ? 'Password' : 'App password'}</strong>
              <br />
              <span className="muted small">
                {signInKind === MailSignIn.Microsoft
                  ? 'Microsoft 365 only, while its administrator allows it. Microsoft turns this off by default at the end of December 2026.'
                  : 'Quick to set up: a 16-letter password you make in your Google Account.'}
              </span>
            </span>
          </label>
        </fieldset>
      )}

      {showSignIn ? (
        <div className="mail-signin">
          {signedIn && mail.signIn === signInKind ? (
            <p>
              Signed in as <strong>{mail.account}</strong>
              {mail.connectedAt && <> since {new Date(mail.connectedAt).toLocaleDateString()}</>}. Email goes out
              through {settings.smtpHost} from that address.
            </p>
          ) : (
            <p className="muted small">
              First register Tesria with {signInName}, once: the Tesria docs’{' '}
              <em>{provider?.supportPage}</em> page walks through it. Then fill in these and sign in.
            </p>
          )}
          <label>
            <span>Redirect address to register with {signInName}</span>
            <span className="mail-signin__redirect">
              <code>{redirect}</code>
              <button
                type="button"
                className="btn btn--ghost btn--sm"
                onClick={() => navigator.clipboard.writeText(redirect).catch(() => {})}
              >
                Copy
              </button>
            </span>
          </label>
          {signInKind === MailSignIn.Google && mail.googlePasteBack && (
            <p className="muted small">
              Google does not return to local addresses like this one, so register a <strong>Desktop app</strong>{' '}
              client. After you sign in, Google sends your browser to a page that does not load; you paste its
              address here.
            </p>
          )}
          <label>
            <span>{signInKind === MailSignIn.Microsoft ? 'Application (client) ID' : 'Client ID'}</span>
            <input value={clientId} onChange={(e) => setClientId(e.target.value)} autoComplete="off" />
          </label>
          <label>
            <span>Client secret</span>
            <PasswordInput value={clientSecret} onChange={(e) => setClientSecret(e.target.value)} autoComplete="off" />
          </label>
          <p className="muted small">
            {secretSet ? 'A secret is stored. Leave blank to keep it.' : 'No secret stored.'}
            {signInKind === MailSignIn.Microsoft && ' Paste the secret’s Value, not its Secret ID.'}
          </p>
          {signInKind === MailSignIn.Microsoft && (
            <label>
              <span>Directory (tenant) ID, optional</span>
              <input
                value={tenant}
                placeholder="Leave empty for any account"
                onChange={(e) => setTenant(e.target.value)}
                autoComplete="off"
              />
            </label>
          )}
          <div className="row-gap">
            <button
              type="button"
              className="btn btn--primary"
              disabled={busy || !clientId.trim() || (!secretSet && !clientSecret)}
              onClick={signIn}
            >
              {signedIn && mail.signIn === signInKind ? `Sign in again with ${signInName}` : `Sign in with ${signInName}`}
            </button>
            {signedIn && (
              <button type="button" className="btn btn--ghost" disabled={busy} onClick={signOut}>
                Sign out
              </button>
            )}
          </div>
          {pasteBack && (
            <form className="mail-signin__paste" onSubmit={finishPasted}>
              <label>
                <span>Paste the address of the page that did not load</span>
                <input
                  value={pasted}
                  placeholder="http://127.0.0.1/?state=…&code=…"
                  onChange={(e) => setPasted(e.target.value)}
                  autoComplete="off"
                />
              </label>
              <button type="submit" className="btn btn--primary" disabled={busy || !pasted.trim()}>
                Finish signing in
              </button>
            </form>
          )}
        </div>
      ) : (
        <form onSubmit={savePasswordSettings}>
          <label>
            <span>SMTP host</span>
            <input name="smtpHost" value={host} onChange={(e) => setHost(e.target.value)} />
          </label>
          <label>
            <span>Port</span>
            <input name="smtpPort" type="number" min={1} max={65535} value={port} onChange={(e) => setPort(Number(e.target.value))} />
          </label>
          <label>
            <span>Encryption</span>
            <select name="smtpTls" value={tls} onChange={(e) => setTls(Number(e.target.value))}>
              <option value={0}>None</option>
              <option value={1}>STARTTLS</option>
              <option value={2}>SSL on connect</option>
            </select>
          </label>
          <label>
            <span>Username</span>
            <input name="smtpUsername" value={username} onChange={(e) => setUsername(e.target.value)} autoComplete="off" />
          </label>
          <label>
            <span>Password</span>
            <PasswordInput value={password} onChange={(e) => setPassword(e.target.value)} autoComplete="off" />
          </label>
          <p className="muted small">
            {settings.smtpPasswordSet ? 'A password is stored. Leave blank to keep it.' : 'No password stored.'}
          </p>
          <label>
            <span>From address</span>
            <input name="smtpFromAddress" type="email" value={from} onChange={(e) => setFrom(e.target.value)} />
          </label>
          {signedIn && (
            <p className="muted small">
              Saving these stops using the {mail.signIn === MailSignIn.Microsoft ? 'Microsoft' : 'Google'} sign-in.
            </p>
          )}
          <button type="submit" className="btn btn--primary" disabled={busy}>
            Save mail settings
          </button>
        </form>
      )}

      <div className="row-gap mail-test">
        <button
          type="button"
          className="btn btn--ghost"
          disabled={busy || !settings.emailEnabled}
          title={settings.emailEnabled ? undefined : 'Turn on Send email first.'}
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
    </section>
  )
}

