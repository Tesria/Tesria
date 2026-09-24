import { useCallback, useEffect, useState } from 'react'
import {
  api, ApiError, PrincipalType, spaceOperationName, type SpacePermission,
} from '../api/client'
import { PrincipalPicker } from './PrincipalPicker'
import { useConfirm } from './ConfirmDialog'

/**
 * A space's grants: who may view, edit and administer it. The Permissions
 * tab of space settings, and the step that follows importing a wiki pack
 * (dev-plan 15.1), which is why it takes a key rather than the space route's
 * context.
 */
export function SpaceAccessEditor({ spaceKey }: { spaceKey: string }) {
  const space = { key: spaceKey }
  const [rows, setRows] = useState<SpacePermission[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [forbidden, setForbidden] = useState(false)
  const { ask, dialog } = useConfirm()

  // The last-admin rule keeps a private space from being emptied grant by
  // grant, so opening it again is its own, deliberate step (dev-plan 15.3).
  async function makeOpen() {
    const ok = await ask({
      title: 'Make this space open again?',
      danger: true,
      confirmLabel: 'Make it open',
      body: (
        <p>Every grant below is removed, and everyone signed in can view, edit and administer the space and everything in it. Page restrictions stay. Every administrator is alerted.</p>
      ),
    })
    if (!ok) return
    setError(null)
    try {
      await api.spacePermissions.makeOpen(space.key)
      load()
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not open the space.')
    }
  }

  const load = useCallback(() => {
    setError(null)
    api.spacePermissions
      .list(space.key)
      .then((r) => {
        setRows(r)
        setForbidden(false)
      })
      .catch((err: unknown) => {
        if (err instanceof ApiError && err.status === 403) setForbidden(true)
        else setError(err instanceof Error ? err.message : 'Failed to load permissions.')
      })
  }, [space.key])

  useEffect(load, [load])

  async function revoke(row: SpacePermission) {
    setError(null)
    try {
      await api.spacePermissions.revoke(space.key, row.id)
      load()
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not revoke.')
    }
  }

  if (forbidden) {
    return <p className="alert alert--error">You need admin rights on this space to manage its permissions.</p>
  }

  const isOpen = rows !== null && rows.length === 0

  return (
    <>
      {error && <p className="alert alert--error">{error}</p>}

      {isOpen ? (
        <p className="alert" style={{ background: 'var(--bg-alt)' }}>
          <strong>This space is open.</strong> Every signed-in user can view, edit and administer it. Adding the
          first grant below makes the space private: you will be kept as an admin automatically.
        </p>
      ) : (
        <p className="muted small">
          Only the principals listed below can access this space. Admin implies Edit implies View.{' '}
          <button type="button" className="link-btn" onClick={() => void makeOpen()}>Make this space open again</button>
        </p>
      )}

      <div className="card">
        <PrincipalPicker
          operationNames={spaceOperationName}
          addLabel="Grant"
          onAdd={async (input) => {
            await api.spacePermissions.grant(space.key, input)
            load()
          }}
        />
      </div>

      <ul className="version-list">
        {rows?.map((r) => (
          <li key={r.id} className="version">
            <span className="badge">{r.principalType === PrincipalType.User ? 'user' : 'group'}</span>
            <span className="version__num">{r.principalName ?? r.principalId}</span>
            <span className="muted small">{spaceOperationName[r.operation]}</span>
            <span className="version__actions">
              <button type="button" className="link-btn link-btn--danger" onClick={() => revoke(r)}>
                Revoke
              </button>
            </span>
          </li>
        ))}
      </ul>
      {dialog}
    </>
  )
}
