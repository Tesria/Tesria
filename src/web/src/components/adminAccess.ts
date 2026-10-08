import type { AccessExplanation, AdminSpaceAccessSummary } from '../api/client'

/**
 * The words for who has access to what in Administration (dev-plan 21.5),
 * apart from the pages so their cases are tested (adminAccess.test.ts).
 * Spaces and Users read access in these words, and they match the space's
 * Permissions tab: "Everyone signed in", then the space's own groups.
 */

const VERB = ['view', 'edit', 'administer']

/** "Administer", "Edit", "View", or "No access". */
export function levelWord(level: number | null): string {
  if (level == null) return 'No access'
  const verb = VERB[level] ?? 'view'
  return verb[0].toUpperCase() + verb.slice(1)
}

function count(n: number, one: string, many = `${one}s`): string {
  return `${n} ${n === 1 ? one : many}`
}

export type Tone = 'warning' | 'open' | 'private'

/**
 * The first line of a space's Access cell: open to everyone at a level, or
 * private. Everyone administering is a warning, because every account can
 * then change the space's settings and who else gets in.
 */
export function accessHeadline(a: AdminSpaceAccessSummary): { text: string; tone: Tone } {
  if (a.everyoneAccess == null) return { text: 'Private', tone: 'private' }
  return {
    text: `Everyone can ${VERB[a.everyoneAccess] ?? 'view'}`,
    tone: a.everyoneAccess === 2 ? 'warning' : 'open',
  }
}

/**
 * The second line: who is in its groups, other grants, restricted pages.
 * "1 admin, 3 editors · 2 other grants · 1 restricted page". Groups with
 * nobody in them are left out; a private space nobody is in says so.
 */
export function accessDetail(a: AdminSpaceAccessSummary): string {
  const groups = [
    a.admins > 0 && count(a.admins, 'admin'),
    a.editors > 0 && count(a.editors, 'editor'),
    a.viewers > 0 && count(a.viewers, 'viewer'),
    a.reviewers > 0 && count(a.reviewers, 'reviewer'),
  ].filter(Boolean) as string[]
  const parts: string[] = []
  if (groups.length > 0) parts.push(groups.join(', '))
  else if (a.everyoneAccess == null && a.otherGrants === 0) parts.push('Nobody in its groups')
  else parts.push('Nobody in its groups yet')
  if (a.otherGrants > 0) parts.push(count(a.otherGrants, 'other grant'))
  if (a.restrictedPages > 0) parts.push(count(a.restrictedPages, 'restricted page'))
  return parts.join(' · ')
}

/**
 * Whether the open-space review lists it: everyone may administer it and
 * nobody chose that (every space open since before 0.9, or one an API call
 * created without saying).
 */
export function needsReview(a: AdminSpaceAccessSummary | null): boolean {
  return a != null && a.everyoneAccess === 2 && !a.everyoneAdminConfirmed
}

type Reason = AccessExplanation['reasons'][number]

/** One reason, short, for the per-person table: "In Handbook Viewers". */
export function reasonShort(r: Reason): string {
  const text = r.kind === 'everyone' ? 'Everyone signed in'
    : r.kind === 'direct' ? `Given ${levelWord(r.level)} by name`
    : `In ${r.label}`
  return r.counts ? text : `${text} (suspended: does not count)`
}

/**
 * The note under a person's level in one space: whether page restrictions
 * bind them. Only an explicit administrator (in its Admins, or given Admin)
 * sees past them; everyone else may still find some pages hidden.
 */
export function restrictionNote(row: { level: number | null; explicitAdmin: boolean; restrictedPages: number }): string | null {
  if (row.level == null || row.restrictedPages === 0) return null
  if (row.explicitAdmin) return `Sees past its ${count(row.restrictedPages, 'restricted page')}.`
  return `${count(row.restrictedPages, 'restricted page')} may still be hidden from them.`
}
