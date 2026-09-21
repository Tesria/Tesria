import type { JSONContent } from '@tiptap/core'
import type { Schema } from '@tiptap/pm/model'
import { updateYFragment, yXmlFragmentToProsemirrorJSON } from 'y-prosemirror'
import type * as Y from 'yjs'
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
 * into something the human can see and decide about: the difference between
 * the draft and what was published lands *in the draft*, as tracked changes.
 *
 * The diff is **block-level**, by design rather than by expedience. Top-level
 * blocks are compared whole, so a rewritten paragraph reads as the old one
 * struck through followed by the new one highlighted. Character-level merging
 * inside a paragraph is a refinement for later; it is worth noting that the
 * block version is the one that cannot lie, since a word-level merge of two
 * genuinely different sentences produces a third sentence nobody wrote.
 *
 * Kept free of Yjs and of the browser so it can be tested directly and so the
 * sidecar can run it too (it will, via the schema bundle in step 3): the
 * document in, the document out.
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

/* ---- comparing blocks --------------------------------------------------- */

/**
 * A block reduced to the string that decides whether it "is the same block".
 *
 * Pending tracked changes are stripped first, so a block already carrying a
 * highlight compares as the text under it. Without that, reconciling twice
 * would diff against the marks the first pass added and mark everything over
 * again.
 *
 * The plan glosses this as "old as if accepted", and this is deliberately the
 * weaker reading: the *marks* go, the text stays, including text already
 * struck through. Truly accepting would drop that text, which would mean a
 * second write silently accepted the first one's deletion on the human's
 * behalf. Nobody decided that. The cost is that a draft with an unresolved
 * deletion looks different from the page even when accepting it would match,
 * so a redundant reconcile runs; it is idempotent, so it costs a pass and
 * nothing else.
 *
 * Object keys are sorted so two blocks that differ only in attribute order
 * are one block. ProseMirror JSON round-trips through several serialisers
 * here (Postgres, Yjs, the API) and none of them promises key order.
 */
export function blockKey(block: Block): string {
  return JSON.stringify(canonical(stripExternalMarks(block)))
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

/** The document as it would be if every pending tracked change were accepted. */
export function stripExternalMarks(node: Block): Block {
  const marks = node.marks?.filter((m) => !EXTERNAL_MARKS.includes(m.type as never))
  const content = node.content?.map(stripExternalMarks)
  const next: Block = { ...node }
  if (node.marks) {
    if (marks && marks.length > 0) next.marks = marks
    else delete next.marks
  }
  if (content) next.content = content
  return next
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
export function markBlock(block: Block, name: (typeof EXTERNAL_MARKS)[number], origin: ExternalEditOrigin): Block {
  const attrs = {
    source: origin.source,
    actor: origin.actor ?? null,
    at: origin.at ?? new Date().toISOString(),
  }
  const apply = (node: Block): Block => {
    if (node.type === 'text') {
      const existing = (node.marks ?? []).filter((m) => !EXTERNAL_MARKS.includes(m.type as never))
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
 * The longest common subsequence of two block lists, by
 * <see cref="blockKey" />.
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

/**
 * What the draft should become: the blocks it and the published page agree
 * on, plus the ones each has alone, in an order a human can read.
 */
export function diffBlocks(draft: Block[], published: Block[]): ReconcileStep[] {
  const a = draft.map(blockKey)
  const b = published.map(blockKey)
  const table = lcs(a, b)
  const steps: ReconcileStep[] = []
  let i = 0
  let j = 0
  while (i < a.length && j < b.length) {
    if (a[i] === b[j]) {
      steps.push({ kind: 'keep', block: draft[i] })
      i++
      j++
    } else if (table[i + 1][j] >= table[i][j + 1]) {
      // In the draft and not on the page: the write removed it.
      steps.push({ kind: 'removed', block: draft[i] })
      i++
    } else {
      steps.push({ kind: 'added', block: published[j] })
      j++
    }
  }
  while (i < a.length) steps.push({ kind: 'removed', block: draft[i++] })
  while (j < b.length) steps.push({ kind: 'added', block: published[j++] })
  return steps
}

/**
 * The draft, with what an outside write did to the page shown inside it.
 *
 * Removed blocks stay where they were, struck through; added blocks appear
 * highlighted. A replaced paragraph therefore reads as the old one struck
 * through followed by the new one highlighted, which is a diff anyone has
 * read before.
 *
 * A block that is only in the draft because *this* human is still typing it
 * is indistinguishable, at this level, from one the write deleted. That is
 * the honest reading: the page no longer has it, and the human is the one who
 * decides. Rejecting keeps their text.
 */
export function reconcileDocument(draft: Block, published: Block, origin: ExternalEditOrigin): Block {
  const steps = diffBlocks(draft.content ?? [], published.content ?? [])
  const content: Block[] = []
  for (const step of steps) {
    if (step.kind === 'keep') content.push(step.block)
    else if (!hasInlineText(step.block)) {
      // Nothing to mark. An added one appears, a removed one goes: the
      // alternative is a picture nobody can act on.
      if (step.kind === 'added') content.push(step.block)
    } else {
      content.push(markBlock(step.block, step.kind === 'added' ? 'externalInsert' : 'externalDelete', origin))
    }
  }
  return { ...draft, content }
}

/**
 * Whether reconciling would change anything, used to skip the write (and the
 * Yjs transaction, and every connected editor's re-render) in the common case
 * where the draft already matches the page.
 */
export function needsReconcile(draft: Block, published: Block): boolean {
  const a = (draft.content ?? []).map(blockKey)
  const b = (published.content ?? []).map(blockKey)
  return a.length !== b.length || a.some((key, index) => key !== b[index])
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
export function normalise(schema: Schema, document: Block): Block {
  return schema.nodeFromJSON(document).toJSON() as Block
}

/**
 * Puts the reconciled document into the Yjs document the editors are sharing.
 *
 * The write is a *diff*, not a replacement: `updateYFragment` is
 * y-prosemirror's own minimal-change applier, the one the collaboration
 * extension uses for every keystroke, so untouched blocks are left alone in
 * the CRDT. That is what keeps other people's cursors where they were and
 * keeps the change small on the wire. Replacing the fragment wholesale would
 * work exactly once, look identical in a screenshot, and quietly clobber
 * anyone typing at the time.
 *
 * One `transact`, so every connected editor sees the whole reconciliation as
 * a single step rather than watching it assemble itself.
 *
 * The schema is a parameter rather than an import: the client has the
 * editor's, and the sidecar gets the same one from the bundle built in step
 * 3, which is what stops the two drifting.
 *
 * @returns whether anything changed, so callers can skip the version bump.
 */
export function reconcileYDoc(
  ydoc: YDocLike,
  schema: Schema,
  published: Block,
  origin: ExternalEditOrigin,
  fragmentName = 'default',
): boolean {
  const fragment = ydoc.getXmlFragment(fragmentName)

  // Both sides go through the schema before anything is compared, and this is
  // not ceremony. ProseMirror materialises every attribute a node type
  // declares, so a paragraph that came out of the CRDT carries
  // `textIndent: 0` while the same paragraph as the API stored it carries no
  // attrs at all. Comparing those two directly marks every block in the
  // document as changed, every time. (Found by running it, not by reading
  // it: the canonicaliser already forgives a null, and 0 is not a null.)
  const draft = normalise(schema, yXmlFragmentToProsemirrorJSON(fragment) as Block)
  const page = normalise(schema, published)

  // An empty fragment means nobody has opened this page since it was last
  // published, so there is no draft to reconcile against and nothing to mark:
  // seeding it with the page is the whole answer.
  const isEmpty = fragment.length === 0
  if (!isEmpty && !needsReconcile(draft, page)) return false

  const merged = isEmpty ? page : reconcileDocument(draft, page, origin)
  const node = schema.nodeFromJSON(merged)
  ydoc.transact(() => {
    updateYFragment(ydoc, fragment, node, { mapping: new Map(), isOMark: new Map() })
  })
  return true
}
