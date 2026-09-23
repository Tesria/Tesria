/**
 * The data behind a space's icon (dev-plan 6). Separate from `SpaceIcon.tsx`
 * so that file exports only a component, which is what React Fast Refresh
 * needs: the same split as `avatarIdentity.ts`.
 */
import { AVATAR_COLORS, stableIndex } from './avatarIdentity'

/** Matches Api.Domain.SpaceIconKind. */
export const SpaceIconKind = { None: 0, Emoji: 1, Image: 2 } as const
export type SpaceIconKind = (typeof SpaceIconKind)[keyof typeof SpaceIconKind]

/**
 * The tile colors. Deliberately the same twelve as generated avatars: the
 * job is identical (a white glyph on a dark tile, legible against both page
 * grounds) and two palettes doing one job would drift apart.
 */
export const SPACE_ICON_COLORS = AVATAR_COLORS

export type SpaceIconSubject = {
  key: string
  iconKind: SpaceIconKind
  iconValue: string | null
  iconColor: number | null
}

/** The chosen tile color, or a stable one derived from the key. */
export function spaceColorFor(space: SpaceIconSubject): number {
  const chosen = space.iconColor
  if (chosen != null && chosen >= 0 && chosen < SPACE_ICON_COLORS.length) return chosen
  return stableIndex(space.key, SPACE_ICON_COLORS.length)
}

/**
 * The letter on a generated tile: the first character of the key.
 *
 * Keys are `^[A-Z][A-Z0-9]{1,49}$` server-side, so this is always a plain
 * ASCII letter: no grapheme segmentation needed here, unlike display names.
 */
export function spaceInitial(key: string): string {
  return (key[0] ?? '?').toUpperCase()
}

/** Where an uploaded icon lives. The hash makes each version its own URL. */
export const spaceIconUrl = (key: string, hash: string) =>
  `/api/media/space-icons/${encodeURIComponent(key)}?v=${hash}`
