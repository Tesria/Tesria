import { useState, type ChangeEvent, type ReactNode } from 'react'
import type { Editor as TiptapEditor } from '@tiptap/react'
import { uploadAndInsertImage } from './imageUpload'
import { ToolbarButton } from './ToolbarButton'
import { ToolbarDropdown } from './ToolbarDropdown'
import { useEdgeAlign } from '../hooks/useEdgeAlign'
import {
  InlineCodeIcon, HighlightIcon, BulletListIcon, OrderedListIcon, TaskListIcon, BlockquoteIcon,
  CodeBlockIcon, TableIcon, ImageIcon, AlignLeftIcon, AlignCenterIcon, AlignRightIcon, LinkIcon,
} from './icons'

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
  const linkAlign = useEdgeAlign<HTMLFormElement>(linkPopoverOpen)

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

  const btn = (label: ReactNode, isActive: boolean, onClick: () => void, title: string) => (
    <ToolbarButton label={label} isActive={isActive} onClick={onClick} title={title} />
  )
  return (
    <div className="toolbar">
      {btn(<span className="tb-glyph tb-bold">B</span>, editor.isActive('bold'), () => editor.chain().focus().toggleBold().run(), 'Bold')}
      {btn(<span className="tb-glyph tb-italic">I</span>, editor.isActive('italic'), () => editor.chain().focus().toggleItalic().run(), 'Italic')}
      {btn(<span className="tb-glyph tb-underline">U</span>, editor.isActive('underline'), () => editor.chain().focus().toggleUnderline().run(), 'Underline')}
      {btn(<span className="tb-glyph tb-strike">S</span>, editor.isActive('strike'), () => editor.chain().focus().toggleStrike().run(), 'Strikethrough')}
      {btn(<InlineCodeIcon />, editor.isActive('code'), () => editor.chain().focus().toggleCode().run(), 'Inline code')}
      {btn(<HighlightIcon />, editor.isActive('highlight'), () => editor.chain().focus().toggleHighlight().run(), 'Highlight selected text')}
      <span className="toolbar__sep" />
      {/* Heading, list-type, and alignment groups each render twice: a flat
          row (desktop) and a collapsed dropdown (mobile, --bp-mobile). CSS
          picks one via display:none — see .toolbar-dropdown/--flat in
          index.css — so this isn't duplicated by mistake. */}
      <div className="toolbar__flat">
        {btn(<span className="tb-glyph">H1</span>, editor.isActive('heading', { level: 1 }), () => editor.chain().focus().toggleHeading({ level: 1 }).run(), 'Heading 1')}
        {btn(<span className="tb-glyph">H2</span>, editor.isActive('heading', { level: 2 }), () => editor.chain().focus().toggleHeading({ level: 2 }).run(), 'Heading 2')}
        {btn(<span className="tb-glyph">H3</span>, editor.isActive('heading', { level: 3 }), () => editor.chain().focus().toggleHeading({ level: 3 }).run(), 'Heading 3')}
      </div>
      <ToolbarDropdown
        title="Heading"
        options={[
          { key: 'h1', label: 'Heading 1', icon: <span className="tb-glyph">H1</span>, isActive: editor.isActive('heading', { level: 1 }), onSelect: () => editor.chain().focus().toggleHeading({ level: 1 }).run() },
          { key: 'h2', label: 'Heading 2', icon: <span className="tb-glyph">H2</span>, isActive: editor.isActive('heading', { level: 2 }), onSelect: () => editor.chain().focus().toggleHeading({ level: 2 }).run() },
          { key: 'h3', label: 'Heading 3', icon: <span className="tb-glyph">H3</span>, isActive: editor.isActive('heading', { level: 3 }), onSelect: () => editor.chain().focus().toggleHeading({ level: 3 }).run() },
        ]}
      />
      <span className="toolbar__sep" />
      <div className="toolbar__flat">
        {btn(<BulletListIcon />, editor.isActive('bulletList'), () => editor.chain().focus().toggleBulletList().run(), 'Bullet list')}
        {btn(<OrderedListIcon />, editor.isActive('orderedList'), () => editor.chain().focus().toggleOrderedList().run(), 'Ordered list')}
        {btn(<TaskListIcon />, editor.isActive('taskList'), () => editor.chain().focus().toggleTaskList().run(), 'Task list')}
      </div>
      <ToolbarDropdown
        title="List"
        options={[
          { key: 'bullet', label: 'Bullet list', icon: <BulletListIcon />, isActive: editor.isActive('bulletList'), onSelect: () => editor.chain().focus().toggleBulletList().run() },
          { key: 'ordered', label: 'Ordered list', icon: <OrderedListIcon />, isActive: editor.isActive('orderedList'), onSelect: () => editor.chain().focus().toggleOrderedList().run() },
          { key: 'task', label: 'Task list', icon: <TaskListIcon />, isActive: editor.isActive('taskList'), onSelect: () => editor.chain().focus().toggleTaskList().run() },
        ]}
      />
      {btn(<BlockquoteIcon />, editor.isActive('blockquote'), () => editor.chain().focus().toggleBlockquote().run(), 'Blockquote')}
      {btn(<CodeBlockIcon />, editor.isActive('codeBlock'), () => editor.chain().focus().toggleCodeBlock().run(), 'Code block')}
      {btn(<TableIcon />, editor.isActive('table'), () =>
        editor.chain().focus().insertTable({ rows: 3, cols: 3, withHeaderRow: true }).run(), 'Insert table')}
      {getUploadPageId && (
        <label className="toolbar__btn upload-btn" title="Insert image">
          <ImageIcon />
          <input type="file" accept="image/*" hidden onChange={onPickImage} />
        </label>
      )}
      <span className="toolbar__sep" />
      <div className="toolbar__flat">
        {btn(<AlignLeftIcon />, editor.isActive({ textAlign: 'left' }), () => editor.chain().focus().setTextAlign('left').run(), 'Align left')}
        {btn(<AlignCenterIcon />, editor.isActive({ textAlign: 'center' }), () => editor.chain().focus().setTextAlign('center').run(), 'Align center')}
        {btn(<AlignRightIcon />, editor.isActive({ textAlign: 'right' }), () => editor.chain().focus().setTextAlign('right').run(), 'Align right')}
      </div>
      <ToolbarDropdown
        title="Alignment"
        options={[
          { key: 'left', label: 'Align left', icon: <AlignLeftIcon />, isActive: editor.isActive({ textAlign: 'left' }), onSelect: () => editor.chain().focus().setTextAlign('left').run() },
          { key: 'center', label: 'Align center', icon: <AlignCenterIcon />, isActive: editor.isActive({ textAlign: 'center' }), onSelect: () => editor.chain().focus().setTextAlign('center').run() },
          { key: 'right', label: 'Align right', icon: <AlignRightIcon />, isActive: editor.isActive({ textAlign: 'right' }), onSelect: () => editor.chain().focus().setTextAlign('right').run() },
        ]}
      />
      <span className="toolbar__sep" />
      <div className="toolbar__link">
        {btn(<LinkIcon />, editor.isActive('link'), openLinkPopover, 'Insert link')}
        {linkPopoverOpen && (
          <form
            ref={linkAlign.ref}
            className="toolbar__link-popover"
            style={{ left: linkAlign.offsetLeft }}
            onSubmit={(e) => {
              e.preventDefault()
              e.stopPropagation()
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
