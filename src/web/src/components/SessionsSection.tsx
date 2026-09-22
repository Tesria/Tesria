import { useCallback, useEffect, useState } from 'react'
import { api, ApiError, type Session } from '../api/client'

/** Profile → Sessions (dev-plan 3.5): every signed-in browser, with revoke. */
export function SessionsSection() {
  const [sessions, setSessions] = useState<Session[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const load = useCallback(() => {
    api.auth.sessions.list().then(setSessions).catch(() => setError('Could not load sessions.'))
  }, [])
  useEffect(load, [load])

  async function revoke(work: () => Promise<void>) {
    setBusy(true)
    setError(null)
    try {
      await work()
      load()
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not sign that session out.')
    } finally {
      setBusy(false)
    }
  }

  if (!sessions) return <p className="muted small">{error ?? 'Loading…'}</p>
  const live = sessions.filter((s) => !s.revokedAt)

  return (
    <>
      <p className="muted small">
        Every browser signed in to this account. Signing one out takes effect on its
        next request. Sessions end on their own after two weeks unused, and after
        ninety days regardless.
      </p>
      {error && <p className="alert alert--error">{error}</p>}
      <table className="admin-table">
        <thead><tr><th>Where</th><th>Last active</th><th>Signed in</th><th></th></tr></thead>
        <tbody>
          {sessions.map((s) => (
            <tr key={s.id} className={s.revokedAt ? 'muted' : undefined}>
              <td>
                <code>{s.ip ?? '–'}</code>
                {s.current && <span className="badge">this browser</span>}
                {s.revokedAt && <span className="badge">signed out</span>}
                <br />
                <span className="muted small">{describeAgent(s.userAgent)}</span>
              </td>
              <td className="muted small">{new Date(s.lastSeenAt).toLocaleString()}</td>
              <td className="muted small">{new Date(s.createdAt).toLocaleDateString()}</td>
              <td className="admin-table__actions">
                {!s.revokedAt && !s.current && (
                  <button type="button" className="link-btn" disabled={busy}
                    onClick={() => revoke(() => api.auth.sessions.revoke(s.id))}>
                    Sign out
                  </button>
                )}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
      {live.length > 1 && (
        <button type="button" className="btn btn--ghost" disabled={busy}
          onClick={() => revoke(() => api.auth.sessions.revokeOthers())}>
          Sign out all other sessions
        </button>
      )}
    </>
  )
}

/** A readable summary of a User-Agent string, without a parsing library. */
function describeAgent(ua: string | null): string {
  if (!ua) return 'Unknown browser'
  const browser = /Edg\//.test(ua) ? 'Edge' : /OPR\//.test(ua) ? 'Opera' : /Chrome\//.test(ua) ? 'Chrome'
    : /Firefox\//.test(ua) ? 'Firefox' : /Safari\//.test(ua) ? 'Safari' : 'Browser'
  const os = /iPhone|iPad/.test(ua) ? 'iOS' : /Android/.test(ua) ? 'Android' : /Mac OS X/.test(ua) ? 'macOS'
    : /Windows/.test(ua) ? 'Windows' : /Linux/.test(ua) ? 'Linux' : ''
  return os ? `${browser} on ${os}` : browser
}
