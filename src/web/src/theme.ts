/**
 * Light/dark theming.
 *
 * Three preferences, not two: `system` follows the OS and is the default, so a
 * fresh visitor gets whatever they already asked their machine for. `light`
 * and `dark` are explicit overrides that win in both directions, including
 * "dark while the OS is light", which a `prefers-color-scheme` media query
 * alone can never express.
 *
 * The preference is expressed to CSS as a `data-theme` attribute on <html>:
 *   system → attribute absent, so index.css's media query decides
 *   light   → data-theme="light"
 *   dark    → data-theme="dark"
 *
 * The accent color is a second, independent axis, expressed the same way as
 * a `data-accent` attribute. Absent means the default blue.
 *
 * index.html applies both stored values in a tiny inline script before first
 * paint. Without that, the document renders light for one frame and then flips:
 *the classic dark-mode flash. THE STORAGE KEYS AND THE ATTRIBUTE LOGIC ARE
 * DUPLICATED THERE; change them together.
 */

export type ThemePreference = 'system' | 'light' | 'dark'

export const THEME_STORAGE_KEY = 'tesria-theme'

export const THEME_ORDER: ThemePreference[] = ['system', 'light', 'dark']

export const THEME_LABELS: Record<ThemePreference, string> = {
  system: 'System theme',
  light: 'Light theme',
  dark: 'Dark theme',
}

function isPreference(value: unknown): value is ThemePreference {
  return value === 'system' || value === 'light' || value === 'dark'
}

/**
 * Storage access is wrapped because it throws outright, not just returns
 * null, in a browser configured to block site data, and a theme preference
 * is never worth taking the app down for.
 */
/* ---- the instance's branding (dev-plan 13.1) ----------------------------
   The server writes these onto <html> in the page it sends, so they are
   there before any script runs: the inline script in index.html reads them
   for the first paint, and these read them for everything after. Attributes
   rather than script because that script is allowed by the CSP by its hash
   and must not change per instance. */

function brandAttr(name: string): string | null {
  return typeof document === 'undefined' ? null : document.documentElement.getAttribute(name)
}

/** A theme everyone is held to, or null when people choose. */
export function themeLock(): 'light' | 'dark' | null {
  const v = brandAttr('data-theme-lock')
  return v === 'light' || v === 'dark' ? v : null
}

/** Whether the instance has its own accent color, offered as "brand". */
export function hasBrandAccent(): boolean {
  return Boolean(brandAttr('data-brand-accent-light') || brandAttr('data-brand-accent-dark'))
}

/** An accent everyone is held to, or null when people choose. */
export function accentLock(): AccentName | null {
  const v = brandAttr('data-accent-lock')
  return isAccent(v) ? v : null
}

/** The accent someone gets before they have chosen one. */
export function accentDefault(): AccentName {
  const v = brandAttr('data-accent-default')
  return isAccent(v) ? v : DEFAULT_ACCENT
}

export function readPreference(): ThemePreference {
  const locked = themeLock()
  if (locked) return locked
  try {
    const stored = localStorage.getItem(THEME_STORAGE_KEY)
    return isPreference(stored) ? stored : 'system'
  } catch {
    return 'system'
  }
}

export function savePreference(preference: ThemePreference): void {
  try {
    if (preference === 'system') localStorage.removeItem(THEME_STORAGE_KEY)
    else localStorage.setItem(THEME_STORAGE_KEY, preference)
  } catch {
    // Ignore: the theme still applies for this page's lifetime.
  }
}

export function applyPreference(preference: ThemePreference): void {
  const root = document.documentElement
  if (preference === 'system') root.removeAttribute('data-theme')
  else root.setAttribute('data-theme', preference)
}

/** The next preference in the cycle: what the toggle button switches to. */
export function nextPreference(current: ThemePreference): ThemePreference {
  return THEME_ORDER[(THEME_ORDER.indexOf(current) + 1) % THEME_ORDER.length]
}

/** Which theme `system` currently resolves to. Used only for the toggle's icon. */
export function systemTheme(): 'light' | 'dark' {
  return window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light'
}


/* ---- accent color ------------------------------------------------------ */

export type AccentName = 'blue' | 'teal' | 'green' | 'purple' | 'orange' | 'magenta' | 'brand'

export const ACCENT_STORAGE_KEY = 'tesria-accent'

export const DEFAULT_ACCENT: AccentName = 'blue'

/** Order shown in the picker. Labels are the accessible names for each swatch. */
export const ACCENTS: { name: AccentName; label: string }[] = [
  { name: 'blue', label: 'Blue' },
  { name: 'teal', label: 'Teal' },
  { name: 'green', label: 'Green' },
  { name: 'purple', label: 'Purple' },
  { name: 'orange', label: 'Orange' },
  { name: 'magenta', label: 'Magenta' },
]

/** "brand" only counts while the instance actually has a brand color to show. */
function isAccent(value: unknown): value is AccentName {
  if (value === 'brand') return hasBrandAccent()
  return ACCENTS.some((a) => a.name === value)
}

export function readAccent(): AccentName {
  const locked = accentLock()
  if (locked) return locked
  try {
    const stored = localStorage.getItem(ACCENT_STORAGE_KEY)
    return isAccent(stored) ? stored : accentDefault()
  } catch {
    return accentDefault()
  }
}

/**
 * Stored whatever it is, blue included: once an instance has a default of
 * its own, "blue" is a choice someone made, not the absence of one.
 */
export function saveAccent(accent: AccentName): void {
  try {
    localStorage.setItem(ACCENT_STORAGE_KEY, accent)
  } catch {
    // Ignore: the accent still applies for this page's lifetime.
  }
}

export function applyAccent(accent: AccentName): void {
  const root = document.documentElement
  if (accent === DEFAULT_ACCENT) root.removeAttribute('data-accent')
  else root.setAttribute('data-accent', accent)
}


/* ---- favicon ------------------------------------------------------------ */

/**
 * The accent's hex values, light and dark. This is the source of truth the
 * favicon renders from; index.css declares the same pairs for the DOM. They
 * are duplicated because a favicon is a separate document that can never read
 * the page's custom properties, which is also why a single themeable SVG
 * favicon is not possible, and the color has to be baked in per variant.
 */
export const ACCENT_HEX: Record<Exclude<AccentName, 'brand'>, { light: string; dark: string }> = {
  blue: { light: '#2496ed', dark: '#6cb6f7' },
  teal: { light: '#0b6b82', dark: '#6cc3e0' },
  green: { light: '#1a6c45', dark: '#4bce97' },
  purple: { light: '#5b47ba', dark: '#b8acf6' },
  orange: { light: '#9a4d00', dark: '#fea362' },
  magenta: { light: '#a53a7f', dark: '#f797d2' },
}

/**
 * Tesria's mark, stroked in one color. Kept in step with BrandMark.tsx and
 * public/favicon.svg by hand, three copies of two path strings is cheaper
 * than a build step to share them, but they do have to move together.
 *
 * Stroke is 2 rather than the brand's 1.8 for the same reason the static file
 * uses 2: at 16px the thinner stroke loses the lower layer lines.
 */
function faviconSvg(color: string): string {
  return (
    '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" ' +
    `stroke="${color}" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">` +
    '<path d="M12 3l8 4.5-8 4.5-8-4.5L12 3z"/>' +
    '<path d="M4 12l8 4.5 8-4.5M4 16.5L12 21l8-4.5"/>' +
    '</svg>'
  )
}

/**
 * Repaints the favicon for the given accent.
 *
 * Which half of the pair is used follows the *operating system*, not the app's
 * theme setting: the favicon sits in the browser's tab strip, so it should
 * match that chrome rather than the page. Someone running the app in forced
 * light on a dark-themed desktop wants the light-on-dark mark in their tabs.
 *
 * Generated at runtime rather than shipping six files: the colors then have
 * exactly one definition per theme in this file, six static files would drift
 * from the palette the first time an accent is retuned, and a data URI costs
 * no request. public/favicon.svg stays as the pre-JS default.
 */
export function applyFavicon(accent: AccentName): void {
  // An uploaded favicon is linked in the page by the server; painting over
  // it would replace the instance's own icon with Tesria's mark.
  if (brandAttr('data-brand-favicon')) return
  const brand = {
    light: brandAttr('data-brand-accent-light') ?? brandAttr('data-brand-accent-dark') ?? ACCENT_HEX.blue.light,
    dark: brandAttr('data-brand-accent-dark') ?? brandAttr('data-brand-accent-light') ?? ACCENT_HEX.blue.dark,
  }
  const pair = accent === 'brand' ? brand : ACCENT_HEX[accent as Exclude<AccentName, 'brand'>] ?? ACCENT_HEX.blue
  const color = systemTheme() === 'dark' ? pair.dark : pair.light
  const href = 'data:image/svg+xml,' + encodeURIComponent(faviconSvg(color))

  let link = document.querySelector<HTMLLinkElement>('link[rel="icon"]')
  if (!link) {
    link = document.createElement('link')
    link.rel = 'icon'
    document.head.appendChild(link)
  }
  link.type = 'image/svg+xml'
  link.href = href
}

/**
 * Paints the favicon now and repaints it whenever the OS flips light/dark.
 * Called once at startup, so the accent reaches the tab icon on every route,
 * including the sign-in pages, where the appearance menu isn't mounted.
 */
export function startFaviconSync(): () => void {
  applyFavicon(readAccent())
  const query = window.matchMedia('(prefers-color-scheme: dark)')
  const onChange = () => applyFavicon(readAccent())
  query.addEventListener('change', onChange)
  return () => query.removeEventListener('change', onChange)
}
