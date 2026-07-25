import type { ChangeEvent } from 'react'
import type { Editor as TiptapEditor } from '@tiptap/react'
import { uploadAndInsertImage } from './imageUpload'

type Props = {
  editor: TiptapEditor
  /** Resolves the page id image attachments should be uploaded against. Omit to hide the image button. */
  getUploadPageId?: () => Promise<string>
  onUploadError?: (message: string) => void
}

/** Formatting controls, shared by the single-user and collaborative editors. */
export function Toolbar({ editor, getUploadPageId, onUploadError }: Props) {
  async function onPickImage(e: ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0]
    e.target.value = ''
    if (!file || !getUploadPageId) return
    try {
      await uploadAndInsertImage(editor, file, getUploadPageId)
    } catch (err) {
      onUploadError?.(err instanceof Error ? err.message : 'Image upload failed.')
    }
  }

  const btn = (label: string, isActive: boolean, onClick: () => void, title: string) => (
    <button
      type="button"
      className={isActive ? 'toolbar__btn is-active' : 'toolbar__btn'}
      onMouseDown={(e) => e.preventDefault()} // keep the editor selection
      onClick={onClick}
      title={title}
    >
      {label}
    </button>
  )
  return (
    <div className="toolbar">
      {btn('B', editor.isActive('bold'), () => editor.chain().focus().toggleBold().run(), 'Bold')}
      {btn('I', editor.isActive('italic'), () => editor.chain().focus().toggleItalic().run(), 'Italic')}
      {btn('S', editor.isActive('strike'), () => editor.chain().focus().toggleStrike().run(), 'Strikethrough')}
      {btn('Code', editor.isActive('code'), () => editor.chain().focus().toggleCode().run(), 'Inline code')}
      <span className="toolbar__sep" />
      {btn('H1', editor.isActive('heading', { level: 1 }), () => editor.chain().focus().toggleHeading({ level: 1 }).run(), 'Heading 1')}
      {btn('H2', editor.isActive('heading', { level: 2 }), () => editor.chain().focus().toggleHeading({ level: 2 }).run(), 'Heading 2')}
      {btn('H3', editor.isActive('heading', { level: 3 }), () => editor.chain().focus().toggleHeading({ level: 3 }).run(), 'Heading 3')}
      <span className="toolbar__sep" />
      {btn('• List', editor.isActive('bulletList'), () => editor.chain().focus().toggleBulletList().run(), 'Bullet list')}
      {btn('1. List', editor.isActive('orderedList'), () => editor.chain().focus().toggleOrderedList().run(), 'Ordered list')}
      {btn('☑ List', editor.isActive('taskList'), () => editor.chain().focus().toggleTaskList().run(), 'Task list')}
      {btn('❝', editor.isActive('blockquote'), () => editor.chain().focus().toggleBlockquote().run(), 'Blockquote')}
      {btn('{ }', editor.isActive('codeBlock'), () => editor.chain().focus().toggleCodeBlock().run(), 'Code block')}
      {btn('Table', editor.isActive('table'), () =>
        editor.chain().focus().insertTable({ rows: 3, cols: 3, withHeaderRow: true }).run(), 'Insert table')}
      {getUploadPageId && (
        <label className="toolbar__btn upload-btn" title="Insert image">
          Image
          <input type="file" accept="image/*" hidden onChange={onPickImage} />
        </label>
      )}
    </div>
  )
}
