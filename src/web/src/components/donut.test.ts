import { describe, expect, it } from 'vitest'
import { donutFontSize, donutSegments } from './donut'

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
