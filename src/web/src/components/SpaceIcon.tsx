import {
  SPACE_ICON_COLORS,
  SpaceIconKind,
  spaceColorFor,
  spaceIconUrl,
  spaceInitial,
  type SpaceIconSubject,
} from './spaceIconIdentity'

/**
 * A space's icon: an uploaded picture, a chosen emoji, or, the default,
 * the key's first letter on a coloured tile. Every space therefore has one
 * from the moment it is created, with no storage and no round trip, the same
 * reasoning as generated avatars.
 *
 * Rounded *squares*, where avatars are circles. That is the only thing
 * telling a reader at a glance whether a small tile is a person or a place,
 * so it is a deliberate distinction rather than a style choice.
 *
 * Decorative in every position it is used: the space name always sits beside
 * it, so an alt text would only repeat that to a screen reader.
 */
export function SpaceIcon({ space, size = 24 }: { space: SpaceIconSubject; size?: number }) {
  const radius = Math.round(size * 0.22)

  if (space.iconKind === SpaceIconKind.Image && space.iconValue) {
    return (
      <img
        className="space-icon"
        src={spaceIconUrl(space.key, space.iconValue)}
        width={size}
        height={size}
        style={{ borderRadius: radius }}
        alt=""
        aria-hidden="true"
      />
    )
  }

  const background = SPACE_ICON_COLORS[spaceColorFor(space)]

  if (space.iconKind === SpaceIconKind.Emoji && space.iconValue) {
    return (
      <span
        className="space-icon space-icon--emoji"
        style={{ width: size, height: size, borderRadius: radius, fontSize: Math.round(size * 0.62) }}
        aria-hidden="true"
      >
        {space.iconValue}
      </span>
    )
  }

  return (
    <svg
      className="space-icon"
      width={size}
      height={size}
      viewBox="0 0 40 40"
      role="img"
      aria-hidden="true"
      focusable="false"
    >
      <rect width="40" height="40" rx="9" fill={background} />
      <text
        x="20"
        y="20"
        textAnchor="middle"
        dominantBaseline="central"
        fill="#ffffff"
        fontSize="19"
        fontWeight="700"
        fontFamily="inherit"
      >
        {spaceInitial(space.key)}
      </text>
    </svg>
  )
}
