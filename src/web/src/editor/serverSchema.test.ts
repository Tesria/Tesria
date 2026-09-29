import { describe, expect, it } from 'vitest'
import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { getSchema } from '@tiptap/core'
import { getSharedExtensions } from './extensions'

/**
 * The server refuses a page whose document has an element or mark the
 * editor does not know (T5-023), using the names in
 * `src/Api/Features/Pages/editor-schema.json`. If that list and this schema
 * disagree, the server either refuses the editor's own pages (a name missing
 * there) or accepts pages the editor shows empty (a name left over there).
 * Adding a node or mark to `extensions.ts` means adding its name there too.
 */
const serverList = JSON.parse(readFileSync(
  fileURLToPath(new URL('../../../Api/Features/Pages/editor-schema.json', import.meta.url)),
  'utf8',
)) as { nodes: string[]; marks: string[] }

describe('the server’s list of editor elements', () => {
  for (const [label, options] of [
    ['collaborative editor', { collaborative: true }],
    ['read-only view', { editable: false }],
  ] as const) {
    it(`matches the ${label}’s schema`, () => {
      const schema = getSchema(getSharedExtensions(options))
      expect([...serverList.nodes].sort()).toEqual(Object.keys(schema.nodes).sort())
      expect([...serverList.marks].sort()).toEqual(Object.keys(schema.marks).sort())
    })
  }
})
