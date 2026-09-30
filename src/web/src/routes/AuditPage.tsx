import { useEffect, useMemo, useRef, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { api, type AuditEntry, type Directory } from '../api/client'
import { AUDIT_ACTION_GROUPS, FOLDED_ACTION, dayRange, foldRuns } from './auditFilters'

const PAGE = 50

/**
 * The audit trail: who did what, when. Filtered by action, person and dates,
 * and read further back with Show Older (T7-019). The filters live in the
 * address, so a filtered view can be reloaded or sent to another admin.
 */
export function AuditPage() {
  const [params, setParams] = useSearchParams()
  const action = params.get('action') ?? ''
  const actor = params.get('actor') ?? ''
  const from = params.get('from') ?? ''
  const to = params.get('to') ?? ''
  const filtered = Boolean(action || actor || from || to)

  const [entries, setEntries] = useState<AuditEntry[] | null>(null)
  const [nextBefore, setNextBefore] = useState<number | null>(null)
  const [loadingOlder, setLoadingOlder] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [people, setPeople] = useState<Directory[]>([])
  const [openRuns, setOpenRuns] = useState<Set<string>>(() => new Set())
  // Answers to a request made before the filters changed are dropped.
  const generation = useRef(0)

  const query = useMemo(
    () => ({ take: PAGE, action: action || undefined, actorId: actor || undefined, ...dayRange(from, to) }),
    [action, actor, from, to],
  )

  useEffect(() => {
    const mine = ++generation.current
    setEntries(null)
    setNextBefore(null)
    setError(null)
    api
      .audit(query)
      .then((page) => {
        if (mine !== generation.current) return
        setEntries(page.entries)
        setNextBefore(page.nextBefore)
      })
      .catch((err: unknown) => mine === generation.current && setError(err instanceof Error ? err.message : 'Failed to load.'))
  }, [query])

  useEffect(() => {
    let canceled = false
    // For the person picker only: without it the log still loads.
    api.users.list().then((u) => !canceled && setPeople(u)).catch(() => {})
    return () => {
      canceled = true
    }
  }, [])

  const showOlder = () => {
    if (nextBefore === null) return
    const mine = generation.current
    setLoadingOlder(true)
    setError(null)
    api
      .audit({ ...query, before: nextBefore })
      .then((page) => {
        if (mine !== generation.current) return
        setEntries((e) => [...(e ?? []), ...page.entries])
        setNextBefore(page.nextBefore)
      })
      .catch((err: unknown) => mine === generation.current && setError(err instanceof Error ? err.message : 'Failed to load.'))
      .finally(() => mine === generation.current && setLoadingOlder(false))
  }

  const setFilter = (key: string, value: string) => {
    const next = new URLSearchParams(params)
    if (value) next.set(key, value)
    else next.delete(key)
    setParams(next, { replace: true })
  }
  const clear = () => setParams(new URLSearchParams(), { replace: true })

  // Two people with one name are told apart by email, where it is shown.
  const personLabel = (p: Directory) =>
    people.some((o) => o.id !== p.id && o.displayName === p.displayName) && p.email ? `${p.displayName} (${p.email})` : p.displayName
  const actorMissing = actor !== '' && people.length > 0 && !people.some((p) => p.id === actor)
  const knownAction = action === '' || AUDIT_ACTION_GROUPS.some((g) => g.prefixes.some((p) => p.prefix === action || p.actions.includes(action)))

  const rows = useMemo(() => foldRuns(entries ?? []), [entries])
  const toggleRun = (id: string) =>
    setOpenRuns((s) => {
      const n = new Set(s)
      if (n.has(id)) n.delete(id)
      else n.add(id)
      return n
    })

  const line = (e: AuditEntry, className = 'version') => (
    <li key={e.id} className={className}>
      <span className="badge">{e.action}</span>
      {e.actorId && e.actorName ? (
        <button type="button" className="link-btn audit__actor" title={`Show only ${e.actorName}’s entries`}
          onClick={() => setFilter('actor', e.actorId!)}>{e.actorName}</button>
      ) : (
        <span>{e.actorName ?? 'system'}</span>
      )}
      <span className="muted small">{new Date(e.createdAt).toLocaleString()}</span>
      {e.metadataJson && <span className="version__comment">{e.metadataJson}</span>}
    </li>
  )

  // Rendered inside the admin shell (Admin → Audit).
  return (
    <div className="audit">
      <p className="muted small">
        Changes across all spaces you can see, newest first. Every entry is hash-chained;
        the Security tab verifies the chain.
      </p>

      <div className="audit-filters" role="search" aria-label="Filter the audit log">
        <label className="audit-filters__field audit-filters__field--action">
          Action
          <span className="glass-select-wrap">
            <select className="glass-select" value={action} onChange={(e) => setFilter('action', e.target.value)}>
              <option value="">All Actions</option>
              {!knownAction && <option value={action}>{action}</option>}
              {AUDIT_ACTION_GROUPS.map((g) => (
                <optgroup key={g.label} label={g.label}>
                  {g.prefixes.flatMap((p) => [
                    <option key={p.prefix} value={p.prefix}>{`Every ${p.prefix}* action`}</option>,
                    ...p.actions.map((a) => <option key={a} value={a}>{a}</option>),
                  ])}
                </optgroup>
              ))}
            </select>
          </span>
        </label>
        <label className="audit-filters__field">
          Person
          <span className="glass-select-wrap">
            <select className="glass-select" value={actor} onChange={(e) => setFilter('actor', e.target.value)}>
              <option value="">Anyone</option>
              {actorMissing && <option value={actor}>Someone else</option>}
              {people.map((p) => <option key={p.id} value={p.id}>{personLabel(p)}</option>)}
            </select>
          </span>
        </label>
        <label className="audit-filters__field audit-filters__field--date">
          From
          <input type="date" value={from} max={to || undefined} onChange={(e) => setFilter('from', e.target.value)} />
        </label>
        <label className="audit-filters__field audit-filters__field--date">
          To
          <input type="date" value={to} min={from || undefined} onChange={(e) => setFilter('to', e.target.value)} />
        </label>
        {filtered && (
          <button type="button" className="btn btn--ghost audit-filters__clear" onClick={clear}>Clear</button>
        )}
      </div>

      {error && <p className="alert alert--error">{error}</p>}
      {!entries && !error && <p className="muted">Loading…</p>}
      {entries && entries.length === 0 && (
        <p className="muted">{filtered ? 'No entries match these filters.' : 'Nothing recorded yet.'}</p>
      )}
      <ul className="version-list">
        {rows.flatMap((r) => {
          if (r.kind === 'entry') return [line(r.entry)]
          const first = r.entries[0]
          const last = r.entries[r.entries.length - 1]
          const open = openRuns.has(first.id)
          return [
            <li key={`run-${first.id}`} className="version audit__run">
              <span className="badge">{FOLDED_ACTION}</span>
              <span>{r.entries.length} in a row</span>
              <span className="muted small">
                {new Date(last.createdAt).toLocaleString()} to {new Date(first.createdAt).toLocaleString()}
              </span>
              <button type="button" className="link-btn" aria-expanded={open} onClick={() => toggleRun(first.id)}>
                {open ? 'Hide Them' : 'Show Them'}
              </button>
            </li>,
            ...(open ? r.entries.map((e) => line(e, 'version audit__run-item')) : []),
          ]
        })}
      </ul>
      {entries && entries.length > 0 && (
        <div className="audit__more">
          {nextBefore !== null ? (
            <button type="button" className="btn btn--outline" onClick={showOlder} disabled={loadingOlder}>
              {loadingOlder ? 'Loading…' : 'Show Older'}
            </button>
          ) : (
            <p className="muted small">{filtered ? 'No older entries match these filters.' : 'No older entries.'}</p>
          )}
        </div>
      )}
    </div>
  )
}
