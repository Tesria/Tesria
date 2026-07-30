import StarterKit from '@tiptap/starter-kit'
import { ReactNodeViewRenderer } from '@tiptap/react'
import CodeBlockLowlight from '@tiptap/extension-code-block-lowlight'
import { TableKit, Table as BaseTable } from '@tiptap/extension-table'
import TaskList from '@tiptap/extension-task-list'
import TaskItem from '@tiptap/extension-task-item'
import Highlight from '@tiptap/extension-highlight'
import TextAlign from '@tiptap/extension-text-align'
import type { AnyExtension } from '@tiptap/core'
import { lowlight } from './lowlight'
import { CodeBlockView } from './CodeBlockView'
import { SlashCommand } from './slash/SlashCommand'
import { Image } from './imageExtension'
import { CommentMark } from './commentMark'

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

// TableKit's `configure({ table: {...} })` only tweaks its built-in Table
// node's options — it can't take a custom-extended node in its place. So,
// same idiom as CodeBlock above: disable TableKit's own `table` and add this
// extended one alongside it (same "table" node name, so stored content and
// the export renderer are unaffected by which extension instance made it).
const Table = BaseTable.extend({
  addAttributes() {
    return {
      ...this.parent?.(),
      // Manually dragged width (TableWidthControls' edge handle), in px.
      // null = unset, i.e. today's unchanged default-width behavior.
      //
      // This can't just render a plain `style="width: ..."` — prosemirror-
      // tables' own TableView NodeView (installed whenever `resizable` is on,
      // which is always for this node) recalculates and overwrites
      // `table.style.width` itself via updateColumns() *after* HTMLAttributes
      // are applied, in both read-only and editable rendering. A regular
      // inline style is silently clobbered. Instead, this carries the value
      // through a custom property (which updateColumns never touches) and a
      // stylesheet rule with !important applies it — one of the few cases
      // where a stylesheet rule can legitimately override an inline style.
      // See index.css's `--table-target-width` rule.
      width: {
        default: null,
        parseHTML: (element: HTMLElement) => {
          const w = element.style.getPropertyValue('--table-target-width')
          return w ? parseInt(w, 10) || null : null
        },
        // Receives the whole node's attrs, not just its own — read `layout`
        // too so only one attribute ever emits `style` (avoids relying on
        // merge order between two attributes both wanting that key).
        renderHTML: (attributes: { width?: number | null; layout?: string }) => {
          if (attributes.layout === 'full-width') return { style: '--table-target-width: 100%' }
          if (attributes.width) return { style: `--table-target-width: ${attributes.width}px` }
          return {}
        },
      },
      // Independent of `width`: "always fill the container," which is a
      // relative/live intent (matches whatever full-width means right now)
      // rather than a captured pixel size. Set back to 'default' whenever
      // the user manually drags the table's edge.
      layout: {
        default: 'default',
        parseHTML: (element: HTMLElement) => element.getAttribute('data-layout') ?? 'default',
        renderHTML: (attributes: { layout?: string }) =>
          attributes.layout === 'full-width' ? { 'data-layout': 'full-width' } : {},
      },
    }
  },
}).configure({ resizable: true })

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
    TableKit.configure({ table: false }),
    Table,
    TaskList,
    TaskItem.configure({ nested: true }),
    Image,
    Highlight,
    TextAlign.configure({ types: ['heading', 'paragraph'] }),
    CommentMark,
    // Read-only rendering never needs "/" commands — skip mounting the
    // suggestion plugin entirely rather than just hiding its output.
    ...(editable ? [SlashCommand] : []),
  ]
}
