/**
 * Table of contents options: Confluence Cloud's macro parameters
 * (support.atlassian.com, "Insert the table of contents macro").
 *
 * Every option has a default equal to how a table of contents rendered
 * before options existed, so a document saved without them is unchanged.
 * The export renderer (ProseMirrorRenderer.cs, TocOptions) implements the
 * same rules; ExportTests pins them. Change both or neither.
 */

export const TOC_BULLET_STYLES = ['bullet', 'mixed', 'circle', 'square', 'numbered', 'none'] as const
export type TocBulletStyle = (typeof TOC_BULLET_STYLES)[number]

export const TOC_BULLET_LABELS: Record<TocBulletStyle, string> = {
  bullet: 'Bullet',
  mixed: 'Mixed',
  circle: 'Circle',
  square: 'Square',
  numbered: 'Numbered',
  none: 'None',
}

export type TocOptions = {
  /** Vertical list (nested) or horizontal list (one line of links). */
  display: 'vertical' | 'horizontal'
  bulletStyle: TocBulletStyle
  minLevel: number
  maxLevel: number
  /** Outline numbering: 1, 1.1, 1.2, 2… */
  sectionNumbers: boolean
  /** A CSS length for each nesting step of a vertical list, e.g. 10px. Empty means the stylesheet's. */
  indent: string
  /** `|`-separated, case-sensitive patterns; `*` any run of characters, `?` one. Empty means all. */
  include: string
  exclude: string
  /** Extra class names on the table of contents, for a site's own styles. */
  cssClass: string
  /** Leave the table of contents out of PDF export and printing. */
  excludeInPdf: boolean
}

export const TOC_DEFAULTS: TocOptions = {
  display: 'vertical',
  bulletStyle: 'bullet',
  minLevel: 1,
  maxLevel: 6,
  sectionNumbers: false,
  indent: '',
  include: '',
  exclude: '',
  cssClass: '',
  excludeInPdf: false,
}

const LENGTH = /^(0|\d+(\.\d+)?(px|em|rem|pt|%))$/
const CLASS_TOKEN = /^[A-Za-z_][A-Za-z0-9_-]*$/

function clampLevel(value: unknown, fallback: number): number {
  const n = typeof value === 'number' ? value : parseInt(String(value ?? ''), 10)
  return Number.isFinite(n) ? Math.min(6, Math.max(1, Math.trunc(n))) : fallback
}

/** Node attributes → options, with anything unrecognized replaced by its default. */
export function normalizeTocOptions(attrs: Record<string, unknown> | null | undefined): TocOptions {
  const a = attrs ?? {}
  const minLevel = clampLevel(a.minLevel, TOC_DEFAULTS.minLevel)
  const maxLevel = clampLevel(a.maxLevel, TOC_DEFAULTS.maxLevel)
  return {
    display: a.display === 'horizontal' ? 'horizontal' : 'vertical',
    bulletStyle: (TOC_BULLET_STYLES as readonly string[]).includes(String(a.bulletStyle))
      ? (a.bulletStyle as TocBulletStyle)
      : TOC_DEFAULTS.bulletStyle,
    minLevel: Math.min(minLevel, maxLevel),
    maxLevel: Math.max(minLevel, maxLevel),
    sectionNumbers: a.sectionNumbers === true,
    // Validated, not escaped: it lands in a style attribute.
    indent: typeof a.indent === 'string' && LENGTH.test(a.indent.trim()) ? a.indent.trim() : '',
    include: typeof a.include === 'string' ? a.include : '',
    exclude: typeof a.exclude === 'string' ? a.exclude : '',
    cssClass: typeof a.cssClass === 'string' ? a.cssClass.split(/\s+/).filter((t) => CLASS_TOKEN.test(t)).slice(0, 5).join(' ') : '',
    excludeInPdf: a.excludeInPdf === true,
  }
}

function globToRegExp(pattern: string): RegExp {
  const body = pattern.replace(/[.+^${}()[\]\\]/g, '\\$&').replace(/\*/g, '.*').replace(/\?/g, '.')
  return new RegExp(`^${body}$`)
}

function patterns(value: string): RegExp[] {
  return value.split('|').map((p) => p.trim()).filter(Boolean).map(globToRegExp)
}

export type TocHeading = { level: number; text: string; id: string }
export type TocEntry = TocHeading & { number: string; children: TocEntry[] }

/** The headings a table of contents lists, in document order, after levels and include/exclude. */
export function filterTocHeadings(headings: TocHeading[], options: TocOptions): TocHeading[] {
  const include = patterns(options.include)
  const exclude = patterns(options.exclude)
  return headings.filter((h) =>
    h.level >= options.minLevel && h.level <= options.maxLevel
    && (include.length === 0 || include.some((r) => r.test(h.text)))
    && !exclude.some((r) => r.test(h.text)))
}

/** Nest by level (an H3 under the H2 before it) and number the outline: 1, 1.1, 1.2, 2. */
export function buildTocTree(headings: TocHeading[]): TocEntry[] {
  const roots: TocEntry[] = []
  const stack: TocEntry[] = []
  for (const h of headings) {
    const entry: TocEntry = { ...h, number: '', children: [] }
    while (stack.length > 0 && stack[stack.length - 1].level >= h.level) stack.pop()
    const siblings = stack.length === 0 ? roots : stack[stack.length - 1].children
    siblings.push(entry)
    entry.number = (stack.length === 0 ? '' : `${stack[stack.length - 1].number}.`) + String(siblings.length)
    stack.push(entry)
  }
  return roots
}

/** The whole tree flattened back to document order, numbers kept: the horizontal list. */
export function flattenTocTree(entries: TocEntry[]): TocEntry[] {
  return entries.flatMap((e) => [e, ...flattenTocTree(e.children)])
}

/**
 * The list-style for a nesting depth (0 = top level), or '' for "leave it to
 * the stylesheet". Bullet returns '' so a table of contents saved before
 * options existed looks exactly as it did (browsers already vary the bullet
 * by depth). Mixed cycles disc, circle, square explicitly.
 *
 * Section numbers sit alongside whatever bullet was chosen, except
 * Numbered, which would print two numbers per line ("1." and "1.1"), so
 * there the outline numbers replace the list's own.
 */
export function tocListStyle(style: TocBulletStyle, depth: number, sectionNumbers: boolean): string {
  if (sectionNumbers && style === 'numbered') return 'none'
  switch (style) {
    case 'mixed': return ['disc', 'circle', 'square'][depth % 3]
    case 'circle': return 'circle'
    case 'square': return 'square'
    case 'numbered': return 'decimal'
    case 'none': return 'none'
    default: return ''
  }
}
