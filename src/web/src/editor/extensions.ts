import StarterKit from '@tiptap/starter-kit'
import { ReactNodeViewRenderer } from '@tiptap/react'
import CodeBlockLowlight from '@tiptap/extension-code-block-lowlight'
import { TableKit } from '@tiptap/extension-table'
import TaskList from '@tiptap/extension-task-list'
import TaskItem from '@tiptap/extension-task-item'
import Image from '@tiptap/extension-image'
import type { AnyExtension } from '@tiptap/core'
import { lowlight } from './lowlight'
import { CodeBlockView } from './CodeBlockView'

type SharedExtensionOptions = {
  /** Collaborative editors let Yjs own undo/redo history instead of StarterKit's. */
  collaborative?: boolean
}

const CodeBlock = CodeBlockLowlight.extend({
  addNodeView() {
    return ReactNodeViewRenderer(CodeBlockView)
  },
}).configure({ lowlight })

/**
 * Single source of truth for the TipTap/ProseMirror schema (node/mark types),
 * shared by the plain Editor, the Yjs-backed CollaborativeEditor, and anything
 * that renders stored content read-only. Yjs requires every collaborator to
 * agree on one exact schema, so this list must never diverge between editors
 * — new node/mark extensions get added here, not inline in either component.
 */
export function getSharedExtensions({ collaborative = false }: SharedExtensionOptions = {}): AnyExtension[] {
  return [
    // The plain CodeBlock is disabled in favour of the syntax-highlighted one
    // below — both use the same "codeBlock" node type name and `language`
    // attr, so stored content and the export renderer are unaffected.
    StarterKit.configure({ codeBlock: false, ...(collaborative ? { undoRedo: false } : {}) }),
    CodeBlock,
    TableKit.configure({ table: { resizable: true } }),
    TaskList,
    TaskItem.configure({ nested: true }),
    Image,
  ]
}
