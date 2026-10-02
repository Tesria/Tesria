import type { Schema } from '@tiptap/pm/model'
import { updateYFragment } from 'y-prosemirror'
import * as Y from 'yjs'
import { inlineAtomText, MAX_QUOTE_LENGTH, normalizeQuote } from './commentQuote'

/**
 * Inline comments in the shared draft, done to the Yjs document directly
 * (dev-plan 22.2): finding a quoted passage, placing a comment's highlight on
 * it, taking one off again, and keeping highlights through a reset or merge
 * of the draft.
 *
 * **Why `Y.XmlText.format`, not a rewrite of the document.** A highlight is
 * a mark on text, and in Yjs a mark is a formatting attribute on a run of a
 * `Y.XmlText`. Formatting that run in place leaves every character, and so
 * everyone's cursor and any typing not yet synced, exactly where it was.
 * `updateYFragment` would diff the whole document instead, and can reorder a
 * concurrent insert (the 22.2 review). The search and the format happen in
 * one transaction, so no remote update lands between them.
 *
 * **What a block is.** A paragraph, heading, code block, or any other node
 * holding text directly. In Yjs that is a `Y.XmlElement` whose children are
 * `Y.XmlText` runs (each a sequence of text with formatting) and inline
 * elements (a mention, a date, a status…), as y-prosemirror lays it out. A
 * passage must lie in one block: a highlight across two paragraphs is two
 * highlights, and nobody asked for those.
 *
 * Duck-typed rather than `instanceof Y.XmlText`: the sidecar and the tests
 * each have one Yjs, but a check against the wrong copy fails silently.
 */

type YDocLike = {
  getXmlFragment(name: string): Y.XmlFragment
  transact(fn: () => void): void
}

type YTextLike = {
  toDelta(): Array<{ insert: unknown; attributes?: Record<string, unknown> }>
  format(index: number, length: number, attributes: Record<string, unknown>): void
}

type YElementLike = {
  nodeName: string
  toArray(): unknown[]
  getAttributes(): Record<string, unknown>
}

const isYText = (node: unknown): node is YTextLike =>
  typeof node === 'object' && node !== null && typeof (node as YTextLike).toDelta === 'function'
    && typeof (node as YTextLike).format === 'function'
const isYElement = (node: unknown): node is YElementLike =>
  typeof node === 'object' && node !== null && typeof (node as YElementLike).nodeName === 'string'
    && typeof (node as YElementLike).toArray === 'function'

/** y-prosemirror's names for a mark: `comment`, or `comment--<hash>` for one of several (excludes: ''). */
const markName = (attr: string) => /^(.*)--[a-zA-Z0-9+/=]{8}$/.exec(attr)?.[1] ?? attr
const isCommentAttr = (attr: string) => markName(attr) === 'comment'

/** A comment's id in a formatting attribute's value, or null. */
function commentIdOf(value: unknown): string | null {
  if (typeof value !== 'object' || value === null) return null
  const id = (value as { commentId?: unknown }).commentId
  return typeof id === 'string' && id ? id : null
}

/* ---- reading the document as text --------------------------------------- */

/** A run of text in one Y.XmlText: where it starts in that text, and what is on it. */
type TextSegment = {
  kind: 'text'
  text: string
  ytext: YTextLike
  index: number
  struck: boolean
  /** Comment attribute key to comment id, for every comment mark on the run. */
  comments: Array<{ key: string; id: string; value: unknown }>
}
type AtomSegment = { kind: 'atom'; text: string }
type Segment = TextSegment | AtomSegment

/** Every block of the draft, in document order, as its inline segments. */
function blocksOf(fragment: Y.XmlFragment, schema: Schema): Segment[][] {
  const blocks: Segment[][] = []
  const visit = (children: unknown[]) => {
    for (const child of children) {
      if (!isYElement(child)) continue
      const type = schema.nodes[child.nodeName]
      if (type?.isTextblock) blocks.push(segmentsOf(child, schema))
      else if (type && !type.isInline) visit(child.toArray())
    }
  }
  visit(fragment.toArray())
  return blocks
}

function segmentsOf(block: YElementLike, schema: Schema): Segment[] {
  const out: Segment[] = []
  for (const child of block.toArray()) {
    if (isYText(child)) {
      let index = 0
      for (const op of child.toDelta()) {
        if (typeof op.insert !== 'string') continue
        const attrs = op.attributes ?? {}
        const comments = Object.entries(attrs)
          .filter(([key]) => isCommentAttr(key))
          .flatMap(([key, value]) => {
            const id = commentIdOf(value)
            return id ? [{ key, id, value }] : []
          })
        out.push({
          kind: 'text', text: op.insert, ytext: child, index,
          struck: Object.keys(attrs).some((key) => markName(key) === 'externalDelete'),
          comments,
        })
        index += op.insert.length
      }
    } else if (isYElement(child) && schema.nodes[child.nodeName]?.isInline) {
      out.push({ kind: 'atom', text: inlineAtomText(child.nodeName, child.getAttributes()) })
    }
  }
  return out
}

/**
 * One block's visible text, normalized, with where each character came
 * from: a range of a `Y.XmlText`, or an inline element (no range). Built the
 * way {@link normalizeQuote} normalizes a quote, so the two compare.
 */
type Visible = {
  text: string
  source: Array<{ seg: TextSegment; start: number; end: number } | null>
  /** The comment ids on each character's text, for finding a highlight's passage. */
  comments: Array<TextSegment['comments']>
}

/** A base character and the combining marks after it, so NFC never splits a source range. */
const CLUSTER = /\P{M}\p{M}*|\p{M}+/gsu

function visibleOf(block: Segment[]): Visible {
  const v: Visible = { text: '', source: [], comments: [] }
  let space: Visible['source'][number] | undefined
  let spaceComments: TextSegment['comments'] = []
  const push = (chars: string, src: Visible['source'][number], comments: TextSegment['comments']) => {
    if (space !== undefined) {
      if (v.text.length > 0) {
        v.text += ' '
        v.source.push(space)
        v.comments.push(spaceComments)
      }
      space = undefined
    }
    for (let i = 0; i < chars.length; i++) {
      v.text += chars[i]
      v.source.push(src)
      v.comments.push(comments)
    }
  }
  const pushSpace = (src: Visible['source'][number], comments: TextSegment['comments']) => {
    // One space for a whole run; a run inside one piece of text widens it,
    // so a highlight across "a   b" covers all three spaces.
    if (space && src && space.seg === src.seg && space.end === src.start) space = { ...space, end: src.end }
    else if (space === undefined) {
      space = src
      spaceComments = comments
    }
  }
  for (const seg of block) {
    if (seg.kind === 'atom') {
      for (const cluster of seg.text.match(CLUSTER) ?? []) {
        if (/^\s+$/u.test(cluster)) pushSpace(null, [])
        else push(cluster.normalize('NFC'), null, [])
      }
      continue
    }
    if (seg.struck) continue
    let at = 0
    for (const cluster of seg.text.match(CLUSTER) ?? []) {
      const src = { seg, start: seg.index + at, end: seg.index + at + cluster.length }
      at += cluster.length
      if (/^\s+$/u.test(cluster)) pushSpace(src, seg.comments)
      else push(cluster.normalize('NFC'), src, seg.comments)
    }
  }
  return v
}

/* ---- finding a passage --------------------------------------------------- */

export type QuoteRefusal = {
  code: 'quote_empty' | 'quote_not_found' | 'quote_ambiguous' | 'quote_too_long' | 'quote_spans_blocks'
  message: string
  /** How many times the passage appears, where that is the reason. */
  count?: number
}

/** Where a passage is: the block, and the runs of text to put a mark on. */
export type QuoteMatch = {
  block: number
  /** Which occurrence in the whole document, from 1. */
  occurrence: number
  ranges: Array<{ ytext: YTextLike; index: number; length: number }>
}

type Occurrence = { block: number; at: number; nthInBlock: number }

function occurrencesIn(visible: Visible[], quote: string): Occurrence[] {
  const found: Occurrence[] = []
  visible.forEach((v, block) => {
    let nth = 0
    for (let at = v.text.indexOf(quote); at !== -1; at = v.text.indexOf(quote, at + quote.length)) {
      found.push({ block, at, nthInBlock: nth++ })
    }
  })
  return found
}

/** The runs of text an occurrence covers, merged per Y.XmlText; inline elements get none. */
function rangesOf(v: Visible, at: number, length: number): QuoteMatch['ranges'] {
  const ranges: QuoteMatch['ranges'] = []
  for (let i = at; i < at + length; i++) {
    const src = v.source[i]
    if (!src) continue
    const last = ranges[ranges.length - 1]
    if (last && last.ytext === src.seg.ytext && last.index + last.length === src.start) {
      last.length = Math.max(last.length, src.end - last.index)
    } else if (last && last.ytext === src.seg.ytext && src.start >= last.index && src.end <= last.index + last.length) {
      // The same cluster again (NFC made several characters of one).
    } else {
      ranges.push({ ytext: src.seg.ytext, index: src.start, length: src.end - src.start })
    }
  }
  return ranges
}

/** Checks a quote by itself, before any document is looked at. */
export function quoteProblem(quote: string): QuoteRefusal | null {
  const q = normalizeQuote(quote)
  if (q.length === 0) return { code: 'quote_empty', message: 'The quote is empty: quote the words the comment is about.' }
  if (q.length > MAX_QUOTE_LENGTH)
    return {
      code: 'quote_too_long',
      message: `The quote is ${q.length} characters long; quote at most ${MAX_QUOTE_LENGTH}, a sentence or two is plenty.`,
    }
  return null
}

/**
 * Finds a quoted passage in the draft. `occurrence` picks one when it appears
 * more than once (from 1, in document order); without it, more than one is a
 * refusal that says how many, so the caller can choose.
 */
export function findQuote(
  ydoc: YDocLike, schema: Schema, quote: string, occurrence?: number | null, fragmentName = 'default',
): QuoteMatch | QuoteRefusal {
  const problem = quoteProblem(quote)
  if (problem) return problem
  const q = normalizeQuote(quote)
  const visible = blocksOf(ydoc.getXmlFragment(fragmentName), schema).map(visibleOf)
  const found = occurrencesIn(visible, q)

  if (found.length === 0) {
    const joined = visible.map((v) => v.text).filter((t) => t.length > 0).join(' ')
    if (joined.includes(q))
      return {
        code: 'quote_spans_blocks',
        message: 'That passage runs across more than one paragraph, heading, list item or cell; quote a part of one.',
      }
    return {
      code: 'quote_not_found',
      count: 0,
      message: 'That passage is not on the page as it is now. Quote the words exactly as they read, without Markdown.',
    }
  }
  if (occurrence == null && found.length > 1)
    return {
      code: 'quote_ambiguous',
      count: found.length,
      message: `That passage appears ${found.length} times on the page; say which one with occurrence (1 to ${found.length}), or quote more of it.`,
    }
  const n = occurrence ?? 1
  if (n < 1 || n > found.length)
    return {
      code: 'quote_not_found',
      count: found.length,
      message: found.length === 1
        ? `That passage appears once on the page, so occurrence ${n} does not exist.`
        : `That passage appears ${found.length} times on the page, so occurrence ${n} does not exist.`,
    }
  const hit = found[n - 1]
  const ranges = rangesOf(visible[hit.block], hit.at, q.length)
  if (ranges.length === 0)
    return {
      code: 'quote_not_found',
      count: found.length,
      message: 'That passage has no text a highlight can sit on (it is only an element such as a mention or a status).',
    }
  return { block: hit.block, occurrence: n, ranges }
}

export const isRefusal = (r: QuoteMatch | QuoteRefusal): r is QuoteRefusal => 'code' in r

/* ---- the highlight ------------------------------------------------------- */

const keys = new Map<string, string>()

/**
 * The formatting key y-prosemirror gives a comment mark: `comment--<hash of
 * the mark>`, since a comment mark may overlap another (excludes: ''). Asked
 * of y-prosemirror itself, by converting one marked character, so it is the
 * key an editor writes and reads, not a copy of how it is computed.
 */
export function commentAttrKey(schema: Schema, commentId: string): string {
  const cached = keys.get(commentId)
  if (cached) return cached
  // The node is made by this schema and handed to updateYFragment, never as
  // JSON to a y-prosemirror helper: in the sidecar, y-prosemirror has its own
  // prosemirror-model, which cannot read this bundle's schema (found by a
  // smoke test of the built bundle).
  const probe = new Y.Doc()
  const fragment = probe.getXmlFragment('probe')
  const node = schema.nodeFromJSON({
    type: 'doc',
    content: [{ type: 'paragraph', content: [{ type: 'text', text: 'x', marks: [{ type: 'comment', attrs: { commentId } }] }] }],
  })
  probe.transact(() => updateYFragment(probe, fragment, node, { mapping: new Map(), isOMark: new Map() }))
  const block = fragment.get(0) as unknown as YElementLike
  const text = block.toArray()[0] as YTextLike
  const key = Object.keys(text.toDelta()[0]?.attributes ?? {}).find(isCommentAttr) ?? 'comment'
  if (keys.size > 1000) keys.clear()
  keys.set(commentId, key)
  return key
}

/** Whether a comment's highlight is anywhere in the draft. */
export function hasComment(ydoc: YDocLike, schema: Schema, commentId: string, fragmentName = 'default'): boolean {
  return blocksOf(ydoc.getXmlFragment(fragmentName), schema)
    .some((block) => block.some((seg) => seg.kind === 'text' && seg.comments.some((c) => c.id === commentId)))
}

export type PlaceResult =
  | { status: 'placed'; block: number; occurrence: number }
  | { status: 'already' }
  | ({ status: 'refused' } & QuoteRefusal)

/**
 * Puts a comment's highlight on a quoted passage of the draft, in one
 * transaction: the search, then the format. A comment already highlighted is
 * left as it is, so asking twice is harmless.
 */
export function placeComment(
  ydoc: YDocLike, schema: Schema,
  request: { commentId: string; quote: string; occurrence?: number | null },
  fragmentName = 'default',
): PlaceResult {
  let result: PlaceResult = { status: 'already' }
  ydoc.transact(() => {
    if (hasComment(ydoc, schema, request.commentId, fragmentName)) return
    const found = findQuote(ydoc, schema, request.quote, request.occurrence, fragmentName)
    if (isRefusal(found)) {
      result = { status: 'refused', ...found }
      return
    }
    const key = commentAttrKey(schema, request.commentId)
    for (const range of found.ranges)
      range.ytext.format(range.index, range.length, { [key]: { commentId: request.commentId } })
    result = { status: 'placed', block: found.block, occurrence: found.occurrence }
  })
  return result
}

/** Takes a comment's highlight off the draft. Returns whether there was one. */
export function removeComment(ydoc: YDocLike, schema: Schema, commentId: string, fragmentName = 'default'): boolean {
  let removed = false
  ydoc.transact(() => {
    for (const block of blocksOf(ydoc.getXmlFragment(fragmentName), schema))
      for (const seg of block) {
        if (seg.kind !== 'text') continue
        for (const c of seg.comments) {
          if (c.id !== commentId) continue
          seg.ytext.format(seg.index, seg.text.length, { [c.key]: null })
          removed = true
        }
      }
  })
  return removed
}

/* ---- keeping highlights through a reset or a merge ----------------------- */

/** A highlight as found in the draft: enough to find its passage again. */
export type HeldComment = { id: string; key: string; value: unknown; quote: string; block: number; nthInBlock: number }

/** Every comment highlight in the draft, with the passage it covers. */
export function collectComments(ydoc: YDocLike, schema: Schema, fragmentName = 'default'): HeldComment[] {
  const held = new Map<string, HeldComment>()
  blocksOf(ydoc.getXmlFragment(fragmentName), schema).forEach((block, b) => {
    const v = visibleOf(block)
    const spans = new Map<string, { first: number; last: number; key: string; value: unknown }>()
    v.comments.forEach((list, i) => {
      for (const c of list) {
        const span = spans.get(c.id)
        if (span) span.last = i
        else spans.set(c.id, { first: i, last: i, key: c.key, value: c.value })
      }
    })
    for (const [id, span] of spans) {
      // A highlight split over two blocks is kept in its first.
      if (held.has(id)) continue
      const quote = v.text.slice(span.first, span.last + 1).trim()
      if (!quote) continue
      let nth = 0
      for (let at = v.text.indexOf(quote); at !== -1 && at < span.first; at = v.text.indexOf(quote, at + quote.length)) nth++
      held.set(id, { id, key: span.key, value: span.value, quote, block: b, nthInBlock: nth })
    }
  })
  return [...held.values()]
}

/**
 * Puts back each highlight a rewrite of the draft lost, on its passage as
 * the draft now reads: where it was if that is still there (the same block,
 * the same occurrence in it), else the nearest block holding the passage. A
 * passage no longer on the page is gone, and so is its highlight. One that
 * survived the rewrite is left alone.
 *
 * @returns the ids put back.
 */
export function restoreComments(ydoc: YDocLike, schema: Schema, held: HeldComment[], fragmentName = 'default'): string[] {
  const restored: string[] = []
  if (held.length === 0) return restored
  ydoc.transact(() => {
    for (const h of held) {
      if (hasComment(ydoc, schema, h.id, fragmentName)) continue
      const visible = blocksOf(ydoc.getXmlFragment(fragmentName), schema).map(visibleOf)
      const found = occurrencesIn(visible, h.quote)
      if (found.length === 0) continue
      const best = found.find((o) => o.block === h.block && o.nthInBlock === h.nthInBlock)
        ?? [...found].sort((a, b) => Math.abs(a.block - h.block) - Math.abs(b.block - h.block) || a.block - b.block)[0]
      const ranges = rangesOf(visible[best.block], best.at, h.quote.length)
      if (ranges.length === 0) continue
      for (const range of ranges) range.ytext.format(range.index, range.length, { [h.key]: h.value })
      restored.push(h.id)
    }
  })
  return restored
}
