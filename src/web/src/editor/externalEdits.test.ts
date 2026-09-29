import { describe, expect, it } from 'vitest'
import type { JSONContent } from '@tiptap/core'
import { Schema } from '@tiptap/pm/model'
import { updateYFragment, yXmlFragmentToProsemirrorJSON } from 'y-prosemirror'
import * as Y from 'yjs'
import { EditorState } from '@tiptap/pm/state'
import {
  acceptedKey, blockKey, diffBlocks, differsFromPage, hasInlineText, hasUnpublishedChanges, markBlock, mergeDocument,
  reconcileDocument, reconcileYDoc, resetYDoc, sameDocument, stripExternalMarks,
} from './externalEdits'
import { ExternalInsert, resolveExternalEdits } from './externalEditMarks'

/**
 * The block diff behind dev-plan 8.6.
 *
 * What these pin is the behavior a human sees when an assistant writes to a
 * page they have open: which blocks are treated as the same block, what a
 * replacement looks like, and the two cases the plan calls out by name, a
 * draft that already carries pending marks and a block with nothing a mark
 * can sit on.
 */

const ORIGIN = { source: 'mcp' as const, actor: 'Docs Bot', at: '2026-09-20T22:00:00.000Z' }

const p = (text: string): JSONContent => ({
  type: 'paragraph',
  content: [{ type: 'text', text }],
})

const doc = (...content: JSONContent[]): JSONContent => ({ type: 'doc', content })

/** Every piece of text in a node that carries the named mark. */
function textWith(node: JSONContent, mark: string): string[] {
  const found: string[] = []
  const walk = (n: JSONContent) => {
    if (n.type === 'text' && n.marks?.some((m) => m.type === mark)) found.push(n.text ?? '')
    n.content?.forEach(walk)
  }
  walk(node)
  return found
}

const kinds = (steps: ReturnType<typeof diffBlocks>) => steps.map((s) => s.kind)

describe('deciding whether two blocks are the same block', () => {
  it('ignores attribute order, because nothing in the round trip promises it', () => {
    const a: JSONContent = { type: 'heading', attrs: { level: 2, textAlign: 'left' }, content: [{ type: 'text', text: 'Hi' }] }
    const b: JSONContent = { type: 'heading', attrs: { textAlign: 'left', level: 2 }, content: [{ type: 'text', text: 'Hi' }] }

    expect(blockKey(a)).toBe(blockKey(b))
  })

  it('ignores attributes that are null, which is how TipTap spells "not set"', () => {
    const withNulls: JSONContent = { type: 'paragraph', attrs: { textAlign: null }, content: [{ type: 'text', text: 'Hi' }] }

    expect(blockKey(withNulls)).toBe(blockKey(p('Hi')))
  })

  it('ignores an empty container, which is the same difference one level up', () => {
    const withEmpties: JSONContent = { type: 'paragraph', attrs: {}, content: [{ type: 'text', text: 'Hi', marks: [] }] }
    const plain: JSONContent = { type: 'paragraph', content: [{ type: 'text', text: 'Hi' }] }

    expect(blockKey(withEmpties)).toBe(blockKey(plain))
    // An empty block is still not a block with something in it.
    expect(blockKey({ type: 'paragraph', content: [] })).not.toBe(blockKey(plain))
  })

  it('still tells genuinely different blocks apart', () => {
    expect(blockKey(p('one'))).not.toBe(blockKey(p('two')))
    expect(blockKey(p('one'))).not.toBe(blockKey({ type: 'heading', attrs: { level: 1 }, content: [{ type: 'text', text: 'one' }] }))
  })

  it('compares a draft as if its pending changes were accepted', () => {
    // The case the plan names. Without this, reconciling twice would diff
    // against the marks the first pass added and mark everything again.
    const pending = markBlock(p('Hi'), 'externalInsert', ORIGIN)

    expect(blockKey(pending)).toBe(blockKey(p('Hi')))
  })
})

describe('stripping pending marks', () => {
  it('keeps the text and any other marks on it', () => {
    const node: JSONContent = {
      type: 'paragraph',
      content: [{
        type: 'text',
        text: 'Hi',
        marks: [{ type: 'bold' }, { type: 'externalInsert', attrs: { source: 'mcp' } }, { type: 'link', attrs: { href: 'https://example.com' } }],
      }],
    }

    const stripped = stripExternalMarks(node)

    expect(stripped.content?.[0].marks?.map((m) => m.type)).toEqual(['bold', 'link'])
    expect(stripped.content?.[0].text).toBe('Hi')
  })

  it('drops the marks property entirely when nothing is left on it', () => {
    const stripped = stripExternalMarks(markBlock(p('Hi'), 'externalDelete', ORIGIN))

    expect(stripped.content?.[0]).not.toHaveProperty('marks')
  })
})

describe('the diff', () => {
  it('keeps blocks both sides agree on', () => {
    expect(kinds(diffBlocks([p('a'), p('b')], [p('a'), p('b')]))).toEqual(['keep', 'keep'])
  })

  it('reads a replacement as the old block removed and the new one added', () => {
    expect(kinds(diffBlocks([p('old')], [p('new')]))).toEqual(['removed', 'added'])
  })

  it('finds an insertion in the middle without disturbing its neighbors', () => {
    const steps = diffBlocks([p('a'), p('c')], [p('a'), p('b'), p('c')])

    expect(kinds(steps)).toEqual(['keep', 'added', 'keep'])
    expect(steps[1].block).toEqual(p('b'))
  })

  it('finds a deletion in the middle', () => {
    const steps = diffBlocks([p('a'), p('b'), p('c')], [p('a'), p('c')])

    expect(kinds(steps)).toEqual(['keep', 'removed', 'keep'])
    expect(steps[1].block).toEqual(p('b'))
  })

  it('treats a moved block as a removal and an addition rather than inventing a move', () => {
    // Honest rather than clever: the write's order is what the page has, and
    // pretending to know a block "moved" would hide one of the two edits.
    const steps = diffBlocks([p('a'), p('b')], [p('b'), p('a')])

    expect(kinds(steps).filter((k) => k !== 'keep').length).toBeGreaterThan(0)
    expect(steps.some((s) => s.kind === 'keep')).toBe(true)
  })

  it('handles an empty side at either end', () => {
    expect(kinds(diffBlocks([], [p('a')]))).toEqual(['added'])
    expect(kinds(diffBlocks([p('a')], []))).toEqual(['removed'])
    expect(diffBlocks([], [])).toEqual([])
  })
})

describe('the reconciled document', () => {
  it('shows a replacement as the old struck through, then the new highlighted', () => {
    const result = reconcileDocument(doc(p('old sentence')), doc(p('new sentence')), ORIGIN)

    expect(result.content).toHaveLength(2)
    expect(textWith(result, 'externalDelete')).toEqual(['old sentence'])
    expect(textWith(result, 'externalInsert')).toEqual(['new sentence'])
  })

  it('carries the source, the actor and the time onto every mark', () => {
    const result = reconcileDocument(doc(), doc(p('added')), ORIGIN)
    const mark = result.content?.[0].content?.[0].marks?.[0]

    expect(mark?.type).toBe('externalInsert')
    expect(mark?.attrs).toEqual({ source: 'mcp', actor: 'Docs Bot', at: ORIGIN.at })
  })

  it('leaves untouched blocks exactly as they were', () => {
    const untouched = p('unchanged')
    const result = reconcileDocument(doc(untouched, p('old')), doc(untouched, p('new')), ORIGIN)

    expect(result.content?.[0]).toEqual(untouched)
  })

  it('keeps the human text a write deleted, so rejecting can restore it', () => {
    const result = reconcileDocument(doc(p('mine')), doc(), ORIGIN)

    expect(textWith(result, 'externalDelete')).toEqual(['mine'])
  })

  it('replaces a pending mark rather than stacking a second one on it', () => {
    // Two writes in a row, where the second changes the first's text again.
    const once = reconcileDocument(doc(p('original')), doc(p('first write')), ORIGIN)
    const twice = reconcileDocument(once, doc(p('second write')), { ...ORIGIN, actor: 'Someone else' })

    const marksOnText: string[][] = []
    const walk = (n: JSONContent) => {
      if (n.type === 'text') marksOnText.push((n.marks ?? []).map((m) => m.type))
      n.content?.forEach(walk)
    }
    walk(twice)
    for (const marks of marksOnText) {
      expect(marks.filter((m) => m === 'externalInsert' || m === 'externalDelete').length).toBeLessThanOrEqual(1)
    }
  })

  it('is idempotent: reconciling against the same page again changes nothing', () => {
    const once = reconcileDocument(doc(p('old')), doc(p('new')), ORIGIN)
    const twice = reconcileDocument(once, doc(p('new')), ORIGIN)

    expect(twice).toEqual(once)
  })
})

describe('blocks with nothing a mark can sit on', () => {
  // The other case the plan names. A mark lives on inline content, so an
  // image or a divider cannot carry one.
  const image: JSONContent = { type: 'image', attrs: { src: '/api/attachments/x/download', alt: 'a diagram' } }
  const rule: JSONContent = { type: 'horizontalRule' }

  it('are recognized as having no text', () => {
    expect(hasInlineText(image)).toBe(false)
    expect(hasInlineText(rule)).toBe(false)
    expect(hasInlineText(p('words'))).toBe(true)
  })

  it('appear plainly when a write adds one', () => {
    const result = reconcileDocument(doc(p('a')), doc(p('a'), image), ORIGIN)

    expect(result.content).toHaveLength(2)
    expect(result.content?.[1]).toEqual(image)
  })

  it('disappear when a write removes one, rather than leaving a picture nobody can act on', () => {
    const result = reconcileDocument(doc(p('a'), image), doc(p('a')), ORIGIN)

    expect(result.content).toHaveLength(1)
    expect(result.content?.[0]).toEqual(p('a'))
  })

  it('does not mark an empty paragraph either', () => {
    const empty: JSONContent = { type: 'paragraph' }
    const result = reconcileDocument(doc(empty), doc(), ORIGIN)

    expect(result.content).toHaveLength(0)
  })
})

describe('knowing when there is nothing to do', () => {
  it('leaves a draft that already matches the page exactly as it is', () => {
    const draft = doc(p('a'), p('b'))

    expect(sameDocument(reconcileDocument(draft, doc(p('a'), p('b')), ORIGIN), draft)).toBe(true)
  })

  it('keeps a pending deletion pending, rather than accepting it on the human\'s behalf', () => {
    const pending = reconcileDocument(doc(p('old')), doc(p('new')), ORIGIN)

    // The draft holds both "old" (struck) and "new" (highlighted). Read as
    // accepted it matches the page, so nothing more is marked; but the struck
    // block stays, because nobody has decided about it yet.
    expect(reconcileDocument(pending, doc(p('new')), ORIGIN)).toEqual(pending)
    expect(textWith(pending, 'externalDelete')).toEqual(['old'])
  })

  it('notices when the page has genuinely moved on', () => {
    const draft = doc(p('a'))

    expect(sameDocument(reconcileDocument(draft, doc(p('a'), p('b')), ORIGIN), draft)).toBe(false)
  })
})

/* ---- 0.8.2: the three-way merge ----------------------------------------- */

const li = (text: string): JSONContent => ({ type: 'listItem', content: [p(text)] })
const list = (...items: string[]): JSONContent => ({ type: 'bulletList', content: items.map(li) })

/** Each top-level block as "text" plus what is pending on it: +added, -removed. */
function outline(node: JSONContent): string[] {
  return (node.content ?? []).map((block) => {
    const text: string[] = []
    const walk = (n: JSONContent) => {
      if (n.type === 'text') text.push(n.text ?? '')
      n.content?.forEach(walk)
    }
    walk(block)
    const flag = textWith(block, 'externalInsert').length > 0 ? '+'
      : textWith(block, 'externalDelete').length > 0 ? '-' : ''
    return `${flag}${text.join('|')}`
  })
}

describe('the three-way merge: only what the write changed is highlighted', () => {
  it('never strikes through words the human typed and has not published (T5-032)', () => {
    const base = doc(p('Alpha paragraph.'), p('Beta paragraph.'))
    const draft = doc(p('Alpha paragraph. Priya is typing here.'), p('Beta paragraph.'))
    const page = doc(p('Alpha paragraph, edited by the agent.'), p('Beta paragraph.'))

    const merged = mergeDocument(base, draft, page, ORIGIN)

    // Both changed the same paragraph: hers stays exactly as it is, and the
    // agent's version follows it as a highlighted suggestion.
    expect(outline(merged)).toEqual([
      'Alpha paragraph. Priya is typing here.',
      '+Alpha paragraph, edited by the agent.',
      'Beta paragraph.',
    ])
    expect(textWith(merged, 'externalDelete')).toEqual([])
    expect(merged.content?.[0]).toEqual(draft.content?.[0])
  })

  it('shows the write\'s change to a block the human did not touch as a replacement', () => {
    const base = doc(p('one'), p('two'))
    const draft = doc(p('one, mine'), p('two'))
    const page = doc(p('one'), p('two, theirs'))

    expect(outline(mergeDocument(base, draft, page, ORIGIN))).toEqual(['one, mine', '-two', '+two, theirs'])
  })

  it('leaves the human\'s new paragraphs alone when the write changed something else', () => {
    const base = doc(p('one'), p('two'))
    const draft = doc(p('one'), p('my new paragraph'), p('two'))
    const page = doc(p('one'), p('two'), p('their new paragraph'))

    expect(outline(mergeDocument(base, draft, page, ORIGIN)))
      .toEqual(['one', 'my new paragraph', 'two', '+their new paragraph'])
  })

  it('strikes a block the write removed, but not one the human has since changed', () => {
    const base = doc(p('keep'), p('untouched'), p('edited'))
    const draft = doc(p('keep'), p('untouched'), p('edited by me'))
    const page = doc(p('keep'))

    // "untouched" is the write's to remove; "edited by me" is the human's
    // work, and removing a block is never a reason to lose it.
    expect(outline(mergeDocument(base, draft, page, ORIGIN))).toEqual(['keep', '-untouched', 'edited by me'])
  })

  it('shows nothing where both made the same change', () => {
    const base = doc(p('typo hre'))
    const draft = doc(p('typo here'))
    const page = doc(p('typo here'))

    const merged = mergeDocument(base, draft, page, ORIGIN)
    expect(merged).toEqual(draft)
  })

  it('keeps the human\'s deletion and offers the write\'s rewrite of that block as a suggestion', () => {
    const base = doc(p('a'), p('b'))
    const draft = doc(p('a'))
    const page = doc(p('a'), p('b, rewritten'))

    expect(outline(mergeDocument(base, draft, page, ORIGIN))).toEqual(['a', '+b, rewritten'])
  })

  it('is idempotent once the draft has been brought up to the new version', () => {
    const base = doc(p('Alpha.'), p('Beta.'))
    const draft = doc(p('Alpha. Mine.'), p('Beta.'))
    const page = doc(p('Alpha.'), p('Beta, theirs.'))

    const once = mergeDocument(base, draft, page, ORIGIN)
    // meta.version now names the new page, so it is the base.
    const twice = mergeDocument(page, once, page, { ...ORIGIN, at: '2026-09-30T00:00:00.000Z' })

    expect(twice).toEqual(once)
  })

  it('keeps an earlier write\'s pending deletion where it was through a second write', () => {
    const v1 = doc(p('first'), p('second'), p('third'))
    const v2 = doc(p('first'), p('third'))
    const draft = mergeDocument(v1, v1, v2, ORIGIN)
    expect(outline(draft)).toEqual(['first', '-second', 'third'])

    const v3 = doc(p('first'), p('third, changed'))
    const merged = mergeDocument(v2, draft, v3, ORIGIN)

    expect(outline(merged)).toEqual(['first', '-second', '-third', '+third, changed'])
  })

  it('drops an earlier write\'s pending addition when a later write removes it again', () => {
    const v1 = doc(p('a'))
    const v2 = doc(p('a'), p('added'))
    const draft = mergeDocument(v1, v1, v2, ORIGIN)
    expect(outline(draft)).toEqual(['a', '+added'])

    // It was never the human's, and it is no longer the page's.
    expect(outline(mergeDocument(v2, draft, v1, ORIGIN))).toEqual(['a'])
  })

  it('treats a list with an item added as the old list replaced, next to it (T5-002)', () => {
    // The editor keeps an empty paragraph after a trailing list; it must not
    // end up between the old list and its replacement.
    const empty: JSONContent = { type: 'paragraph' }
    const base = doc(p('Intro.'), list('One', 'Two'))
    const draft = doc(p('Intro.'), list('One', 'Two'), empty)
    const page = doc(p('Intro.'), list('One', 'Two', 'Three'))

    const merged = mergeDocument(base, draft, page, ORIGIN)

    expect(outline(merged)).toEqual(['Intro.', '-One|Two', '+One|Two|Three', ''])
  })

  it('compares highlighted text as the ordinary text it becomes when accepted', () => {
    const pending: JSONContent = {
      type: 'paragraph',
      content: [
        { type: 'text', text: 'Alpha ', marks: [{ type: 'externalInsert', attrs: { source: 'mcp' } }] },
        { type: 'text', text: 'paragraph' },
        { type: 'text', text: ' gone', marks: [{ type: 'externalDelete', attrs: { source: 'mcp' } }] },
      ],
    }

    expect(acceptedKey(pending)).toBe(acceptedKey(p('Alpha paragraph')))
  })

  it('falls back to the plain comparison when the base is unknown', () => {
    const draft = doc(p('mine'))

    expect(outline(mergeDocument(null, draft, doc(p('theirs')), ORIGIN))).toEqual(['-mine', '+theirs'])
  })
})

describe('knowing whether a draft holds unpublished work', () => {
  const empty: JSONContent = { type: 'paragraph' }

  it('says no for a draft that is the page', () => {
    expect(hasUnpublishedChanges(doc(p('a'), list('x')), doc(p('a'), list('x')))).toBe(false)
  })

  it('ignores the empty paragraph the editor keeps after a trailing list', () => {
    expect(hasUnpublishedChanges(doc(p('a'), list('x'), empty), doc(p('a'), list('x')))).toBe(false)
    expect(hasUnpublishedChanges(doc(empty), doc())).toBe(false)
  })

  it('says yes for typing nobody has published', () => {
    expect(hasUnpublishedChanges(doc(p('a, and more')), doc(p('a')))).toBe(true)
  })

  it('says yes for outside changes nobody has decided on', () => {
    expect(hasUnpublishedChanges(reconcileDocument(doc(p('a')), doc(p('b')), ORIGIN), doc(p('b')))).toBe(true)
  })

  it('does not count undecided outside changes as a person\'s unpublished work', () => {
    const pending = reconcileDocument(doc(p('a')), doc(p('b')), ORIGIN)

    expect(differsFromPage(pending, doc(p('b')))).toBe(false)
    expect(differsFromPage(doc(p('b, and mine')), doc(p('b')))).toBe(true)
  })
})

describe('accepting and rejecting leave no leftover copy (T5-002)', () => {
  const schema = new Schema({
    nodes: {
      doc: { content: 'block+' },
      paragraph: { group: 'block', content: 'inline*', toDOM: () => ['p', 0] },
      bulletList: { group: 'block', content: 'listItem+', toDOM: () => ['ul', 0] },
      listItem: { content: 'paragraph block*', toDOM: () => ['li', 0] },
      table: { group: 'block', content: 'tableRow+', toDOM: () => ['table', ['tbody', 0]] },
      tableRow: { content: 'tableCell+', toDOM: () => ['tr', 0] },
      tableCell: { content: 'block+', toDOM: () => ['td', 0] },
      text: { group: 'inline' },
    },
    marks: {
      externalInsert: { attrs: { source: { default: null }, actor: { default: null }, at: { default: null } }, toDOM: () => ['span', 0] },
      externalDelete: { attrs: { source: { default: null }, actor: { default: null }, at: { default: null } }, toDOM: () => ['span', 0] },
    },
  })

  const resolve = (json: JSONContent, mode: 'accept' | 'reject') => {
    const state = EditorState.create({ doc: schema.nodeFromJSON(json) })
    const tr = state.tr
    resolveExternalEdits(tr, mode)
    return tr.doc.toJSON() as JSONContent
  }

  const row = (...cells: string[]): JSONContent => ({ type: 'tableRow', content: cells.map((c) => ({ type: 'tableCell', content: [p(c)] })) })
  const table = (...rows: JSONContent[]): JSONContent => ({ type: 'table', content: rows })

  const listChange = mergeDocument(
    doc(p('Intro.'), list('One', 'Two')),
    doc(p('Intro.'), list('One', 'Two')),
    doc(p('Intro.'), list('One', 'Two', 'Three')),
    ORIGIN,
  )

  it('accepting a list change leaves one list, the new one', () => {
    const result = resolve(listChange, 'accept')

    expect(outline(result)).toEqual(['Intro.', 'One|Two|Three'])
    expect(textWith(result, 'externalInsert')).toEqual([])
  })

  it('rejecting it leaves one list, the old one', () => {
    expect(outline(resolve(listChange, 'reject'))).toEqual(['Intro.', 'One|Two'])
  })

  it('does the same for a table with a row added', () => {
    const tableChange = mergeDocument(
      doc(p('x'), table(row('a', 'b'))),
      doc(p('x'), table(row('a', 'b'))),
      doc(p('x'), table(row('a', 'b'), row('c', 'd'))),
      ORIGIN,
    )

    expect(outline(resolve(tableChange, 'accept'))).toEqual(['x', 'a|b|c|d'])
    expect(outline(resolve(tableChange, 'reject'))).toEqual(['x', 'a|b'])
  })

  it('still removes only the marked words inside a paragraph the human kept', () => {
    const json = doc({
      type: 'paragraph',
      content: [
        { type: 'text', text: 'keep ' },
        { type: 'text', text: 'drop', marks: [{ type: 'externalDelete', attrs: { source: 'mcp' } }] },
      ],
    })

    expect(outline(resolve(json, 'accept'))).toEqual(['keep '])
  })

  it('accepting a suggestion next to the human\'s own version keeps both, for the human to finish', () => {
    const merged = mergeDocument(doc(p('A.')), doc(p('A. Mine.')), doc(p('A, theirs.')), ORIGIN)

    expect(outline(resolve(merged, 'accept'))).toEqual(['A. Mine.', 'A, theirs.'])
    expect(outline(resolve(merged, 'reject'))).toEqual(['A. Mine.'])
  })
})

describe('applying it to a Yjs document', () => {
  // A small schema rather than the application's: what is under test is the
  // Yjs write, and the real schema would drag every node view in with it.
  const schema = new Schema({
    nodes: {
      doc: { content: 'block+' },
      // `textIndent` has a non-null default, exactly like the real schema's,
      // which is what the regression test below turns on.
      paragraph: {
        group: 'block',
        content: 'inline*',
        attrs: { textAlign: { default: null }, textIndent: { default: 0 } },
        toDOM: () => ['p', 0],
      },
      image: { group: 'block', attrs: { src: {} }, toDOM: () => ['img'] },
      text: { group: 'inline' },
    },
    marks: {
      externalInsert: { attrs: { source: { default: null }, actor: { default: null }, at: { default: null } }, toDOM: () => ['span', 0] },
      externalDelete: { attrs: { source: { default: null }, actor: { default: null }, at: { default: null } }, toDOM: () => ['span', 0] },
    },
  })

  const seed = (json: JSONContent) => {
    const ydoc = new Y.Doc()
    const fragment = ydoc.getXmlFragment('default')
    ydoc.transact(() => {
      updateYFragment(ydoc, fragment, schema.nodeFromJSON(json), { mapping: new Map(), isOMark: new Map() })
    })
    return ydoc
  }

  const read = (ydoc: Y.Doc) => yXmlFragmentToProsemirrorJSON(ydoc.getXmlFragment('default')) as JSONContent

  it('seeds an untouched document straight from the page, with nothing marked', () => {
    // Nobody has opened this page since it was published, so there is no
    // draft to disagree with and nothing to highlight.
    const ydoc = new Y.Doc()

    expect(reconcileYDoc(ydoc, schema, doc(p('from the page')), ORIGIN)).toBe(true)
    expect(textWith(read(ydoc), 'externalInsert')).toEqual([])
    expect(read(ydoc).content).toHaveLength(1)
  })

  it('writes the tracked changes into the shared document', () => {
    const ydoc = seed(doc(p('old')))

    expect(reconcileYDoc(ydoc, schema, doc(p('new')), ORIGIN)).toBe(true)

    const result = read(ydoc)
    expect(textWith(result, 'externalDelete')).toEqual(['old'])
    expect(textWith(result, 'externalInsert')).toEqual(['new'])
  })

  it('does nothing at all when the draft already matches the page', () => {
    const ydoc = seed(doc(p('same')))
    let updates = 0
    ydoc.on('update', () => { updates++ })

    expect(reconcileYDoc(ydoc, schema, doc(p('same')), ORIGIN)).toBe(false)
    expect(updates).toBe(0)
  })

  it('leaves the blocks it did not touch alone in the CRDT', () => {
    // The property that keeps other people's cursors where they were: an
    // untouched paragraph must not be deleted and re-inserted.
    const ydoc = seed(doc(p('untouched'), p('old')))
    const before = ydoc.getXmlFragment('default').get(0)

    reconcileYDoc(ydoc, schema, doc(p('untouched'), p('new')), ORIGIN)

    expect(ydoc.getXmlFragment('default').get(0)).toBe(before)
  })

  it('applies the whole reconciliation as one step', () => {
    const ydoc = seed(doc(p('a'), p('b'), p('c')))
    let updates = 0
    ydoc.on('update', () => { updates++ })

    reconcileYDoc(ydoc, schema, doc(p('a'), p('B'), p('C')), ORIGIN)

    expect(updates).toBe(1)
  })

  it('reaches every editor sharing the document', () => {
    // Two peers, as two browser tabs would be.
    const a = seed(doc(p('old')))
    const b = new Y.Doc()
    Y.applyUpdate(b, Y.encodeStateAsUpdate(a))
    a.on('update', (u: Uint8Array) => Y.applyUpdate(b, u))

    reconcileYDoc(a, schema, doc(p('new')), ORIGIN)

    expect(textWith(read(b), 'externalInsert')).toEqual(['new'])
    expect(textWith(read(b), 'externalDelete')).toEqual(['old'])
  })

  it('does not mistake the schema\'s own default attributes for a change', () => {
    // The bug this is here for: ProseMirror materialises every attribute a
    // node declares, so a paragraph out of the CRDT carries `textIndent: 0`
    // while the same paragraph as the API stored it carries no attrs at all.
    // Compared directly, every block in every document reads as changed, on
    // every reconcile. Found by running the sidecar, not by reading the code.
    const ydoc = seed(doc(p('unchanged')))
    const asTheApiStoredIt: JSONContent = {
      type: 'doc',
      content: [{ type: 'paragraph', content: [{ type: 'text', text: 'unchanged' }] }],
    }

    expect(reconcileYDoc(ydoc, schema, asTheApiStoredIt, ORIGIN)).toBe(false)
    expect(textWith(read(ydoc), 'externalInsert')).toEqual([])
    expect(read(ydoc).content).toHaveLength(1)
  })

  it('carries a block with no inline content through without marking it', () => {
    const image: JSONContent = { type: 'image', attrs: { src: '/x.png' } }
    const ydoc = seed(doc(p('a')))

    reconcileYDoc(ydoc, schema, doc(p('a'), image), ORIGIN)

    const result = read(ydoc)
    expect(result.content?.[1]?.type).toBe('image')
    expect(textWith(result, 'externalInsert')).toEqual([])
  })

  it('merges against the base it is given, so typing in progress is never struck', () => {
    const base = doc(p('Alpha.'), p('Beta.'))
    const ydoc = seed(doc(p('Alpha. Typing.'), p('Beta.')))

    reconcileYDoc(ydoc, schema, doc(p('Alpha, agent.'), p('Beta.')), ORIGIN, { base })

    expect(textWith(read(ydoc), 'externalDelete')).toEqual([])
    expect(outline(read(ydoc))).toEqual(['Alpha. Typing.', '+Alpha, agent.', 'Beta.'])
  })

  it('merges a reconnecting editor\'s offline typing with the server\'s reconcile, once each (T5-015)', () => {
    // The server's copy and the browser's copy of one document.
    const server = seed(doc(p('Line one.'), p('Line two.')))
    const browser = new Y.Doc()
    Y.applyUpdate(browser, Y.encodeStateAsUpdate(server))

    // Offline: the browser types into line one; the server, restarted,
    // reconciles to the version the API wrote meanwhile.
    const typed = doc(p('Line one. Offline edit.'), p('Line two.'))
    browser.transact(() => {
      updateYFragment(browser, browser.getXmlFragment('default'), schema.nodeFromJSON(typed), { mapping: new Map(), isOMark: new Map() })
    })
    reconcileYDoc(server, schema, doc(p('Line one.'), p('Line two changed by API.')), ORIGIN,
      { base: doc(p('Line one.'), p('Line two.')) })

    // Reconnecting exchanges both ways.
    Y.applyUpdate(server, Y.encodeStateAsUpdate(browser))
    Y.applyUpdate(browser, Y.encodeStateAsUpdate(server))

    const result = outline(read(browser))
    expect(result).toEqual(['Line one. Offline edit.', '-Line two.', '+Line two changed by API.'])
    expect(outline(read(server))).toEqual(result)
  })

  it('resets a draft to the page exactly, with nothing marked', () => {
    const ydoc = seed(doc(p('published'), p('somebody\'s unpublished paragraph')))

    expect(resetYDoc(ydoc, schema, doc(p('published')))).toBe(true)

    expect(outline(read(ydoc))).toEqual(['published'])
    expect(resetYDoc(ydoc, schema, doc(p('published')))).toBe(false)
  })

  it('resets in place, so an editor that still holds the old document does not get two copies', () => {
    const server = seed(doc(p('one'), p('two, unpublished')))
    const browser = new Y.Doc()
    Y.applyUpdate(browser, Y.encodeStateAsUpdate(server))

    resetYDoc(server, schema, doc(p('one'), p('two')))
    Y.applyUpdate(browser, Y.encodeStateAsUpdate(server))
    Y.applyUpdate(server, Y.encodeStateAsUpdate(browser))

    expect(outline(read(browser))).toEqual(['one', 'two'])
  })
})

describe('the hover label', () => {
  // Rendered through the real mark, since that is where the wording lives.
  const titleOf = (attrs: Record<string, unknown>) => {
    const rendered = ExternalInsert.config.renderHTML?.call(
      { options: {}, name: 'externalInsert' } as never,
      { HTMLAttributes: {}, mark: { attrs } } as never,
    ) as [string, Record<string, string>, number]
    return rendered[1].title
  }

  it('says who and how long ago, in words', () => {
    const label = titleOf({ source: 'mcp', actor: 'Docs Bot', at: new Date(Date.now() - 125_000).toISOString() })

    expect(label).toBe('Added by MCP · Docs Bot, 2 minutes ago')
  })

  it('uses the singular for one of anything', () => {
    expect(titleOf({ source: 'api', at: new Date(Date.now() - 3_600_000).toISOString() }))
      .toBe('Added by the API, 1 hour ago')
  })

  it('says "just now" rather than "0 seconds ago", or a time in the future', () => {
    expect(titleOf({ source: 'page', at: new Date().toISOString() })).toContain('just now')
    // A clock that disagrees is not worth a sentence about the future.
    expect(titleOf({ source: 'page', at: new Date(Date.now() + 60_000).toISOString() })).toContain('just now')
  })

  it('falls back to whatever was stored rather than printing NaN', () => {
    expect(titleOf({ source: 'mcp', at: 'not a date' })).toContain('not a date')
  })

  it('still reads when nobody is named', () => {
    expect(titleOf({ source: 'mcp' })).toBe('Added by MCP')
  })
})
