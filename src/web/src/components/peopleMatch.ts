import type { Directory } from '../api/client'

/**
 * Which people a picker offers for what has been typed (dev-plan 21.2): the
 * pure half of PeoplePicker, kept apart so its edge cases are tested
 * (peopleMatch.test.ts) without rendering anything.
 *
 * Every word typed must appear in the name or the address, in any order and
 * any case, so "lee sam" finds Sam Lee. People already chosen are left out.
 * At most `limit` are shown; `more` says how many matched beyond them, so
 * the picker can ask for a narrower search instead of a list of hundreds.
 */
export function matchPeople(
  people: Directory[], query: string, exclude: ReadonlySet<string>, limit = 8,
): { shown: Directory[]; more: number } {
  const words = query.toLocaleLowerCase().split(/\s+/).filter(Boolean)
  const matching = people.filter((p) => {
    if (exclude.has(p.id)) return false
    if (words.length === 0) return true
    const haystack = `${p.displayName} ${p.email ?? ''}`.toLocaleLowerCase()
    return words.every((w) => haystack.includes(w))
  })
  return { shown: matching.slice(0, limit), more: Math.max(0, matching.length - limit) }
}

/** A person's label in a picker: the name, and the address when the caller may see it. */
export function personLabel(p: Pick<Directory, 'displayName' | 'email'>): string {
  return p.email ? `${p.displayName} (${p.email})` : p.displayName
}
