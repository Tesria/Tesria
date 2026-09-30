import { describe, expect, it } from 'vitest'
import { project, type FlatNode } from './PageTree'

// Delta, Alpha, Bravo > (Charlie, Bravo one): QA T3-023's tree.
const rows: FlatNode[] = [
  { id: 'delta', title: 'Delta', parentId: null, depth: 0 },
  { id: 'alpha', title: 'Alpha', parentId: null, depth: 0 },
  { id: 'bravo', title: 'Bravo', parentId: null, depth: 0 },
  { id: 'charlie', title: 'Charlie', parentId: 'bravo', depth: 1 },
  { id: 'bravo1', title: 'Bravo one', parentId: 'bravo', depth: 1 },
]

describe('project', () => {
  it('moves the last page out a level when dragged left over its own row', () => {
    const result = project(rows, 'bravo1', 'bravo1', -40)
    expect(result?.depth).toBe(0)
    expect(result?.parentId).toBeNull()
    expect(result?.previousId).toBe('charlie')
  })

  it('leaves a page where it is when dragged over its own row without moving sideways', () => {
    const result = project(rows, 'bravo1', 'bravo1', 3)
    expect(result?.depth).toBe(1)
    expect(result?.parentId).toBe('bravo')
  })

  it('moves a page in a level under the one above it when dragged right over its own row', () => {
    const result = project(rows, 'alpha', 'alpha', 20)
    expect(result?.depth).toBe(1)
    expect(result?.parentId).toBe('delta')
  })

  it('keeps a first sub-page in while a sibling follows it', () => {
    // Moving Charlie out would leave Bravo one under a page it was not under.
    const result = project(rows, 'charlie', 'charlie', -40)
    expect(result?.depth).toBe(1)
    expect(result?.parentId).toBe('bravo')
  })

  it('still moves a page out when dragged left over the row below it', () => {
    const result = project(rows, 'charlie', 'bravo1', -40)
    expect(result?.depth).toBe(0)
    expect(result?.parentId).toBeNull()
  })
})
