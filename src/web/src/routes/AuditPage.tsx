import { useEffect, useState } from 'react'
import { api, type AuditEntry } from '../api/client'

/** Recent audit trail: who did what, when. */
export function AuditPage() {
  const [entries, setEntries] = useState<AuditEntry[] | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let cancelled = false
    api
      .audit()
      .then((e) => !cancelled && setEntries(e))
      .catch((err: unknown) => !cancelled && setError(err instanceof Error ? err.message : 'Failed to load.'))
    return () => {
      cancelled = true
    }
  }, [])

  // Rendered inside the admin shell (Admin → Audit).
  return (
    <div>
      <p className="muted small">
        Recent changes across all spaces you can see. Every entry is hash-chained;
        the Security tab verifies the chain.
      </p>
      {error && <p className="alert alert--error">{error}</p>}
      {!entries && !error && <p className="muted">Loading…</p>}
      {entries && entries.length === 0 && <p className="muted">Nothing recorded yet.</p>}
      <ul className="version-list">
        {entries?.map((e) => (
          <li key={e.id} className="version">
            <span className="badge">{e.action}</span>
            <span>{e.actorName ?? 'system'}</span>
            <span className="muted small">{new Date(e.createdAt).toLocaleString()}</span>
            {e.metadataJson && <span className="version__comment">{e.metadataJson}</span>}
          </li>
        ))}
      </ul>
    </div>
  )
}
