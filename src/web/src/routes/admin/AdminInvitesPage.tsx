import { type FormEvent, useEffect, useState } from 'react'
import { api, ApiError, type Invite } from '../../api/client'

/** Admin → Invites (dev-plan 1.4's management surface). */
export function AdminInvitesPage() {
  const [invites, setInvites] = useState<Invite[] | null>(null)
  const [email, setEmail] = useState('')
  const [days, setDays] = useState(7)
  const [issued, setIssued] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  async function load() {
    setInvites(await api.admin.invites.list())
  }

  useEffect(() => {
    load().catch(() => setError('Could not load invites.'))
  }, [])

  async function create(e: FormEvent) {
    e.preventDefault()
    setBusy(true)
    setError(null)
    try {
      const invite = await api.admin.invites.create({
        email: email.trim() || undefined,
        expiresInDays: days,
      })
      // Built from the current origin: the server is behind a proxy and does
      // not reliably know its own public address.
      setIssued(`${window.location.origin}${invite.path}`)
      setEmail('')
      await load()
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not create an invite.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <>
      <p className="muted small">
        Single-use registration links. The only way to add someone while public
        registration is closed and no email server is configured.
      </p>

      <form className="form-inline" onSubmit={create}>
        <label>
          Email (optional)
          <input
            type="email"
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            placeholder="Binds the invite to one address"
          />
        </label>
        <label>
          Expires in (days)
          <input
            type="number"
            min={1}
            max={90}
            value={days}
            onChange={(e) => setDays(Number(e.target.value))}
          />
        </label>
        <span />
        <button type="submit" className="btn btn--primary" disabled={busy}>
          {busy ? 'Creating…' : 'Create invite'}
        </button>
      </form>

      {error && <p className="alert alert--error">{error}</p>}

      {issued && (
        <div className="admin__notice">
          <p>Invite link, shown once:</p>
          <code className="admin__link">{issued}</code>
          <div className="row-gap">
            <button
              type="button"
              className="btn btn--ghost btn--sm"
              onClick={() => navigator.clipboard.writeText(issued).catch(() => {})}
            >
              Copy
            </button>
            <button type="button" className="btn btn--ghost btn--sm" onClick={() => setIssued(null)}>
              Dismiss
            </button>
          </div>
        </div>
      )}

      <table className="admin-table">
        <thead>
          <tr>
            <th>For</th>
            <th>Status</th>
            <th>Expires</th>
            <th />
          </tr>
        </thead>
        <tbody>
          {invites?.map((i) => (
            <tr key={i.id}>
              <td>{i.email ?? <span className="muted">Anyone</span>}</td>
              <td>
                {i.usedAt
                  ? <span className="badge">used</span>
                  : new Date(i.expiresAt) < new Date()
                    ? <span className="badge badge--danger">expired</span>
                    : 'Unused'}
              </td>
              <td className="muted small">{new Date(i.expiresAt).toLocaleDateString()}</td>
              <td>
                {!i.usedAt && (
                  <button
                    type="button"
                    className="link-btn link-btn--danger"
                    onClick={() => api.admin.invites.revoke(i.id).then(load).catch(() =>
                      setError('Could not revoke that invite.'))}
                  >
                    Revoke
                  </button>
                )}
              </td>
            </tr>
          ))}
          {invites?.length === 0 && (
            <tr><td colSpan={4} className="muted">No invites yet.</td></tr>
          )}
        </tbody>
      </table>
    </>
  )
}
