import { useState, type ChangeEvent } from 'react'
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
  const [linkPopoverOpen, setLinkPopoverOpen] = useState(false)
  const [linkUrl, setLinkUrl] = useState('')

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

  function openLinkPopover() {
    setLinkUrl((editor.getAttributes('link').href as string | undefined) ?? '')
    setLinkPopoverOpen(true)
  }

  function applyLink() {
    if (linkUrl.trim()) editor.chain().focus().extendMarkRange('link').setLink({ href: linkUrl.trim() }).run()
    setLinkPopoverOpen(false)
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
      {btn('U', editor.isActive('underline'), () => editor.chain().focus().toggleUnderline().run(), 'Underline')}
      {btn('S', editor.isActive('strike'), () => editor.chain().focus().toggleStrike().run(), 'Strikethrough')}
      {btn('Code', editor.isActive('code'), () => editor.chain().focus().toggleCode().run(), 'Inline code')}
      {btn('Mark', editor.isActive('highlight'), () => editor.chain().focus().toggleHighlight().run(), 'Highlight')}
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
      <span className="toolbar__sep" />
      {btn('←', editor.isActive({ textAlign: 'left' }), () => editor.chain().focus().setTextAlign('left').run(), 'Align left')}
      {btn('↔', editor.isActive({ textAlign: 'center' }), () => editor.chain().focus().setTextAlign('center').run(), 'Align center')}
      {btn('→', editor.isActive({ textAlign: 'right' }), () => editor.chain().focus().setTextAlign('right').run(), 'Align right')}
      <span className="toolbar__sep" />
      <div className="toolbar__link">
        {btn('Link', editor.isActive('link'), openLinkPopover, 'Insert link')}
        {linkPopoverOpen && (
          <form
            className="toolbar__link-popover"
            onSubmit={(e) => {
              e.preventDefault()
              applyLink()
            }}
          >
            <input
              autoFocus
              value={linkUrl}
              onChange={(e) => setLinkUrl(e.target.value)}
              placeholder="https://…"
              onKeyDown={(e) => {
                if (e.key === 'Escape') setLinkPopoverOpen(false)
              }}
            />
            <button type="submit" className="link-btn">Apply</button>
          </form>
        )}
      </div>
    </div>
  )
}
