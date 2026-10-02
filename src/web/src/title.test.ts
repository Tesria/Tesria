import { describe, expect, it } from 'vitest'
import { formatTitle, sectionFor } from './title'

// The same cases as BrandingTests.Title_* on the server: the two copies of
// this rule must agree, or a tab changes its title a moment after loading.
describe('formatTitle', () => {
  it('names a page by instance, space and page', () => {
    expect(formatTitle('Acme Docs', { space: 'Engineering', page: 'Architecture' }))
      .toBe('Acme Docs - Engineering / Architecture')
  })
  it('names a space with no page open', () => {
    expect(formatTitle('Acme Docs', { space: 'Engineering' })).toBe('Acme Docs - Engineering')
  })
  it('shows only the space while the page title is still loading', () => {
    expect(formatTitle('Acme Docs', { space: 'Engineering', page: '' })).toBe('Acme Docs - Engineering')
  })
  it('names a section outside any space', () => {
    expect(formatTitle('Acme Docs', { section: 'Search' })).toBe('Acme Docs - Search')
  })
  it('is just the instance name with nothing else to say', () => {
    expect(formatTitle('Acme Docs')).toBe('Acme Docs')
  })
  it('falls back to Tesria for a blank instance name', () => {
    expect(formatTitle('  ', { section: 'Search' })).toBe('Tesria - Search')
  })
  it('leaves slashes and hyphens in names alone', () => {
    expect(formatTitle('A-B', { space: 'R&D / Ops', page: 'Q3 - plan' })).toBe('A-B - R&D / Ops / Q3 - plan')
  })
  it('prefers the space over a section', () => {
    expect(formatTitle('X', { space: 'S', section: 'Search' })).toBe('X - S')
  })
  it('shortens a long space name and page title with an ellipsis (t3-R04)', () => {
    expect(formatTitle('X', { space: 'N'.repeat(200), page: 'Q'.repeat(500) }))
      .toBe(`X - ${'N'.repeat(79)}\u2026 / ${'Q'.repeat(79)}\u2026`)
    expect(formatTitle('X', { space: 'S', page: 'Q'.repeat(80) })).toBe(`X - S / ${'Q'.repeat(80)}`)
  })
  it('does not cut an emoji in half when shortening', () => {
    expect(formatTitle('X', { space: 'S', page: 'a'.repeat(78) + '\u{1F984}' + 'b'.repeat(10) }))
      .toBe(`X - S / ${'a'.repeat(78)}\u2026`)
  })
})

describe('sectionFor', () => {
  it('knows the sections', () => {
    expect(sectionFor('/spaces')).toBe('Spaces')
    expect(sectionFor('/admin/branding')).toBe('Administration')
    expect(sectionFor('/login')).toBe('Sign In')
    expect(sectionFor('/reset')).toBe('Reset Your Password')
    expect(sectionFor('/confirm-email')).toBe('Confirm Your Email')
  })
  it('leaves spaces to the space', () => {
    expect(sectionFor('/spaces/ENG/pages/1')).toBeNull()
  })
  it('has nothing to say about the root', () => {
    expect(sectionFor('/')).toBeNull()
  })
})
