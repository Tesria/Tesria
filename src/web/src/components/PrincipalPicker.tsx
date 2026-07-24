import { type FormEvent, useEffect, useState } from 'react'
import { api, PrincipalType, type Directory, type Group } from '../api/client'

type Props = {
  /** Labels for each operation value, indexed by the enum value. */
  operationNames: string[]
  /** Called with the chosen principal and operation. */
  onAdd: (input: { principalType: number; principalId: string; operation: number }) => Promise<void>
  addLabel?: string
}

/**
 * Picks a user or group plus an operation — shared by the space-permission and
 * page-restriction editors so both grant flows behave identically.
 */
export function PrincipalPicker({ operationNames, onAdd, addLabel = 'Add' }: Props) {
  const [type, setType] = useState<number>(PrincipalType.User)
  const [users, setUsers] = useState<Directory[]>([])
  const [groups, setGroups] = useState<Group[]>([])
  const [principalId, setPrincipalId] = useState('')
  const [operation, setOperation] = useState(0)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    Promise.all([api.users.list(), api.groups.list()])
      .then(([u, g]) => {
        setUsers(u)
        setGroups(g)
      })
      .catch(() => {})
  }, [])

  // Reset the selection when switching between users and groups.
  useEffect(() => setPrincipalId(''), [type])

  const options = type === PrincipalType.User
    ? users.map((u) => ({ id: u.id, label: `${u.displayName} (${u.email})` }))
    : groups.map((g) => ({ id: g.id, label: g.name }))

  async function submit(e: FormEvent) {
    e.preventDefault()
    if (!principalId) return
    setBusy(true)
    setError(null)
    try {
      await onAdd({ principalType: type, principalId, operation })
      setPrincipalId('')
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not save.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <form className="principal-picker" onSubmit={submit}>
      <select value={type} onChange={(e) => setType(Number(e.target.value))} aria-label="Principal type">
        <option value={PrincipalType.User}>User</option>
        <option value={PrincipalType.Group}>Group</option>
      </select>

      <select
        value={principalId}
        onChange={(e) => setPrincipalId(e.target.value)}
        aria-label="Principal"
        required
      >
        <option value="">Choose…</option>
        {options.map((o) => (
          <option key={o.id} value={o.id}>{o.label}</option>
        ))}
      </select>

      <select value={operation} onChange={(e) => setOperation(Number(e.target.value))} aria-label="Operation">
        {operationNames.map((name, value) => (
          <option key={name} value={value}>{name}</option>
        ))}
      </select>

      <button type="submit" className="btn btn--primary btn--sm" disabled={busy || !principalId}>
        {busy ? 'Saving…' : addLabel}
      </button>
      {error && <span className="small" style={{ color: 'var(--danger)' }}>{error}</span>}
    </form>
  )
}
