import { Node, mergeAttributes } from '@tiptap/core'
import { ReactNodeViewRenderer } from '@tiptap/react'
import type { Editor } from '@tiptap/react'
import { DynamicBlockView } from './DynamicBlockView'

/** Flat, string-valued: it travels as a query string, and the server decides what a value means. */
export type BlockParams = Record<string, string>

declare module '@tiptap/core' {
  interface Commands<ReturnType> {
    dynamicBlock: {
      insertDynamicBlock: (kind: string, params?: BlockParams) => ReturnType
    }
  }
}

/**
 * The host page a block belongs to, stashed on editor.storage the same way
 * the slash menu's upload callbacks are: node views are built by the shared
 * schema and cannot take React props. Editor/CollaborativeEditor set it from
 * their `getPageId` prop; where nobody does (history previews, template
 * previews) the block shows a quiet placeholder: history is not live.
 */
export type DynamicBlockStorage = { getPageId?: () => Promise<string> }

export function setDynamicBlockStorage(editor: Editor, storage: DynamicBlockStorage): void {
  (editor.storage as unknown as Record<string, unknown>).dynamicBlock = storage
}

export function getDynamicBlockStorage(editor: Editor): DynamicBlockStorage | undefined {
  return (editor.storage as unknown as Record<string, unknown>).dynamicBlock as DynamicBlockStorage | undefined
}

function parseParams(raw: string | null): BlockParams {
  if (!raw) return {}
  try {
    const parsed: unknown = JSON.parse(raw)
    if (!parsed || typeof parsed !== 'object') return {}
    return Object.fromEntries(
      Object.entries(parsed as Record<string, unknown>)
        .filter(([, v]) => typeof v === 'string' || typeof v === 'number' || typeof v === 'boolean')
        .map(([k, v]) => [k, String(v)]),
    )
  } catch {
    return {}
  }
}

/**
 * A block whose content is the answer to a query, computed when the page is
 * looked at: Confluence's Children display, Recently updated, Task report
 * and the rest are all *kinds* of this one node (architecture.md, "Dynamic
 * blocks"). The document holds the question (`kind` + `params`); the answer
 * is fetched by the node view for whoever is looking, and is never stored.
 */
export const DynamicBlock = Node.create({
  name: 'dynamicBlock',
  group: 'block',
  atom: true,
  selectable: true,

  addAttributes() {
    return {
      kind: {
        default: 'children',
        parseHTML: (element: HTMLElement) => element.getAttribute('data-kind') ?? 'children',
        renderHTML: (attributes: { kind?: string }) => ({ 'data-kind': attributes.kind ?? 'children' }),
      },
      params: {
        default: {} as BlockParams,
        parseHTML: (element: HTMLElement) => parseParams(element.getAttribute('data-params')),
        renderHTML: (attributes: { params?: BlockParams }) => ({ 'data-params': JSON.stringify(attributes.params ?? {}) }),
      },
    }
  },

  parseHTML() {
    return [{ tag: 'div[data-type="dynamic-block"]' }]
  },

  renderHTML({ HTMLAttributes }) {
    return ['div', mergeAttributes(HTMLAttributes, { 'data-type': 'dynamic-block', class: 'dynamic-block' })]
  },

  addNodeView() {
    return ReactNodeViewRenderer(DynamicBlockView)
  },

  addCommands() {
    return {
      insertDynamicBlock:
        (kind, params = {}) =>
        ({ commands }) =>
          commands.insertContent({ type: this.name, attrs: { kind, params } }),
    }
  },
})
