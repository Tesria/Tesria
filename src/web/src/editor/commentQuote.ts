/**
 * How a passage of a page reads as plain text, for inline comments
 * (dev-plan 22.2). One set of rules for the three places that need them: the
 * live-editing service finding the passage an agent quoted, a person's inline
 * comment storing the passage they selected, and a reset or merge of the
 * draft finding a highlight again afterwards.
 *
 * The text is the page as `get_page` writes it, without the Markdown around
 * it: an inline element reads as the export renders it (`@Name`, a status as
 * `` `On track` ``, a date as `10 Sep 2026`, `$x^2$`, `<https://…>`), so a
 * quote copied out of `get_page` matches. Whitespace runs are one space, both
 * sides are NFC, and case counts.
 *
 * No Yjs and no browser here: the Yjs half is `commentAnchor.ts`.
 */

/** The longest quote anyone may ask for, after whitespace is collapsed. */
export const MAX_QUOTE_LENGTH = 1000

/** A quote as it is compared: NFC, every whitespace run one space, trimmed. */
export function normalizeQuote(text: string): string {
  return text.normalize('NFC').replace(/\s+/gu, ' ').trim()
}

const MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec']

/** "10 Sep 2026", as ProseMirrorRenderer.DateText writes a yyyy-mm-dd date. */
function dateText(value: unknown): string {
  const raw = typeof value === 'string' ? value : ''
  const m = /^(\d{4})-(\d{2})-(\d{2})$/.exec(raw)
  if (!m) return raw
  const [y, mo, d] = [Number(m[1]), Number(m[2]), Number(m[3])]
  const check = new Date(Date.UTC(y, mo - 1, d))
  if (check.getUTCFullYear() !== y || check.getUTCMonth() !== mo - 1 || check.getUTCDate() !== d) return raw
  return `${d} ${MONTHS[mo - 1]} ${y}`
}

/** An http(s) address as the export writes it (Uri.ToString), or null. */
function safeUrl(value: unknown): string | null {
  if (typeof value !== 'string' || !value.trim()) return null
  try {
    const url = new URL(value.trim())
    return url.protocol === 'http:' || url.protocol === 'https:' ? url.href : null
  } catch {
    return null
  }
}

const trimmed = (value: unknown) => (typeof value === 'string' ? value.trim() : '')

/**
 * The text an inline element stands for, as `get_page` renders it
 * (ProseMirrorRenderer.ToMarkdown). A hard break is a space. Anything else
 * inline and unknown reads as nothing.
 */
export function inlineAtomText(type: string, attrs: Record<string, unknown> | null | undefined): string {
  const a = attrs ?? {}
  switch (type) {
    case 'hardBreak':
      return ' '
    case 'mention':
      return '@' + (trimmed(a.label) || 'Unknown user')
    case 'status':
      return '`' + (trimmed(a.text) || 'STATUS').replaceAll('`', "'") + '`'
    case 'date':
      return dateText(a.date)
    case 'math': {
      const latex = typeof a.latex === 'string' ? a.latex : ''
      return a.display === true || a.display === 'true' ? ` $$ ${latex} $$ ` : `$${latex}$`
    }
    case 'smartLinkInline': {
      const url = safeUrl(a.url)
      return url ? `<${url}>` : ''
    }
    default:
      return ''
  }
}

/** A ProseMirror node, as much of one as this needs (a real node or its JSON). */
type InlineLike = {
  type: { name: string } | string
  text?: string | null
  attrs?: Record<string, unknown> | null
  marks?: ReadonlyArray<{ type: { name: string } | string }> | null
  isText?: boolean
}

const nameOf = (t: { name: string } | string) => (typeof t === 'string' ? t : t.name)

/** Whether a piece of text is struck through as a pending outside deletion, and so not on the page. */
export function isStruckThrough(node: InlineLike): boolean {
  return (node.marks ?? []).some((m) => nameOf(m.type) === 'externalDelete')
}

/** As much of a ProseMirror node as {@link selectionQuote} walks. */
type DocLike = {
  nodesBetween(from: number, to: number, f: (node: BlockLike, pos: number) => boolean | void): void
}
type BlockLike = InlineLike & {
  isTextblock: boolean
  forEach(f: (child: InlineLike & { nodeSize: number }, offset: number) => void): void
}

/**
 * The passage a person selected, as the same rules read it (dev-plan 22.2):
 * stored with their inline comment, so it shows in the Comments panel and
 * reads the same as an agent's. Blocks are joined by a space.
 */
export function selectionQuote(doc: DocLike, from: number, to: number): string {
  const parts: string[] = []
  doc.nodesBetween(from, to, (node, pos) => {
    if (!node.isTextblock) return true
    let text = ''
    node.forEach((child, offset) => {
      const start = pos + 1 + offset
      const end = start + child.nodeSize
      if (end <= from || start >= to) return
      if (nameOf(child.type) === 'text' || child.isText) {
        if (isStruckThrough(child)) return
        text += (child.text ?? '').slice(Math.max(0, from - start), Math.max(0, Math.min(end, to) - start))
      } else {
        text += visibleInlineText(child)
      }
    })
    parts.push(text)
    return false
  })
  return normalizeQuote(parts.join(' ')).slice(0, MAX_QUOTE_LENGTH)
}

/**
 * A person's inline comment as its anchor records it: the passage, and
 * which occurrence of it on the page this is (the text before the selection
 * counted block by block), as an agent's request would say it.
 */
export function selectionAnchor(
  doc: DocLike & { descendants(f: (node: BlockLike, pos: number) => boolean | void): void },
  from: number, to: number,
): { type: 'text'; quote: string; occurrence: number } {
  const quote = selectionQuote(doc, from, to)
  let before = 0
  if (quote) {
    doc.descendants((node, pos) => {
      if (!node.isTextblock) return true
      const start = pos + 1
      if (start >= from) return false
      const end = start + (node as BlockLike & { content: { size: number } }).content.size
      const text = selectionQuote(doc, start, Math.min(end, from))
      for (let at = text.indexOf(quote); at !== -1; at = text.indexOf(quote, at + quote.length)) before++
      return false
    })
  }
  return { type: 'text', quote, occurrence: before + 1 }
}

/** One inline node's visible text: its own text, or what the element stands for. */
export function visibleInlineText(node: InlineLike): string {
  const type = nameOf(node.type)
  if (type === 'text' || node.isText) return isStruckThrough(node) ? '' : node.text ?? ''
  return inlineAtomText(type, node.attrs)
}
