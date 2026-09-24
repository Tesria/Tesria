import { type FormEvent, useCallback, useEffect, useState } from 'react'
import {
  AlertStatus,
  api,
  ApiError,
  SecuritySeverity,
  type AuditChainReport,
  type BlockedNetwork,
  type SecurityAlert,
  type SecurityEvent,
  type SecurityLimits,
  type SecurityOverview,
  UserStatus,
} from '../../api/client'
import { ALERT_KIND_LABEL } from './alertKinds'
import { useConfirm } from '../../components/ConfirmDialog'

const SEVERITY_LABEL: Record<SecuritySeverity, string> = { 0: 'info', 1: 'warning', 2: 'critical' }

function meta(json: string | null): Record<string, unknown> {
  try {
    return json ? (JSON.parse(json) as Record<string, unknown>) : {}
  } catch {
    return {}
  }
}

function Severity({ level }: { level: SecuritySeverity }) {
  const cls = level === SecuritySeverity.Critical ? 'badge badge--danger' : level === SecuritySeverity.Warning ? 'badge badge--warn' : 'badge'
  return <span className={cls}>{SEVERITY_LABEL[level]}</span>
}

const LIMIT_FIELDS: Array<{ key: keyof Omit<SecurityLimits, 'activeLockouts'>; label: string; hint: string }> = [
  { key: 'loginRateLimitPerMinute', label: 'Credential attempts per address per minute',
    hint: 'Sign-in, registration and recovery share this budget. Raise it if many users sit behind one NAT.' },
  { key: 'anonymousRateLimitPerMinute', label: 'Anonymous requests per address per minute',
    hint: 'Applies to callers with no session. Signed-in users are not limited this way.' },
  { key: 'tokenMintLimitPerHour', label: 'API tokens per account per hour', hint: '' },
  { key: 'lockoutThreshold', label: 'Failed sign-ins before lockout', hint: '' },
  { key: 'lockoutBaseSeconds', label: 'First lockout (seconds)',
    hint: 'Doubles with each further failure, up to the maximum. Never permanent.' },
  { key: 'lockoutMaxSeconds', label: 'Maximum lockout (seconds)', hint: '' },
]

/** Admin → Security (dev-plan 3.2; 3.3 adds events, alerts and mitigations). */
export function AdminSecurityPage() {
  const { ask, dialog } = useConfirm()
  const [limits, setLimits] = useState<SecurityLimits | null>(null)
  const [overview, setOverview] = useState<SecurityOverview | null>(null)
  const [alerts, setAlerts] = useState<SecurityAlert[]>([])
  const [showResolved, setShowResolved] = useState(false)
  const [events, setEvents] = useState<SecurityEvent[]>([])
  const [blocks, setBlocks] = useState<BlockedNetwork[]>([])
  const [report, setReport] = useState<AuditChainReport | null>(null)
  const [status, setStatus] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  // Which alert is being resolved, if any. The note used to come from
  // window.prompt, which throws where a browser refuses dialogs (the in-app
  // browser always, Chrome once someone ticks "prevent additional dialogs"),
  // so the click died before it ever called the API and Resolve looked dead.
  const [resolving, setResolving] = useState<string | null>(null)

  const load = useCallback(() => {
    Promise.all([
      api.admin.security.limits(),
      api.admin.security.overview(),
      api.admin.security.alerts(showResolved ? 'all' : 'open'),
      api.admin.security.events(100),
      api.admin.security.blocks.list(),
    ])
      .then(([l, o, a, e, b]) => {
        setLimits(l)
        setOverview(o)
        setAlerts(a)
        setEvents(e)
        setBlocks(b)
      })
      .catch(() => setError('Could not load security settings.'))
  }, [showResolved])

  useEffect(load, [load])

  async function run<T>(work: () => Promise<T>, done: string | ((result: T) => string), failure: string) {
    setBusy(true)
    setError(null)
    setStatus(null)
    try {
      const result = await work()
      setStatus(typeof done === 'function' ? done(result) : done)
    } catch (err) {
      setError(err instanceof ApiError ? err.message : failure)
    } finally {
      setBusy(false)
    }
  }

  async function saveLimits(e: FormEvent) {
    e.preventDefault()
    const form = new FormData(e.currentTarget as HTMLFormElement)
    const input = Object.fromEntries(LIMIT_FIELDS.map((f) => [f.key, Number(form.get(f.key))]))
    await run(() => api.admin.settings.update(input as never), 'Limits saved.', 'Could not save the limits.')
    load()
  }

  async function unlock(userId: string) {
    await run(() => api.admin.users.unlock(userId), 'Lockout cleared.', 'Could not clear the lockout.')
    load()
  }

  function verify() {
    return run(
      async () => {
        const r = await api.admin.security.verifyAuditChain()
        setReport(r)
        return r
      },
      (r) => (r.ok ? `Audit chain intact: ${r.checked} rows.` : 'Audit chain BROKEN: see below.'),
      'Could not verify the audit chain.',
    )
  }

  async function act(work: () => Promise<unknown>, done: string, failure: string) {
    await run(work, done, failure)
    load()
  }

  async function addBlock(e: FormEvent) {
    e.preventDefault()
    const form = e.currentTarget as HTMLFormElement
    const data = new FormData(form)
    const hours = Number(data.get('expiresInHours'))
    await act(
      () => api.admin.security.blocks.add({
        cidr: String(data.get('cidr') ?? ''),
        reason: String(data.get('reason') ?? '') || undefined,
        expiresInHours: hours > 0 ? hours : undefined,
      }),
      'Address blocked.',
      'Could not block that address.',
    )
    form.reset()
  }

  /** The mitigations relevant to an alert's key, each asked first (dev-plan 15.4). */
  function mitigations(a: SecurityAlert) {
    const actions: Array<{ label: string; run: () => Promise<unknown>; done: string; ask: string }> = []
    if (a.ip) {
      actions.push({
        label: `Block ${a.ip}`,
        run: () => api.admin.security.blocks.add({ cidr: a.ip!, reason: `alert: ${a.kind}`, expiresInHours: 24 }),
        done: `${a.ip} blocked for 24 hours.`,
        ask: `Every request from ${a.ip} is refused for 24 hours, whoever it is.`,
      })
    }
    // For account-keyed alerts the key is the user id; the actor may be the
    // person who did it (promotion) or the account affected (new address).
    const userId = a.kind.startsWith('account.') || a.kind === 'login.admin_new_address' || a.kind === 'token.minting_burst' || a.kind === 'content.mass_removal'
      ? a.key
      : a.kind === 'admin.promoted' ? a.key : null
    if (userId) {
      actions.push(
        { label: 'Sign out everywhere', run: () => api.admin.users.revokeSessions(userId), done: 'Sessions revoked.',
          ask: 'Every browser signed in to this account is signed out. They can sign in again.' },
        { label: 'Revoke tokens', run: () => api.admin.users.revokeTokens(userId), done: 'Tokens revoked.',
          ask: 'Every API token of this account is deleted; scripts and assistants using them stop working.' },
        { label: 'Suspend', run: () => api.admin.users.setStatus(userId, UserStatus.Suspended), done: 'Account suspended.',
          ask: 'The account is signed out and cannot sign in or use its tokens until someone reactivates it.' },
      )
    }
    return actions
  }

  if (!limits || !overview) return <p className="muted">{error ?? 'Loading…'}</p>

  return (
    <>
      {error && <p className="alert alert--error">{error}</p>}
      {status && <p className="profile__ok">{status}</p>}

      <div className="dash__grid">
        <div className="stat">
          <p className="stat__label">Open alerts</p>
          <p className="stat__value">{overview.openAlerts}</p>
          {overview.criticalOpen > 0 && <p className="alert alert--error small">{overview.criticalOpen} critical</p>}
        </div>
        <div className="stat">
          <p className="stat__label">Events (24h)</p>
          <p className="stat__value">{overview.eventsLast24h}</p>
        </div>
        <div className="stat">
          <p className="stat__label">Blocked networks</p>
          <p className="stat__value">{overview.blockedNetworks}</p>
          <p className="muted small">{overview.blockedHits} requests refused since start</p>
        </div>
        <div className="stat">
          <p className="stat__label">Exposure</p>
          <p className="stat__value">{overview.allowPublicSpaces ? 'Public' : 'Private'}</p>
          <p className="muted small">Registration {overview.allowPublicRegistration ? 'open' : 'by invite'}</p>
        </div>
      </div>

      <section className="profile__section profile__section--wide">
        <h2>Alerts</h2>
        <label className="admin__toggle admin__toggle--inline">
          <input type="checkbox" checked={showResolved} onChange={(e) => setShowResolved(e.target.checked)} />
          <span>Show resolved</span>
        </label>
        {alerts.length === 0 ? (
          <p className="muted small">Nothing needs attention.</p>
        ) : (
          <ul className="alerts">
            {alerts.map((a) => (
              <li key={a.id} className={`alerts__item alerts__item--${SEVERITY_LABEL[a.severity]}`}>
                <div className="alerts__head">
                  <Severity level={a.severity} />
                  <strong>{ALERT_KIND_LABEL[a.kind] ?? a.kind}</strong>
                  <span className="muted small">{new Date(a.createdAt).toLocaleString()}</span>
                  {a.status !== AlertStatus.Open && (
                    <span className="badge">{a.status === AlertStatus.Resolved ? 'resolved' : 'acknowledged'}</span>
                  )}
                </div>
                <p className="muted small">
                  {/* Joined, so an alert with no details does not end in a
                      separator ("by Sam Okafor ·", found 2026-09-24). */}
                  {[
                    a.ip && <>address <code>{a.ip}</code></>,
                    a.actorName && <>by {a.actorName}</>,
                    ...Object.entries(meta(a.metadataJson)).map(([k, v]) => `${k}: ${String(v)}`),
                    a.note && <>note: “{a.note}”</>,
                  ].filter(Boolean).map((part, i) => <span key={i}>{i > 0 && ' · '}{part}</span>)}
                </p>
                {a.status !== AlertStatus.Resolved && (
                  <div className="alerts__actions">
                    {a.status === AlertStatus.Open && (
                      <button type="button" className="btn btn--ghost btn--sm" disabled={busy}
                        onClick={() => act(() => api.admin.security.acknowledge(a.id), 'Acknowledged.', 'Could not acknowledge.')}>
                        Acknowledge
                      </button>
                    )}
                    {resolving === a.id ? (
                      <form
                        className="alerts__resolve"
                        onSubmit={(e) => {
                          e.preventDefault()
                          const note = new FormData(e.currentTarget).get('note')?.toString().trim()
                          setResolving(null)
                          void act(
                            () => api.admin.security.resolve(a.id, note || undefined),
                            'Resolved.', 'Could not resolve.',
                          )
                        }}
                      >
                        <input name="note" placeholder="Resolution note (optional)" autoFocus
                          onKeyDown={(e) => e.key === 'Escape' && setResolving(null)} />
                        <button type="submit" className="btn btn--ghost btn--sm" disabled={busy}>Resolve</button>
                        <button type="button" className="btn btn--ghost btn--sm" disabled={busy}
                          onClick={() => setResolving(null)}>
                          Cancel
                        </button>
                      </form>
                    ) : (
                      <button type="button" className="btn btn--ghost btn--sm" disabled={busy}
                        onClick={() => setResolving(a.id)}>
                        Resolve
                      </button>
                    )}
                    {mitigations(a).map((m) => (
                      <button key={m.label} type="button" className="btn btn--ghost btn--sm" disabled={busy}
                        onClick={async () => {
                          if (await ask({ title: `${m.label}?`, confirmLabel: m.label, danger: true, body: <p>{m.ask}</p> }))
                            await act(m.run, m.done, 'The action failed.')
                        }}>
                        {m.label}
                      </button>
                    ))}
                  </div>
                )}
              </li>
            ))}
          </ul>
        )}
      </section>

      <section className="profile__section profile__section--wide">
        <h2>Kill switches</h2>
        <p className="muted small">Instance-wide, immediate, audited. Each is also on the Settings page.</p>
        <label className="admin__toggle">
          <input type="checkbox" checked={overview.allowPublicSpaces} disabled={busy}
            onChange={(e) => act(() => api.admin.settings.update({ allowPublicSpaces: e.target.checked } as never),
              e.target.checked ? 'Public spaces enabled.' : 'All public spaces disabled.', 'Could not change the setting.')} />
          <span><strong>Allow public spaces</strong><br />
            <span className="muted small">Off hides every public space from anonymous readers at once.</span></span>
        </label>
        <label className="admin__toggle">
          <input type="checkbox" checked={overview.allowPublicRegistration} disabled={busy}
            onChange={(e) => act(() => api.admin.settings.update({ allowPublicRegistration: e.target.checked } as never),
              e.target.checked ? 'Registration opened.' : 'Registration closed.', 'Could not change the setting.')} />
          <span><strong>Allow public registration</strong><br />
            <span className="muted small">Off means new accounts need an invite link.</span></span>
        </label>
        <label className="admin__toggle">
          <input type="checkbox" checked={overview.requireTotpForAdmins} disabled={busy}
            onChange={(e) => act(() => api.admin.settings.update({ requireTotpForAdmins: e.target.checked } as never),
              e.target.checked ? 'Administrators must use two-factor sign-in.' : 'Two-factor no longer required.', 'Could not change the setting.')} />
          <span><strong>Require two-factor for administrators</strong><br />
            <span className="muted small">An administrator without two-factor sign-in sees the setup page instead of the admin tabs until they turn it on.</span></span>
        </label>
      </section>

      <section className="profile__section profile__section--wide">
        <h2>Blocked networks</h2>
        <form onSubmit={addBlock} className="block-form">
          <input name="cidr" placeholder="203.0.113.7 or 203.0.113.0/24" required />
          <input name="reason" placeholder="Reason (optional)" />
          <input name="expiresInHours" type="number" min={0} placeholder="Hours (blank = until removed)" />
          <button type="submit" className="btn btn--primary" disabled={busy}>Block</button>
        </form>
        {blocks.length === 0 ? (
          <p className="muted small">No addresses are blocked.</p>
        ) : (
          <table className="admin-table">
            <thead><tr><th>Range</th><th>Reason</th><th>Expires</th><th>Added</th><th></th></tr></thead>
            <tbody>
              {blocks.map((b) => (
                <tr key={b.id}>
                  <td><code>{b.cidr}</code></td>
                  <td>{b.reason ?? <span className="muted">–</span>}</td>
                  <td>{b.expiresAt ? new Date(b.expiresAt).toLocaleString() : 'Never'}</td>
                  <td className="muted small">{b.createdByName ?? 'system'} · {new Date(b.createdAt).toLocaleDateString()}</td>
                  <td>
                    <div className="admin-table__actions">
                      <button type="button" className="link-btn" disabled={busy}
                        onClick={async () => {
                          if (await ask({ title: `Unblock ${b.cidr}?`, confirmLabel: 'Unblock',
                            body: <p>Requests from {b.cidr} are accepted again straight away.</p> }))
                            await act(() => api.admin.security.blocks.remove(b.id), 'Unblocked.', 'Could not unblock.')
                        }}>
                        Remove
                      </button>
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </section>

      <section className="profile__section profile__section--wide">
        <h2>Recent events</h2>
        {events.length === 0 ? (
          <p className="muted small">No security events recorded.</p>
        ) : (
          <table className="admin-table">
            <thead><tr><th>When</th><th>Severity</th><th>What</th><th>Address</th><th>Who</th></tr></thead>
            <tbody>
              {events.map((e) => (
                <tr key={e.id}>
                  <td className="muted small nowrap">{new Date(e.createdAt).toLocaleString()}</td>
                  <td><Severity level={e.severity} /></td>
                  <td>{ALERT_KIND_LABEL[e.kind] ?? e.kind}</td>
                  <td className="nowrap">{e.ip ? <code>{e.ip}</code> : <span className="muted">–</span>}</td>
                  <td>{e.actorName ?? <span className="muted">–</span>}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </section>

      <section className="profile__section profile__section--wide">
        <h2>Brute-force protection</h2>
        <form onSubmit={saveLimits} className="limits-form">
          {LIMIT_FIELDS.map((f) => (
            <label key={f.key}>
              {f.label}
              <input name={f.key} type="number" min={1} defaultValue={limits[f.key]} required />
              {f.hint && <span className="muted small">{f.hint}</span>}
            </label>
          ))}
          <button type="submit" className="btn btn--primary" disabled={busy}>Save limits</button>
        </form>
      </section>

      <section className="profile__section profile__section--wide">
        <h2>Active lockouts</h2>
        {limits.activeLockouts.length === 0 ? (
          <p className="muted small">No accounts are locked out.</p>
        ) : (
          <table className="admin-table">
            <thead>
              <tr><th>Account</th><th>Failures</th><th>Locked until</th><th></th></tr>
            </thead>
            <tbody>
              {limits.activeLockouts.map((l) => (
                <tr key={l.userId}>
                  <td><strong>{l.displayName}</strong><br /><span className="muted small">{l.email}</span></td>
                  <td>{l.failedLoginCount}</td>
                  <td>{new Date(l.lockedUntil).toLocaleTimeString()}</td>
                  <td>
                    <div className="admin-table__actions">
                      <button type="button" className="link-btn" disabled={busy} onClick={() => unlock(l.userId)}>
                        Unlock
                      </button>
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </section>

      <section className="profile__section profile__section--wide">
        <h2>Audit log integrity</h2>
        <p className="muted small">
          Every audit entry is linked to the one before it by a hash. Verifying walks
          the whole chain and names the first entry that was altered or removed. It
          also runs automatically once a day.
        </p>
        <button type="button" className="btn btn--ghost" disabled={busy} onClick={verify}>
          {busy ? 'Verifying…' : 'Verify now'}
        </button>
        {report && (
          <p className={report.ok ? 'muted small' : 'alert alert--error'}>
            {report.ok
              ? `${report.checked} entries verified at ${new Date(report.verifiedAt).toLocaleTimeString()}.`
              : `Broken at entry ${report.brokenAtSequence}: ${report.problem}`}
            {report.unchained > 0 && ` ${report.unchained} entries predate the chain and are unverified.`}
          </p>
        )}
      </section>
      {dialog}
    </>
  )
}
