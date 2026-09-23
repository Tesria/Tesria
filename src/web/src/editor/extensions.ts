import StarterKit from '@tiptap/starter-kit'
import Document from '@tiptap/extension-document'
import { ReactNodeViewRenderer } from '@tiptap/react'
import CodeBlockLowlight from '@tiptap/extension-code-block-lowlight'
import {
  TableKit, Table as BaseTable, TableCell as BaseTableCell, TableHeader as BaseTableHeader,
} from '@tiptap/extension-table'
import TaskList from '@tiptap/extension-task-list'
import TaskItem from '@tiptap/extension-task-item'
import Highlight from '@tiptap/extension-highlight'
import TextAlign from '@tiptap/extension-text-align'
import Subscript from '@tiptap/extension-subscript'
import Superscript from '@tiptap/extension-superscript'
import type { AnyExtension } from '@tiptap/core'
import { lowlight } from './lowlight'
import { CodeBlockView } from './CodeBlockView'
import { SlashCommand } from './slash/SlashCommand'
import { Image } from './imageExtension'
import { CommentMark } from './commentMark'
import { ExternalDelete, ExternalEditCommands, ExternalInsert } from './externalEditMarks'
import { Panel } from './panelExtension'
import { HeadingAnchors } from './headingAnchors'
import { TableOfContents } from './tocExtension'
import { Expand } from './expandExtension'
import { Status } from './statusExtension'
import { DateChip } from './dateExtension'
import { Decision } from './decisionExtension'
import { LayoutColumn, LayoutSection } from './layoutExtension'
import { TextColorMark } from './textColorMark'
import { TextIndent } from './textFormatting'
import { LinkShortcut } from './linkShortcut'
import { Mention } from './mentionExtension'
import { MentionSuggestion } from './suggest/MentionSuggestion'
import { EmojiSuggestion } from './suggest/EmojiSuggestion'
import { TaskAssignee } from './taskAssignee'
import { DynamicBlock } from './dynamicBlock'
import { Excerpt, PageProperties } from './excerptExtension'
import { Embed, SmartLink } from './embedExtension'
import { AttachmentBlock, Gallery } from './mediaExtension'
import { Math as MathNode } from './mathExtension'
import { Chart } from './chartExtension'

type SharedExtensionOptions = {
  /** Collaborative editors let Yjs own undo/redo history instead of StarterKit's. */
  collaborative?: boolean
  /**
   * False only for read-only rendering (PageView, HistoryPanel previews).
   * Controls whether links navigate on click: in editable mode a click
   * should place the cursor, not hijack navigation; the read-only view keeps
   * the default click-to-open behavior so viewers can click through.
   */
  editable?: boolean
}

const CodeBlock = CodeBlockLowlight.extend({
  addAttributes() {
    return {
      ...this.parent?.(),
      // Per-block visual toggle (CodeBlockView's "#" button), not read by the
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
// node's options: it can't take a custom-extended node in its place. So,
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
      // This can't just render a plain `style="width: ..."`: prosemirror-
      // tables' own TableView NodeView (installed whenever `resizable` is on,
      // which is always for this node) recalculates and overwrites
      // `table.style.width` itself via updateColumns() *after* HTMLAttributes
      // are applied, in both read-only and editable rendering. A regular
      // inline style is silently clobbered. Instead, this carries the value
      // through a custom property (which updateColumns never touches) and a
      // stylesheet rule with !important applies it, one of the few cases
      // where a stylesheet rule can legitimately override an inline style.
      // See index.css's `--table-target-width` rule.
      width: {
        default: null,
        parseHTML: (element: HTMLElement) => {
          const w = element.style.getPropertyValue('--table-target-width')
          return w ? parseInt(w, 10) || null : null
        },
        // Receives the whole node's attrs, not just its own: read `layout`
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
 * Cell background color (TableCellMenu's "Background color" palette), stored
 * as a hex string; null = today's unchanged default background.
 *
 * Applied to both `tableCell` and `tableHeader` via this shared mixin, and,
 * unlike the Table node's `width` above, a plain inline `style` is safe here.
 * prosemirror-tables' TableView only rewrites the *table*'s own width and its
 * colgroup, and never touches cell style attributes, so there's nothing to
 * clobber it and no need for the custom-property indirection `width` needs.
 */
/**
 * An action item can name who it is for (dev-plan Phase 7 Wave C). Stored as
 * an id plus a snapshot of the name, for the same reason a mention is: an
 * exported file and an old page version have no directory to look the name up
 * in. Wave D's Task report queries the id.
 */
const TaskItemWithAssignee = TaskItem.extend({
  addAttributes() {
    return {
      ...this.parent?.(),
      assigneeId: {
        default: null as string | null,
        parseHTML: (element: HTMLElement) => element.getAttribute('data-assignee-id'),
        renderHTML: (attributes: { assigneeId?: string | null }) =>
          attributes.assigneeId ? { 'data-assignee-id': attributes.assigneeId } : {},
      },
      assigneeName: {
        default: null as string | null,
        parseHTML: (element: HTMLElement) => element.getAttribute('data-assignee-name'),
        renderHTML: (attributes: { assigneeName?: string | null }) =>
          attributes.assigneeName ? { 'data-assignee-name': attributes.assigneeName } : {},
      },
    }
  },
}).configure({ nested: true })

const cellBackgroundAttribute = {
  backgroundColor: {
    default: null as string | null,
    parseHTML: (element: HTMLElement) =>
      element.getAttribute('data-background-color') || element.style.backgroundColor || null,
    renderHTML: (attributes: { backgroundColor?: string | null }) =>
      attributes.backgroundColor
        ? {
            'data-background-color': attributes.backgroundColor,
            style: `background-color: ${attributes.backgroundColor}`,
          }
        : {},
  },
}

// Same idiom as the extended Table above: TableKit can't take a customized
// node in place of its built-in one, so its `tableCell`/`tableHeader` are
// disabled and these extended equivalents registered alongside. Identical node
// names, so stored documents and the export renderer are unaffected.
const TableCell = BaseTableCell.extend({
  addAttributes() {
    return { ...this.parent?.(), ...cellBackgroundAttribute }
  },
})

const TableHeader = BaseTableHeader.extend({
  addAttributes() {
    return { ...this.parent?.(), ...cellBackgroundAttribute }
  },
})

/**
 * Single source of truth for the TipTap/ProseMirror schema (node/mark types),
 * shared by the plain Editor, the Yjs-backed CollaborativeEditor, and anything
 * that renders stored content read-only. Yjs requires every collaborator to
 * agree on one exact schema, so this list must never diverge between editors:
 *new node/mark extensions get added here, not inline in either component.
 */
export function getSharedExtensions({ collaborative = false, editable = true }: SharedExtensionOptions = {}): AnyExtension[] {
  return [
    // The plain CodeBlock is disabled in favor of the syntax-highlighted one
    // below: both use the same "codeBlock" node type name and `language`
    // attr, so stored content and the export renderer are unaffected.
    StarterKit.configure({
      codeBlock: false,
      document: false,
      link: { openOnClick: !editable },
      ...(collaborative ? { undoRedo: false } : {}),
    }),
    // Layout sections live only at the top level (layoutExtension.ts): they
    // are not `block`s, so this is the one place the schema admits them.
    Document.extend({ content: '(block | layoutSection)+' }),
    CodeBlock,
    TableKit.configure({ table: false, tableCell: false, tableHeader: false }),
    Table,
    TableCell,
    TableHeader,
    TaskList,
    TaskItemWithAssignee,
    Image,
    // multicolor: the highlight button is a color palette (Toolbar.tsx), so
    // the mark carries a `color` attr. Highlights stored before this stay
    // valid: no color attr renders as the plain default <mark>.
    Highlight.configure({ multicolor: true }),
    TextAlign.configure({ types: ['heading', 'paragraph'] }),
    CommentMark,
    // dev-plan 8.6: how a write from the API or MCP shows up in a live draft.
    // Part of the *shared* schema, not the editor's alone, because the Yjs
    // document carries these marks and every peer has to be able to read one.
    ExternalInsert,
    ExternalDelete,
    ExternalEditCommands,
    Panel,
    // Phase 7 Wave A structural blocks. Heading ids are decorations, not
    // attributes: see headingAnchors.ts.
    HeadingAnchors,
    TableOfContents,
    Expand,
    Status,
    DateChip,
    Decision,
    LayoutSection,
    LayoutColumn,
    // Wave B formatting. TextIndent adds a `textIndent` attribute to the same
    // block types TextAlign is configured for, so it must stay in step with
    // the line above it.
    Subscript,
    Superscript,
    TextColorMark,
    TextIndent,
    // Wave C. The node is part of the schema (read-only rendering has to draw
    // a mention too); the `@` and `:` suggestions are editor-only, below.
    Mention,
    TaskAssignee,
    // Wave D: one node for every query-backed block; kinds live on the server.
    DynamicBlock,
    // The two static containers the include/report kinds read from. Plain
    // containers, so export renders their contents as ordinary content.
    Excerpt,
    PageProperties,
    // Wave E: third-party frames (allowlisted server-side), link previews,
    // and attachment-backed media.
    Embed,
    SmartLink,
    AttachmentBlock,
    Gallery,
    // Wave F. Mermaid is not a node: it is a code-block language whose view
    // draws rather than highlights (CodeBlockView), so its source stays an
    // ordinary fenced block in every export.
    MathNode,
    Chart,
    // Read-only rendering never needs "/" commands or a link shortcut: skip
    // mounting the plugins entirely rather than just hiding their output.
    ...(editable ? [SlashCommand, LinkShortcut, MentionSuggestion, EmojiSuggestion] : []),
  ]
}
