import { describe, expect, it } from 'vitest'
import { audienceSentence, everyoneAccessOf, groupMembers, keyProblem, suggestKey } from './spaceWizard'

describe('suggestKey (dev-plan 21.2)', () => {
  it('takes the first six letters and digits, upper-cased', () => {
    expect(suggestKey('Team handbook')).toBe('TEAMHA')
    expect(suggestKey('HR')).toBe('HR')
  })

  it('starts with a letter, as a key must', () => {
    expect(suggestKey('2026 plans')).toBe('PLANS')
    expect(suggestKey('42')).toBe('')
  })

  it('drops what a key cannot hold', () => {
    expect(suggestKey('Café & Bar')).toBe('CAFBAR')
    expect(suggestKey('  ')).toBe('')
  })
})

describe('keyProblem', () => {
  it('accepts what the server accepts', () => {
    expect(keyProblem('ENG')).toBeNull()
    expect(keyProblem('A1')).toBeNull()
    expect(keyProblem('A'.repeat(50))).toBeNull()
  })

  it('says what is wrong, in words', () => {
    expect(keyProblem('')).toBe('Choose a key.')
    expect(keyProblem('E')).toMatch(/at least 2/)
    expect(keyProblem('1AB')).toMatch(/starts with a letter/)
    expect(keyProblem('EN G')).toMatch(/only letters and digits/)
    expect(keyProblem('ÉCOLE')).toMatch(/only letters and digits/)
    expect(keyProblem('A'.repeat(51))).toMatch(/at most 50/)
  })
})

describe('everyoneAccessOf', () => {
  it('sends the level, or null for only the groups', () => {
    expect(everyoneAccessOf({ kind: 'everyone', level: 0 })).toBe(0)
    expect(everyoneAccessOf({ kind: 'everyone', level: 2 })).toBe(2)
    expect(everyoneAccessOf({ kind: 'groups' })).toBeNull()
  })

  it('has a sentence for each choice', () => {
    expect(audienceSentence({ kind: 'groups' })).toMatch(/Only the people/)
    expect(audienceSentence({ kind: 'everyone', level: 1 })).toMatch(/read and edit/)
  })
})

describe('groupMembers', () => {
  it('lists the four groups, Admins first, without the creator or anyone twice', () => {
    expect(groupMembers({ 2: ['me', 'a', 'a'], 1: ['b'], 3: ['me'] }, 'me')).toEqual([
      { role: 2, userIds: ['a'] },
      { role: 1, userIds: ['b'] },
      { role: 0, userIds: [] },
      // Only Admins leaves the creator out: they may review as well.
      { role: 3, userIds: ['me'] },
    ])
  })
})
