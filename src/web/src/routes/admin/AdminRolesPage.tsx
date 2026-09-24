import { type FormEvent, useCallback, useEffect, useMemo, useState } from 'react'
import {
  api,
  ApiError,
  UserRole,
  type InstancePermissionDto,
  type InstanceRole,
  type PermissionMatrix,
} from '../../api/client'
import { useAuth } from '../../auth/AuthContext'
import { useConfirm } from '../../components/ConfirmDialog'

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

function label(catalog: InstancePermissionDto[], key: string): string {
  return catalog.find((p) => p.key === key)?.label ?? key
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
  const [creating, setCreating] = useState(false)
  // Renaming is an inline field, not window.prompt: a browser that refuses
  // dialogs throws there, which is how Resolve on the Security page was dead
  // for days.
  const [renaming, setRenaming] = useState<string | null>(null)
  const { ask, dialog } = useConfirm()

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

  async function create(e: FormEvent) {
    e.preventDefault()
    const form = new FormData(e.currentTarget as HTMLFormElement)
    const copyFrom = String(form.get('copyFrom') ?? '')
    await run(
      () => api.admin.roles.create({
        name: String(form.get('name') ?? '').trim(),
        description: String(form.get('description') ?? '').trim() || undefined,
        tier: Number(form.get('tier')) as UserRole,
        copyFrom: copyFrom || undefined,
      }),
      'Role created.',
      'Could not create the role.',
    )
    setCreating(false)
  }

  async function rename(role: InstanceRole, name: string) {
    setRenaming(null)
    if (!name || name === role.name) return
    await run(() => api.admin.roles.rename(role.id, { name }), 'Role renamed.', 'Could not rename the role.')
  }

  async function remove(role: InstanceRole) {
    const ok = await ask({
      title: `Delete the role ${role.name}?`,
      danger: true,
      confirmLabel: 'Delete the role',
      body: (
        <>
          <p>The rights it holds go with it. Nothing else changes.</p>
          <p>
            {role.members === 0
              ? 'Nobody holds it, so it can go now.'
              : `${role.members} account${role.members === 1 ? '' : 's'} still `
                + `hold${role.members === 1 ? 's' : ''} it. Move them to another role first, `
                + 'or this will be refused.'}
          </p>
        </>
      ),
    })
    if (!ok) return
    await run(() => api.admin.roles.remove(role.id), 'Role deleted.', 'Could not delete the role.')
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

  const areas = [...new Set(matrix.catalog.map((p) => p.area))]
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
        <div className="roles-actions roles-actions--top">
          {(can('permissions.edit_user_tier') || can('permissions.edit_admin_tier')) && (
            <button type="button" className="btn btn--ghost btn--sm" disabled={busy}
              onClick={() => setCreating((v) => !v)}>
              {creating ? 'Cancel' : 'New role'}
            </button>
          )}
          <span className="muted small">
            A role is a set of rights within a tier. Someone's tier still decides who may act on whom.
          </span>
        </div>

        {creating && (
          <form className="roles-new" onSubmit={create}>
            <label>Name<input name="name" required maxLength={60} autoFocus /></label>
            <label>Description<input name="description" maxLength={500} /></label>
            <label>
              Tier
              <select name="tier" defaultValue={String(UserRole.Member)}>
                <option value={String(UserRole.Member)}>User</option>
                {can('permissions.edit_admin_tier') && (
                  <option value={String(UserRole.Admin)}>Administrator</option>
                )}
              </select>
            </label>
            <label>
              Copy rights from
              <select name="copyFrom" defaultValue="">
                <option value="">The tier's built-in role</option>
                {matrix.roles.filter((r) => r.tier < UserRole.Owner).map((r) => (
                  <option key={r.id} value={r.id}>{r.name}</option>
                ))}
              </select>
            </label>
            <button type="submit" className="btn btn--primary" disabled={busy}>Create role</button>
          </form>
        )}

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
                  {role.editable && !role.builtIn && (
                    renaming === role.id ? (
                      <form
                        className="roles-table__rename"
                        onSubmit={(e) => {
                          e.preventDefault()
                          void rename(role, new FormData(e.currentTarget).get('name')?.toString().trim() ?? '')
                        }}
                      >
                        <input name="name" defaultValue={role.name} autoFocus aria-label={`Rename ${role.name}`}
                          onKeyDown={(e) => e.key === 'Escape' && setRenaming(null)} />
                        <button type="submit" className="link-btn" disabled={busy}>Save</button>
                      </form>
                    ) : (
                      <div className="roles-table__role-actions">
                        <button type="button" className="link-btn" disabled={busy}
                          onClick={() => setRenaming(role.id)}>Rename</button>
                        <button type="button" className="link-btn link-btn--danger" disabled={busy}
                          onClick={() => remove(role)}>Delete</button>
                      </div>
                    )
                  )}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {areas.map((area) => (
              <RoleArea
                key={area}
                area={area}
                permissions={matrix.catalog.filter((p) => p.area === area)}
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
                void (async () => {
                  const ok = await ask({
                    title: `Reset ${role.name} to the defaults?`,
                    confirmLabel: 'Reset the role',
                    body: <p>Every right this role holds goes back to what Tesria ships for its tier.
                      Unsaved changes in the matrix are left alone.</p>,
                  })
                  if (!ok) return
                  await run(() => api.admin.roles.reset(role.id), `${role.name} reset.`, 'Could not reset the role.')
                })()
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
                <p className="small">Gains: {added.map((k) => label(matrix.catalog, k)).join(', ')}</p>
              )}
              {removed.length > 0 && (
                <p className="small">Loses: {removed.map((k) => label(matrix.catalog, k)).join(', ')}</p>
              )}
            </div>
          ))}
          {/* The server alerts only for these: a user-tier role cannot gain
              administration rights at all (it is promoted instead). */}
          {pending.some((c) => c.added.length > 0 && c.role.tier >= UserRole.Admin) && (
            <p className="alert alert--error small">
              An administrator role that gains rights is announced to every administrator as a security alert.
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

      {dialog}
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
              {/* Administration rights belong to administrator roles (dev-plan
                  15.1): a user-tier role is promoted, not widened. */}
              {role.tier === UserRole.Member && p.scope === 'Administration' ? (
                <span className="muted" title="An administration right: promote the person to an administrator role to give it.">–</span>
              ) : (
                <input
                  type="checkbox"
                  aria-label={`${p.label} for ${role.name}`}
                  checked={draft[role.id]?.has(p.key) ?? false}
                  disabled={busy || !role.editable}
                  onChange={() => onToggle(role, p.key)}
                />
              )}
            </td>
          ))}
        </tr>
      ))}
    </>
  )
}
