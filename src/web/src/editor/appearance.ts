/**
 * A block's own style (0.8.1): follow the reader's theme, or always flat, or
 * always glass. On status, charts, diagrams and code blocks.
 *
 * Stored as `appearance` on the node and rendered as `data-appearance` only
 * when it is not "theme", so every existing document is unchanged. The glass
 * look itself is CSS keyed to that attribute (glass.css), which is what
 * makes an override survive an export: HTML, PDF and site exports are
 * captures of the page, where no reader's style applies, so "theme" comes out
 * flat and an explicit choice comes out as chosen.
 */
export const APPEARANCES = ['theme', 'flat', 'glass'] as const
export type Appearance = (typeof APPEARANCES)[number]

export const APPEARANCE_LABELS: Record<Appearance, string> = {
  theme: 'Theme default',
  flat: 'Flat',
  glass: 'Glass',
}

export function isAppearance(value: unknown): value is Appearance {
  return typeof value === 'string' && (APPEARANCES as readonly string[]).includes(value)
}

/** The node attribute, shared by every node that has one. */
export const appearanceAttribute = {
  default: 'theme' as Appearance,
  parseHTML: (element: HTMLElement) => {
    const raw = element.getAttribute('data-appearance')
    return isAppearance(raw) ? raw : 'theme'
  },
  renderHTML: (attributes: { appearance?: string }) =>
    isAppearance(attributes.appearance) && attributes.appearance !== 'theme'
      ? { 'data-appearance': attributes.appearance }
      : {},
}

/** For a node view's wrapper: the attribute, or nothing when it follows the theme. */
export function appearanceData(value: unknown): { 'data-appearance'?: Appearance } {
  return isAppearance(value) && value !== 'theme' ? { 'data-appearance': value } : {}
}
