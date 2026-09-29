import { describe, expect, it } from 'vitest'
import { outsideRange, restoreTimeRange, toLocalInput } from './restoreTime'

// Built from local parts, so the expectations hold in any time zone.
const local = (h: number, m: number, s = 0) => new Date(2026, 8, 29, h, m, s)
const iso = (d: Date) => d.toISOString()
const input = (h: number, m: number) => toLocalInput(local(h, m))

describe('restoreTimeRange', () => {
  it('keeps the default inside the range when the backup ended after the last reported WAL (KI-19)', () => {
    // The QA case: backup end 01:38:43, archive reported to 01:37:41.
    const r = restoreTimeRange(iso(local(1, 11, 35)), iso(local(1, 37, 41)), iso(local(1, 38, 43)))
    expect(r.max).toBe(input(1, 37))
    expect(r.initial).toBe(input(1, 37))
    expect(outsideRange(r.initial, r)).toBe(false)
  })

  it('rounds the minimum up and the maximum down, so every minute offered is inside', () => {
    const r = restoreTimeRange(iso(local(1, 11, 35)), iso(local(1, 38, 43)), iso(local(1, 38, 43)))
    expect(r.min).toBe(input(1, 12))
    expect(r.max).toBe(input(1, 38))
    expect(r.initial).toBe(input(1, 38))
  })

  it('leaves a whole minute as it is', () => {
    const r = restoreTimeRange(iso(local(1, 12)), iso(local(1, 38)), iso(local(1, 20)))
    expect(r).toEqual({ min: input(1, 12), max: input(1, 38), initial: input(1, 20) })
  })

  it('moves a default before the range up to its start', () => {
    const r = restoreTimeRange(iso(local(1, 11, 35)), iso(local(1, 38)), iso(local(1, 11, 35)))
    expect(r.initial).toBe(input(1, 12))
  })

  it('copes with no range at all', () => {
    expect(restoreTimeRange(null, null, iso(local(1, 20)))).toEqual({ min: undefined, max: undefined, initial: input(1, 20) })
    expect(restoreTimeRange(null, null, null).initial).toBe('')
  })
})

describe('outsideRange', () => {
  const r = restoreTimeRange(iso(local(1, 12)), iso(local(1, 38)), null)
  it('accepts the ends and what is between', () => {
    expect(outsideRange(input(1, 12), r)).toBe(false)
    expect(outsideRange(input(1, 38), r)).toBe(false)
    expect(outsideRange(input(1, 20), r)).toBe(false)
  })
  it('refuses what is outside', () => {
    expect(outsideRange(input(1, 11), r)).toBe(true)
    expect(outsideRange(input(1, 39), r)).toBe(true)
  })
  it('does not call an empty field outside', () => {
    expect(outsideRange('', r)).toBe(false)
  })
})
