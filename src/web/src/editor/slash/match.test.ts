import { describe, expect, it } from 'vitest'
import { rankMatches } from './match'

const items = [
  { title: 'Heading 1', keywords: ['h1', 'heading', 'title'] },
  { title: 'Diagram (Mermaid)', keywords: ['mermaid', 'diagram', 'flowchart', 'graph'] },
  { title: 'Chart', keywords: ['chart', 'graph', 'bar', 'pie', 'line'] },
  { title: 'Info panel', keywords: ['panel', 'callout', 'info'] },
]
const titles = (q: string) => rankMatches(items, q).map((i) => i.title)

describe('rankMatches', () => {
  it('puts an exact match ahead of one that only contains the word', () => {
    expect(titles('chart')).toEqual(['Chart', 'Diagram (Mermaid)'])
  })
  it('puts a word that starts with the query ahead of one that contains it', () => {
    expect(titles('pan')).toEqual(['Info panel'])
    expect(titles('ch')).toEqual(['Chart', 'Diagram (Mermaid)'])
  })
  it('keeps the catalog order among equal matches', () => {
    expect(titles('graph')).toEqual(['Diagram (Mermaid)', 'Chart'])
  })
  it('returns everything for an empty query and nothing for no match', () => {
    expect(titles('')).toHaveLength(4)
    expect(titles('zzz')).toEqual([])
  })
})
