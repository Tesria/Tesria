import { type FormEvent, useCallback, useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import {
  api, ApiError, PrincipalType, SpaceGroupRole, spaceOperationName, TESRIA_ADMINS_GROUP_ID,
  type Directory, type GroupMember, type RestrictedPage, type SpaceAccess, type SpaceGroup, type SpacePermission,
} from '../api/client'
import { PrincipalPicker } from './PrincipalPicker'
import { useConfirm } from './ConfirmDialog'
import {
  EVERYONE_ADMINISTERS, EVERYONE_LEVELS, everyoneLabel, everyoneSummary, globalReadersNote, widens,
} from './spaceAccess'

/** What each of a space's groups may do, in the tab's words. */
const GROUP_LEVEL: Record<number, string> = {
  [SpaceGroupRole.Viewers]: 'Can view',
  [SpaceGroupRole.Editors]: 'Can view and edit',
  [SpaceGroupRole.Admins]: 'Can view, edit and manage the space, and lift page restrictions',
  [SpaceGroupRole.Reviewers]: 'Can view; reviewing arrives with review mode',
}

/**
 * Who may open a space (dev-plan 21.1): what everyone signed in gets, the
 * space's own four groups and their members, and any other access given to
 * people or groups. The Permissions tab of space settings, and the step that
 * follows importing a wiki pack (dev-plan 15.1), which is why it takes a key
 * rather than the space route's context.
 */
export function SpaceAccessEditor({ spaceKey }: { spaceKey: string }) {
  const [access, setAccess] = useState<SpaceAccess | null>(null)
  const [users, setUsers] = useState<Directory[]>([])
  const [error, setError] = useState<string | null>(null)
  const [forbidden, setForbidden] = useState(false)
  const [busy, setBusy] = useState(false)
  const { ask, dialog } = useConfirm()

  const load = useCallback(() => {
    api.spacePermissions
      .list(spaceKey)
      .then((r) => {
        setAccess(r)
        setForbidden(false)
      })
      .catch((err: unknown) => {
        if (err instanceof ApiError && err.status === 403) setForbidden(true)
        else setError(err instanceof Error ? err.message : 'Failed to load permissions.')
      })
  }, [spaceKey])

  useEffect(load, [load])
  useEffect(() => {
    api.users.list().then(setUsers).catch(() => {})
  }, [])

  /**
   * Widening asks first and then, on the server's say, for the password
   * again, as making a space open always has (dev-plan 15.3): it lets
   * everyone into everything at once, and every administrator is alerted.
   */
  async function changeEveryone(to: number | null) {
    if (!access || to === access.everyoneAccess) return
    const from = access.everyoneAccess
    const opening = widens(from, to)
    const ok = await ask(opening
      ? {
          title: `Let Everyone Signed In ${everyoneLabel(to)}?`,
          danger: true,
          confirmLabel: `Let Everyone ${everyoneLabel(to)}`,
          body: (
            <>
              <p>{everyoneSummary(to)}</p>
              {to === 2 && <p><strong>{EVERYONE_ADMINISTERS}</strong> Choose Edit instead if they only need to write.</p>}
              <p>That reaches every page in the space, except those restricted to particular people. Every administrator is alerted.</p>
            </>
          ),
        }
      : {
          title: to === null ? 'Close This Space to Everyone Else?' : `Let Everyone Signed In Only ${everyoneLabel(to)}?`,
          confirmLabel: to === null ? 'Close It' : `Change to ${everyoneLabel(to)}`,
          body: <p>{everyoneSummary(to)}</p>,
        })
    if (!ok) return
    setError(null)
    setBusy(true)
    try {
      await api.spacePermissions.setEveryone(spaceKey, to)
      load()
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not change who can open the space.')
    } finally {
      setBusy(false)
    }
  }

  async function revoke(row: SpacePermission) {
    setError(null)
    try {
      await api.spacePermissions.revoke(spaceKey, row.id)
      load()
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not revoke.')
    }
  }

  if (forbidden) {
    return <p className="alert alert--error">You need admin rights on this space to manage its permissions.</p>
  }
  if (!access) return error ? <p className="alert alert--error">{error}</p> : <p className="muted">Loading…</p>

  const globals = globalReadersNote(access.globalViewers, access.globalReviewers)
  // Tesria's administrators as administrators of this space (21.6): an
  // Admin grant to the built-in Admins group, shown as one switch.
  const adminsGrant = access.grants.find((r) =>
    r.principalType === PrincipalType.Group && r.principalId === TESRIA_ADMINS_GROUP_ID && r.operation === 2)

  async function setTesriaAdmins(on: boolean) {
    setError(null)
    setBusy(true)
    try {
      if (on) await api.spacePermissions.grant(spaceKey, { principalType: PrincipalType.Group, principalId: TESRIA_ADMINS_GROUP_ID, operation: 2 })
      else if (adminsGrant) await api.spacePermissions.revoke(spaceKey, adminsGrant.id)
      load()
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not change it.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="space-access">
      {error && <p className="alert alert--error">{error}</p>}

      <section className="profile__section profile__section--wide">
        <h2>Everyone Signed In</h2>
        <label className="space-access__everyone">
          Everyone signed in can
          <span className="glass-select-wrap"><select
            className="glass-select"
            value={access.everyoneAccess ?? ''}
            disabled={busy}
            onChange={(e) => void changeEveryone(e.target.value === '' ? null : Number(e.target.value))}
          >
            {EVERYONE_LEVELS.map((l) => (
              <option key={l.label} value={l.value ?? ''}>{l.label}</option>
            ))}
          </select></span>
        </label>
        <p className="muted small">{everyoneSummary(access.everyoneAccess)}</p>
        {globals && <p className="muted small">{globals} They see every space, and page restrictions still apply to them.</p>}
      </section>

      <section className="profile__section profile__section--wide">
        <h2>This Space’s Groups</h2>
        <p className="muted small">
          Made with the space and named after it. Add people to a group to give them its access.
        </p>
        {access.groups.map((g) => (
          <SpaceGroupCard
            key={g.id}
            group={g}
            users={users}
            // Who administers the space is its explicit administrators' to
            // decide, never everyone's because the space is open (21.1).
            mayManage={g.role !== SpaceGroupRole.Admins || access.canManageAdmins}
            onChanged={load}
            ask={ask}
          />
        ))}
      </section>

      <section className="profile__section profile__section--wide">
        <h2>Tesria Administrators</h2>
        <label className="open-review__choice">
          <input type="checkbox" checked={!!adminsGrant} disabled={busy || !access.canManageAdmins}
            onChange={(e) => void setTesriaAdmins(e.target.checked)} />
          <span>Tesria’s administrators can administer this space</span>
        </label>
        <p className="muted small">
          New spaces start this way unless Administration’s defaults say otherwise. Untick it for a space that
          should stay among its own people. Page restrictions bind them like anyone.
          {!access.canManageAdmins && ' Only the people in this space’s Admins group can change it.'}
        </p>
      </section>

      <section className="profile__section profile__section--wide">
        <h2>Other Access</h2>
        <p className="muted small">
          Access given to people, or to groups that are not this space’s own. Admin includes Edit,
          and Edit includes View.
        </p>
        <PrincipalPicker
          operationNames={spaceOperationName}
          addLabel="Grant"
          onAdd={async (input) => {
            await api.spacePermissions.grant(spaceKey, input)
            load()
          }}
        />
        {access.grants.length === 0 ? (
          <p className="muted small">Nobody else.</p>
        ) : (
          <ul className="version-list">
            {access.grants.filter((r) => r !== adminsGrant).map((r) => (
              <li key={r.id} className="version">
                <span className="badge">{r.principalType === PrincipalType.User ? 'person' : 'group'}</span>
                <span className="version__num">{r.principalName ?? r.principalId}</span>
                <span className="muted small">{spaceOperationName[r.operation]}</span>
                {(r.operation !== 2 || access.canManageAdmins) && (
                  <span className="version__actions">
                    <button type="button" className="link-btn link-btn--danger" onClick={() => void revoke(r)}>
                      Revoke
                    </button>
                  </span>
                )}
              </li>
            ))}
          </ul>
        )}
      </section>

      {access.canManageAdmins && <RestrictedPagesSection spaceKey={spaceKey} ask={ask} />}
      {dialog}
    </div>
  )
}

/**
 * The space's restricted pages (dev-plan 21.6), for its explicit
 * administrators: restrictions bind them like anyone, Confluence's rule, so
 * they see that a page is restricted and to whom, and may lift it. Lifting
 * is recorded and tells the page's author, so it is never a quiet way in.
 */
function RestrictedPagesSection({ spaceKey, ask }: { spaceKey: string; ask: Ask }) {
  const [pages, setPages] = useState<RestrictedPage[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [version, setVersion] = useState(0)

  useEffect(() => {
    let live = true
    api.spaces.restrictedPages(spaceKey)
      .then((r) => { if (live) setPages(r) })
      .catch((err: unknown) => { if (live) setError(err instanceof ApiError ? err.message : 'Could not load its restricted pages.') })
    return () => { live = false }
  }, [spaceKey, version])

  async function lift(p: RestrictedPage) {
    const ok = await ask({
      title: `Lift the restrictions on “${p.title}”?`,
      danger: true,
      confirmLabel: 'Lift the Restrictions',
      body: (
        <>
          <p>
            Everyone who can open this space will be able to read it
            {p.pagesUnder > 0 && `, and the ${p.pagesUnder} page${p.pagesUnder === 1 ? '' : 's'} under it unless they carry restrictions of their own`}.
          </p>
          <p>{p.createdByName ? `${p.createdByName}, who wrote it, is told.` : 'Its author is told.'} It is recorded in the audit log, with what was removed.</p>
        </>
      ),
    })
    if (!ok) return
    setError(null)
    try {
      await api.pageRestrictions.lift(p.id)
      setVersion((v) => v + 1)
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not lift the restrictions.')
    }
  }

  return (
    <section className="profile__section profile__section--wide">
      <h2>Restricted Pages</h2>
      <p className="muted small">
        Pages restricted to particular people. The restrictions bind this space’s administrators too: you see that
        a page is restricted, and to whom, but read it only if you are named. Lifting the restrictions opens it to
        everyone who can open the space, and tells its author.
      </p>
      {error && <p className="alert alert--error">{error}</p>}
      {pages && pages.length === 0 && <p className="muted small">None.</p>}
      {pages && pages.length > 0 && (
        <ul className="version-list">
          {pages.map((p) => (
            <li key={p.id} className="version restricted-page">
              <span className="version__num">
                {p.youCanRead
                  ? <Link to={`/spaces/${encodeURIComponent(spaceKey)}/pages/${p.id}`}>{p.title}</Link>
                  : p.title}
              </span>
              {p.draft && <span className="badge">draft</span>}
              <span className="muted small">
                {p.restrictions.map((r) => `${r.operation === 0 ? 'Read' : 'Edit'}: ${r.principalName}`).join(' · ')}
                {p.pagesUnder > 0 && ` · ${p.pagesUnder} page${p.pagesUnder === 1 ? '' : 's'} under it`}
              </span>
              <span className="version__actions">
                <button type="button" className="link-btn link-btn--danger" onClick={() => void lift(p)}>Lift</button>
              </span>
            </li>
          ))}
        </ul>
      )}
    </section>
  )
}

type Ask = ReturnType<typeof useConfirm>['ask']

/** One of the space's groups, with its members, and adding and removing them. */
function SpaceGroupCard({ group, users, mayManage, onChanged, ask }: {
  group: SpaceGroup
  users: Directory[]
  mayManage: boolean
  onChanged: () => void
  ask: Ask
}) {
  const [userId, setUserId] = useState('')
  const [error, setError] = useState<string | null>(null)

  async function add(e: FormEvent) {
    e.preventDefault()
    if (!userId) return
    setError(null)
    try {
      await api.groups.addMember(group.id, userId)
      setUserId('')
      onChanged()
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not add them.')
    }
  }

  async function remove(member: GroupMember) {
    const ok = await ask({
      title: `Remove ${member.displayName} from ${group.name}?`,
      confirmLabel: 'Remove From the Group',
      body: <p>They lose what they could do here only through {group.name}.</p>,
    })
    if (!ok) return
    setError(null)
    try {
      await api.groups.removeMember(group.id, member.userId)
      onChanged()
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not remove them.')
    }
  }

  const candidates = users.filter((u) => !group.members.some((m) => m.userId === u.id))

  return (
    <div className="card space-group">
      <h3 className="space-group__name">
        {group.name} <span className="badge" title="Belongs to this space, and goes when it does">this space</span>
      </h3>
      <p className="muted small">{GROUP_LEVEL[group.role]}</p>
      {error && <p className="alert alert--error">{error}</p>}
      {group.members.length === 0 ? (
        <p className="muted small">Nobody yet.</p>
      ) : (
        <ul className="attachment-list">
          {group.members.map((m) => (
            <li key={m.userId} className="attachment">
              <span>{m.displayName}</span>
              {m.email && <span className="muted small">{m.email}</span>}
              {m.active === false && <span className="badge" title="Suspended accounts keep their groups but get nothing from them">suspended</span>}
              {mayManage && (
                <button type="button" className="link-btn link-btn--danger" onClick={() => void remove(m)}>
                  Remove
                </button>
              )}
            </li>
          ))}
        </ul>
      )}
      {mayManage ? (
        <form className="principal-picker" onSubmit={add}>
          <span className="glass-select-wrap"><select className="glass-select" value={userId} onChange={(e) => setUserId(e.target.value)} aria-label={`Person to add to ${group.name}`}>
            <option value="">Choose a person…</option>
            {candidates.map((u) => (
              <option key={u.id} value={u.id}>{u.displayName}{u.email ? ` (${u.email})` : ''}</option>
            ))}
          </select></span>
          <button type="submit" className="btn btn--primary btn--sm" disabled={!userId}>Add</button>
        </form>
      ) : group.members.length === 0 ? (
        // Every space that was open before 21.1 starts like this: nobody may
        // add to Admins, so recover-access is the way in.
        <p className="muted small">
          Only the people in Admins, or given Admin here, can change who is in it. While it is empty, a Tesria
          administrator can join it with Get Access, in Administration, Spaces.
        </p>
      ) : (
        <p className="muted small">Only the people in Admins, or given Admin here, can change who is in it.</p>
      )}
    </div>
  )
}
