import { describe, expect, it } from 'vitest'
import type { GroupOverview } from '../api/client'
import { accessSummary, memberCountText, pageChoices, parseEmailList, reasonText, sectionsOf, spaceAnswer } from './groupsList'

function group(over: Partial<GroupOverview>): GroupOverview {
  return {
    id: over.id ?? Math.random().toString(36),
    name: 'G',
    description: null,
    kind: 'custom',
    computed: false,
    spaceId: null,
    spaceKey: null,
    spaceName: null,
    spaceRole: null,
    activeMembers: 0,
    suspendedMembers: 0,
    canManageMembers: true,
    everySpace: false,
    grants: [],
    restrictions: [],
    matches: [],
    ...over,
  }
}

describe('sectionsOf (dev-plan 21.4)', () => {
  it('keeps the order and gives each space its own section with its key', () => {
    const sections = sectionsOf([
      group({ name: 'Owner', kind: 'builtin' }),
      group({ name: 'Global Viewers', kind: 'global' }),
      group({ name: 'Design', kind: 'custom' }),
      group({ name: 'Handbook Admins', kind: 'space', spaceId: 'a', spaceKey: 'HB', spaceName: 'Handbook' }),
      group({ name: 'Handbook Editors', kind: 'space', spaceId: 'a', spaceKey: 'HB', spaceName: 'Handbook' }),
      group({ name: 'Docs Admins', kind: 'space', spaceId: 'b', spaceKey: 'DOCS', spaceName: 'Docs' }),
    ])
    expect(sections.map((s) => s.title)).toEqual(['Built in', 'Global', 'Custom', 'Handbook', 'Docs'])
    expect(sections.map((s) => s.spaceKey)).toEqual([null, null, null, 'HB', 'DOCS'])
    expect(sections[3].groups.map((g) => g.name)).toEqual(['Handbook Admins', 'Handbook Editors'])
  })

  it('keeps two spaces of one name apart', () => {
    const sections = sectionsOf([
      group({ kind: 'space', spaceId: 'a', spaceKey: 'A', spaceName: 'Notes' }),
      group({ kind: 'space', spaceId: 'b', spaceKey: 'B', spaceName: 'Notes' }),
    ])
    expect(sections).toHaveLength(2)
  })

  it('has no sections for no groups', () => {
    expect(sectionsOf([])).toEqual([])
  })
})

describe('memberCountText', () => {
  it('counts active members and names suspended ones apart', () => {
    expect(memberCountText({ activeMembers: 0, suspendedMembers: 0 })).toBe('No members')
    expect(memberCountText({ activeMembers: 1, suspendedMembers: 0 })).toBe('1 member')
    expect(memberCountText({ activeMembers: 3, suspendedMembers: 1 })).toBe('3 members, 1 suspended')
    expect(memberCountText({ activeMembers: 0, suspendedMembers: 2 })).toBe('0 members, 2 suspended')
  })
})

describe('accessSummary', () => {
  it('puts every space first, then grants, then restrictions counted', () => {
    const summary = accessSummary(group({
      everySpace: true,
      grants: [{ spaceId: 'a', spaceKey: 'A', spaceName: 'Alpha', operation: 1 }],
      restrictions: [{ pageId: 'p', pageTitle: 'P', spaceKey: 'A', operation: 0 }],
    }))
    expect(summary).toEqual({ shown: ['View on every space', 'Edit on Alpha', 'named on 1 page restriction'], more: 0 })
  })

  it('shows the first few and counts the rest', () => {
    const grants = ['A', 'B', 'C', 'D', 'E'].map((n) => ({ spaceId: n, spaceKey: n, spaceName: n, operation: 0 }))
    expect(accessSummary(group({ grants }), 3)).toEqual({ shown: ['View on A', 'View on B', 'View on C'], more: 2 })
  })

  it('is empty for a group that grants nothing', () => {
    expect(accessSummary(group({}))).toEqual({ shown: [], more: 0 })
  })
})

describe('parseEmailList', () => {
  it('takes commas, semicolons, lines and spaces', () => {
    expect(parseEmailList('a@x.com, b@x.com;c@x.com\nd@x.com e@x.com').emails)
      .toEqual(['a@x.com', 'b@x.com', 'c@x.com', 'd@x.com', 'e@x.com'])
  })

  it('reads the address out of a name and angle brackets, as mail programs copy them', () => {
    expect(parseEmailList('Jane Doe <jane@x.com>, "Bob, Jr." <bob@x.com>')).toEqual({
      emails: ['jane@x.com', 'bob@x.com'],
      invalid: [],
    })
  })

  it('keeps repeats once, ignoring case', () => {
    expect(parseEmailList('A@x.com a@X.com').emails).toEqual(['A@x.com'])
  })

  it('says what it could not read instead of dropping it', () => {
    expect(parseEmailList('Mei, sam@, <nobody>')).toEqual({ emails: [], invalid: ['Mei', 'sam@', '<nobody>'] })
  })

  it('strips quotes and trailing punctuation around a bare address', () => {
    expect(parseEmailList('(a@x.com). "b@x.com"').emails).toEqual(['a@x.com', 'b@x.com'])
  })

  it('is empty for nothing pasted', () => {
    expect(parseEmailList('  \n ,; ')).toEqual({ emails: [], invalid: [] })
  })
})

describe('pageChoices', () => {
  const tree = [
    { id: '1', title: 'Guide', position: 0, children: [
      { id: '2', title: 'Install', position: 0, children: [{ id: '3', title: 'Install on a Mac', position: 0, children: [] }] },
    ] },
    { id: '4', title: 'FAQ', position: 1, children: [] },
  ]

  it('lists the tree in order with depths', () => {
    expect(pageChoices(tree).map((p) => [p.title, p.depth])).toEqual([
      ['Guide', 0], ['Install', 1], ['Install on a Mac', 2], ['FAQ', 0],
    ])
  })

  it('keeps matching titles at any depth, flat, ignoring case', () => {
    expect(pageChoices(tree, ' install ').map((p) => [p.title, p.depth])).toEqual([['Install', 0], ['Install on a Mac', 0]])
    expect(pageChoices(tree, 'nothing')).toEqual([])
  })
})

describe('reasonText and spaceAnswer', () => {
  const reason = { level: 1, counts: true, label: 'Handbook Editors', groupId: 'g', groupKind: 'space' as const, recoveredAt: null }

  it('says each kind of reason in words', () => {
    expect(reasonText({ ...reason, kind: 'group' })).toBe('They are in Handbook Editors, which has Edit here.')
    expect(reasonText({ ...reason, kind: 'everyone', label: 'Everyone signed in' })).toBe('Everyone signed in may edit this space.')
    expect(reasonText({ ...reason, kind: 'everyone', label: 'Everyone signed in', level: 2 })).toBe('Everyone signed in may administer this space.')
    expect(reasonText({ ...reason, kind: 'direct' })).toBe('They were given Edit here by name.')
    expect(reasonText({ ...reason, kind: 'global', label: 'Global Viewers', level: 0 }))
      .toBe('They are in Global Viewers, which can view every space.')
  })

  it('answers with the highest level, or that they cannot see it', () => {
    const base = { person: { id: 'u', displayName: 'Mei', email: null, active: true }, space: { id: 's', key: 'HB', name: 'Handbook', everyoneAccess: null, archived: false } }
    expect(spaceAnswer({ ...base, level: null })).toBe('Mei cannot see Handbook.')
    expect(spaceAnswer({ ...base, level: 0 })).toBe('Mei can view Handbook.')
    expect(spaceAnswer({ ...base, level: 2 })).toBe('Mei can administer Handbook.')
  })
})
