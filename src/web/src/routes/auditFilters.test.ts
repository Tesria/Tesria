import { describe, expect, it } from 'vitest'
import type { AuditEntry } from '../api/client'
import { AUDIT_ACTION_GROUPS, dayRange, foldRuns } from './auditFilters'

let n = 0
const entry = (action: string): AuditEntry => ({
  id: `id-${++n}`, action, targetType: 'instance', targetId: null, actorId: null, actorName: null,
  metadataJson: null, createdAt: '2026-09-29T10:00:00Z', sequence: 1000 - n,
})

describe('dayRange', () => {
  it('reads the fields as whole local days: from the first day’s start to the start of the day after the last', () => {
    const r = dayRange('2026-09-28', '2026-09-28')
    expect(r.from).toBe(new Date(2026, 8, 28).toISOString())
    expect(r.to).toBe(new Date(2026, 8, 29).toISOString())
  })

  it('crosses a month and a year end', () => {
    expect(dayRange('', '2026-09-30').to).toBe(new Date(2026, 9, 1).toISOString())
    expect(dayRange('', '2026-12-31').to).toBe(new Date(2027, 0, 1).toISOString())
  })

  it('leaves out an empty or unreadable field', () => {
    expect(dayRange('', '')).toEqual({})
    expect(dayRange('2026-09-28', '')).toEqual({ from: new Date(2026, 8, 28).toISOString() })
    expect(dayRange('yesterday', '2026-13-45x')).toEqual({})
  })
})

describe('foldRuns', () => {
  it('folds three or more failed sign-ins in a row into one row, keeping order and every entry', () => {
    const list = [entry('page.created'), ...Array.from({ length: 29 }, () => entry('user.login_failed')), entry('settings.updated')]
    const rows = foldRuns(list)
    expect(rows.map((r) => r.kind)).toEqual(['entry', 'run', 'entry'])
    const run = rows[1]
    expect(run.kind === 'run' && run.entries.length).toBe(29)
    const flat = rows.flatMap((r) => (r.kind === 'run' ? r.entries : [r.entry]))
    expect(flat).toEqual(list)
  })

  it('leaves one or two failures as they are, and folds a run at either end', () => {
    const two = [entry('user.login_failed'), entry('user.login_failed'), entry('user.login')]
    expect(foldRuns(two).map((r) => r.kind)).toEqual(['entry', 'entry', 'entry'])
    const ends = [entry('user.login_failed'), entry('user.login_failed'), entry('user.login_failed'), entry('user.login'),
      entry('user.login_failed'), entry('user.login_failed'), entry('user.login_failed'), entry('user.login_failed')]
    expect(foldRuns(ends).map((r) => r.kind)).toEqual(['run', 'entry', 'run'])
    expect(foldRuns([])).toEqual([])
  })

  it('does not fold other repeated actions', () => {
    const list = Array.from({ length: 5 }, () => entry('page.updated'))
    expect(foldRuns(list).every((r) => r.kind === 'entry')).toBe(true)
  })
})

describe('AUDIT_ACTION_GROUPS', () => {
  it('lists each action once, under its own prefix', () => {
    const all = AUDIT_ACTION_GROUPS.flatMap((g) => g.prefixes.flatMap((p) => p.actions))
    expect(new Set(all).size).toBe(all.length)
    for (const g of AUDIT_ACTION_GROUPS)
      for (const p of g.prefixes) {
        expect(p.prefix.endsWith('.')).toBe(true)
        for (const a of p.actions) expect(a.startsWith(p.prefix)).toBe(true)
      }
    const prefixes = AUDIT_ACTION_GROUPS.flatMap((g) => g.prefixes.map((p) => p.prefix))
    expect(new Set(prefixes).size).toBe(prefixes.length)
  })
})
