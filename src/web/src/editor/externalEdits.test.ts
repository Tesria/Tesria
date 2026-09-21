import { describe, expect, it } from 'vitest'
import type { JSONContent } from '@tiptap/core'
import { Schema } from '@tiptap/pm/model'
import { updateYFragment, yXmlFragmentToProsemirrorJSON } from 'y-prosemirror'
import * as Y from 'yjs'
import {
  blockKey, diffBlocks, hasInlineText, markBlock, needsReconcile, reconcileDocument,
  reconcileYDoc, stripExternalMarks,
} from './externalEdits'
import { ExternalInsert } from './externalEditMarks'

/**
 * The block diff behind dev-plan 8.6.
 *
 * What these pin is the behaviour a human sees when an assistant writes to a
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

  it('finds an insertion in the middle without disturbing its neighbours', () => {
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

  it('are recognised as having no text', () => {
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
  it('says no for a draft that already matches the page', () => {
    expect(needsReconcile(doc(p('a'), p('b')), doc(p('a'), p('b')))).toBe(false)
  })

  it('says yes for a draft that still shows a pending deletion, and that is not a bug', () => {
    const pending = reconcileDocument(doc(p('old')), doc(p('new')), ORIGIN)

    // The draft now holds both "old" (struck) and "new" (highlighted), so it
    // does not match a page holding only "new", and the guard says so. The
    // cost of the false positive is one redundant pass, and that pass is
    // idempotent (see above), so it cannot corrupt anything. The alternative,
    // treating a struck block as already gone, would mean a second write
    // silently accepted the first one's deletion on the human's behalf, which
    // is the one thing this whole phase exists to stop.
    expect(needsReconcile(pending, doc(p('new')))).toBe(true)
    expect(reconcileDocument(pending, doc(p('new')), ORIGIN)).toEqual(pending)
  })

  it('says yes when the page has genuinely moved on', () => {
    expect(needsReconcile(doc(p('a')), doc(p('a'), p('b')))).toBe(true)
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
