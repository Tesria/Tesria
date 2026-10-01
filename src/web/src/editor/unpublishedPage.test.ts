import { describe, expect, it } from 'vitest'
import { parseUnpublished, unpublishedKey, worthKeeping } from './unpublishedPage'

describe('unpublishedPage', () => {
  it('keys a copy by space and parent', () => {
    expect(unpublishedKey('s1', null)).toBe('tesria-unpublished-page:s1:top')
    expect(unpublishedKey('s1', 'p2')).toBe('tesria-unpublished-page:s1:p2')
  })

  it('keeps a page with a title or any content, not an empty one', () => {
    expect(worthKeeping('', '{"type":"doc","content":[]}')).toBe(false)
    expect(worthKeeping('  ', '{"type":"doc","content":[{"type":"paragraph"}]}')).toBe(false)
    expect(worthKeeping('Plan', '{"type":"doc","content":[]}')).toBe(true)
    expect(worthKeeping('', '{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"x"}]}]}')).toBe(true)
    expect(worthKeeping('', '{"type":"doc","content":[{"type":"horizontalRule"}]}')).toBe(true)
    expect(worthKeeping('', 'not json')).toBe(false)
  })

  it('reads back only a whole copy worth offering', () => {
    const good = { draftId: 'd', title: 'Plan', contentJson: '{"type":"doc","content":[]}', savedAt: '2026-10-01T12:00:00Z' }
    expect(parseUnpublished(JSON.stringify(good))).toEqual(good)
    expect(parseUnpublished(null)).toBeNull()
    expect(parseUnpublished('{')).toBeNull()
    expect(parseUnpublished(JSON.stringify({ ...good, draftId: 3 }))).toBeNull()
    expect(parseUnpublished(JSON.stringify({ ...good, title: '' }))).toBeNull()
  })
})
