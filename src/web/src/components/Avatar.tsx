import { avatarUrl } from '../api/client'
import { AVATAR_COLORS, avatarVariantFor, initialsOf, type AvatarSubject } from './avatarIdentity'

/**
 * A user's avatar: their uploaded image if they have one, otherwise a
 * generated one drawn from their id and name.
 *
 * The generated form is inline SVG rather than a request, so every user has an
 * avatar from the moment they register: no storage, no round trip, and no
 * broken-image state while one loads.
 */
export function Avatar({ subject, size = 28 }: { subject: AvatarSubject; size?: number }) {
  const label = subject.displayName || 'User'

  if (subject.avatarHash) {
    return (
      <img
        className="avatar"
        src={avatarUrl(subject.id, subject.avatarHash)}
        width={size}
        height={size}
        alt=""
        // Decorative: it always sits beside the name it belongs to, so an alt
        // text would just repeat it to a screen reader.
        aria-hidden="true"
      />
    )
  }

  const background = AVATAR_COLORS[avatarVariantFor(subject)]
  const initials = initialsOf(label)

  return (
    <svg
      className="avatar"
      width={size}
      height={size}
      viewBox="0 0 40 40"
      role="img"
      aria-hidden="true"
      focusable="false"
    >
      <rect width="40" height="40" rx="20" fill={background} />
      <text
        x="20"
        y="20"
        textAnchor="middle"
        dominantBaseline="central"
        fill="#ffffff"
        fontSize={initials.length > 1 ? 15 : 18}
        fontWeight="600"
        // Inherit the app's stack rather than naming fonts here, so initials
        // match the surrounding UI on every platform.
        fontFamily="inherit"
      >
        {initials}
      </text>
    </svg>
  )
}
