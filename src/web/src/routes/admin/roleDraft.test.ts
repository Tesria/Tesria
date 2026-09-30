import { describe, expect, it } from 'vitest'
import { changes, rebase, toDraft } from './roleDraft'

const sorted = (s: Set<string>) => [...s].sort()

describe('rebase (T7-024)', () => {
  it("keeps someone else's saved change and adds mine on top", () => {
    const loaded = [{ id: 'user', permissions: ['export'] }]
    const draft = { user: new Set(['export', 'delete_any']) } // I tick delete_any
    const fresh = [{ id: 'user', permissions: ['export', 'invites'] }] // they gave invites

    const next = rebase(loaded, draft, fresh)
    expect(sorted(next.user)).toEqual(['delete_any', 'export', 'invites'])
    expect(changes(fresh[0], next.user)).toEqual({ added: ['delete_any'], removed: [] })
  })

  it('applies my clears, and leaves a right they removed removed', () => {
    const loaded = [{ id: 'user', permissions: ['a', 'b', 'c'] }]
    const draft = { user: new Set(['a', 'c']) } // I clear b
    const fresh = [{ id: 'user', permissions: ['a', 'b'] }] // they cleared c

    const next = rebase(loaded, draft, fresh)
    expect(sorted(next.user)).toEqual(['a'])
  })

  it('an untouched role simply becomes what it is now', () => {
    const loaded = [{ id: 'user', permissions: ['a'] }, { id: 'admin', permissions: ['x'] }]
    const draft = { ...toDraft(loaded), user: new Set(['a', 'b']) }
    const fresh = [{ id: 'user', permissions: ['a'] }, { id: 'admin', permissions: ['x', 'y'] }]

    const next = rebase(loaded, draft, fresh)
    expect(sorted(next.admin)).toEqual(['x', 'y'])
    expect(changes(fresh[1], next.admin)).toEqual({ added: [], removed: [] })
  })

  it('drops a role that was deleted and starts a new one as it is', () => {
    const loaded = [{ id: 'gone', permissions: ['a'] }]
    const draft = { gone: new Set(['a', 'b']) }
    const fresh = [{ id: 'new', permissions: ['c'] }]

    const next = rebase(loaded, draft, fresh)
    expect(Object.keys(next)).toEqual(['new'])
    expect(sorted(next.new)).toEqual(['c'])
  })

  it('a change they already made is no change of mine', () => {
    const loaded = [{ id: 'user', permissions: ['a'] }]
    const draft = { user: new Set(['a', 'b']) }
    const fresh = [{ id: 'user', permissions: ['a', 'b'] }]

    const next = rebase(loaded, draft, fresh)
    expect(changes(fresh[0], next.user)).toEqual({ added: [], removed: [] })
  })
})
