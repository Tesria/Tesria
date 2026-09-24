import { type FormEvent, useEffect, useState } from 'react'
import { api, ApiError, type Directory, type Group, type GroupMember } from '../api/client'
import { useConfirm } from '../components/ConfirmDialog'

export function GroupsPage() {
  const [groups, setGroups] = useState<Group[] | null>(null)
  const { ask, dialog } = useConfirm()
  const [error, setError] = useState<string | null>(null)
  const [name, setName] = useState('')
  const [description, setDescription] = useState('')
  const [selected, setSelected] = useState<Group | null>(null)
  // The group being renamed, with its draft name and description.
  const [editing, setEditing] = useState<{ id: string; name: string; description: string } | null>(null)

  function load() {
    api.groups
      .list()
      .then(setGroups)
      .catch((err: unknown) => setError(err instanceof Error ? err.message : 'Failed to load groups.'))
  }

  useEffect(load, [])

  async function create(e: FormEvent) {
    e.preventDefault()
    if (!name.trim()) return
    setError(null)
    try {
      await api.groups.create({ name, description: description || null })
      setName('')
      setDescription('')
      load()
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not create the group.')
    }
  }

  async function saveEdit(e: FormEvent) {
    e.preventDefault()
    if (!editing || !editing.name.trim()) return
    setError(null)
    try {
      await api.groups.update(editing.id, { name: editing.name.trim(), description: editing.description.trim() || null })
      setEditing(null)
      load()
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not rename the group.')
    }
  }

  async function remove(g: Group) {
    const ok = await ask({
      title: `Delete the group ${g.name}?`,
      danger: true,
      confirmLabel: 'Delete the group',
      body: (
        <>
          <p>Everyone in it stays; only the group goes.</p>
          <p>Any space permission or page restriction granted to this group is removed with it, so people who reached a space only through it lose that access. Where the group is the only access to something, deleting it is refused, because removing that access would open it up.</p>
        </>
      ),
    })
    if (!ok) return
    setError(null)
    try {
      await api.groups.remove(g.id)
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not delete the group.')
      return
    }
    if (selected?.id === g.id) setSelected(null)
    load()
  }

  // Rendered inside the admin shell (Admin → Groups), which supplies the
  // heading and tabs.
  return (
    <div>
      <p className="muted small">
        Groups let you grant space and page access to a whole team at once.
      </p>
      {error && <p className="alert alert--error">{error}</p>}

      <form className="card form-inline" onSubmit={create}>
        <label>
          Name
          <input value={name} onChange={(e) => setName(e.target.value)} placeholder="Engineering" required />
        </label>
        <label>
          Description
          <input value={description} onChange={(e) => setDescription(e.target.value)} placeholder="Optional" />
        </label>
        <button type="submit" className="btn btn--primary">Create group</button>
      </form>

      {groups && groups.length === 0 && <p className="muted">No groups yet.</p>}
      <ul className="version-list">
        {groups?.map((g) => editing?.id === g.id ? (
          <li key={g.id} className="version">
            <form className="form-inline group-edit" onSubmit={saveEdit}>
              <label>
                Name
                <input value={editing.name} onChange={(e) => setEditing({ ...editing, name: e.target.value })} required autoFocus />
              </label>
              <label>
                Description
                <input value={editing.description} onChange={(e) => setEditing({ ...editing, description: e.target.value })} placeholder="Optional" />
              </label>
              <button type="submit" className="btn btn--primary btn--sm">Save</button>
              <button type="button" className="btn btn--ghost btn--sm" onClick={() => setEditing(null)}>Cancel</button>
            </form>
          </li>
        ) : (
          <li key={g.id} className="version">
            <span className="version__num">{g.name}</span>
            {g.builtIn && <span className="badge" title="Its members follow each account's role; it cannot be renamed or deleted.">built in</span>}
            <span className="muted small">{g.memberCount} member{g.memberCount === 1 ? '' : 's'}</span>
            {g.description && <span className="version__comment">{g.description}</span>}
            <span className="version__actions">
              <button type="button" className="link-btn"
                onClick={() => setSelected(selected?.id === g.id ? null : g)}>
                {selected?.id === g.id ? 'Close' : 'Members'}
              </button>
              {!g.builtIn && (
                <>
                  <button type="button" className="link-btn"
                    onClick={() => setEditing({ id: g.id, name: g.name, description: g.description ?? '' })}>
                    Edit
                  </button>
                  <button type="button" className="link-btn link-btn--danger" onClick={() => remove(g)}>
                    Delete
                  </button>
                </>
              )}
            </span>
          </li>
        ))}
      </ul>

      {selected && <MemberEditor group={selected} onChanged={load} />}

      {dialog}
    </div>
  )
}

function MemberEditor({ group, onChanged }: { group: Group; onChanged: () => void }) {
  const [members, setMembers] = useState<GroupMember[]>([])
  const [users, setUsers] = useState<Directory[]>([])
  const [userId, setUserId] = useState('')
  const [error, setError] = useState<string | null>(null)

  function load() {
    api.groups.members(group.id).then(setMembers).catch(() => {})
  }

  useEffect(() => {
    load()
    api.users.list().then(setUsers).catch(() => {})
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [group.id])

  async function add(e: FormEvent) {
    e.preventDefault()
    if (!userId) return
    setError(null)
    try {
      await api.groups.addMember(group.id, userId)
      setUserId('')
      load()
      onChanged()
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not add the member.')
    }
  }

  const { ask, dialog } = useConfirm()

  async function remove(member: GroupMember) {
    // Asked, not done on one click (dev-plan 15.4): it can take someone's
    // access to every space shared with this group.
    const ok = await ask({
      title: `Remove ${member.displayName} from ${group.name}?`,
      confirmLabel: 'Remove from the group',
      body: <p>They lose anything they could reach only through {group.name}.</p>,
    })
    if (!ok) return
    setError(null)
    try {
      await api.groups.removeMember(group.id, member.userId)
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not remove the member.')
      return
    }
    load()
    onChanged()
  }

  const candidates = users.filter((u) => !members.some((m) => m.userId === u.id))

  return (
    <div className="card">
      <h2 style={{ fontSize: '1.05rem', marginTop: 0 }}>Members of {group.name}</h2>
      {error && <p className="alert alert--error">{error}</p>}
      {group.builtIn ? (
        <p className="muted small">Built in: its members follow each account’s role, so they are not added or removed here.</p>
      ) : (
      <form className="principal-picker" onSubmit={add}>
        <select value={userId} onChange={(e) => setUserId(e.target.value)} aria-label="User to add" required>
          <option value="">Choose a user…</option>
          {candidates.map((u) => (
            <option key={u.id} value={u.id}>{u.displayName}{u.email ? ` (${u.email})` : ''}</option>
          ))}
        </select>
        <button type="submit" className="btn btn--primary btn--sm" disabled={!userId}>Add member</button>
      </form>
      )}

      {members.length === 0 && <p className="muted small">No members yet.</p>}
      <ul className="attachment-list">
        {members.map((m) => (
          <li key={m.userId} className="attachment">
            <span>{m.displayName}</span>
            {m.email && <span className="muted small">{m.email}</span>}
            {!group.builtIn && (
              <button type="button" className="link-btn link-btn--danger" onClick={() => remove(m)}>
                Remove
              </button>
            )}
          </li>
        ))}
      </ul>
      {dialog}
    </div>
  )
}
