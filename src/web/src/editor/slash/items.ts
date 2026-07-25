import type { Editor } from '@tiptap/react'
import { uploadAndInsertImage } from '../imageUpload'

export type SlashItem = {
  title: string
  description: string
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

const ITEMS: SlashItem[] = [
  {
    title: 'Heading 1',
    description: 'Big section heading',
    command: (editor, range) => editor.chain().focus().deleteRange(range).toggleHeading({ level: 1 }).run(),
  },
  {
    title: 'Heading 2',
    description: 'Medium section heading',
    command: (editor, range) => editor.chain().focus().deleteRange(range).toggleHeading({ level: 2 }).run(),
  },
  {
    title: 'Heading 3',
    description: 'Small section heading',
    command: (editor, range) => editor.chain().focus().deleteRange(range).toggleHeading({ level: 3 }).run(),
  },
  {
    title: 'Bullet list',
    description: 'Simple bullet list',
    keywords: ['ul', 'unordered'],
    command: (editor, range) => editor.chain().focus().deleteRange(range).toggleBulletList().run(),
  },
  {
    title: 'Ordered list',
    description: 'Numbered list',
    keywords: ['ol', 'numbered'],
    command: (editor, range) => editor.chain().focus().deleteRange(range).toggleOrderedList().run(),
  },
  {
    title: 'Task list',
    description: 'Checkboxes to track tasks',
    keywords: ['todo', 'checkbox'],
    command: (editor, range) => editor.chain().focus().deleteRange(range).toggleTaskList().run(),
  },
  {
    title: 'Blockquote',
    description: 'Quoted text',
    keywords: ['quote'],
    command: (editor, range) => editor.chain().focus().deleteRange(range).toggleBlockquote().run(),
  },
  {
    title: 'Code block',
    description: 'Syntax-highlighted code',
    keywords: ['code', 'snippet'],
    command: (editor, range) => editor.chain().focus().deleteRange(range).toggleCodeBlock().run(),
  },
  {
    title: 'Table',
    description: '3×3 table with a header row',
    command: (editor, range) =>
      editor.chain().focus().deleteRange(range).insertTable({ rows: 3, cols: 3, withHeaderRow: true }).run(),
  },
  {
    title: 'Image',
    description: 'Upload an image',
    keywords: ['picture', 'photo', 'upload'],
    command: imageCommand,
  },
  {
    title: 'Divider',
    description: 'Horizontal rule',
    keywords: ['hr', 'rule', 'separator'],
    command: (editor, range) => editor.chain().focus().deleteRange(range).setHorizontalRule().run(),
  },
]

export function filterSlashItems(query: string): SlashItem[] {
  if (!query) return ITEMS
  const q = query.toLowerCase()
  return ITEMS.filter(
    (item) => item.title.toLowerCase().includes(q) || item.keywords?.some((k) => k.includes(q)),
  )
}
