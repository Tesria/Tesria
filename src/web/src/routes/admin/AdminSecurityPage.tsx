import { type FormEvent, useCallback, useEffect, useState } from 'react'
import { api, ApiError, type AuditChainReport, type SecurityLimits } from '../../api/client'

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
  const [limits, setLimits] = useState<SecurityLimits | null>(null)
  const [report, setReport] = useState<AuditChainReport | null>(null)
  const [status, setStatus] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const load = useCallback(() => {
    api.admin.security
      .limits()
      .then(setLimits)
      .catch(() => setError('Could not load security settings.'))
  }, [])

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
      (r) => (r.ok ? `Audit chain intact: ${r.checked} rows.` : 'Audit chain BROKEN — see below.'),
      'Could not verify the audit chain.',
    )
  }

  if (!limits) return <p className="muted">{error ?? 'Loading…'}</p>

  return (
    <>
      {error && <p className="alert alert--error">{error}</p>}
      {status && <p className="profile__ok">{status}</p>}

      <section className="profile__section">
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

      <section className="profile__section">
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
                  <td className="admin-table__actions">
                    <button type="button" className="link-btn" disabled={busy} onClick={() => unlock(l.userId)}>
                      Unlock
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </section>

      <section className="profile__section">
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
    </>
  )
}
