import { describe, expect, it } from 'vitest'
import type { Directory } from '../api/client'
import { matchPeople, personLabel } from './peopleMatch'

const person = (id: string, displayName: string, email: string | null = null): Directory =>
  ({ id, displayName, email, avatarHash: null, avatarVariant: null })

const people = [
  person('1', 'Sam Lee', 'sam@example.com'),
  person('2', 'Priya Natarajan', 'priya@example.com'),
  person('3', 'Jordan Ávila'),
  person('4', 'Samira Khan', 'skhan@example.com'),
]

describe('matchPeople (dev-plan 21.2)', () => {
  it('offers everyone not chosen when nothing is typed', () => {
    expect(matchPeople(people, '', new Set(['2'])).shown.map((p) => p.id)).toEqual(['1', '3', '4'])
  })

  it('needs every word, in any order and case', () => {
    expect(matchPeople(people, 'LEE sam', new Set()).shown.map((p) => p.id)).toEqual(['1'])
    expect(matchPeople(people, 'sam', new Set()).shown.map((p) => p.id)).toEqual(['1', '4'])
  })

  it('finds by address only where the address is shown', () => {
    expect(matchPeople(people, 'skhan', new Set()).shown.map((p) => p.id)).toEqual(['4'])
    expect(matchPeople(people, 'example', new Set()).shown.map((p) => p.id)).toEqual(['1', '2', '4'])
  })

  it('keeps accents as typed', () => {
    expect(matchPeople(people, 'ávila', new Set()).shown.map((p) => p.id)).toEqual(['3'])
  })

  it('ignores spaces around and between words', () => {
    expect(matchPeople(people, '  priya   nat ', new Set()).shown.map((p) => p.id)).toEqual(['2'])
  })

  it('shows at most the limit and counts the rest', () => {
    const many = Array.from({ length: 20 }, (_, i) => person(String(i), `Person ${i}`))
    const { shown, more } = matchPeople(many, 'person', new Set(['0']), 8)
    expect(shown).toHaveLength(8)
    expect(more).toBe(11)
    expect(matchPeople(many, 'person 1', new Set(), 8).more).toBe(3) // 1, 10 to 19: 11 in all
  })

  it('says nobody when everyone is chosen', () => {
    expect(matchPeople(people, '', new Set(['1', '2', '3', '4']))).toEqual({ shown: [], more: 0 })
  })
})

describe('personLabel', () => {
  it('adds the address only when there is one', () => {
    expect(personLabel(people[0])).toBe('Sam Lee (sam@example.com)')
    expect(personLabel(people[2])).toBe('Jordan Ávila')
  })
})
