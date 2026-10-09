import type { Group } from '../api/client'

/**
 * The pure half of a space's Permissions tab (dev-plan 21.1), kept apart so
 * its edge cases are tested (spaceAccess.test.ts) without rendering anything.
 */

/** What everyone signed in may do, in the words the tab uses; null is nothing. */
export const EVERYONE_LEVELS: { value: number | null; label: string }[] = [
  { value: null, label: 'No Access' },
  { value: 0, label: 'View' },
  { value: 1, label: 'Edit' },
  { value: 2, label: 'Administer' },
]

/** The label for a level, as the select shows it. */
export function everyoneLabel(level: number | null): string {
  return EVERYONE_LEVELS.find((l) => l.value === level)?.label ?? 'No Access'
}

/**
 * Whether going from one level to another lets more people do more. That is
 * the direction that asks for the password and alerts every administrator,
 * as making a space open always has.
 */
export function widens(from: number | null, to: number | null): boolean {
  return to !== null && (from === null || to > from)
}

/** What a level means, in a sentence for under the select. */
export function everyoneSummary(level: number | null): string {
  switch (level) {
    case 0:
      return 'Everyone with an account can read this space. Only the people below can change it.'
    case 1:
      return 'Everyone with an account can read and edit this space. Only its Admins can manage it.'
    case 2:
      return 'Everyone with an account can read, edit and manage this space. Page restrictions still apply to them, and only the people in Admins, or given Admin below, may lift one.'
    default:
      return 'Only the people in this space’s groups, and anyone given access under Other Access, can open it.'
  }
}

/** The note about the global groups, or null when nobody is in either. */
export function globalReadersNote(viewers: number, reviewers: number): string | null {
  if (viewers > 0 && reviewers > 0) return 'Global Viewers and Global Reviewers can read this space.'
  if (viewers > 0) return 'Global Viewers can read this space.'
  if (reviewers > 0) return 'Global Reviewers can read this space.'
  return null
}

/**
 * The groups a picker offers: global and custom ones always, and a space's
 * own groups only in that space (the server refuses any other). A space's
 * permissions leave its own out too, since they are listed with their fixed
 * access above the picker.
 */
export function pickerGroups(groups: Group[], spaceId: string | null, includeOwn: boolean): Group[] {
  return groups.filter((g) => !g.spaceId || (includeOwn && spaceId !== null && g.spaceId === spaceId))
}

/**
 * What letting everyone administer a space hands every account (dev-plan
 * 21.5), said before they choose it: the widest setting there is, and five
 * of the owner's seven spaces had it without anyone choosing it.
 */
export const EVERYONE_ADMINISTERS =
  'Every account will be able to change this space’s name and settings, archive it, choose who else gets in (all but its Admins), add webhooks, and permanently delete pages from its trash.'
