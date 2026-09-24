import { describe, expect, it } from 'vitest'
import { splitMatch, visibleRows } from './treeFilter'

const rows = [
  { title: 'Getting started', depth: 0, marker: '1' },
  { title: 'Quick start', depth: 1, marker: '1.1' },
  { title: 'Installation', depth: 0, marker: '2' },
  { title: 'Backups and recovery', depth: 1, marker: '2.1' },
  { title: 'Offsite copies', depth: 2, marker: '2.1.1' },
  { title: 'Upgrading', depth: 1, marker: '2.2' },
  { title: 'Café rules', depth: 0, marker: '3' },
]

describe('visibleRows', () => {
  it('shows nothing special for an empty query', () => {
    expect(visibleRows(rows, '  ')).toBeNull()
  })

  it('shows a match with every parent above it, and nothing else', () => {
    expect([...visibleRows(rows, 'offsite')!].sort()).toEqual([2, 3, 4])
  })

  it('ignores case and accents', () => {
    expect([...visibleRows(rows, 'CAFE')!]).toEqual([6])
  })

  it('finds a page by its number in a numbered tree', () => {
    expect([...visibleRows(rows, '2.1.1')!].sort()).toEqual([2, 3, 4])
  })

  it('matches several pages at once', () => {
    expect([...visibleRows(rows, 'start')!].sort()).toEqual([0, 1])
  })

  it('shows the pages under a match too, when asked', () => {
    expect([...visibleRows(rows, 'installation', true)!].sort()).toEqual([2, 3, 4, 5])
    expect([...visibleRows(rows, 'installation')!]).toEqual([2])
  })

  it('shows nothing when nothing matches', () => {
    expect(visibleRows(rows, 'zzz')!.size).toBe(0)
  })
})

describe('splitMatch', () => {
  it('splits a title around the first match, keeping its case', () => {
    expect(splitMatch('Quick start', 'START')).toEqual(['Quick ', 'start', ''])
  })
  it('returns null when only the number matched', () => {
    expect(splitMatch('Offsite copies', '2.1')).toBeNull()
  })
})
