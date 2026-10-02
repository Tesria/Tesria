import type { InviteSpace } from '../../api/client'

/**
 * The pure half of the invite wizard (dev-plan 21.3), tested in
 * invitePlaces.test.ts without rendering anything.
 */

/** What a place in a space is called in the wizard, by the space group's role (0 Viewers to 3 Reviewers). */
export const PLACE_WORDS: Record<number, string> = { 0: 'Viewer', 1: 'Editor', 2: 'Admin', 3: 'Reviewer' }

/** The order the choice offers them: the most common first. */
export const PLACE_ORDER = [0, 1, 3, 2]

/**
 * Whether the invite must name its address: whoever holds the link gets
 * what it carries, so making an administrator or a reader of every space is
 * bound to one person (the server refuses it otherwise).
 */
export function needsAddress(admin: boolean, globals: readonly string[]): boolean {
  return admin || globals.length > 0
}

/** The group ids to send: the global groups chosen, then one group per space given a place. */
export function inviteGroupIds(
  globals: readonly string[], places: Readonly<Record<string, number | undefined>>, spaces: readonly InviteSpace[],
): string[] {
  const ids = [...new Set(globals)]
  for (const space of spaces) {
    const role = places[space.id]
    if (role === undefined) continue
    const group = space.groups.find((g) => g.role === role)
    if (group) ids.push(group.id)
  }
  return ids
}

/** The spaces given a place, as "Handbook: Editor", in the spaces' own order. */
export function placeSummary(places: Readonly<Record<string, number | undefined>>, spaces: readonly InviteSpace[]): string[] {
  return spaces
    .filter((s) => places[s.id] !== undefined)
    .map((s) => `${s.name}: ${PLACE_WORDS[places[s.id]!]}`)
}

/** Spaces whose name or key holds every word typed; all of them for nothing typed. */
export function filterSpaces(spaces: readonly InviteSpace[], query: string): InviteSpace[] {
  const words = query.toLocaleLowerCase().split(/\s+/).filter(Boolean)
  if (words.length === 0) return [...spaces]
  return spaces.filter((s) => {
    const haystack = `${s.name} ${s.key}`.toLocaleLowerCase()
    return words.every((w) => haystack.includes(w))
  })
}
