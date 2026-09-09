import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { api, ApiError, type AdminSpace } from '../../api/client'

/** Formats bytes for humans; storage figures are the point of this page. */
function bytes(value: number): string {
  if (value === 0) return '—'
  const units = ['B', 'KB', 'MB', 'GB', 'TB']
  const i = Math.min(Math.floor(Math.log(value) / Math.log(1024)), units.length - 1)
  return `${(value / 1024 ** i).toFixed(i === 0 ? 0 : 1)} ${units[i]}`
}

/**
 * Admin → Spaces (dev-plan 2.4).
 *
 * Metadata only, deliberately. Admins do not bypass space permissions, so this
 * shows ownership, size and counts — never content. To read a space they hold
 * no grant for, an admin uses recover-access, which is audited.
 */
export function AdminSpacesPage() {
  const [spaces, setSpaces] = useState<AdminSpace[] | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    api.admin.spaces
      .list()
      .then(setSpaces)
      .catch((err: unknown) =>
        setError(err instanceof ApiError ? err.message : 'Could not load spaces.'))
  }, [])

  if (error) return <p className="alert alert--error">{error}</p>
  if (!spaces) return <p className="muted">Loading…</p>

  return (
    <table className="admin-table">
      <thead>
        <tr>
          <th>Space</th>
          <th>Owner</th>
          <th>Pages</th>
          <th>Storage</th>
          <th>Created</th>
        </tr>
      </thead>
      <tbody>
        {spaces.map((s) => (
          <tr key={s.id}>
            <td>
              <Link to={`/spaces/${s.key}`}>
                <strong>{s.name}</strong>
              </Link>{' '}
              <span className="badge">{s.key}</span>
              {s.archived && <span className="badge">archived</span>}
            </td>
            <td className="muted small">{s.createdByName}</td>
            <td>{s.pageCount}</td>
            <td>{bytes(s.storageBytes)}</td>
            <td className="muted small">{new Date(s.createdAt).toLocaleDateString()}</td>
          </tr>
        ))}
      </tbody>
    </table>
  )
}
