import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'
import { getSchema } from '@tiptap/core'
import { getSharedExtensions } from './extensions'

/**
 * The server keeps a list of the editor's element and mark names
 * (`src/Api/Features/Export/EditorSchema.cs`), to refuse a wiki pack page
 * the editor could not show (t6-015). The schema itself is declared only in
 * `extensions.ts`, so this is what keeps the copy honest: a type added here
 * and not there would make every pack that uses it fail to import.
 */

const serverFile = fileURLToPath(new URL('../../../Api/Features/Export/EditorSchema.cs', import.meta.url))

/** The quoted names in one of the file's sets, by the set's name. */
function serverList(name: 'NodeTypes' | 'MarkTypes'): string[] {
  const source = readFileSync(serverFile, 'utf8')
  const block = new RegExp(`${name}\\s*=\\s*new HashSet<string>\\([^)]*\\)\\s*\\{([^}]*)\\}`).exec(source)
  if (!block) throw new Error(`${name} not found in EditorSchema.cs`)
  return [...block[1].matchAll(/"([^"]+)"/g)].map((m) => m[1]).sort()
}

/** One of the file's string-to-string dictionaries, by its name. */
function serverMap(name: 'Content' | 'Groups'): Record<string, string> {
  const source = readFileSync(serverFile, 'utf8')
  const block = new RegExp(`${name}\\s*=\\s*new Dictionary<string, string>\\([^)]*\\)\\s*\\{([\\s\\S]*?)\\n\\s*\\};`).exec(source)
  if (!block) throw new Error(`${name} not found in EditorSchema.cs`)
  return Object.fromEntries([...block[1].matchAll(/\["([^"]+)"\]\s*=\s*"([^"]*)"/g)].map((m) => [m[1], m[2]]))
}

describe("the server's copy of the editor schema", () => {
  for (const collaborative of [false, true]) {
    const schema = getSchema(getSharedExtensions({ collaborative }))
    const mode = collaborative ? 'collaborative' : 'single-user'

    it(`names every element type of the ${mode} editor, and no others`, () => {
      expect(serverList('NodeTypes')).toEqual(Object.keys(schema.nodes).sort())
    })

    it(`names every mark type of the ${mode} editor, and no others`, () => {
      expect(serverList('MarkTypes')).toEqual(Object.keys(schema.marks).sort())
    })

    // Where each element may go (t5-R05): the server checks documents
    // against these, so they must be the editor's own.
    it(`copies every content rule of the ${mode} editor`, () => {
      expect(serverMap('Content')).toEqual(Object.fromEntries(Object.values(schema.nodes).map((n) => [n.name, n.spec.content ?? ''])))
    })

    it(`copies every group of the ${mode} editor`, () => {
      expect(serverMap('Groups')).toEqual(Object.fromEntries(Object.values(schema.nodes).map((n) => [n.name, n.spec.group ?? ''])))
    })
  }
})
