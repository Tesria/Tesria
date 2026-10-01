import { describe, expect, it } from 'vitest'
import { pointerMoved } from './pointerMoved'

describe('pointerMoved', () => {
  it('ignores the first event and a pointer standing still (t4-R04)', () => {
    expect(pointerMoved(null, { x: 10, y: 10 })).toBe(false)
    expect(pointerMoved({ x: 10, y: 10 }, { x: 10, y: 10 })).toBe(false)
  })
  it('follows a pointer that moves', () => {
    expect(pointerMoved({ x: 10, y: 10 }, { x: 10, y: 11 })).toBe(true)
  })
})
