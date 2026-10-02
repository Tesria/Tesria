import { describe, expect, it } from 'vitest'
import type { InviteSpace } from '../../api/client'
import { filterSpaces, inviteGroupIds, needsAddress, placeSummary } from './invitePlaces'

const space = (id: string, name: string, key: string): InviteSpace => ({
  id, name, key, groups: [2, 1, 0, 3].map((role) => ({ id: `${id}-${role}`, role })),
})
const spaces = [space('a', 'Handbook', 'HB'), space('b', 'Engineering', 'ENG'), space('c', 'Family Recipes', 'FOOD')]

describe('needsAddress (dev-plan 21.3)', () => {
  it('is needed for an administrator or a global group, not otherwise', () => {
    expect(needsAddress(false, [])).toBe(false)
    expect(needsAddress(true, [])).toBe(true)
    expect(needsAddress(false, ['g'])).toBe(true)
  })
})

describe('inviteGroupIds', () => {
  it('sends the global groups, then the chosen group of each space', () => {
    expect(inviteGroupIds(['gv', 'gv'], { a: 1, c: 3 }, spaces)).toEqual(['gv', 'a-1', 'c-3'])
  })

  it('leaves out spaces with no place, and places in spaces no longer offered', () => {
    expect(inviteGroupIds([], { b: undefined, gone: 0 }, spaces)).toEqual([])
  })
})

describe('placeSummary', () => {
  it('names each place in the spaces’ order', () => {
    expect(placeSummary({ c: 0, a: 2 }, spaces)).toEqual(['Handbook: Admin', 'Family Recipes: Viewer'])
  })
})

describe('filterSpaces', () => {
  it('matches the name or the key, every word', () => {
    expect(filterSpaces(spaces, 'eng').map((s) => s.id)).toEqual(['b'])
    expect(filterSpaces(spaces, 'food').map((s) => s.id)).toEqual(['c'])
    expect(filterSpaces(spaces, 'recipes family').map((s) => s.id)).toEqual(['c'])
    expect(filterSpaces(spaces, ' ').map((s) => s.id)).toEqual(['a', 'b', 'c'])
  })
})
