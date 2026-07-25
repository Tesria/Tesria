import StarterKit from '@tiptap/starter-kit'
import { ReactNodeViewRenderer } from '@tiptap/react'
import CodeBlockLowlight from '@tiptap/extension-code-block-lowlight'
import { TableKit } from '@tiptap/extension-table'
import TaskList from '@tiptap/extension-task-list'
import TaskItem from '@tiptap/extension-task-item'
import Image from '@tiptap/extension-image'
import Highlight from '@tiptap/extension-highlight'
import TextAlign from '@tiptap/extension-text-align'
import type { AnyExtension } from '@tiptap/core'
import { lowlight } from './lowlight'
import { CodeBlockView } from './CodeBlockView'
import { SlashCommand } from './slash/SlashCommand'

type SharedExtensionOptions = {
  /** Collaborative editors let Yjs own undo/redo history instead of StarterKit's. */
  collaborative?: boolean
  /**
   * False only for read-only rendering (PageView, HistoryPanel previews).
   * Controls whether links navigate on click: in editable mode a click
   * should place the cursor, not hijack navigation; the read-only view keeps
   * the default click-to-open behaviour so viewers can click through.
   */
  editable?: boolean
}

const CodeBlock = CodeBlockLowlight.extend({
  addAttributes() {
    return {
      ...this.parent?.(),
      // Per-block visual toggle (CodeBlockView's "#" button) — not read by the
      // export renderer, purely an editor display preference.
      lineNumbers: {
        default: false,
        parseHTML: (element: HTMLElement) => element.hasAttribute('data-line-numbers'),
        renderHTML: (attributes: { lineNumbers?: boolean }) =>
          attributes.lineNumbers ? { 'data-line-numbers': 'true' } : {},
      },
    }
  },
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
export function getSharedExtensions({ collaborative = false, editable = true }: SharedExtensionOptions = {}): AnyExtension[] {
  return [
    // The plain CodeBlock is disabled in favour of the syntax-highlighted one
    // below — both use the same "codeBlock" node type name and `language`
    // attr, so stored content and the export renderer are unaffected.
    StarterKit.configure({
      codeBlock: false,
      link: { openOnClick: !editable },
      ...(collaborative ? { undoRedo: false } : {}),
    }),
    CodeBlock,
    TableKit.configure({ table: { resizable: true } }),
    TaskList,
    TaskItem.configure({ nested: true }),
    Image,
    Highlight,
    TextAlign.configure({ types: ['heading', 'paragraph'] }),
    // Read-only rendering never needs "/" commands — skip mounting the
    // suggestion plugin entirely rather than just hiding its output.
    ...(editable ? [SlashCommand] : []),
  ]
}
