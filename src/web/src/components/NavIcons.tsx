import type { SVGProps } from 'react'

/**
 * The space sidebar's icons.
 *
 * Drawn in the same language as `BrandMark` and the editor's icon set (a
 * 24×24 viewBox, 1.8px stroke, round caps and joins, `fill: none`) so the
 * navigation reads as part of this app rather than as whatever glyphs the
 * operating system happens to ship. These were emoji (📑 ⚙ 🔒 🪝 🗑), which
 * render as small full-color pictures: a different visual weight on every
 * platform, and nothing to do with the accent color.
 *
 * Everything is `currentColor`, so `.nav-icon` can point them at
 * `--primary` and they follow both the theme and the chosen accent for
 * free: exactly the trick `.brand__mark` uses.
 */
function NavIcon({ children, ...props }: SVGProps<SVGSVGElement>) {
  return (
    <svg
      className="nav-icon"
      width="16"
      height="16"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="1.8"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
      focusable="false"
      {...props}
    >
      {children}
    </svg>
  )
}

/**
 * Pages: a sheet of paper with a folded corner and two lines of text.
 *
 * The obvious drawing for "pages" rather than a clever one. The brand's
 * rhombus was tried here first and read as a shape, not as a document:
 * it belongs to the logo, where the stack gives it its meaning.
 *
 * Two body lines, not three: at 16px a third crowds the fold.
 */
export function PagesIcon() {
  return (
    <NavIcon>
      <path d="M13.5 3H7a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2V8.5L13.5 3z" />
      <path d="M13.5 3v4a1.5 1.5 0 0 0 1.5 1.5h4" />
      <path d="M8.75 13h6.5M8.75 16.75h4.5" />
    </NavIcon>
  )
}

/**
 * Settings: a cog as body, hub and eight teeth, rather than one long
 * traced outline: at 16px the traced kind turns to mush, and radial ticks
 * stay legible.
 */
export function SettingsIcon() {
  // Eight teeth at 45°, each a tick crossing the body from r=7 to r=9.2.
  const teeth = Array.from({ length: 8 }, (_, i) => {
    const angle = (i * Math.PI) / 4
    const [cos, sin] = [Math.cos(angle), Math.sin(angle)]
    const from = `${(12 + 7 * cos).toFixed(2)} ${(12 + 7 * sin).toFixed(2)}`
    const to = `${(12 + 9.2 * cos).toFixed(2)} ${(12 + 9.2 * sin).toFixed(2)}`
    return `M${from}L${to}`
  }).join('')

  return (
    <NavIcon>
      <circle cx="12" cy="12" r="7" />
      <circle cx="12" cy="12" r="2.8" />
      <path d={teeth} />
    </NavIcon>
  )
}

/** Permissions: a padlock, shackle closed. */
export function PermissionsIcon() {
  return (
    <NavIcon>
      <rect x="4.5" y="10" width="15" height="10.5" rx="2.2" />
      <path d="M8 10V7.5a4 4 0 0 1 8 0V10" />
      <path d="M12 14.2v2.6" />
    </NavIcon>
  )
}

/**
 * Webhooks: one event fanning out to its subscribers.
 *
 * A hook (the emoji it replaces) says nothing about what a webhook does; a
 * source connected to two destinations does, and stays readable at 16px
 * where a literal hook would not.
 */
export function WebhooksIcon() {
  return (
    <NavIcon>
      <circle cx="5.5" cy="12" r="2.2" />
      <circle cx="18.5" cy="6.5" r="2.2" />
      <circle cx="18.5" cy="17.5" r="2.2" />
      <path d="M7.5 11.1l9-3.7M7.5 12.9l9 3.7" />
    </NavIcon>
  )
}

/** Trash: a bin with its lid, handle and two ribs. */
export function TrashIcon() {
  return (
    <NavIcon>
      <path d="M4 7h16" />
      <path d="M9.5 7V5.6A1.6 1.6 0 0 1 11.1 4h1.8a1.6 1.6 0 0 1 1.6 1.6V7" />
      <path d="M6.2 7l.8 12.1A2 2 0 0 0 9 21h6a2 2 0 0 0 2-1.9L17.8 7" />
      <path d="M10.4 11v6M13.6 11v6" />
    </NavIcon>
  )
}

/**
 * Watching: an eye.
 *
 * One icon for both states: the button's own label says which way the
 * toggle is pointing ("Watching" / "Watch this page"), so a struck-through
 * variant would be describing the state in one breath and the action in the
 * next.
 */
export function WatchIcon() {
  return (
    <NavIcon>
      <path d="M2.5 12s3.6-6.2 9.5-6.2 9.5 6.2 9.5 6.2-3.6 6.2-9.5 6.2S2.5 12 2.5 12z" />
      <circle cx="12" cy="12" r="2.7" />
    </NavIcon>
  )
}

/** A panel with its left side marked: hide or show the sidebar. */
export function SidebarIcon() {
  return (
    <NavIcon>
      <rect x="3.5" y="4.5" width="17" height="15" rx="2" />
      <path d="M9 4.5v15" />
    </NavIcon>
  )
}
