import type { MailProvider } from '../api/client'

/**
 * The Provider choice at the top of the email settings and the setup
 * wizard's Email step (dev-plan 18.1). Choosing one fills the host, port and
 * encryption; the caller does that in `onChange`, and the fields stay
 * editable. An empty value is Other: today's form, filled in by hand.
 */
export function MailProviderPicker({
  providers, value, onChange, disabled,
}: {
  providers: MailProvider[]
  value: string
  onChange: (provider: MailProvider | null) => void
  disabled?: boolean
}) {
  const mail = providers.filter((p) => p.group === 'mail')
  const services = providers.filter((p) => p.group === 'service')
  return (
    <label>
      <span>Provider</span>
      <select
        value={value}
        disabled={disabled}
        onChange={(e) => onChange(providers.find((p) => p.id === e.target.value) ?? null)}
      >
        <option value="">Other (fill in the server yourself)</option>
        {mail.length > 0 && (
          <optgroup label="Email accounts">
            {mail.map((p) => <option key={p.id} value={p.id}>{p.name}</option>)}
          </optgroup>
        )}
        {services.length > 0 && (
          <optgroup label="Sending services">
            {services.map((p) => <option key={p.id} value={p.id}>{p.name}</option>)}
          </optgroup>
        )}
      </select>
    </label>
  )
}

/** What the chosen provider wants in the username and password, and where the steps are. */
export function MailProviderHint({ provider, signingIn }: { provider: MailProvider; signingIn?: boolean }) {
  return (
    <div className="mail-hint">
      {!signingIn && (
        <ul>
          <li><strong>Username:</strong> {provider.username}.</li>
          <li><strong>Password:</strong> {provider.password}.</li>
        </ul>
      )}
      {provider.note && <p>{provider.note}</p>}
      <p>
        Step by step: <em>{provider.supportPage}</em>, in the Tesria docs.
      </p>
    </div>
  )
}
