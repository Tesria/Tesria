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

  async function remove(g: Group) {
    const ok = await ask({
      title: `Delete the group ${g.name}?`,
      danger: true,
      confirmLabel: 'Delete the group',
      body: (
        <>
          <p>Everyone in it stays; only the group goes.</p>
          <p>Any space permission granted to this group is removed with it, so people who reached a space only through it lose that access.</p>
        </>
      ),
    })
    if (!ok) return
    await api.groups.remove(g.id)
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
        {groups?.map((g) => (
          <li key={g.id} className="version">
            <span className="version__num">{g.name}</span>
            <span className="muted small">{g.memberCount} member{g.memberCount === 1 ? '' : 's'}</span>
            {g.description && <span className="version__comment">{g.description}</span>}
            <span className="version__actions">
              <button type="button" className="link-btn"
                onClick={() => setSelected(selected?.id === g.id ? null : g)}>
                {selected?.id === g.id ? 'Close' : 'Members'}
              </button>
              <button type="button" className="link-btn link-btn--danger" onClick={() => remove(g)}>
                Delete
              </button>
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

  async function remove(id: string) {
    await api.groups.removeMember(group.id, id)
    load()
    onChanged()
  }

  const candidates = users.filter((u) => !members.some((m) => m.userId === u.id))

  return (
    <div className="card">
      <h2 style={{ fontSize: '1.05rem', marginTop: 0 }}>Members of {group.name}</h2>
      {error && <p className="alert alert--error">{error}</p>}
      <form className="principal-picker" onSubmit={add}>
        <select value={userId} onChange={(e) => setUserId(e.target.value)} aria-label="User to add" required>
          <option value="">Choose a user…</option>
          {candidates.map((u) => (
            <option key={u.id} value={u.id}>{u.displayName} ({u.email})</option>
          ))}
        </select>
        <button type="submit" className="btn btn--primary btn--sm" disabled={!userId}>Add member</button>
      </form>

      {members.length === 0 && <p className="muted small">No members yet.</p>}
      <ul className="attachment-list">
        {members.map((m) => (
          <li key={m.userId} className="attachment">
            <span>{m.displayName}</span>
            <span className="muted small">{m.email}</span>
            <button type="button" className="link-btn link-btn--danger" onClick={() => remove(m.userId)}>
              Remove
            </button>
          </li>
        ))}
      </ul>
    </div>
  )
}
