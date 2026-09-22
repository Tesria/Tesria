import { useCallback, useEffect, useState } from 'react'
import {
  api, ApiError, PrincipalType, pageOperationName, type PageRestriction,
} from '../../api/client'
import { PrincipalPicker } from '../../components/PrincipalPicker'

/** Manages who may view/edit a single page. Restrictions inherit to sub-pages. */
export function RestrictionsPanel({ pageId }: { pageId: string }) {
  const [rows, setRows] = useState<PageRestriction[] | null>(null)
  const [error, setError] = useState<string | null>(null)

  const load = useCallback(() => {
    setError(null)
    api.pageRestrictions
      .list(pageId)
      .then(setRows)
      .catch((err: unknown) => setError(err instanceof Error ? err.message : 'Failed to load restrictions.'))
  }, [pageId])

  useEffect(load, [load])

  async function remove(row: PageRestriction) {
    setError(null)
    try {
      await api.pageRestrictions.remove(pageId, row.id)
      load()
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not remove the restriction.')
    }
  }

  const unrestricted = rows !== null && rows.length === 0

  return (
    <div className="restrictions">
      {error && <p className="alert alert--error">{error}</p>}
      {unrestricted ? (
        <p className="muted small">
          This page inherits access from its space and any restricted ancestor. Adding a restriction
          limits it to the principals listed: you will keep access automatically. Sub-pages inherit
          whatever you set here.
        </p>
      ) : (
        <p className="muted small">
          Only these principals can access this page and its sub-pages (space admins always can).
        </p>
      )}

      <PrincipalPicker
        operationNames={pageOperationName}
        addLabel="Restrict"
        onAdd={async (input) => {
          await api.pageRestrictions.add(pageId, input)
          load()
        }}
      />

      <ul className="version-list">
        {rows?.map((r) => (
          <li key={r.id} className="version">
            <span className="badge">{r.principalType === PrincipalType.User ? 'user' : 'group'}</span>
            <span className="version__num">{r.principalName ?? r.principalId}</span>
            <span className="muted small">{pageOperationName[r.operation]}</span>
            <span className="version__actions">
              <button type="button" className="link-btn link-btn--danger" onClick={() => remove(r)}>
                Remove
              </button>
            </span>
          </li>
        ))}
      </ul>
    </div>
  )
}
