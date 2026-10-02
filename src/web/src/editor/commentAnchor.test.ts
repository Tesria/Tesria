import { describe, expect, it } from 'vitest'
import type { JSONContent } from '@tiptap/core'
import { getSchema } from '@tiptap/core'
import { updateYFragment, yXmlFragmentToProsemirrorJSON } from 'y-prosemirror'
import * as Y from 'yjs'
import { getSharedExtensions } from './extensions'
import { findQuote, isRefusal, placeComment, removeComment, type QuoteMatch, type QuoteRefusal } from './commentAnchor'
import { anchorQuote } from '../routes/panels/commentThreads'
import { inlineAtomText, normalizeQuote } from './commentQuote'
import { differsFromPage, hasUnpublishedChanges, mergeDocument, reconcileYDoc, resetYDoc } from './externalEdits'

/**
 * Inline comments placed by the server (dev-plan 22.2). The finder reads the
 * draft as `get_page` reads the page and marks text runs only; a highlight
 * then has to survive whatever happens to the draft afterwards: a reset to
 * the published page, a merge of an outside write, a second highlight on the
 * same words, a person typing at the same moment. The real schema, because
 * the comment mark's `excludes` is part of what is under test.
 */

const schema = getSchema(getSharedExtensions({ collaborative: true }))
const ORIGIN = { source: 'mcp' as const, actor: 'Docs Bot', at: '2026-10-02T09:00:00.000Z' }
const A = '11111111-1111-4111-8111-111111111111'
const B = '22222222-2222-4222-8222-222222222222'

const t = (text: string, marks?: JSONContent['marks']): JSONContent =>
  marks?.length ? { type: 'text', text, marks } : { type: 'text', text }
const para = (...content: JSONContent[]): JSONContent => ({ type: 'paragraph', content })
const p = (text: string) => para(t(text))
const doc = (...content: JSONContent[]): JSONContent => ({ type: 'doc', content })
const comment = (id: string) => [{ type: 'comment', attrs: { commentId: id } }]

function seed(json: JSONContent): Y.Doc {
  const ydoc = new Y.Doc()
  ydoc.transact(() => {
    updateYFragment(ydoc, ydoc.getXmlFragment('default'), schema.nodeFromJSON(json), { mapping: new Map(), isOMark: new Map() })
  })
  return ydoc
}

const read = (ydoc: Y.Doc) => yXmlFragmentToProsemirrorJSON(ydoc.getXmlFragment('default')) as JSONContent

/** The text each comment's highlight covers, in order, by comment id. */
function highlighted(json: JSONContent): Record<string, string[]> {
  const out: Record<string, string[]> = {}
  const walk = (n: JSONContent) => {
    if (n.type === 'text') {
      for (const m of n.marks ?? []) {
        if (m.type !== 'comment') continue
        const id = String(m.attrs?.commentId)
        ;(out[id] ??= []).push(n.text ?? '')
      }
    }
    n.content?.forEach(walk)
  }
  walk(json)
  return out
}

/** All the text of a document, block by block. */
const texts = (json: JSONContent) => (json.content ?? []).map((b) => {
  let s = ''
  const walk = (n: JSONContent) => { if (n.type === 'text') s += n.text ?? ''; n.content?.forEach(walk) }
  walk(b)
  return s
})

const refusal = (r: QuoteMatch | QuoteRefusal) => {
  expect(isRefusal(r)).toBe(true)
  return r as QuoteRefusal
}

describe('reading a passage as get_page writes it', () => {
  it('renders inline elements the way the Markdown export does', () => {
    expect(inlineAtomText('mention', { label: ' Sam ' })).toBe('@Sam')
    expect(inlineAtomText('mention', { label: '' })).toBe('@Unknown user')
    expect(inlineAtomText('status', { text: 'On track' })).toBe('`On track`')
    expect(inlineAtomText('date', { date: '2026-09-10' })).toBe('10 Sep 2026')
    expect(inlineAtomText('math', { latex: 'x^2' })).toBe('$x^2$')
    expect(inlineAtomText('smartLinkInline', { url: 'https://example.com' })).toBe('<https://example.com/>')
    expect(inlineAtomText('smartLinkInline', { url: 'javascript:alert(1)' })).toBe('')
    expect(inlineAtomText('hardBreak', {})).toBe(' ')
  })

  it('compares NFC, with whitespace collapsed, case and all', () => {
    expect(normalizeQuote('  Café\n\t opens ')).toBe('Café opens')
  })

  it('reads a stored anchor\'s passage for the Comments panel', () => {
    expect(anchorQuote('{"type":"text","quote":"on Friday","occurrence":1}')).toBe('on Friday')
    expect(anchorQuote('{"type":"text"}')).toBeNull()
    expect(anchorQuote('{"type":"image","src":"/x.png"}')).toBeNull()
    expect(anchorQuote('not json')).toBeNull()
    expect(anchorQuote(null)).toBeNull()
  })
})

describe('finding a quoted passage', () => {
  it('finds an exact passage', () => {
    const ydoc = seed(doc(p('We launch on Friday at noon.')))
    const found = findQuote(ydoc, schema, 'Friday at noon') as QuoteMatch
    expect(found.block).toBe(0)
    expect(found.ranges).toHaveLength(1)
    expect(found.ranges[0].index).toBe('We launch on '.length)
    expect(found.ranges[0].length).toBe('Friday at noon'.length)
  })

  it('ignores differences in spacing and line breaks, but not in capitals', () => {
    const ydoc = seed(doc({ type: 'codeBlock', attrs: { language: 'plaintext' }, content: [t('We   launch\non Friday')] }))
    expect(isRefusal(findQuote(ydoc, schema, 'We launch on\n  Friday'))).toBe(false)
    expect(refusal(findQuote(ydoc, schema, 'we launch')).code).toBe('quote_not_found')
  })

  it('matches composed and decomposed accents alike', () => {
    const ydoc = seed(doc(p('The Café opens at nine.')))
    expect(isRefusal(findQuote(ydoc, schema, 'Café opens'))).toBe(false)
    const other = seed(doc(p('The Café opens at nine.')))
    expect(isRefusal(findQuote(other, schema, 'Café opens'))).toBe(false)
  })

  it('runs across bold and a link, and marks every piece of text in it', () => {
    const ydoc = seed(doc(para(
      t('Ship '), t('the new', [{ type: 'bold' }]), t(' '),
      t('release notes', [{ type: 'link', attrs: { href: 'https://example.com/notes' } }]), t(' soon.'),
    )))
    expect(placeComment(ydoc, schema, { commentId: A, quote: 'the new release notes' }).status).toBe('placed')
    expect(highlighted(read(ydoc))[A]).toEqual(['the new', ' ', 'release notes'])
  })

  it('reads a mention and a status as get_page does, and marks only the text around them', () => {
    const ydoc = seed(doc(para(
      t('Ask '), { type: 'mention', attrs: { userId: B, label: 'Sam' } }, t(' whether it is '),
      { type: 'status', attrs: { text: 'On track', color: 'green' } }, t(' today.'),
    )))
    expect(placeComment(ydoc, schema, { commentId: A, quote: 'Ask @Sam whether it is `On track` today' }).status).toBe('placed')
    const json = read(ydoc)
    expect(highlighted(json)[A]).toEqual(['Ask ', ' whether it is ', ' today'])
    // The elements themselves carry no mark.
    const inline = json.content![0].content!
    expect(inline.filter((n) => n.type !== 'text').every((n) => !n.marks?.length)).toBe(true)
  })

  it('does not find text struck through as a pending outside deletion', () => {
    const struck = [{ type: 'externalDelete', attrs: { source: 'mcp', actor: null, at: null } }]
    const added = [{ type: 'externalInsert', attrs: { source: 'mcp', actor: null, at: null } }]
    const ydoc = seed(doc(para(t('old wording', struck), t('new wording', added))))
    expect(refusal(findQuote(ydoc, schema, 'old wording')).code).toBe('quote_not_found')
    expect(isRefusal(findQuote(ydoc, schema, 'new wording'))).toBe(false)
  })

  it('refuses a passage that runs across two blocks, and says so', () => {
    const ydoc = seed(doc(p('The first part'), p('the second part')))
    expect(refusal(findQuote(ydoc, schema, 'first part the second')).code).toBe('quote_spans_blocks')
  })

  it('picks an occurrence, and says how many there are when it cannot', () => {
    const ydoc = seed(doc(p('go and go'), { type: 'heading', attrs: { level: 2 }, content: [t('go')] }))
    expect((findQuote(ydoc, schema, 'go', 1) as QuoteMatch).block).toBe(0)
    expect((findQuote(ydoc, schema, 'go', 3) as QuoteMatch).block).toBe(1)
    const tooFar = refusal(findQuote(ydoc, schema, 'go', 4))
    expect(tooFar.code).toBe('quote_not_found')
    expect(tooFar.count).toBe(3)
    const which = refusal(findQuote(ydoc, schema, 'go'))
    expect(which.code).toBe('quote_ambiguous')
    expect(which.count).toBe(3)
  })

  it('refuses an empty quote and one over 1000 characters', () => {
    const ydoc = seed(doc(p('anything')))
    expect(refusal(findQuote(ydoc, schema, '  \n ')).code).toBe('quote_empty')
    expect(refusal(findQuote(ydoc, schema, 'a'.repeat(1001))).code).toBe('quote_too_long')
  })
})

describe('placing a highlight in the shared draft', () => {
  it('does nothing the second time for the same comment', () => {
    const ydoc = seed(doc(p('We launch on Friday.')))
    expect(placeComment(ydoc, schema, { commentId: A, quote: 'Friday' }).status).toBe('placed')
    let updates = 0
    ydoc.on('update', () => { updates++ })
    expect(placeComment(ydoc, schema, { commentId: A, quote: 'Friday' }).status).toBe('already')
    expect(updates).toBe(0)
  })

  it('places it in one step, and leaves the words alone', () => {
    const ydoc = seed(doc(p('We launch on Friday.')))
    let updates = 0
    ydoc.on('update', () => { updates++ })
    placeComment(ydoc, schema, { commentId: A, quote: 'Friday' })
    expect(updates).toBe(1)
    expect(texts(read(ydoc))).toEqual(['We launch on Friday.'])
  })

  it('keeps a concurrent insert from another editor where it was typed', () => {
    const a = seed(doc(p('We launch on Friday at noon.')))
    const b = new Y.Doc()
    Y.applyUpdate(b, Y.encodeStateAsUpdate(a))
    // Someone types at both ends of the paragraph, not yet synced...
    const text = b.getXmlFragment('default').get(0) as Y.XmlElement
    const run = text.get(0) as Y.XmlText
    run.insert(0, 'So, ')
    run.insert(run.length, ' Really.')
    // ...while the highlight is placed on the other copy.
    placeComment(a, schema, { commentId: A, quote: 'Friday at noon' })
    Y.applyUpdate(a, Y.encodeStateAsUpdate(b))
    Y.applyUpdate(b, Y.encodeStateAsUpdate(a))

    for (const ydoc of [a, b]) {
      expect(texts(read(ydoc))).toEqual(['So, We launch on Friday at noon. Really.'])
      expect(highlighted(read(ydoc))[A]).toEqual(['Friday at noon'])
    }
  })

  it('lets two comments share words, and both survive a round trip', () => {
    const ydoc = seed(doc(p('We launch on Friday at noon.')))
    placeComment(ydoc, schema, { commentId: A, quote: 'Friday at noon' })
    placeComment(ydoc, schema, { commentId: B, quote: 'at noon' })
    const once = read(ydoc)
    expect(highlighted(once)[A].join('')).toBe('Friday at noon')
    expect(highlighted(once)[B].join('')).toBe('at noon')

    // Through ProseMirror and back, as an editor opening it does.
    const again = read(seed(schema.nodeFromJSON(once).toJSON() as JSONContent))
    expect(highlighted(again)[A].join('')).toBe('Friday at noon')
    expect(highlighted(again)[B].join('')).toBe('at noon')
  })

  it('can be taken off again', () => {
    const ydoc = seed(doc(p('We launch on Friday.')))
    placeComment(ydoc, schema, { commentId: A, quote: 'Friday' })
    expect(removeComment(ydoc, schema, A)).toBe(true)
    expect(highlighted(read(ydoc))[A]).toBeUndefined()
    expect(removeComment(ydoc, schema, A)).toBe(false)
  })
})

describe('a highlight through a reset or a merge of the draft', () => {
  const page = doc(p('We launch on Friday.'), p('Questions to Sam.'))

  it('stays when the draft is reset to the same text', () => {
    const ydoc = seed(page)
    placeComment(ydoc, schema, { commentId: A, quote: 'Friday' })
    expect(resetYDoc(ydoc, schema, page)).toBe(false)
    expect(highlighted(read(ydoc))[A]).toEqual(['Friday'])
  })

  it('stays when a reset rewrites the rest of the page', () => {
    const ydoc = seed(page)
    placeComment(ydoc, schema, { commentId: A, quote: 'Friday' })
    expect(resetYDoc(ydoc, schema, doc(p('We launch on Friday.'), p('Questions to Priya.')))).toBe(true)
    expect(texts(read(ydoc))).toEqual(['We launch on Friday.', 'Questions to Priya.'])
    expect(highlighted(read(ydoc))[A]).toEqual(['Friday'])
  })

  it('goes when a reset takes its words away', () => {
    const ydoc = seed(page)
    placeComment(ydoc, schema, { commentId: A, quote: 'Friday' })
    resetYDoc(ydoc, schema, doc(p('We launch on Monday.'), p('Questions to Sam.')))
    expect(highlighted(read(ydoc))[A]).toBeUndefined()
  })

  it('follows its words when a reset moves them to another block', () => {
    const ydoc = seed(page)
    placeComment(ydoc, schema, { commentId: A, quote: 'Friday' })
    resetYDoc(ydoc, schema, doc(p('Intro.'), p('Questions to Sam.'), p('We launch on Friday.')))
    expect(highlighted(read(ydoc))[A]).toEqual(['Friday'])
  })

  it('is not a change in a three-way merge, so nothing is duplicated', () => {
    const ydoc = seed(page)
    placeComment(ydoc, schema, { commentId: A, quote: 'Friday' })
    expect(reconcileYDoc(ydoc, schema, page, ORIGIN, { base: page })).toBe(false)

    // The page gains a paragraph: one paragraph added, none repeated.
    const next = doc(p('We launch on Friday.'), p('Questions to Sam.'), p('Snacks provided.'))
    expect(reconcileYDoc(ydoc, schema, next, ORIGIN, { base: page })).toBe(true)
    expect(texts(read(ydoc))).toEqual(['We launch on Friday.', 'Questions to Sam.', 'Snacks provided.'])
    expect(highlighted(read(ydoc))[A]).toEqual(['Friday'])
  })

  it('does not count as unpublished work', () => {
    const marked = doc(para(t('We launch on '), t('Friday', comment(A)), t('.')), p('Questions to Sam.'))
    expect(differsFromPage(marked, page)).toBe(false)
    expect(hasUnpublishedChanges(marked, page)).toBe(false)
    const typed = doc(para(t('We launch on '), t('Friday', comment(A)), t('!')), p('Questions to Sam.'))
    expect(differsFromPage(typed, page)).toBe(true)
    expect(hasUnpublishedChanges(typed, page)).toBe(true)
  })

  it('keeps the draft\'s block, mark and all, when a merge finds it unchanged', () => {
    const marked = doc(para(t('We launch on '), t('Friday', comment(A)), t('.')), p('Questions to Sam.'))
    const merged = mergeDocument(page, marked, page, ORIGIN)
    expect(merged.content).toHaveLength(2)
    expect(highlighted(merged)[A]).toEqual(['Friday'])
  })
})
