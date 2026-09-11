import type { ComponentType } from 'react'
import type { Editor } from '@tiptap/react'
import { uploadAndInsertImage } from '../imageUpload'
import { PANEL_TYPES, PANEL_LABELS } from '../panelExtension'
import { DYNAMIC_KINDS, defaultParams } from '../dynamicBlockKinds'
import {
  BlockquoteIcon, BulletListIcon, CodeBlockIcon, DateIcon, DecisionIcon, DividerIcon, ErrorPanelIcon, ExpandIcon,
  HeadingIcon, ImageIcon, InfoPanelIcon, LayoutIcon, NotePanelIcon, OrderedListIcon, StatusIcon, SuccessPanelIcon,
  TableIcon, TaskListIcon, TocIcon, WarningPanelIcon,
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
  command: (editor: Editor, range: { from: number; to: number }) => void
}

/** Data the extension stashes on editor.storage so the Image item can reach it. */
export type SlashCommandStorage = {
  getUploadPageId?: () => Promise<string>
  onUploadError?: (message: string) => void
}

/** editor.storage is an untyped dictionary externally — these centralize the one cast it needs. */
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
 * The one catalogue of insertable things. The slash menu filters it by
 * query; the toolbar's Insert menu (Toolbar.tsx) lists its block and panel
 * groups. One list, so the two cannot drift — a new block added here
 * appears in both.
 */
export const SLASH_ITEMS: SlashItem[] = [
  {
    title: 'Heading 1',
    group: 'text',
    icon: HeadingIcon,
    description: 'Big section heading',
    command: (editor, range) => editor.chain().focus().deleteRange(range).toggleHeading({ level: 1 }).run(),
  },
  {
    title: 'Heading 2',
    group: 'text',
    icon: HeadingIcon,
    description: 'Medium section heading',
    command: (editor, range) => editor.chain().focus().deleteRange(range).toggleHeading({ level: 2 }).run(),
  },
  {
    title: 'Heading 3',
    group: 'text',
    icon: HeadingIcon,
    description: 'Small section heading',
    command: (editor, range) => editor.chain().focus().deleteRange(range).toggleHeading({ level: 3 }).run(),
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
    title: 'Blockquote',
    group: 'block',
    icon: BlockquoteIcon,
    description: 'Quoted text',
    keywords: ['quote'],
    command: (editor, range) => editor.chain().focus().deleteRange(range).toggleBlockquote().run(),
  },
  {
    title: 'Code block',
    group: 'block',
    icon: CodeBlockIcon,
    description: 'Syntax-highlighted code',
    keywords: ['code', 'snippet'],
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
    command: (editor, range) => editor.chain().focus().deleteRange(range).setExpand().run(),
  },
  {
    title: 'Layout',
    group: 'block',
    icon: LayoutIcon,
    description: 'Two columns — change the shape from the layout bar',
    keywords: ['columns', 'section', 'grid'],
    command: (editor, range) => editor.chain().focus().insertLayout('two-equal', range).run(),
  },
  {
    title: 'Decision',
    group: 'block',
    icon: DecisionIcon,
    description: 'Record something that was agreed',
    keywords: ['decided', 'agreed'],
    command: (editor, range) => editor.chain().focus().deleteRange(range).setDecision().run(),
  },
  {
    title: 'Status',
    group: 'block',
    icon: StatusIcon,
    description: 'Coloured lozenge, e.g. IN PROGRESS',
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
  // Dynamic blocks (dev-plan Phase 7 Wave D): generated from the kind
  // catalogue, so a kind added there appears here and in the + menu.
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
