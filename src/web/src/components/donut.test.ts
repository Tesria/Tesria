import { describe, expect, it } from 'vitest'
import { donutArcPath, donutFontSize, donutSegments } from './donut'

describe('donutSegments', () => {
  it('turns values into percentages of the ring, clockwise from the top', () => {
    expect(donutSegments([1, 3])).toEqual([
      { length: 25, start: 0 },
      { length: 75, start: 25 },
    ])
  })

  it('makes one value the whole ring', () => {
    expect(donutSegments([0, 5, 0])).toEqual([
      { length: 0, start: 0 },
      { length: 100, start: 0 },
      { length: 0, start: 100 },
    ])
  })

  it('counts negative and non-numeric values as nothing', () => {
    expect(donutSegments([-4, 2, Number.NaN, 2]).map((s) => s.length)).toEqual([0, 50, 0, 50])
  })

  it('draws no segment when everything is zero', () => {
    expect(donutSegments([0, 0]).every((s) => s.length === 0)).toBe(true)
    expect(donutSegments([])).toEqual([])
  })

  it('adds up to the whole ring', () => {
    const segments = donutSegments([3, 7, 11, 13])
    const last = segments[segments.length - 1]
    expect(last.start + last.length).toBeCloseTo(100, 10)
  })
})

describe('donutSegments with a minimum', () => {
  it('gives a tiny slice a visible sliver, taken from the others in proportion', () => {
    // The Backups page's disk: the wiki and its backups are far under 1%.
    const seg = donutSegments([0.2, 1.5, 1200, 656], 1)
    expect(seg[0].length).toBe(1)
    expect(seg[1].length).toBe(1)
    expect(seg[2].length / seg[3].length).toBeCloseTo(1200 / 656, 6)
    const last = seg[seg.length - 1]
    expect(last.start + last.length).toBeCloseTo(100, 10)
  })

  it('leaves slices at or above the minimum, and zeros, alone', () => {
    expect(donutSegments([1, 3, 0], 1).map((s) => s.length)).toEqual([25, 75, 0])
  })

  it('gives up the minimum when there are too many tiny slices to fit', () => {
    const seg = donutSegments(Array(120).fill(1), 1)
    expect(seg[0].length).toBeCloseTo(100 / 120, 10)
  })
})

describe('donutArcPath', () => {
  it('draws a quarter clockwise from the positive x axis', () => {
    expect(donutArcPath(0, 0, 10, 0, 25)).toBe('M 10.0000 0.0000 A 10.0000 10.0000 0 0 1 0.0000 10.0000')
  })

  it('uses the large-arc flag past half the ring', () => {
    expect(donutArcPath(0, 0, 10, 0, 75)).toContain(' 0 1 1 ')
    expect(donutArcPath(0, 0, 10, 0, 50)).toContain(' 0 0 1 ')
  })
})

describe('donutFontSize', () => {
  it('keeps short text at the largest size', () => {
    expect(donutFontSize('38%')).toBe(7.5)
  })

  it('shrinks long text to fit the hole', () => {
    const size = donutFontSize('1,234,567')
    expect(size).toBeLessThan(7.5)
    expect(size * 0.6 * '1,234,567'.length).toBeLessThanOrEqual(20.0001)
  })
})
