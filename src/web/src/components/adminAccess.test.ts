import { describe, expect, it } from 'vitest'
import type { AdminSpaceAccessSummary } from '../api/client'
import { accessDetail, accessHeadline, levelWord, needsReview, reasonShort, restrictionNote } from './adminAccess'

const summary = (over: Partial<AdminSpaceAccessSummary> = {}): AdminSpaceAccessSummary => ({
  everyoneAccess: null,
  everyoneAdminConfirmed: false,
  admins: 0,
  editors: 0,
  viewers: 0,
  reviewers: 0,
  otherGrants: 0,
  hasExplicitAdmin: false,
  restrictedPages: 0,
  tesriaAdministrators: false,
  ...over,
})

describe('levelWord', () => {
  it('names each level, and no level', () => {
    expect([0, 1, 2, null].map(levelWord)).toEqual(['View', 'Edit', 'Administer', 'No access'])
  })
})

describe('accessHeadline', () => {
  it('warns only when everyone administers', () => {
    expect(accessHeadline(summary({ everyoneAccess: 2 }))).toEqual({ text: 'Everyone can administer', tone: 'warning' })
    expect(accessHeadline(summary({ everyoneAccess: 1 }))).toEqual({ text: 'Everyone can edit', tone: 'open' })
    expect(accessHeadline(summary({ everyoneAccess: 0 }))).toEqual({ text: 'Everyone can view', tone: 'open' })
    expect(accessHeadline(summary())).toEqual({ text: 'Private', tone: 'private' })
  })
})

describe('accessDetail', () => {
  it('counts the groups with people in them, then other grants and restricted pages', () => {
    expect(accessDetail(summary({ admins: 1, editors: 3, otherGrants: 2, restrictedPages: 1 })))
      .toBe('1 admin, 3 editors · 2 other grants · 1 restricted page')
    expect(accessDetail(summary({ viewers: 2, reviewers: 1 }))).toBe('2 viewers, 1 reviewer')
    expect(accessDetail(summary({ everyoneAccess: 1, admins: 1, tesriaAdministrators: true })))
      .toBe('Tesria administrators administer · 1 admin')
  })

  it('says when nobody is in its groups', () => {
    expect(accessDetail(summary())).toBe('Nobody in its groups')
    expect(accessDetail(summary({ everyoneAccess: 2 }))).toBe('Nobody in its groups yet')
    expect(accessDetail(summary({ otherGrants: 1 }))).toBe('Nobody in its groups yet · 1 other grant')
  })
})

describe('needsReview', () => {
  it('lists only spaces everyone administers that nobody chose', () => {
    expect(needsReview(summary({ everyoneAccess: 2 }))).toBe(true)
    expect(needsReview(summary({ everyoneAccess: 2, everyoneAdminConfirmed: true }))).toBe(false)
    expect(needsReview(summary({ everyoneAccess: 1 }))).toBe(false)
    expect(needsReview(summary())).toBe(false)
    expect(needsReview(null)).toBe(false)
  })
})

describe('reasonShort', () => {
  const reason = { level: 0, counts: true, label: 'Handbook Viewers', groupId: null, groupKind: null, recoveredAt: null }
  it('words each kind of reason', () => {
    expect(reasonShort({ ...reason, kind: 'everyone', label: 'Everyone signed in' })).toBe('Everyone signed in')
    expect(reasonShort({ ...reason, kind: 'group' })).toBe('In Handbook Viewers')
    expect(reasonShort({ ...reason, kind: 'global', label: 'Global Viewers' })).toBe('In Global Viewers')
    expect(reasonShort({ ...reason, kind: 'direct', level: 1, label: 'Sam' })).toBe('Given Edit by name')
  })

  it('says when a reason gives a suspended account nothing', () => {
    expect(reasonShort({ ...reason, kind: 'everyone', counts: false })).toBe('Everyone signed in (suspended: does not count)')
  })
})

describe('restrictionNote', () => {
  it('says whether restricted pages bind them', () => {
    expect(restrictionNote({ level: 0, explicitAdmin: false, restrictedPages: 2 })).toBe('2 restricted pages may be hidden from them.')
    expect(restrictionNote({ level: 2, explicitAdmin: true, restrictedPages: 1 })).toBe('1 restricted page may be hidden from them; they may lift the restrictions.')
    expect(restrictionNote({ level: 2, explicitAdmin: false, restrictedPages: 0 })).toBeNull()
    expect(restrictionNote({ level: null, explicitAdmin: false, restrictedPages: 3 })).toBeNull()
  })
})
