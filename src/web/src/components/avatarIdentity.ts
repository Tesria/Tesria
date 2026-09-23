/**
 * The data behind a generated avatar: colors, variant selection and
 * initials. Separate from `Avatar.tsx` so that file exports only a
 * component, which is what React Fast Refresh needs to work properly.
 */

/**
 * The twelve generated-avatar backgrounds. Every one carries white text at
 * 4.5:1 or better (measured, not judged by eye), and all are dark enough to
 * read against both the light and dark page grounds, so a generated avatar
 * needs no per-theme treatment at all.
 *
 * Deliberately not the editor's `palette.ts`: those are content colors an
 * author picks for a table cell or a highlight, and they are light tints
 * chosen to sit *behind* dark body text. These are the opposite job.
 */
export const AVATAR_COLORS = [
  '#0c66e4', '#0b6b82', '#1a6c45', '#5b47ba',
  '#9a4d00', '#a53a7f', '#216e4e', '#ae2e24',
  '#4c3f9e', '#0f6674', '#7a5c00', '#44505e',
] as const

export type AvatarSubject = {
  id: string
  displayName: string
  avatarHash?: string | null
  /** Explicit choice from the picker; null means "derive it from the id". */
  avatarVariant?: number | null
}

/**
 * A stable number in [0, modulo) for a string.
 *
 * FNV-1a rather than summing char codes: two ids that are anagrams of each
 * other would collide under a sum, and user ids are hex GUIDs, which share
 * an alphabet and a length: exactly the case where a weak hash clusters.
 *
 * Exported because space icons (dev-plan 6) pick a tile color the same way,
 * from a space key rather than a user id.
 */
export function stableIndex(value: string, modulo: number): number {
  let hash = 0x811c9dc5
  for (let i = 0; i < value.length; i++) {
    hash ^= value.charCodeAt(i)
    hash = Math.imul(hash, 0x01000193) >>> 0
  }
  return hash % modulo
}

/** Which of the twelve a user gets: their choice, or a stable one from their id. */
export function avatarVariantFor(subject: AvatarSubject): number {
  const chosen = subject.avatarVariant
  if (chosen != null && chosen >= 0 && chosen < AVATAR_COLORS.length) return chosen
  return stableIndex(subject.id, AVATAR_COLORS.length)
}

/**
 * Up to two initials. Takes the first letter of the first and last words, so
 * "Ada Lovelace" reads as AL, and a single word gives one letter rather than
 * two consecutive ones, which look like an acronym that isn't there.
 *
 * `Intl.Segmenter` is used so an emoji or a surrogate pair counts as one
 * character; `[0]` on a JS string would split it and render a replacement box.
 */
export function initialsOf(displayName: string): string {
  const words = displayName.trim().split(/\s+/).filter(Boolean)
  if (words.length === 0) return '?'

  const firstOf = (word: string) => {
    const segmenter = new Intl.Segmenter(undefined, { granularity: 'grapheme' })
    const [first] = segmenter.segment(word)
    return first ? first.segment.toUpperCase() : ''
  }

  return words.length === 1
    ? firstOf(words[0])
    : firstOf(words[0]) + firstOf(words[words.length - 1])
}
