import { describe, expect, it } from 'vitest'
import { appearanceAttribute, appearanceData, isAppearance } from './appearance'

// A block's own style (0.8.1). "theme" is the default and is never written
// out, so documents from before 0.8.1, and blocks nobody touched, are
// unchanged; only a real override reaches the page, and so an export.
describe('appearance', () => {
  it('knows its three values and nothing else', () => {
    expect(['theme', 'flat', 'glass'].every(isAppearance)).toBe(true)
    expect(isAppearance('frosted')).toBe(false)
    expect(isAppearance(undefined)).toBe(false)
  })

  it('writes the attribute only for an override', () => {
    expect(appearanceAttribute.renderHTML({ appearance: 'glass' })).toEqual({ 'data-appearance': 'glass' })
    expect(appearanceAttribute.renderHTML({ appearance: 'flat' })).toEqual({ 'data-appearance': 'flat' })
    expect(appearanceAttribute.renderHTML({ appearance: 'theme' })).toEqual({})
    expect(appearanceAttribute.renderHTML({})).toEqual({})
    expect(appearanceData('theme')).toEqual({})
    expect(appearanceData('glass')).toEqual({ 'data-appearance': 'glass' })
  })

  it('reads an unknown or missing value as the theme', () => {
    const el = (v: string | null) => ({ getAttribute: () => v }) as unknown as HTMLElement
    expect(appearanceAttribute.parseHTML(el('glass'))).toBe('glass')
    expect(appearanceAttribute.parseHTML(el('neon'))).toBe('theme')
    expect(appearanceAttribute.parseHTML(el(null))).toBe('theme')
  })
})
