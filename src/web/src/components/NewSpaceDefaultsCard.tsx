import { useEffect, useState } from 'react'
import { api, ApiError, TESRIA_ADMINS_GROUP_ID, type Group, type SpaceDefaults } from '../api/client'
import { EVERYONE_ADMINISTERS, EVERYONE_LEVELS } from './spaceAccess'

const GROUP_LEVELS = [
  { value: 0, label: 'View' },
  { value: 1, label: 'Edit' },
  { value: 2, label: 'Administer' },
]

type Row = { groupId: string; name: string; level: number }

/**
 * Default access for new spaces (dev-plan 21.6), in Admin, Spaces: after
 * Confluence's "Defaults for new spaces". What everyone signed in gets, what
 * Tesria's administrators get, and any custom groups. A starting point, not
 * a limit: the New Space wizard starts from it and the creator may change it,
 * and no existing space changes when it does.
 */
export function NewSpaceDefaultsCard() {
  const [saved, setSaved] = useState<SpaceDefaults | null>(null)
  const [everyone, setEveryone] = useState<number | null>(null)
  const [admins, setAdmins] = useState<number | null>(null)
  const [custom, setCustom] = useState<Row[]>([])
  const [groups, setGroups] = useState<Group[]>([])
  const [adding, setAdding] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)

  function adopt(d: SpaceDefaults) {
    setSaved(d)
    setEveryone(d.everyoneAccess)
    setAdmins(d.groups.find((g) => g.groupId === TESRIA_ADMINS_GROUP_ID)?.level ?? null)
    setCustom(d.groups.filter((g) => g.groupId !== TESRIA_ADMINS_GROUP_ID)
      .map((g) => ({ groupId: g.groupId, name: g.name, level: g.level })))
  }

  useEffect(() => {
    api.spaces.defaults().then(adopt).catch((err: unknown) =>
      setError(err instanceof ApiError ? err.message : 'Could not load the defaults.'))
    api.groups.list().then(setGroups).catch(() => {})
  }, [])

  const choosable = groups.filter((g) => !g.builtIn && !g.spaceId && !custom.some((c) => c.groupId === g.id))

  async function save() {
    setBusy(true)
    setError(null)
    setNotice(null)
    try {
      const next = await api.admin.spaces.setDefaults({
        everyoneAccess: everyone,
        groups: [
          ...(admins == null ? [] : [{ groupId: TESRIA_ADMINS_GROUP_ID, level: admins }]),
          ...custom.map((c) => ({ groupId: c.groupId, level: c.level })),
        ],
      })
      adopt(next)
      setNotice('Saved. Spaces made from now on start here; no existing space changed.')
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not save the defaults.')
    } finally {
      setBusy(false)
    }
  }

  if (!saved) return error ? <p className="alert alert--error">{error}</p> : null

  return (
    <section className="card space-defaults" aria-labelledby="space-defaults-title">
      <h2 id="space-defaults-title">Defaults for New Spaces</h2>
      <p className="muted small">
        Where every new space starts. Whoever creates one can change it in the New Space wizard, and a space’s
        administrators can change it later. Changing this changes no existing space.
      </p>
      {error && <p className="alert alert--error">{error}</p>}
      {notice && <p className="profile__ok" role="status">{notice}</p>}

      <div className="space-defaults__grid">
        <label>
          Everyone signed in
          <span className="glass-select-wrap"><select className="glass-select" value={everyone ?? ''}
            onChange={(e) => setEveryone(e.target.value === '' ? null : Number(e.target.value))}>
            {EVERYONE_LEVELS.map((l) => <option key={l.label} value={l.value ?? ''}>{l.label}</option>)}
          </select></span>
        </label>
        <label>
          Tesria administrators
          <span className="glass-select-wrap"><select className="glass-select" value={admins ?? ''}
            onChange={(e) => setAdmins(e.target.value === '' ? null : Number(e.target.value))}>
            <option value="">No Access</option>
            {GROUP_LEVELS.map((l) => <option key={l.value} value={l.value}>{l.label}</option>)}
          </select></span>
        </label>
      </div>
      {everyone === 2 && <p className="alert alert--warning">{EVERYONE_ADMINISTERS}</p>}
      {admins === 2 && (
        <p className="muted small">
          Tesria’s administrators can then manage every new space: its settings and who gets in. Page restrictions
          still bind them; they can lift one, which is recorded and tells the page’s author.
        </p>
      )}

      {custom.length > 0 && (
        <ul className="space-defaults__groups">
          {custom.map((c) => (
            <li key={c.groupId}>
              <span className="space-defaults__name">{c.name}</span>
              <span className="glass-select-wrap"><select className="glass-select" aria-label={`Level for ${c.name}`} value={c.level}
                onChange={(e) => setCustom(custom.map((x) => x.groupId === c.groupId ? { ...x, level: Number(e.target.value) } : x))}>
                {GROUP_LEVELS.map((l) => <option key={l.value} value={l.value}>{l.label}</option>)}
              </select></span>
              <button type="button" className="link-btn link-btn--danger" onClick={() => setCustom(custom.filter((x) => x.groupId !== c.groupId))}>
                Remove
              </button>
            </li>
          ))}
        </ul>
      )}
      {choosable.length > 0 && (
        <div className="space-defaults__add">
          <span className="glass-select-wrap"><select className="glass-select" aria-label="Add a group" value={adding}
            onChange={(e) => setAdding(e.target.value)}>
            <option value="">Add a group…</option>
            {choosable.map((g) => <option key={g.id} value={g.id}>{g.name}</option>)}
          </select></span>
          <button type="button" className="btn btn--ghost btn--sm" disabled={!adding} onClick={() => {
            const g = groups.find((x) => x.id === adding)
            if (g) setCustom([...custom, { groupId: g.id, name: g.name, level: 0 }])
            setAdding('')
          }}>Add</button>
        </div>
      )}

      <div className="row-gap">
        <button type="button" className="btn btn--primary btn--sm" disabled={busy} onClick={() => void save()}>
          {busy ? 'Saving…' : 'Save Defaults'}
        </button>
      </div>
    </section>
  )
}
