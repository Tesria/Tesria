import type { JSONContent } from '@tiptap/core'
import type { Schema } from '@tiptap/pm/model'
import { updateYFragment, yXmlFragmentToProsemirrorJSON } from 'y-prosemirror'
import type * as Y from 'yjs'
import { collectComments, restoreComments } from './commentAnchor'
import { EXTERNAL_MARKS, type ExternalEditSource } from './externalEditMarks'

/** What this needs of a `Y.Doc`, which is all the sidecar has to hand it. */
type YDocLike = {
  getXmlFragment(name: string): Y.XmlFragment
  transact(fn: () => void): void
}

/**
 * Reconciling a write that happened outside this editing session (dev-plan
 * 8.6).
 *
 * A page has two stores: the published version in Postgres and the Yjs
 * document the editor edits. The API, `PageWriter` and MCP write the first
 * and never touch the second, so an outside write used to be invisible in an
 * open editor and got overwritten by the next Update. This turns such a write
 * into something the human can see and decide about: what the write changed
 * lands *in the draft*, as tracked changes.
 *
 * **A three-way merge, against the version the draft started from** (0.8.2).
 * The first version compared the draft with the new page directly, so every
 * word the human had typed and not yet published read as something the write
 * had removed, and Update threw it away (QA T5-032, T5-008). Now the draft's
 * base, the published version it was last brought up to date with, is the
 * third side: what differs between base and draft is the human's, what
 * differs between base and page is the write's, and only the write's is
 * highlighted. The human's own typing is never struck through.
 *
 * The comparison is **block-level**, by design rather than by expedience.
 * Top-level blocks are compared whole, so a rewritten paragraph reads as the
 * old one struck through followed by the new one highlighted. Where the human
 * and the write both changed the same block, the human's version stays as it
 * is and the write's follows it as a highlighted suggestion: accepting keeps
 * both, for the human to finish; rejecting drops the suggestion. A word-level
 * merge of two genuinely different sentences would produce a third sentence
 * nobody wrote.
 *
 * Kept free of Yjs and of the browser so it can be tested directly and so the
 * sidecar can run it too, via the schema bundle: the document in, the
 * document out.
 */

/** Where an outside write came from, and enough to say so in a highlight. */
export type ExternalEditOrigin = {
  source: ExternalEditSource
  /** Display name of the account or token behind the write. */
  actor?: string | null
  /** ISO timestamp; defaults to now. */
  at?: string | null
}

type Block = JSONContent
type ExternalMarkName = (typeof EXTERNAL_MARKS)[number]

const isExternal = (type: string | undefined) => EXTERNAL_MARKS.includes(type as never)
const hasMark = (node: Block, name: ExternalMarkName) => node.marks?.some((m) => m.type === name) ?? false

/* ---- comparing blocks --------------------------------------------------- */

/**
 * A block reduced to the string that decides whether it "is the same block",
 * with any pending tracked changes read as *marks stripped*: the text under a
 * struck-through run is still there.
 *
 * Kept for the history comparison, which diffs two stored versions that
 * carry no marks. The merge below compares by {@link acceptedKey} instead.
 *
 * Object keys are sorted so two blocks that differ only in attribute order
 * are one block. ProseMirror JSON round-trips through several serializers
 * here (Postgres, Yjs, the API) and none of them promises key order.
 */
export function blockKey(block: Block): string {
  return JSON.stringify(canonical(joinText(stripCommentMarks(stripExternalMarks(block)))))
}

/**
 * A block reduced to the string that decides whether it "is the same block",
 * read as it will be once its pending changes are accepted: struck text gone,
 * highlighted text ordinary.
 *
 * That is the right reading for the merge, because the base it is compared
 * with is a published page, and a published page is what accepting produces.
 * It does not accept anything on the human's behalf: blocks still waiting to
 * be removed are set aside and put back exactly as they were (see
 * {@link mergeDocument}).
 */
export function acceptedKey(block: Block): string {
  return JSON.stringify(canonical(joinText(stripCommentMarks(acceptBlock(block)))))
}

function canonical(value: unknown): unknown {
  if (Array.isArray(value)) return value.map(canonical)
  if (value === null || typeof value !== 'object') return value
  const out: Record<string, unknown> = {}
  const entries = Object.entries(value as Record<string, unknown>)
    .sort(([a], [b]) => (a < b ? -1 : a > b ? 1 : 0))
  for (const [key, raw] of entries) {
    // `null` is what TipTap emits for "not set" (a paragraph it wrote carries
    // `attrs: { textAlign: null }`, one the API wrote carries no attrs at
    // all), so a comparison that kept it would call an unchanged block
    // changed on every reconcile.
    if (raw === null || raw === undefined) continue
    const child = canonical(raw)
    // ...and once the nulls are gone the container is often empty, which is
    // the same "absent or explicitly nothing" difference one level up. Two
    // sides that are both empty stay equal; two that differ still differ.
    if (isEmpty(child)) continue
    out[key] = child
  }
  return out
}

function isEmpty(value: unknown): boolean {
  if (Array.isArray(value)) return value.length === 0
  return value !== null && typeof value === 'object' && Object.keys(value as object).length === 0
}

/**
 * Adjacent text nodes with the same marks, joined. Once a tracked-change mark
 * is taken off, "Alpha " (highlighted) and "paragraph" (plain) are one run of
 * text, and must compare equal to the page's single "Alpha paragraph".
 */
function joinText(node: Block): Block {
  if (!node.content) return node
  const content: Block[] = []
  for (const raw of node.content) {
    const child = joinText(raw)
    const last = content[content.length - 1]
    if (
      last && last.type === 'text' && child.type === 'text'
      && JSON.stringify(canonical(last.marks ?? [])) === JSON.stringify(canonical(child.marks ?? []))
    ) {
      content[content.length - 1] = { ...last, text: (last.text ?? '') + (child.text ?? '') }
    } else {
      content.push(child)
    }
  }
  return { ...node, content }
}

/** The node with the two tracked-change marks taken off it and everything in it. */
export function stripExternalMarks(node: Block): Block {
  const marks = node.marks?.filter((m) => !isExternal(m.type))
  const content = node.content?.map(stripExternalMarks)
  const next: Block = { ...node }
  if (node.marks) {
    if (marks && marks.length > 0) next.marks = marks
    else delete next.marks
  }
  if (content) next.content = content
  return next
}

/**
 * The node with every inline-comment highlight taken off (dev-plan 22.2),
 * for comparing only. A highlight is not a change to the words: an agent's
 * inline comment puts one in the draft, and counting it made the next editor
 * see "unpublished changes" nobody typed, prompted on Close, and read the
 * block as rewritten in a merge, which duplicated it on Accept (the 22.2
 * review). Kept apart from {@link stripExternalMarks}, which is not only for
 * comparing.
 */
export function stripCommentMarks(node: Block): Block {
  const marks = node.marks?.filter((m) => m.type !== 'comment')
  const content = node.content?.map(stripCommentMarks)
  const next: Block = { ...node }
  if (node.marks) {
    if (marks && marks.length > 0) next.marks = marks
    else delete next.marks
  }
  if (content) next.content = content
  return next
}

/**
 * Attributes the editor derives from a node's content as soon as it opens a
 * document (`taskAssignee.ts` copies a task's first mention into
 * `assigneeId`/`assigneeName`). A page written through the API has none, so
 * an editor that merely opened it holds a "different" task list. They are
 * left out of every comparison: the content they come from is compared
 * anyway, and counting them made untouched pages ask about unpublished
 * changes on Close and read as the human's edits in a merge.
 */
const DERIVED_ATTRS: Record<string, readonly string[]> = {
  taskItem: ['assigneeId', 'assigneeName'],
}

function withoutDerivedAttrs(node: Block): Block {
  const derived = node.type ? DERIVED_ATTRS[node.type] : undefined
  if (!derived || !node.attrs) return node
  const attrs = { ...node.attrs }
  for (const name of derived) delete attrs[name]
  return { ...node, attrs }
}

/**
 * The block as accepting would leave it: struck text removed, highlights
 * unwrapped, and attributes the editor derives left out (for comparing only).
 */
export function acceptBlock(block: Block): Block {
  const visit = (node: Block): Block => {
    const next = stripExternalMarks({ ...withoutDerivedAttrs(node), content: undefined })
    if (!node.content) return next
    const content = node.content
      .filter((child) => !(child.type === 'text' && hasMark(child, 'externalDelete')))
      .map(visit)
    return joinText({ ...next, content })
  }
  return visit(block)
}

/** Whether every piece of text in a block carries the mark, and it has some. */
function fullyMarked(block: Block, name: ExternalMarkName): boolean {
  let marked = false
  let unmarked = false
  const walk = (node: Block) => {
    if (node.type === 'text') {
      if (hasMark(node, name)) marked = true
      else if (node.text) unmarked = true
      return
    }
    node.content?.forEach(walk)
  }
  walk(block)
  return marked && !unmarked
}

/** A block an earlier outside write removed, struck through and not yet decided. */
export const isPendingDeletion = (block: Block) => fullyMarked(block, 'externalDelete')
/** A block an earlier outside write added, highlighted and not yet decided. */
export const isPendingInsertion = (block: Block) => fullyMarked(block, 'externalInsert')

/** Whether a node carries either tracked-change mark anywhere inside it. */
function hasExternalMarks(node: Block): boolean {
  if (node.marks?.some((m) => isExternal(m.type))) return true
  return (node.content ?? []).some(hasExternalMarks)
}

/* ---- marking ------------------------------------------------------------ */

/**
 * Puts a tracked-change mark on every piece of text inside a block.
 *
 * A mark cannot sit on a block node, only on inline content, so a block with
 * no text in it (an image, a divider, a live block) is carried through
 * unmarked: it is inserted or removed plainly, and the banner's count is what
 * tells the human it happened. Marking the *text* also means the human can
 * edit inside a highlighted run, which is the point: it is ordinary text that
 * happens to carry a mark.
 */
export function markBlock(block: Block, name: ExternalMarkName, origin: ExternalEditOrigin): Block {
  const attrs = {
    source: origin.source,
    actor: origin.actor ?? null,
    at: origin.at ?? new Date().toISOString(),
  }
  const apply = (node: Block): Block => {
    if (node.type === 'text') {
      const existing = (node.marks ?? []).filter((m) => !isExternal(m.type))
      return { ...node, marks: [...existing, { type: name, attrs }] }
    }
    return node.content ? { ...node, content: node.content.map(apply) } : node
  }
  return apply(block)
}

/** Whether a block has any text a mark could attach to. */
export function hasInlineText(block: Block): boolean {
  if (block.type === 'text') return true
  return (block.content ?? []).some(hasInlineText)
}

/* ---- the diff ----------------------------------------------------------- */

export type ReconcileStep =
  | { kind: 'keep'; block: Block }
  | { kind: 'removed'; block: Block }
  | { kind: 'added'; block: Block }

/**
 * The longest common subsequence of two key lists, as a table.
 *
 * Classic dynamic programming, which is O(n*m) in blocks rather than in
 * characters. A page of a few hundred blocks is nothing; the 300-block site
 * export limit is a useful upper bound on what anyone actually writes.
 */
function lcs(a: string[], b: string[]): number[][] {
  const table: number[][] = Array.from({ length: a.length + 1 }, () => new Array(b.length + 1).fill(0))
  for (let i = a.length - 1; i >= 0; i--) {
    for (let j = b.length - 1; j >= 0; j--) {
      table[i][j] = a[i] === b[j] ? table[i + 1][j + 1] + 1 : Math.max(table[i + 1][j], table[i][j + 1])
    }
  }
  return table
}

/** The index pairs one longest common subsequence of two key lists matches up. */
function matches(a: string[], b: string[]): Array<[number, number]> {
  const table = lcs(a, b)
  const pairs: Array<[number, number]> = []
  let i = 0
  let j = 0
  while (i < a.length && j < b.length) {
    if (a[i] === b[j]) {
      pairs.push([i, j])
      i++
      j++
    } else if (table[i + 1][j] >= table[i][j + 1]) i++
    else j++
  }
  return pairs
}

/**
 * The difference between two block lists, in order: what both have, what
 * only the first has, what only the second has.
 */
export function diffBlocks(draft: Block[], published: Block[]): ReconcileStep[] {
  const a = draft.map(blockKey)
  const b = published.map(blockKey)
  const steps: ReconcileStep[] = []
  let i = 0
  let j = 0
  for (const [mi, mj] of [...matches(a, b), [a.length, b.length] as [number, number]]) {
    while (i < mi) steps.push({ kind: 'removed', block: draft[i++] })
    while (j < mj) steps.push({ kind: 'added', block: published[j++] })
    if (mi < a.length) steps.push({ kind: 'keep', block: draft[mi] })
    i = mi + 1
    j = mj + 1
  }
  return steps
}

/**
 * One side of a three-way merge, relative to the base: which base blocks it
 * kept (and where), and which gap between base blocks each of its own new
 * blocks sits in. Gap `g` is just before base block `g`; gap `n` is the end.
 */
type Side = { keptAt: Array<number | null>; gapOf: number[] }

function sideOf(baseKeys: string[], keys: string[]): Side {
  const keptAt: Array<number | null> = new Array(baseKeys.length).fill(null)
  const baseOf = new Map<number, number>()
  for (const [i, j] of matches(baseKeys, keys)) {
    keptAt[i] = j
    baseOf.set(j, i)
  }
  const gapOf: number[] = new Array(keys.length).fill(-1)
  let next = baseKeys.length
  for (let j = keys.length - 1; j >= 0; j--) {
    const i = baseOf.get(j)
    if (i !== undefined) next = i
    else gapOf[j] = next
  }
  return { keptAt, gapOf }
}

/**
 * The draft, with what an outside write did to the page shown inside it.
 *
 * `base` is the published version the draft was last brought up to date with
 * (`meta.version`). Relative to it:
 *
 * - a block the write changed and the human did not: the old one struck
 *   through, the new one highlighted after it;
 * - a block the human changed and the write did not: the human's, untouched;
 * - a block both changed: the human's, untouched, and the write's version
 *   highlighted after it as a suggestion;
 * - a block both changed the same way, or a block the write removed that the
 *   human had removed too: nothing to show.
 *
 * Blocks with nothing a mark can sit on (an image, a divider) are added or
 * removed plainly.
 *
 * Without a base (a version that no longer exists), the draft is taken to be
 * the base, which is the plain two-way comparison: everything that differs
 * reads as the write's doing.
 *
 * Changes an earlier write left pending are kept as they are. A block still
 * waiting to be removed is set aside and put back where it was; a block
 * waiting to be added that this write removes again simply goes, since it was
 * never the human's.
 */
export function mergeDocument(
  base: Block | null,
  draft: Block,
  published: Block,
  origin: ExternalEditOrigin,
): Block {
  const live: Block[] = []
  // Set-aside pending deletions, by the live block they follow (-1: the start).
  const setAside = new Map<number, Block[]>()
  for (const block of draft.content ?? []) {
    if (isPendingDeletion(block)) {
      const after = live.length - 1
      setAside.set(after, [...(setAside.get(after) ?? []), block])
    } else live.push(block)
  }

  const draftKeys = live.map(acceptedKey)
  const baseKeys = base ? (base.content ?? []).map(acceptedKey) : draftKeys
  const pageBlocks = published.content ?? []
  const pageKeys = pageBlocks.map(acceptedKey)
  const human = sideOf(baseKeys, draftKeys)
  const page = sideOf(baseKeys, pageKeys)
  const n = baseKeys.length

  const added = (block: Block): Block => (hasInlineText(block) ? markBlock(block, 'externalInsert', origin) : block)
  const removed = (block: Block): Block | null =>
    !hasInlineText(block) || isPendingInsertion(block) ? null : markBlock(block, 'externalDelete', origin)

  const out: Array<{ block: Block | null; live?: number }> = []
  for (let g = 0; g <= n; g++) {
    const humanAdds = draftKeys.flatMap((_, j) => (human.gapOf[j] === g ? [j] : []))
    const theirs = new Set(humanAdds.map((j) => draftKeys[j]))
    // A block both sides added in the same place is one block.
    const pageAdds = pageKeys.flatMap((key, k) => (page.gapOf[k] === g && !theirs.has(key) ? [k] : []))
    const emitHuman = () => humanAdds.forEach((j) => out.push({ block: live[j], live: j }))
    const emitPage = () => pageAdds.forEach((k) => out.push({ block: added(pageBlocks[k]) }))
    // The write's new blocks go straight after the block they replace when
    // the human kept that block (so it is struck through just above), and
    // after the human's own new blocks otherwise: "your version, then its
    // suggestion".
    const replacing = g > 0 && human.keptAt[g - 1] !== null && page.keptAt[g - 1] === null
    if (replacing) {
      emitPage()
      emitHuman()
    } else {
      emitHuman()
      emitPage()
    }
    if (g === n) break

    const j = human.keptAt[g]
    // The human removed or rewrote it; a rewrite is among their additions.
    if (j === null) continue
    out.push({ block: page.keptAt[g] !== null ? live[j] : removed(live[j]), live: j })
  }

  const content: Block[] = [...(setAside.get(-1) ?? [])]
  for (const entry of out) {
    if (entry.block) content.push(entry.block)
    if (entry.live !== undefined) content.push(...(setAside.get(entry.live) ?? []))
  }
  return { ...draft, content }
}

/**
 * The two-way comparison: the first document with what the second changed
 * shown inside it, as tracked changes. Used by History to compare two
 * versions, where there is no draft and so no third side.
 */
export function reconcileDocument(draft: Block, published: Block, origin: ExternalEditOrigin): Block {
  return mergeDocument(null, draft, published, origin)
}

/** Whether two documents are the same, marks and all, except inline-comment highlights (see {@link stripCommentMarks}). */
export function sameDocument(a: Block, b: Block): boolean {
  return JSON.stringify(canonical(joinText(stripCommentMarks(a)))) === JSON.stringify(canonical(joinText(stripCommentMarks(b))))
}

const isEmptyParagraph = (block: Block) =>
  block.type === 'paragraph' && !(block.content ?? []).some((child) => child.type !== 'text' || child.text)

/**
 * Whether a draft says anything the published page does not: text nobody
 * has published yet, or outside changes nobody has decided on (dev-plan 8.6,
 * and 0.8.2's Close prompt and draft banner).
 *
 * Empty paragraphs at the end do not count. The editor keeps one after a
 * trailing list or table so there is somewhere to type, and a page is not
 * "changed" by that.
 */
export function hasUnpublishedChanges(draft: Block, published: Block): boolean {
  return (draft.content ?? []).some(hasExternalMarks) || differsFromPage(draft, published)
}

/**
 * Whether a draft, read as its pending outside changes accepted, differs from
 * the page: that is, whether it holds changes a *person* made and has not
 * published. Pending outside changes alone do not count; the banner for
 * those is a different one.
 */
export function differsFromPage(draft: Block, published: Block): boolean {
  const keys = (list: Block[]) => {
    const trimmed = list.filter((block) => !isPendingDeletion(block))
    while (trimmed.length > 0 && isEmptyParagraph(trimmed[trimmed.length - 1])) trimmed.pop()
    return trimmed.map(acceptedKey)
  }
  const a = keys(draft.content ?? [])
  const b = keys(published.content ?? [])
  return a.length !== b.length || a.some((key, i) => key !== b[i])
}

/* ---- applying it to the shared document --------------------------------- */

/**
 * A document as the schema itself would write it, so two of them can be
 * compared.
 *
 * Throws if the content does not fit the schema, which for the published side
 * means a document this build cannot represent. The caller must treat that as
 * "leave the draft alone": refusing to reconcile loses nothing, while writing
 * a half-understood document over somebody's draft loses their work.
 */
export function normalize(schema: Schema, document: Block): Block {
  return schema.nodeFromJSON(document).toJSON() as Block
}

/** The draft in a shared document, as the schema writes it. */
export function readYDoc(ydoc: YDocLike, schema: Schema, fragmentName = 'default'): Block {
  return normalize(schema, yXmlFragmentToProsemirrorJSON(ydoc.getXmlFragment(fragmentName)) as Block)
}

/**
 * Writes a document into the shared one as a *diff*, not a replacement:
 * `updateYFragment` is y-prosemirror's own minimal-change applier, the one
 * the collaboration extension uses for every keystroke, so untouched blocks
 * are left alone in the CRDT. That is what keeps other people's cursors where
 * they were and keeps the change small on the wire. Replacing the fragment
 * wholesale would work exactly once, look identical in a screenshot, and
 * quietly clobber anyone typing at the time; and a *fresh* document merged
 * with a client that still holds the old one would show every block twice.
 *
 * One `transact`, so every connected editor sees it as a single step.
 */
function writeYDoc(ydoc: YDocLike, schema: Schema, document: Block, fragmentName: string): void {
  const fragment = ydoc.getXmlFragment(fragmentName)
  const node = schema.nodeFromJSON(document)
  ydoc.transact(() => {
    // Inline-comment highlights survive the rewrite (dev-plan 22.2): a reset
    // to the published page used to take off every one the page did not
    // have, an agent's among them, and Discard did the same. Each is found
    // again by its passage afterwards, in the same transaction.
    const held = schema.marks.comment ? collectComments(ydoc, schema, fragmentName) : []
    updateYFragment(ydoc as Y.Doc, fragment, node, { mapping: new Map(), isOMark: new Map() })
    restoreComments(ydoc, schema, held, fragmentName)
  })
}

export type ReconcileOptions = {
  /**
   * The published version the draft was last brought up to date with, as
   * stored. Null or missing when it cannot be found: the comparison is then
   * two-way.
   */
  base?: Block | null
  fragmentName?: string
}

/**
 * Puts what an outside write changed into the Yjs document the editors are
 * sharing, as tracked changes against the draft's base.
 *
 * The schema is a parameter rather than an import: the client has the
 * editor's, and the sidecar gets the same one from the schema bundle, which
 * is what stops the two drifting.
 *
 * @returns whether anything changed, so callers can skip the version bump.
 */
export function reconcileYDoc(
  ydoc: YDocLike,
  schema: Schema,
  published: Block,
  origin: ExternalEditOrigin,
  options: ReconcileOptions = {},
): boolean {
  const fragmentName = options.fragmentName ?? 'default'
  const fragment = ydoc.getXmlFragment(fragmentName)

  // Every side goes through the schema before anything is compared, and this
  // is not ceremony. ProseMirror materialises every attribute a node type
  // declares, so a paragraph that came out of the CRDT carries
  // `textIndent: 0` while the same paragraph as the API stored it carries no
  // attrs at all. Comparing those two directly marks every block in the
  // document as changed, every time. (Found by running it, not by reading
  // it: the canonicaliser already forgives a null, and 0 is not a null.)
  const page = normalize(schema, published)

  // An empty fragment means nobody has opened this page since it was last
  // published, so there is no draft to reconcile against and nothing to mark:
  // seeding it with the page is the whole answer.
  if (fragment.length === 0) {
    writeYDoc(ydoc, schema, page, fragmentName)
    return true
  }

  const draft = readYDoc(ydoc, schema, fragmentName)
  let base: Block | null = null
  if (options.base) {
    try {
      base = normalize(schema, options.base)
    } catch {
      base = null
    }
  }
  const merged = mergeDocument(base, draft, page, origin)
  if (sameDocument(merged, draft)) return false
  writeYDoc(ydoc, schema, merged, fragmentName)
  return true
}

/**
 * Makes the shared draft exactly the published page, throwing away every
 * unpublished change and every pending highlight (0.8.2: Discard, and a
 * publish through the API or MCP while nobody is editing).
 *
 * Written as a diff of the existing document rather than as a new one, for
 * the reason {@link writeYDoc} gives: an editor that is offline still holds
 * the old document, and reconnecting has to merge with this, not beside it.
 *
 * @returns whether anything changed.
 */
export function resetYDoc(ydoc: YDocLike, schema: Schema, published: Block, fragmentName = 'default'): boolean {
  const page = normalize(schema, published)
  const fragment = ydoc.getXmlFragment(fragmentName)
  if (fragment.length > 0 && sameDocument(readYDoc(ydoc, schema, fragmentName), page)) return false
  writeYDoc(ydoc, schema, page, fragmentName)
  return true
}
