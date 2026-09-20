import { useCallback, useEffect, useMemo, useState } from 'react'
import {
  api,
  ApiError,
  UserRole,
  type InstancePermissionDto,
  type InstanceRole,
  type PermissionMatrix,
} from '../../api/client'
import { useAuth } from '../../auth/AuthContext'

/** Draft state: role id to the set of keys it would hold after Save. */
type Draft = Record<string, Set<string>>

function toDraft(roles: InstanceRole[]): Draft {
  return Object.fromEntries(roles.map((r) => [r.id, new Set(r.permissions)]))
}

function changes(role: InstanceRole, draft: Set<string>): { added: string[]; removed: string[] } {
  const held = new Set(role.permissions)
  return {
    added: [...draft].filter((k) => !held.has(k)).sort(),
    removed: [...held].filter((k) => !draft.has(k)).sort(),
  }
}

function label(catalogue: InstancePermissionDto[], key: string): string {
  return catalogue.find((p) => p.key === key)?.label ?? key
}

/** Admin → Roles (dev-plan 11.1): what each role on this instance may do. */
export function AdminRolesPage() {
  const { can, refresh } = useAuth()
  const [matrix, setMatrix] = useState<PermissionMatrix | null>(null)
  const [draft, setDraft] = useState<Draft>({})
  const [error, setError] = useState<string | null>(null)
  const [status, setStatus] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [reviewing, setReviewing] = useState(false)

  const load = useCallback(() => {
    api.admin.roles
      .matrix()
      .then((m) => {
        setMatrix(m)
        setDraft(toDraft(m.roles))
      })
      .catch((err: unknown) => setError(err instanceof ApiError ? err.message : 'Could not load roles.'))
  }, [])

  useEffect(load, [load])

  const pending = useMemo(
    () =>
      (matrix?.roles ?? [])
        .map((role) => ({ role, ...changes(role, draft[role.id] ?? new Set()) }))
        .filter((c) => c.added.length > 0 || c.removed.length > 0),
    [matrix, draft],
  )

  function toggle(role: InstanceRole, key: string) {
    setDraft((d) => {
      const next = new Set(d[role.id])
      if (next.has(key)) next.delete(key)
      else next.add(key)
      return { ...d, [role.id]: next }
    })
  }

  async function run<T>(work: () => Promise<T>, done: string, failure: string) {
    setBusy(true)
    setError(null)
    setStatus(null)
    try {
      await work()
      setStatus(done)
      load()
      // Rights may have changed for the person doing this; the nav and the
      // tabs read them from the session.
      await refresh()
    } catch (err) {
      setError(err instanceof ApiError ? err.message : failure)
    } finally {
      setBusy(false)
    }
  }

  async function save() {
    await run(
      () => Promise.all(pending.map((c) => api.admin.roles.savePermissions(c.role.id, [...draft[c.role.id]]))),
      'Roles saved.',
      'Could not save the roles.',
    )
    setReviewing(false)
  }

  if (!matrix) return <p className="muted">{error ?? 'Loading…'}</p>

  const areas = [...new Set(matrix.catalogue.map((p) => p.area))]
  const neverReviewed = matrix.reviewedAt === null

  return (
    <>
      {error && <p className="alert alert--error">{error}</p>}
      {status && <p className="profile__ok">{status}</p>}

      {neverReviewed && (
        <div className="admin__notice">
          <p>
            These are the starting defaults. One of them changed when this instance upgraded:{' '}
            <strong>users can no longer delete pages other people created</strong>, only their own.
            Review the matrix and save, or keep it as it is.
          </p>
          <button
            type="button"
            className="btn btn--ghost btn--sm"
            disabled={busy}
            onClick={() => run(() => api.admin.roles.review(), 'Defaults kept.', 'Could not record that.')}
          >
            Keep these defaults
          </button>
        </div>
      )}

      <p className="muted small">
        A role says what someone may do at all. Where they may do it is still the space's own permissions:
        a right to delete other people's pages does not reach a space they cannot edit.
        {matrix.reviewedAt && (
          <> Last reviewed {new Date(matrix.reviewedAt).toLocaleString()}
            {matrix.reviewedByName ? ` by ${matrix.reviewedByName}` : ''}.</>
        )}
      </p>

      <div className="profile__section profile__section--wide">
        <table className="admin-table roles-table">
          <thead>
            <tr>
              <th>Right</th>
              {matrix.roles.map((role) => (
                <th key={role.id} className="roles-table__role">
                  {role.name}
                  <div className="muted small">
                    {/* The tier only when it is not already the name: a custom
                        role (11.2) needs it, a built-in would just repeat. */}
                    {!role.builtIn && (
                      <>{role.tier === UserRole.Owner ? 'Owner' : role.tier === UserRole.Admin ? 'Administrator' : 'User'}{' · '}</>
                    )}
                    {role.members} {role.members === 1 ? 'account' : 'accounts'}
                  </div>
                  {!role.editable && <div className="muted small">Only the owner edits this role</div>}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {areas.map((area) => (
              <RoleArea
                key={area}
                area={area}
                permissions={matrix.catalogue.filter((p) => p.area === area)}
                roles={matrix.roles}
                draft={draft}
                busy={busy}
                onToggle={toggle}
              />
            ))}
            <tr className="roles-table__area">
              <th colSpan={matrix.roles.length + 1}>Always the owner</th>
            </tr>
            {matrix.reserved.map((p) => (
              <tr key={p.key}>
                <td>
                  <strong>{p.label}</strong>
                  <div className="muted small">{p.description}</div>
                </td>
                {matrix.roles.map((role) => (
                  <td key={role.id} className="roles-table__cell">
                    {role.tier === UserRole.Owner ? <span title="Always held by the owner">Yes</span> : ''}
                  </td>
                ))}
              </tr>
            ))}
          </tbody>
        </table>

        <div className="roles-actions">
          <button type="button" className="btn btn--primary" disabled={busy || pending.length === 0}
            onClick={() => setReviewing(true)}>
            Review changes
          </button>
          {pending.length > 0 && (
            <button type="button" className="btn btn--ghost" disabled={busy}
              onClick={() => setDraft(toDraft(matrix.roles))}>
              Discard
            </button>
          )}
          {matrix.roles.filter((r) => r.editable).map((role) => (
            <button key={role.id} type="button" className="link-btn" disabled={busy}
              onClick={() => {
                if (!window.confirm(`Reset ${role.name} to the defaults?`)) return
                void run(() => api.admin.roles.reset(role.id), `${role.name} reset.`, 'Could not reset the role.')
              }}>
              Reset {role.name}
            </button>
          ))}
        </div>
      </div>

      {reviewing && (
        <div className="backup-preview" role="dialog" aria-label="Confirm the role changes">
          <h3>These changes take effect at once</h3>
          {pending.map(({ role, added, removed }) => (
            <div key={role.id} className="backup-preview__agent">
              <p><strong>{role.name}</strong> ({role.members} {role.members === 1 ? 'account' : 'accounts'})</p>
              {added.length > 0 && (
                <p className="small">Gains: {added.map((k) => label(matrix.catalogue, k)).join(', ')}</p>
              )}
              {removed.length > 0 && (
                <p className="small">Loses: {removed.map((k) => label(matrix.catalogue, k)).join(', ')}</p>
              )}
            </div>
          ))}
          {pending.some((c) => c.added.length > 0) && (
            <p className="alert alert--error small">
              A role that gains rights is announced to every administrator.
            </p>
          )}
          <div className="backup-preview__actions">
            <button type="button" className="btn btn--primary" disabled={busy} onClick={save}>Save roles</button>
            <button type="button" className="btn btn--ghost" disabled={busy} onClick={() => setReviewing(false)}>
              Cancel
            </button>
          </div>
        </div>
      )}

      {!can('permissions.edit_user_tier') && !can('permissions.edit_admin_tier') && (
        <p className="muted small">You can see this matrix but not change it.</p>
      )}
    </>
  )
}

function RoleArea({
  area, permissions, roles, draft, busy, onToggle,
}: {
  area: string
  permissions: InstancePermissionDto[]
  roles: InstanceRole[]
  draft: Draft
  busy: boolean
  onToggle: (role: InstanceRole, key: string) => void
}) {
  return (
    <>
      <tr className="roles-table__area">
        <th colSpan={roles.length + 1}>{area}</th>
      </tr>
      {permissions.map((p) => (
        <tr key={p.key}>
          <td>
            <strong>{p.label}</strong>
            <div className="muted small">{p.description}</div>
          </td>
          {roles.map((role) => (
            <td key={role.id} className="roles-table__cell">
              <input
                type="checkbox"
                aria-label={`${p.label} for ${role.name}`}
                checked={draft[role.id]?.has(p.key) ?? false}
                disabled={busy || !role.editable}
                onChange={() => onToggle(role, p.key)}
              />
            </td>
          ))}
        </tr>
      ))}
    </>
  )
}
