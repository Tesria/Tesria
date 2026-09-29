/**
 * A code block's color scheme (the owner, 2026-09-29): the usual Tesria
 * colors, or one of the schemes people know from their own editors.
 *
 * Stored as `colorScheme` on the node and rendered as `data-color-scheme`
 * only when it is not the default, so every existing document is unchanged.
 * The colors themselves are CSS custom properties keyed to that attribute
 * (index.css), which the Glass console reads too (glass.css), and which
 * survive an export because HTML, PDF and site exports are captures of the
 * page.
 */
export const CODE_SCHEMES = [
  'default', 'github-light', 'github-dark', 'dracula', 'monokai', 'nord', 'solarized-light', 'solarized-dark',
] as const
export type CodeScheme = (typeof CODE_SCHEMES)[number]

export const CODE_SCHEME_LABELS: Record<CodeScheme, string> = {
  default: 'Default',
  'github-light': 'GitHub Light',
  'github-dark': 'GitHub Dark',
  dracula: 'Dracula',
  monokai: 'Monokai',
  nord: 'Nord',
  'solarized-light': 'Solarized Light',
  'solarized-dark': 'Solarized Dark',
}

export function isCodeScheme(value: unknown): value is CodeScheme {
  return typeof value === 'string' && (CODE_SCHEMES as readonly string[]).includes(value)
}

/** The node attribute. */
export const codeSchemeAttribute = {
  default: 'default' as CodeScheme,
  parseHTML: (element: HTMLElement) => {
    const raw = element.getAttribute('data-color-scheme')
    return isCodeScheme(raw) ? raw : 'default'
  },
  renderHTML: (attributes: { colorScheme?: string }) =>
    isCodeScheme(attributes.colorScheme) && attributes.colorScheme !== 'default'
      ? { 'data-color-scheme': attributes.colorScheme }
      : {},
}

/** For the node view's wrapper: the attribute, or nothing for the default. */
export function codeSchemeData(value: unknown): { 'data-color-scheme'?: CodeScheme } {
  return isCodeScheme(value) && value !== 'default' ? { 'data-color-scheme': value } : {}
}
