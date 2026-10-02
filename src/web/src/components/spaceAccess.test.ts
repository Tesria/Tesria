import { describe, expect, it } from 'vitest'
import type { Group } from '../api/client'
import { everyoneLabel, globalReadersNote, pickerGroups, widens } from './spaceAccess'

describe('widens (dev-plan 21.1)', () => {
  it('counts anything from No Access as widening', () => {
    expect(widens(null, 0)).toBe(true)
    expect(widens(null, 2)).toBe(true)
  })

  it('counts a higher level as widening and a lower one as not', () => {
    expect(widens(0, 1)).toBe(true)
    expect(widens(1, 2)).toBe(true)
    expect(widens(2, 1)).toBe(false)
    expect(widens(1, 0)).toBe(false)
  })

  it('never counts No Access, or staying put, as widening', () => {
    expect(widens(2, null)).toBe(false)
    expect(widens(null, null)).toBe(false)
    expect(widens(1, 1)).toBe(false)
  })
})

describe('everyoneLabel', () => {
  it('names each level, and nothing as No Access', () => {
    expect([null, 0, 1, 2].map(everyoneLabel)).toEqual(['No Access', 'View', 'Edit', 'Administer'])
    expect(everyoneLabel(7)).toBe('No Access')
  })
})

describe('globalReadersNote', () => {
  it('says nothing when both global groups are empty', () => {
    expect(globalReadersNote(0, 0)).toBeNull()
  })

  it('names whichever have members', () => {
    expect(globalReadersNote(2, 0)).toBe('Global Viewers can read this space.')
    expect(globalReadersNote(0, 1)).toBe('Global Reviewers can read this space.')
    expect(globalReadersNote(1, 1)).toBe('Global Viewers and Global Reviewers can read this space.')
  })
})

describe('pickerGroups', () => {
  const group = (id: string, spaceId?: string): Group =>
    ({ id, name: id, description: null, memberCount: 0, spaceId: spaceId ?? null })
  const groups = [group('users'), group('team'), group('a-viewers', 'A'), group('b-viewers', 'B')]

  it('offers global and custom groups, never another space’s', () => {
    expect(pickerGroups(groups, 'A', true).map((g) => g.id)).toEqual(['users', 'team', 'a-viewers'])
  })

  it('leaves the space’s own out when asked to', () => {
    expect(pickerGroups(groups, 'A', false).map((g) => g.id)).toEqual(['users', 'team'])
  })

  it('offers no space’s groups when it does not know the space', () => {
    expect(pickerGroups(groups, null, true).map((g) => g.id)).toEqual(['users', 'team'])
  })
})
