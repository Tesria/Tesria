import type { ComponentType } from 'react'
import type { BlockParams } from './dynamicBlock'
import { ChildrenBlockIcon } from './icons'

/**
 * One param of a kind, as the generic editing form needs it. The server is
 * authoritative about defaults and validation; these mirror it so the form
 * shows sensible starting values and the right control.
 */
export type ParamField =
  | { key: string; label: string; type: 'select'; options: { value: string; label: string }[]; default: string }
  | { key: string; label: string; type: 'number'; min: number; max: number; default: number }
  | { key: string; label: string; type: 'text'; placeholder?: string; default?: string }

export type DynamicKind = {
  /** Kebab-case, as the server and the document know it. */
  kind: string
  /** Confluence's name for it, so a Confluence user finds what they expect. */
  title: string
  description: string
  icon: ComponentType
  keywords?: string[]
  params: ParamField[]
}

/**
 * The client catalogue of kinds (architecture.md, "Dynamic blocks",
 * decision 8). The slash menu and the + menu list it; `DynamicBlockMenu`
 * renders any kind's form from its `params`. Adding a kind here is the whole
 * client-side cost of adding one.
 */
export const DYNAMIC_KINDS: DynamicKind[] = [
  {
    kind: 'children',
    title: 'Children display',
    description: 'The pages beneath this one, kept up to date',
    icon: ChildrenBlockIcon,
    keywords: ['child', 'subpages', 'tree'],
    params: [
      { key: 'depth', label: 'Depth', type: 'number', min: 1, max: 3, default: 1 },
      {
        key: 'sort', label: 'Sort by', type: 'select', default: 'position',
        options: [
          { value: 'position', label: 'Tree order' },
          { value: 'title', label: 'Title' },
          { value: 'updated', label: 'Recently updated' },
        ],
      },
    ],
  },
]

export function kindOf(kind: string): DynamicKind | undefined {
  return DYNAMIC_KINDS.find((k) => k.kind === kind)
}

/** The params a fresh block starts with: every field at its default. */
export function defaultParams(kind: DynamicKind): BlockParams {
  return Object.fromEntries(
    kind.params.filter((f) => f.default !== undefined).map((f) => [f.key, String(f.default)]),
  )
}
