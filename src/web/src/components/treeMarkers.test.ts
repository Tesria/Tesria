import { describe, expect, it } from 'vitest'
import { SpaceTreeStyle, treeMarkers } from './treeMarkers'

describe('treeMarkers', () => {
  // Getting started > What is it, Prerequisites; Installation > Docker > Compose; Manual
  const depths = [0, 1, 1, 0, 1, 2, 0]

  it('numbers in outline, restarting under each parent', () => {
    expect(treeMarkers(depths, SpaceTreeStyle.Numbered)).toEqual(['1', '1.1', '1.2', '2', '2.1', '2.1.1', '3'])
  })

  it('bullets by level', () => {
    expect(treeMarkers(depths, SpaceTreeStyle.Bulleted)).toEqual(['•', '◦', '◦', '•', '◦', '▪', '•'])
  })

  it('marks nothing in a plain tree, or one whose style is unknown', () => {
    expect(treeMarkers(depths, SpaceTreeStyle.Plain)).toEqual(depths.map(() => null))
    expect(treeMarkers(depths, undefined)).toEqual(depths.map(() => null))
  })

  it('starts a new level at 1 after going back up', () => {
    expect(treeMarkers([0, 1, 2, 1, 2], SpaceTreeStyle.Numbered)).toEqual(['1', '1.1', '1.1.1', '1.2', '1.2.1'])
  })
})
