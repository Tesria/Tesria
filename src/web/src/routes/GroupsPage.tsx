import { type FormEvent, type ReactNode, useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import {
  api, ApiError, type BulkAddResult, type Directory, type GroupKind, type GroupMember, type GroupOverview,
  type Space,
} from '../api/client'
import { AccessExplainer } from '../components/AccessExplainer'
import { useConfirm } from '../components/ConfirmDialog'
import { accessSummary, memberCountText, parseEmailList, sectionMembers, sectionsOf, type GroupSection } from '../components/groupsList'

/** What the Show menu offers: a kind, or a space's own groups (`space:KEY`). */
type Show = '' | GroupKind | `space:${string}`

export function GroupsPage() {
  const [groups, setGroups] = useState<GroupOverview[] | null>(null)
  const [truncated, setTruncated] = useState(false)
  const { ask, dialog } = useConfirm()
  const [error, setError] = useState<string | null>(null)
  const [name, setName] = useState('')
  const [description, setDescription] = useState('')
  const [selectedId, setSelectedId] = useState<string | null>(null)
  // The group being renamed, with its draft name and description.
  const [editing, setEditing] = useState<{ id: string; name: string; description: string } | null>(null)
  const [spaces, setSpaces] = useState<Space[]>([])
  const [search, setSearch] = useState('')
  const [query, setQuery] = useState('')
  const [show, setShow] = useState<Show>('')
  // Bumped to load the list again after a change.
  const [version, setVersion] = useState(0)

  useEffect(() => {
    api.spaces.list(true).then(setSpaces).catch(() => {})
  }, [])

  // The search waits for a pause in typing, so each letter is not a request.
  useEffect(() => {
    const t = setTimeout(() => setQuery(search.trim()), 250)
    return () => clearTimeout(t)
  }, [search])

  useEffect(() => {
    let live = true
    const space = show.startsWith('space:') ? show.slice('space:'.length) : null
    api.groups
      .overview({ q: query, kind: space || !show ? null : (show as GroupKind), space })
      .then((r) => {
        if (!live) return
        setGroups(r.groups)
        setTruncated(r.truncated)
        setError(null)
      })
      .catch((err: unknown) => { if (live) setError(err instanceof Error ? err.message : 'Failed to load groups.') })
    return () => { live = false }
  }, [query, show, version])

  const reload = () => setVersion((v) => v + 1)
  const sections = useMemo(() => sectionsOf(groups ?? []), [groups])
  const selected = groups?.find((g) => g.id === selectedId) ?? null
  const filtered = query !== '' || show !== ''

  async function create(e: FormEvent) {
    e.preventDefault()
    if (!name.trim()) return
    setError(null)
    try {
      await api.groups.create({ name, description: description || null })
      setName('')
      setDescription('')
      reload()
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
      reload()
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not rename the group.')
    }
  }

  async function remove(g: GroupOverview) {
    const ok = await ask({
      title: `Delete the group ${g.name}?`,
      danger: true,
      confirmLabel: 'Delete the Group',
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
    if (selectedId === g.id) setSelectedId(null)
    reload()
  }

  const row = (g: GroupOverview) => editing?.id === g.id ? (
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
    <GroupRow key={g.id} group={g} open={selectedId === g.id}
      onToggle={() => setSelectedId(selectedId === g.id ? null : g.id)}
      onEdit={() => setEditing({ id: g.id, name: g.name, description: g.description ?? '' })}
      onDelete={() => void remove(g)} />
  )

  // Rendered inside the admin shell (Admin → Groups), which supplies the
  // heading and tabs.
  return (
    <div className="groups-page">
      <p className="muted small">
        Groups let you give space and page access to a whole team at once. Who can open a space is set on the
        space: everyone signed in at a level, its own four groups, and any group or person given access there.{' '}
        <Link to="/admin/spaces">Spaces</Link> shows each space’s access; <Link to="/admin/users">Users</Link> shows
        what one person can see.
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
        <button type="submit" className="btn btn--primary">Create Group</button>
      </form>

      {/* The audit log's filter bar: it wraps on a phone rather than scrolling. */}
      <div className="audit-filters" role="search">
        <label className="audit-filters__field audit-filters__field--action">
          Search groups and members
          <input type="search" value={search} onChange={(e) => setSearch(e.target.value)}
            placeholder="A group, a name or an email" />
        </label>
        <label className="audit-filters__field">
          Show
          <span className="glass-select-wrap"><select className="glass-select" value={show}
            onChange={(e) => { setShow(e.target.value as Show); setSelectedId(null) }}>
            <option value="">All groups</option>
            <option value="builtin">Running Tesria only</option>
            <option value="global">Global only</option>
            <option value="custom">Custom only</option>
            <option value="space">Space groups only</option>
            {spaces.length > 0 && (
              <optgroup label="A space’s own groups">
                {spaces.map((s) => <option key={s.id} value={`space:${s.key}`}>{s.name}</option>)}
              </optgroup>
            )}
          </select></span>
        </label>
        {filtered && (
          <button type="button" className="btn btn--ghost audit-filters__clear"
            onClick={() => { setSearch(''); setQuery(''); setShow('') }}>
            Clear
          </button>
        )}
      </div>
      {truncated && (
        <p className="muted small">That matches a great many people; only the first few hundred were looked at. Type more to narrow it.</p>
      )}

      {groups && groups.length === 0 && (
        <p className="muted">{filtered ? 'No group matches.' : 'No groups yet.'}</p>
      )}
      <div className="groups-list">
      {sections.map((section, i) => section.kind === 'space' ? (
        <SpaceSection key={section.key} section={section} first={sections[i - 1]?.kind !== 'space'}
          open={filtered} renderRow={row} />
      ) : (
        <section key={section.key} className="groups-section">
          <h3 className="groups-section__title">{section.title}</h3>
          {SECTION_NOTES[section.kind] && <p className="muted small groups-section__note">{SECTION_NOTES[section.kind]}</p>}
          <ul className="version-list">{section.groups.map(row)}</ul>
        </section>
      ))}
      </div>

      {selected &&<MemberEditor key={selected.id} group={selected} onChanged={reload} />}

      <AccessExplainer />

      {dialog}
    </div>
  )
}

/**
 * What each kind of group is for, under its heading (dev-plan 21.5): the
 * built-in three sat at the top of a list about access and read as if they
 * opened spaces, which none of them does by itself.
 */
const SECTION_NOTES: Partial<Record<GroupKind, string>> = {
  builtin: 'These decide who runs Tesria: its owner, its administrators, and every account. None of them opens a space by itself; a space counts one only where its Permissions tab names it under Other Access.',
  global: 'Their members can read every space, archived ones included. Page restrictions still apply to them.',
  custom: 'Your own teams. A group gives access wherever a space or a page names it.',
}

/**
 * One space's four groups, folded under the space's name (21.5): listed
 * without asking now, and an instance with many spaces has four for each.
 * Open while searching or filtering, so a match is never hidden.
 */
function SpaceSection({ section, first, open, renderRow }: {
  section: GroupSection
  first: boolean
  open: boolean
  renderRow: (g: GroupOverview) => ReactNode
}) {
  const people = sectionMembers(section)
  return (
    <>
      {first && (
        <section className="groups-section">
          <h3 className="groups-section__title">Spaces</h3>
          <p className="muted small groups-section__note">
            Each space has four groups of its own, made with it and named after it. Its administrators choose who is
            in them, in its Permissions tab.
          </p>
        </section>
      )}
      <details className="groups-space" open={open || undefined}>
        <summary className="groups-space__summary">
          <span className="groups-space__name">{section.title}</span>
          {section.spaceKey && <span className="badge" title="These groups belong to this space and go when it does">{section.spaceKey}</span>}
          <span className="muted small">{people === 0 ? 'Nobody in them' : `${people} member${people === 1 ? '' : 's'}`}</span>
        </summary>
        {section.spaceKey && (
          <p className="small groups-space__link">
            <Link to={`/spaces/${encodeURIComponent(section.spaceKey)}/settings/permissions`}>Permissions tab</Link>
          </p>
        )}
        <ul className="version-list">{section.groups.map(renderRow)}</ul>
      </details>
    </>
  )
}

function GroupRow({ group: g, open, onToggle, onEdit, onDelete }: {
  group: GroupOverview
  open: boolean
  onToggle: () => void
  onEdit: () => void
  onDelete: () => void
}) {
  const access = accessSummary(g)
  const custom = g.kind === 'custom'
  return (
    <li className="version">
      <span className="version__num">{g.name}</span>
      {(g.kind === 'builtin' || g.kind === 'global') && (
        <span className="badge" title={g.computed
          ? "Its members follow each account's role; it cannot be renamed or deleted."
          : 'Its members are chosen here; it cannot be renamed or deleted.'}>built in</span>
      )}
      <span className="muted small">{memberCountText(g)}</span>
      {g.description && <span className="version__comment">{g.description}</span>}
      <span className="version__actions">
        <button type="button" className="link-btn" onClick={onToggle} aria-expanded={open}>
          {open ? 'Close' : 'Members'}
        </button>
        {custom && (
          <>
            <button type="button" className="link-btn" onClick={onEdit}>Edit</button>
            <button type="button" className="link-btn link-btn--danger" onClick={onDelete}>Delete</button>
          </>
        )}
      </span>
      {(access.shown.length > 0 || g.matches.length > 0) && (
        <span className="groups-row__detail">
          {access.shown.length > 0 && (
            <span>
              {access.shown.join(' · ')}
              {access.more > 0 && ` and ${access.more} more`}
            </span>
          )}
          {g.matches.length > 0 && (
            <span className="groups-row__matches">
              Matched: {g.matches.map((m) => m.email ? `${m.displayName} (${m.email})` : m.displayName).join(', ')}
            </span>
          )}
        </span>
      )}
    </li>
  )
}

function MemberEditor({ group, onChanged }: { group: GroupOverview; onChanged: () => void }) {
  const [members, setMembers] = useState<GroupMember[]>([])
  const [users, setUsers] = useState<Directory[]>([])
  const [error, setError] = useState<string | null>(null)
  const [report, setReport] = useState<BulkAddResult | null>(null)

  function load() {
    api.groups.members(group.id).then(setMembers).catch(() => {})
  }

  useEffect(() => {
    load()
    api.users.list().then(setUsers).catch(() => {})
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [group.id])

  const { ask, dialog } = useConfirm()

  async function remove(member: GroupMember) {
    // Asked, not done on one click (dev-plan 15.4): it can take someone's
    // access to every space shared with this group.
    const ok = await ask({
      title: `Remove ${member.displayName} from ${group.name}?`,
      confirmLabel: 'Remove From the Group',
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
      {group.kind === 'global' && (
        <p className="muted small">
          Everyone in {group.name} can read every space, archived ones included. Page restrictions still apply to
          them and drafts stay hidden. Adding someone asks for your password and alerts every administrator.
        </p>
      )}
      {group.computed ? (
        <p className="muted small">Built in: its members follow each account’s role, so they are not added or removed here.</p>
      ) : group.canManageMembers ? (
        <BulkAdd group={group} candidates={candidates} onDone={(r) => { setReport(r); load(); onChanged() }} onError={setError} />
      ) : group.kind === 'space' ? (
        <p className="muted small">
          {group.spaceRole === 2
            ? 'Only this space’s own administrators (the people in its Admins, or given Admin there) can change who is in it.'
            : 'Only this space’s administrators can change who is in it.'}{' '}
          They do it in the space’s Permissions tab.
        </p>
      ) : null}
      {report && <BulkReport report={report} onClose={() => setReport(null)} />}

      {members.length === 0 && <p className="muted small">No members yet.</p>}
      <ul className="attachment-list">
        {members.map((m) => (
          <li key={m.userId} className="attachment">
            <span>{m.displayName}</span>
            {m.email && <span className="muted small">{m.email}</span>}
            {m.active === false && <span className="badge" title="Suspended accounts keep their groups but get nothing from them">suspended</span>}
            {!group.computed && group.canManageMembers && (
              <button type="button" className="link-btn link-btn--danger" onClick={() => void remove(m)}>
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

/**
 * Adding people (dev-plan 21.4): tick several in the list, or paste their
 * email addresses, and add them in one go. The server checks the group's
 * rights once, as for one person, and says who was added, who was already
 * there, and who was not and why.
 */
function BulkAdd({ group, candidates, onDone, onError }: {
  group: GroupOverview
  candidates: Directory[]
  onDone: (report: BulkAddResult) => void
  onError: (message: string | null) => void
}) {
  const [filter, setFilter] = useState('')
  const [picked, setPicked] = useState<Set<string>>(new Set())
  const [pasting, setPasting] = useState(false)
  const [pasted, setPasted] = useState('')
  const [busy, setBusy] = useState(false)

  const wanted = filter.trim().toLowerCase()
  const shown = candidates.filter((u) => !wanted
    || u.displayName.toLowerCase().includes(wanted) || (u.email ?? '').toLowerCase().includes(wanted))
  const visible = shown.slice(0, 100)
  const parsed = parseEmailList(pasted)
  const count = picked.size + (pasting ? parsed.emails.length : 0)

  function toggle(id: string) {
    const next = new Set(picked)
    if (next.has(id)) next.delete(id)
    else next.add(id)
    setPicked(next)
  }

  async function add(e: FormEvent) {
    e.preventDefault()
    if (count === 0) return
    setBusy(true)
    onError(null)
    try {
      const result = await api.groups.addMembers(group.id, {
        userIds: [...picked],
        emails: pasting ? parsed.emails : [],
      })
      // What could not be read as an address is reported with the rest.
      if (pasting) result.refused.push(...parsed.invalid.map((input) => ({ input, reason: 'This is not an email address.' })))
      setPicked(new Set())
      setPasted('')
      onDone(result)
    } catch (err) {
      onError(err instanceof ApiError ? err.message : 'Could not add them.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <form className="people-picker" onSubmit={add}>
      <div className="people-picker__modes">
        <button type="button" className={`link-btn${pasting ? '' : ' is-active'}`} aria-pressed={!pasting} onClick={() => setPasting(false)}>
          Choose people
        </button>
        <button type="button" className={`link-btn${pasting ? ' is-active' : ''}`} aria-pressed={pasting} onClick={() => setPasting(true)}>
          Paste email addresses
        </button>
      </div>
      {pasting ? (
        <label>
          Email addresses, separated by commas or one per line
          <textarea rows={4} value={pasted} onChange={(e) => setPasted(e.target.value)}
            placeholder={'sam@example.com, priya@example.com\nJordan Lee <jordan@example.com>'} />
          {pasted.trim() && (
            <span className="muted small">
              {parsed.emails.length} address{parsed.emails.length === 1 ? '' : 'es'}
              {parsed.invalid.length > 0 && `; not addresses: ${parsed.invalid.join(', ')}`}
            </span>
          )}
        </label>
      ) : (
        <>
          <label>
            Find people
            <input type="search" value={filter} onChange={(e) => setFilter(e.target.value)} placeholder="A name or an email" />
          </label>
          {candidates.length === 0 ? (
            <p className="muted small">Everyone is already in it.</p>
          ) : (
            <ul className="people-picker__list" aria-label={`People to add to ${group.name}`}>
              {visible.map((u) => (
                <li key={u.id}>
                  <label className="people-picker__item">
                    <input type="checkbox" checked={picked.has(u.id)} onChange={() => toggle(u.id)} />
                    <span>{u.displayName}</span>
                    {u.email && <span className="muted small">{u.email}</span>}
                  </label>
                </li>
              ))}
              {shown.length === 0 && <li className="muted small">Nobody matches.</li>}
              {shown.length > visible.length && (
                <li className="muted small">And {shown.length - visible.length} more: type to narrow the list.</li>
              )}
            </ul>
          )}
        </>
      )}
      <button type="submit" className="btn btn--primary btn--sm" disabled={count === 0 || busy}>
        {busy ? 'Adding…' : count > 1 ? `Add ${count} People` : 'Add Member'}
      </button>
    </form>
  )
}

function BulkReport({ report, onClose }: { report: BulkAddResult; onClose: () => void }) {
  const names = (list: BulkAddResult['added']) =>
    list.map((p) => p.displayName + (p.active ? '' : ' (suspended: gets nothing until reactivated)')).join(', ')
  return (
    <div className={`alert ${report.refused.length > 0 ? 'alert--warning' : 'alert--success'} bulk-report`} role="status">
      {report.added.length > 0 && <p><strong>Added {report.added.length}:</strong> {names(report.added)}</p>}
      {report.alreadyMembers.length > 0 && (
        <p><strong>Already in it:</strong> {names(report.alreadyMembers)}</p>
      )}
      {report.refused.length > 0 && (
        <>
          <p><strong>Not added:</strong></p>
          <ul>
            {report.refused.map((r, i) => <li key={i}>{r.input}: {r.reason}</li>)}
          </ul>
        </>
      )}
      {report.added.length + report.alreadyMembers.length + report.refused.length === 0 && <p>Nobody was added.</p>}
      <button type="button" className="link-btn" onClick={onClose}>Dismiss</button>
    </div>
  )
}
