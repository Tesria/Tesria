import { Extension, Mark, mergeAttributes, type Range } from '@tiptap/core'

/**
 * The two marks that show an edit made from outside this editing session
 * (dev-plan 8.6): text an API or MCP write added, and text it removed.
 *
 * A page has two stores, the published version in Postgres and the Yjs
 * document the editor actually edits. A write that only touched the first was
 * invisible here and got overwritten by the next Update. These marks are how
 * such a write becomes visible instead: it lands in the live document the way
 * a second person's typing does, and the human decides.
 *
 * `externalDelete` is text that is *already gone* from the published page and
 * is kept only so the reader can see what changed. Accepting removes it;
 * rejecting un-strikes it and removes the insertions. Neither mark may reach
 * a stored page version, which `PageWriter` enforces server-side rather than
 * trusting every client to have accepted first.
 *
 * Both are `inclusive: false`: typing at the boundary of a highlighted run
 * should produce the human's own ordinary text, not more text attributed to
 * an assistant.
 */

/** Who made the change, which decides the highlight's color and its label. */
/** `version`: a comparison of two versions in History (dev-plan 15.3), where the actor is the version. */
export type ExternalEditSource = 'api' | 'mcp' | 'page' | 'version'

export type ExternalEditAttrs = {
  source: ExternalEditSource | null
  /** Display name of the account or token behind the write. */
  actor: string | null
  /** ISO timestamp, for the hover label's "2 minutes ago". */
  at: string | null
}

export const EXTERNAL_MARKS = ['externalInsert', 'externalDelete'] as const
export type ExternalMarkName = (typeof EXTERNAL_MARKS)[number]

const SOURCE_LABELS: Record<ExternalEditSource, string> = {
  api: 'the API',
  mcp: 'MCP',
  page: 'another session',
  version: 'a later version',
}

/**
 * How long ago, in the words somebody would use.
 *
 * Computed when the mark renders rather than stored with it, which is the
 * point: a stored "2 minutes ago" is a lie within the hour, and a stored
 * timestamp is what the attribute already holds.
 */
function ago(iso: string): string {
  const then = Date.parse(iso)
  if (Number.isNaN(then)) return iso
  const seconds = Math.round((Date.now() - then) / 1000)
  // A clock that disagrees is not worth a sentence about the future.
  if (seconds < 0) return 'just now'
  const [value, unit] =
    seconds < 60 ? [seconds, 'second']
    : seconds < 3600 ? [Math.floor(seconds / 60), 'minute']
    : seconds < 86_400 ? [Math.floor(seconds / 3600), 'hour']
    : [Math.floor(seconds / 86_400), 'day']
  if (value === 0) return 'just now'
  return `${value} ${unit}${value === 1 ? '' : 's'} ago`
}

/**
 * The hover text, built at render time so the "when" stays true as the
 * document ages.
 */
function title(kind: 'Added' | 'Removed', attrs: Partial<ExternalEditAttrs>): string {
  // A comparison says which version, not who wrote it from where.
  if (attrs.source === 'version') return `${kind} in ${attrs.actor ?? 'the later version'}`
  const source = attrs.source ? SOURCE_LABELS[attrs.source] ?? attrs.source : 'outside this session'
  const actor = attrs.actor ? ` · ${attrs.actor}` : ''
  const at = attrs.at ? `, ${ago(attrs.at)}` : ''
  return `${kind} by ${source}${actor}${at}`
}

function attributes() {
  const attr = (name: keyof ExternalEditAttrs) => ({
    default: null,
    parseHTML: (element: HTMLElement) => element.getAttribute(`data-external-${name}`),
    renderHTML: (attrs: Record<string, unknown>) =>
      attrs[name] ? { [`data-external-${name}`]: attrs[name] } : {},
  })
  return { source: attr('source'), actor: attr('actor'), at: attr('at') }
}

export const ExternalInsert = Mark.create({
  name: 'externalInsert',
  inclusive: () => false,
  addAttributes: attributes,
  parseHTML: () => [{ tag: 'span[data-external-insert]' }],
  renderHTML({ HTMLAttributes, mark }) {
    return [
      'span',
      mergeAttributes(HTMLAttributes, {
        'data-external-insert': '',
        class: 'external-edit external-edit--insert',
        title: title('Added', mark.attrs as Partial<ExternalEditAttrs>),
      }),
      0,
    ]
  },
})

export const ExternalDelete = Mark.create({
  name: 'externalDelete',
  inclusive: () => false,
  addAttributes: attributes,
  parseHTML: () => [{ tag: 'span[data-external-delete]' }],
  renderHTML({ HTMLAttributes, mark }) {
    return [
      'span',
      mergeAttributes(HTMLAttributes, {
        'data-external-delete': '',
        class: 'external-edit external-edit--delete',
        title: title('Removed', mark.attrs as Partial<ExternalEditAttrs>),
      }),
      0,
    ]
  },
})

/* ---- finding and resolving pending changes ------------------------------ */

export type PendingExternalEdit = { name: ExternalMarkName; range: Range }

/**
 * Every run of either mark in the document, in document order.
 *
 * Walks the whole document rather than the selection: the banner counts what
 * is pending anywhere, and accepting applies to all of it. Adjacent positions
 * carrying the same mark are one run, so two words an assistant added in one
 * go count once rather than twice.
 */
export function findPendingExternalEdits(doc: import('@tiptap/pm/model').Node): PendingExternalEdit[] {
  const found: PendingExternalEdit[] = []
  doc.descendants((node, pos) => {
    if (!node.isText) return
    for (const name of EXTERNAL_MARKS) {
      if (!node.marks.some((m) => m.type.name === name)) continue
      const last = found.length > 0 ? found[found.length - 1] : null
      // Text nodes split on every differing mark, so a single highlighted run
      // can arrive as several nodes. Join them back up.
      if (last && last.name === name && last.range.to === pos) last.range.to = pos + node.nodeSize
      else found.push({ name, range: { from: pos, to: pos + node.nodeSize } })
    }
  })
  return found
}

export function countPendingExternalEdits(doc: import('@tiptap/pm/model').Node): number {
  return findPendingExternalEdits(doc).length
}

/* ---- accepting and rejecting -------------------------------------------- */

type Op =
  | { from: number; to: number; kind: 'delete' }
  | { from: number; to: number; kind: 'unmark'; mark: ExternalMarkName }

/**
 * What accepting or rejecting does, as a list of edits in document order.
 *
 * Accepting means "the outside write wins": its deletions really go, its
 * insertions become ordinary text. Rejecting is the mirror image. Either way
 * both marks are gone afterwards, which is the property that matters, since
 * neither may reach a published version.
 *
 * The case worth the extra pass is a block the write removed *entirely*. The
 * diff re-inserts it with every character marked, so deleting only the marked
 * text would accept the change and leave an empty paragraph behind. Such a
 * block is removed whole instead.
 */
function plan(doc: import('@tiptap/pm/model').Node, mode: 'accept' | 'reject'): Op[] {
  const doomed: ExternalMarkName = mode === 'accept' ? 'externalDelete' : 'externalInsert'
  const spared: ExternalMarkName = mode === 'accept' ? 'externalInsert' : 'externalDelete'
  const ops: Op[] = []
  const wholeBlocks: number[] = []

  doc.descendants((node, pos) => {
    if (!node.isTextblock || node.content.size === 0) return true
    let marked = false
    let unmarked = false
    node.forEach((child) => {
      if (child.isText && child.marks.some((m) => m.type.name === doomed)) marked = true
      else unmarked = true
    })
    if (marked && !unmarked) {
      ops.push({ from: pos, to: pos + node.nodeSize, kind: 'delete' })
      wholeBlocks.push(pos)
      return false // its inline content goes with it
    }
    return true
  })

  const inWholeBlock = (from: number) =>
    wholeBlocks.some((pos) => {
      const node = doc.nodeAt(pos)
      return node !== null && from > pos && from < pos + node.nodeSize
    })

  for (const edit of findPendingExternalEdits(doc)) {
    if (inWholeBlock(edit.range.from)) continue
    ops.push(
      edit.name === doomed
        ? { ...edit.range, kind: 'delete' }
        : { ...edit.range, kind: 'unmark', mark: spared },
    )
  }
  return ops
}

declare module '@tiptap/core' {
  interface Commands<ReturnType> {
    externalEdits: {
      /** Keep what the outside write did: drop its deletions, unwrap its insertions. */
      acceptExternalEdits: () => ReturnType
      /** Undo what the outside write did: drop its insertions, restore its deletions. */
      rejectExternalEdits: () => ReturnType
    }
  }
}

export const ExternalEditCommands = Extension.create({
  name: 'externalEditCommands',

  addCommands() {
    const run = (mode: 'accept' | 'reject') => () =>
      ({ state, tr, dispatch }: {
        state: import('@tiptap/pm/state').EditorState
        tr: import('@tiptap/pm/state').Transaction
        dispatch?: (tr: import('@tiptap/pm/state').Transaction) => void
      }) => {
        const ops = plan(state.doc, mode)
        if (ops.length === 0) return false
        if (!dispatch) return true
        // Back to front, so each edit's positions are still the ones measured
        // against the original document.
        for (const op of ops.sort((a, b) => b.from - a.from)) {
          if (op.kind === 'delete') tr.delete(op.from, op.to)
          else tr.removeMark(op.from, op.to, state.schema.marks[op.mark])
        }
        dispatch(tr)
        return true
      }

    return { acceptExternalEdits: run('accept'), rejectExternalEdits: run('reject') }
  },
})
