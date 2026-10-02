/**
 * The browser tab's title (dev-plan 13.1, decision 15), in the owner's
 * words: "Instance Name - Space Name / Page Name".
 *
 * The server's copy is Infrastructure/Branding/BrandTitle.cs, used for the
 * first paint and for exports. Keep the two identical: they share a set of
 * test cases (title.test.ts and BrandingTests.cs) so a tab never shows one
 * title and then another for the same page.
 */

export const DEFAULT_TITLE = 'Tesria'

/**
 * The longest a space's name or a page's title runs in the tab before it
 * ends in "…" (QA t3-R04). Mirrors BrandTitle.MaxPart.
 */
export const MAX_TITLE_PART = 80

/** Cut to MAX_TITLE_PART UTF-16 units with "…", never through a surrogate pair. Mirrors BrandTitle.Shorten. */
function shorten(value: string | null): string | null {
  if (value === null || value.length <= MAX_TITLE_PART) return value
  let cut = MAX_TITLE_PART - 1
  const code = value.charCodeAt(cut - 1)
  if (code >= 0xd800 && code <= 0xdbff) cut--
  return value.slice(0, cut).trimEnd() + '\u2026'
}

function clean(value: string | null | undefined): string | null {
  const v = (value ?? '').trim()
  return v.length === 0 ? null : v
}

export function formatTitle(
  instance: string | null | undefined,
  parts: { space?: string | null; page?: string | null; section?: string | null } = {},
): string {
  const name = clean(instance) ?? DEFAULT_TITLE
  const space = shorten(clean(parts.space))
  const page = shorten(clean(parts.page))
  if (space) return page ? `${name} - ${space} / ${page}` : `${name} - ${space}`
  const section = clean(parts.section)
  return section ? `${name} - ${section}` : name
}

/** The section a route belongs to, for routes outside a space. Mirrors BrandTitle.SectionFor. */
export function sectionFor(pathname: string): string | null {
  const p = pathname.replace(/\/+$/, '').toLowerCase()
  if (p === '' || p === '/') return null
  if (p === '/spaces') return 'Spaces'
  if (p.startsWith('/spaces/')) return null
  if (p.startsWith('/search')) return 'Search'
  if (p.startsWith('/labels')) return 'Labels'
  if (p.startsWith('/profile')) return 'Profile'
  if (p.startsWith('/admin')) return 'Administration'
  if (p === '/login') return 'Sign In'
  if (p === '/register') return 'Create Account'
  if (p === '/recover' || p === '/reset') return 'Reset Your Password'
  if (p === '/confirm-email') return 'Confirm Your Email'
  if (p === '/setup') return 'Set Up'
  if (p === '/welcome') return 'Welcome'
  return null
}
