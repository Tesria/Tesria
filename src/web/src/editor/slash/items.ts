import type { ComponentType } from 'react'
import type { Editor } from '@tiptap/react'
import { uploadAndInsertImage } from '../imageUpload'
import { PANEL_TYPES, PANEL_LABELS } from '../panelExtension'
import { DYNAMIC_KINDS, defaultParams } from '../dynamicBlockKinds'
import { insertPageProperties } from '../excerptExtension'
import { triggerLinkDialog } from '../linkShortcut'
import {
  BlockquoteIcon, BulletListIcon, CodeBlockIcon, DateIcon, DecisionIcon, DividerIcon, ErrorPanelIcon, ExpandIcon,
  HeadingIcon, TextIcon, ImageIcon, InfoPanelIcon, LayoutIcon, NotePanelIcon, OrderedListIcon, StatusIcon, SuccessPanelIcon,
  TableIcon, TaskListIcon, TocIcon, WarningPanelIcon, ExcerptIcon, PropertiesIcon,
  EmbedIcon, SmartLinkIcon, PaperclipIcon, GalleryIcon, MermaidIcon, MathIcon, ChartIcon, LinkIcon,
} from '../icons'

/**
 * Where an item belongs. The slash menu lists everything; the toolbar's
 * Insert menu lists the `block`, `panel` and `dynamic` items, because text
 * styles and lists already have their own toolbar controls.
 */
export type SlashGroup = 'text' | 'list' | 'block' | 'panel' | 'dynamic'

export type SlashItem = {
  title: string
  description: string
  group: SlashGroup
  /** A component, not an element: this file is plain TypeScript. */
  icon: ComponentType
  keywords?: string[]
  /**
   * Wraps or converts the blocks it is used on rather than inserting a new
   * one. From the + menu, a selection is what it applies to, not something
   * to replace: choosing "Info panel" over selected text deleted the text
   * and wrapped an empty line until 2026-09-23.
   */
  wraps?: boolean
  command: (editor: Editor, range: { from: number; to: number }) => void
}

/** Data the extension stashes on editor.storage so the Image item can reach it. */
export type SlashCommandStorage = {
  getUploadPageId?: () => Promise<string>
  onUploadError?: (message: string) => void
}

/** editor.storage is an untyped dictionary externally: these centralize the one cast it needs. */
export function setSlashCommandStorage(editor: Editor, storage: SlashCommandStorage): void {
  (editor.storage as unknown as Record<string, unknown>).slashCommand = storage
}

function getSlashCommandStorage(editor: Editor): SlashCommandStorage | undefined {
  return (editor.storage as unknown as Record<string, unknown>).slashCommand as SlashCommandStorage | undefined
}

function imageCommand(editor: Editor, range: { from: number; to: number }): void {
  editor.chain().focus().deleteRange(range).run()
  const storage = getSlashCommandStorage(editor)
  const getUploadPageId = storage?.getUploadPageId
  if (!getUploadPageId) return

  const input = document.createElement('input')
  input.type = 'file'
  input.accept = 'image/*'
  input.onchange = () => {
    const file = input.files?.[0]
    if (!file) return
    uploadAndInsertImage(editor, file, getUploadPageId).catch((err: unknown) => {
      storage?.onUploadError?.(err instanceof Error ? err.message : 'Image upload failed.')
    })
  }
  input.click()
}

const PANEL_DESCRIPTIONS: Record<(typeof PANEL_TYPES)[number], string> = {
  info: 'Blue callout for background detail',
  note: 'Purple callout for an aside',
  success: 'Green callout for a tip',
  warning: 'Yellow callout for a caution',
  error: 'Red callout for a problem',
}

const PANEL_ICONS: Record<(typeof PANEL_TYPES)[number], ComponentType> = {
  info: InfoPanelIcon,
  note: NotePanelIcon,
  success: SuccessPanelIcon,
  warning: WarningPanelIcon,
  error: ErrorPanelIcon,
}

/**
 * The one catalog of insertable things. The slash menu filters it by
 * query; the toolbar's Insert menu (Toolbar.tsx) lists its block and panel
 * groups. One list, so the two cannot drift: a new block added here
 * appears in both.
 */
export const SLASH_ITEMS: SlashItem[] = [
  {
    title: 'Normal text',
    group: 'text',
    icon: TextIcon,
    description: 'Plain body text',
    keywords: ['text', 'paragraph', 'body', 'p'],
    wraps: true,
    command: (editor, range) => editor.chain().focus().deleteRange(range).setParagraph().run(),
  },
  {
    title: 'Heading 1',
    group: 'text',
    icon: HeadingIcon,
    description: 'Big section heading',
    keywords: ['h1', 'heading', 'title'],
    wraps: true,
    command: (editor, range) => editor.chain().focus().deleteRange(range).setHeading({ level: 1 }).run(),
  },
  {
    title: 'Heading 2',
    group: 'text',
    icon: HeadingIcon,
    description: 'Medium section heading',
    keywords: ['h2', 'heading', 'title'],
    wraps: true,
    command: (editor, range) => editor.chain().focus().deleteRange(range).setHeading({ level: 2 }).run(),
  },
  {
    title: 'Heading 3',
    group: 'text',
    icon: HeadingIcon,
    description: 'Small section heading',
    keywords: ['h3', 'heading', 'title'],
    wraps: true,
    command: (editor, range) => editor.chain().focus().deleteRange(range).setHeading({ level: 3 }).run(),
  },
  {
    title: 'Heading 4',
    group: 'text',
    icon: HeadingIcon,
    description: 'A heading within a small section',
    keywords: ['h4', 'heading', 'title'],
    wraps: true,
    command: (editor, range) => editor.chain().focus().deleteRange(range).setHeading({ level: 4 }).run(),
  },
  {
    title: 'Heading 5',
    group: 'text',
    icon: HeadingIcon,
    description: 'A minor heading',
    keywords: ['h5', 'heading', 'title'],
    wraps: true,
    command: (editor, range) => editor.chain().focus().deleteRange(range).setHeading({ level: 5 }).run(),
  },
  {
    title: 'Heading 6',
    group: 'text',
    icon: HeadingIcon,
    description: 'The smallest heading',
    keywords: ['h6', 'heading', 'title'],
    wraps: true,
    command: (editor, range) => editor.chain().focus().deleteRange(range).setHeading({ level: 6 }).run(),
  },
  {
    title: 'Bullet list',
    group: 'list',
    icon: BulletListIcon,
    description: 'Simple bullet list',
    keywords: ['ul', 'unordered'],
    command: (editor, range) => editor.chain().focus().deleteRange(range).toggleBulletList().run(),
  },
  {
    title: 'Ordered list',
    group: 'list',
    icon: OrderedListIcon,
    description: 'Numbered list',
    keywords: ['ol', 'numbered'],
    command: (editor, range) => editor.chain().focus().deleteRange(range).toggleOrderedList().run(),
  },
  {
    title: 'Task list',
    group: 'list',
    icon: TaskListIcon,
    description: 'Checkboxes to track tasks',
    keywords: ['todo', 'checkbox'],
    command: (editor, range) => editor.chain().focus().deleteRange(range).toggleTaskList().run(),
  },
  {
    title: 'Link',
    group: 'block',
    icon: LinkIcon,
    description: 'A link, with its address and the words that carry it',
    keywords: ['url', 'href', 'anchor'],
    // Not a document edit: the dialog does the inserting once it has both
    // fields. The range is the "/link" query when typed, and the SELECTION
    // when chosen from the + menu, and a selection is exactly what the
    // dialog should turn into the link's text, not something to delete.
    // Only a slash query goes; this once removed a selected paragraph.
    command: (editor, range) => {
      const typed = editor.state.doc.textBetween(range.from, range.to, ' ')
      if (typed.startsWith('/')) editor.chain().focus().deleteRange(range).run()
      else editor.chain().focus().run()
      triggerLinkDialog(editor)
    },
  },
  {
    title: 'Blockquote',
    group: 'block',
    icon: BlockquoteIcon,
    description: 'Quoted text',
    keywords: ['quote'],
    wraps: true,
    command: (editor, range) => editor.chain().focus().deleteRange(range).toggleBlockquote().run(),
  },
  {
    title: 'Code block',
    group: 'block',
    icon: CodeBlockIcon,
    description: 'Syntax-highlighted code',
    keywords: ['code', 'snippet'],
    wraps: true,
    command: (editor, range) => editor.chain().focus().deleteRange(range).toggleCodeBlock().run(),
  },
  {
    title: 'Table',
    group: 'block',
    icon: TableIcon,
    description: '3×3 table with a header row',
    command: (editor, range) =>
      editor.chain().focus().deleteRange(range).insertTable({ rows: 3, cols: 3, withHeaderRow: true }).run(),
  },
  {
    title: 'Image',
    group: 'block',
    icon: ImageIcon,
    description: 'Upload an image',
    keywords: ['picture', 'photo', 'upload'],
    command: imageCommand,
  },
  // One entry per panel type, generated so the slash menu can't drift from
  // the toolbar's list (both read PANEL_TYPES/PANEL_LABELS).
  ...PANEL_TYPES.map((type) => ({
    title: `${PANEL_LABELS[type]} panel`,
    description: PANEL_DESCRIPTIONS[type],
    group: 'panel' as const,
    icon: PANEL_ICONS[type],
    keywords: ['panel', 'callout', 'admonition', type],
    wraps: true,
    command: (editor: Editor, range: { from: number; to: number }) =>
      editor.chain().focus().deleteRange(range).setPanel(type).run(),
  })),
  {
    title: 'Divider',
    group: 'block',
    icon: DividerIcon,
    description: 'Horizontal rule',
    keywords: ['hr', 'rule', 'separator'],
    command: (editor, range) => editor.chain().focus().deleteRange(range).setHorizontalRule().run(),
  },
  // Structural blocks (dev-plan Phase 7, Wave A).
  {
    title: 'Table of contents',
    group: 'block',
    icon: TocIcon,
    description: 'Links to the headings on this page',
    keywords: ['toc', 'outline', 'contents'],
    command: (editor, range) => editor.chain().focus().deleteRange(range).insertTableOfContents().run(),
  },
  {
    title: 'Expand',
    group: 'block',
    icon: ExpandIcon,
    description: 'Collapsible section with a title',
    keywords: ['collapse', 'toggle', 'details', 'accordion'],
    wraps: true,
    command: (editor, range) => editor.chain().focus().deleteRange(range).setExpand().run(),
  },
  {
    title: 'Layout',
    group: 'block',
    icon: LayoutIcon,
    description: 'Two columns: change the shape from the layout bar',
    keywords: ['columns', 'section', 'grid'],
    command: (editor, range) => editor.chain().focus().insertLayout('two-equal', range).run(),
  },
  {
    title: 'Decision',
    group: 'block',
    icon: DecisionIcon,
    description: 'Record something that was agreed',
    keywords: ['decided', 'agreed'],
    wraps: true,
    command: (editor, range) => editor.chain().focus().deleteRange(range).setDecision().run(),
  },
  {
    title: 'Status',
    group: 'block',
    icon: StatusIcon,
    description: 'Colored lozenge, e.g. IN PROGRESS',
    keywords: ['lozenge', 'badge', 'tag', 'label'],
    command: (editor, range) => editor.chain().focus().insertStatus(range).run(),
  },
  {
    title: 'Date',
    group: 'block',
    icon: DateIcon,
    description: 'A calendar date, shown in each reader\'s locale',
    keywords: ['calendar', 'today', 'when'],
    command: (editor, range) => editor.chain().focus().insertDate(range).run(),
  },
  // The two static containers the Wave D include/report kinds read from.
  // Blocks, not dynamic kinds: they hold content rather than fetch it.
  {
    title: 'Excerpt',
    group: 'block',
    icon: ExcerptIcon,
    description: 'Mark the part of this page other pages can include',
    keywords: ['excerpt', 'summary', 'snippet'],
    wraps: true,
    command: (editor, range) => editor.chain().focus().deleteRange(range).setExcerpt().run(),
  },
  {
    title: 'Page properties',
    group: 'block',
    icon: PropertiesIcon,
    description: 'A key/value table a properties report can collect',
    keywords: ['properties', 'metadata', 'fields'],
    command: (editor, range) => {
      editor.chain().focus().deleteRange(range).run()
      insertPageProperties(editor)
    },
  },
  // Wave F technical content.
  {
    title: 'Diagram (Mermaid)',
    group: 'block',
    icon: MermaidIcon,
    description: 'A flowchart or sequence diagram from text',
    keywords: ['mermaid', 'diagram', 'flowchart', 'sequence', 'graph'],
    command: (editor, range) =>
      editor.chain().focus().deleteRange(range)
        .insertContent({
          type: 'codeBlock',
          attrs: { language: 'mermaid' },
          content: [{ type: 'text', text: 'flowchart LR\n  A[Start] --> B{Choice}\n  B -->|yes| C[Done]\n  B -->|no| A' }],
        })
        .run(),
  },
  {
    // "Math", the owner's American English (2026-09-23). "maths" stays a
    // keyword so a British reader typing it still finds the element.
    title: 'Math',
    group: 'block',
    icon: MathIcon,
    description: 'A LaTeX equation on its own line',
    keywords: ['maths', 'latex', 'katex', 'equation', 'formula'],
    command: (editor, range) => editor.chain().focus().deleteRange(range).insertMath(true, 'e = mc^2').run(),
  },
  {
    // The node always could be inline; nothing offered it until 2026-09-22,
    // though the Math item's description said it could.
    title: 'Inline math',
    group: 'block',
    icon: MathIcon,
    description: 'A LaTeX expression within a line of text',
    keywords: ['maths', 'latex', 'katex', 'inline', 'formula'],
    command: (editor, range) => editor.chain().focus().deleteRange(range).insertMath(false, 'x^2').run(),
  },
  {
    title: 'Chart',
    group: 'block',
    icon: ChartIcon,
    description: 'Chart the numbers in a table on this page',
    keywords: ['chart', 'graph', 'bar', 'pie', 'line'],
    command: (editor, range) => editor.chain().focus().deleteRange(range).insertChart().run(),
  },
  // Wave E media. The embed asks the server what it may frame; the
  // attachment block picks from what is already on the page.
  {
    title: 'Embed',
    group: 'block',
    icon: EmbedIcon,
    description: 'A video, design or board from an allowed site',
    keywords: ['video', 'youtube', 'vimeo', 'figma', 'iframe', 'embed'],
    command: (editor, range) => editor.chain().focus().deleteRange(range).insertEmbed().run(),
  },
  {
    title: 'Smart link',
    group: 'block',
    icon: SmartLinkIcon,
    description: 'A link that shows the page it points at',
    keywords: ['link', 'preview', 'card', 'unfurl'],
    command: (editor, range) => editor.chain().focus().deleteRange(range).insertSmartLink().run(),
  },
  {
    title: 'File or video',
    group: 'block',
    icon: PaperclipIcon,
    description: 'Play or show a file attached to this page',
    keywords: ['attachment', 'video', 'pdf', 'audio', 'file'],
    command: (editor, range) => editor.chain().focus().deleteRange(range).insertAttachmentBlock().run(),
  },
  {
    // The same element as File or video, already set to play as an
    // animation: people looking for "a GIF" look for this word.
    title: 'Animation',
    group: 'block',
    icon: PaperclipIcon,
    description: 'A short video that loops silently, like a GIF',
    keywords: ['gif', 'clip', 'loop', 'animated', 'video', 'recording'],
    command: (editor, range) => editor.chain().focus().deleteRange(range).insertAnimation().run(),
  },
  {
    title: 'Gallery',
    group: 'block',
    icon: GalleryIcon,
    description: 'Tile the images you put inside it',
    keywords: ['images', 'grid', 'photos', 'gallery'],
    command: (editor, range) => editor.chain().focus().deleteRange(range).insertGallery().run(),
  },
  // Dynamic blocks (dev-plan Phase 7 Wave D): generated from the kind
  // catalog, so a kind added there appears here and in the + menu.
  ...DYNAMIC_KINDS.map((kind) => ({
    title: kind.title,
    group: 'dynamic' as const,
    icon: kind.icon,
    description: kind.description,
    keywords: kind.keywords,
    command: (editor: Editor, range: { from: number; to: number }) =>
      editor.chain().focus().deleteRange(range).insertDynamicBlock(kind.kind, defaultParams(kind)).run(),
  })),
]

export function filterSlashItems(query: string): SlashItem[] {
  if (!query) return SLASH_ITEMS
  const q = query.toLowerCase()
  return SLASH_ITEMS.filter(
    (item) => item.title.toLowerCase().includes(q) || item.keywords?.some((k) => k.includes(q)),
  )
}
