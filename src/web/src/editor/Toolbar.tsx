import { useState, type ReactNode } from 'react'
import type { Editor as TiptapEditor } from '@tiptap/react'
import { ToolbarButton } from './ToolbarButton'
import { HeadingLinkList } from './HeadingLinkList'
import { ToolbarDropdown } from './ToolbarDropdown'
import { ToolbarPopover } from './ToolbarPopover'
import { InsertMenu, type OverflowAction } from './InsertMenu'
import { useToolbarOverflow } from './useToolbarOverflow'
import { useEdgeAlign } from '../hooks/useEdgeAlign'
import { ColorPalette } from './ColorPalette'
import { HIGHLIGHT_TIERS } from './palette'
import {
  InlineCodeIcon, HighlightIcon, BulletListIcon, OrderedListIcon, TaskListIcon,
  AlignLeftIcon, AlignCenterIcon, AlignRightIcon, LinkIcon,
} from './icons'

type Props = {
  editor: TiptapEditor
  /** Resolves the page id image attachments should be uploaded against. Omit to hide the image item. */
  getUploadPageId?: () => Promise<string>
  onUploadError?: (message: string) => void
}

/** A formatting control that can leave the row for the Insert menu's "More" section when space runs out. */
type Collapsible = { key: string; icon: ReactNode; label: string; isActive: boolean; run: () => void }

/**
 * The editing toolbar: one row, edge to edge, never wrapping — the shape of
 * Confluence's. Text style and alignment are dropdowns (not runs of buttons),
 * block elements live behind "+", and whatever formatting buttons
 * still do not fit at a given width are moved into that menu by measurement
 * (`useToolbarOverflow`) rather than pushed onto a second line.
 *
 * Shared by the single-user and collaborative editors. The upload plumbing
 * is passed through to the slash catalogue's Image item, which is what the
 * Insert menu calls.
 */
export function Toolbar({ editor, getUploadPageId, onUploadError }: Props) {
  const [linkPopoverOpen, setLinkPopoverOpen] = useState(false)
  const [linkUrl, setLinkUrl] = useState('')
  const linkAlign = useEdgeAlign<HTMLFormElement>(linkPopoverOpen)

  // The slash catalogue's Image item reads these from editor.storage; the
  // Editor components set them, so nothing to do here beyond noting that
  // getUploadPageId/onUploadError are consumed there.
  void getUploadPageId
  void onUploadError

  function openLinkPopover() {
    setLinkUrl((editor.getAttributes('link').href as string | undefined) ?? '')
    setLinkPopoverOpen(true)
  }

  function applyLink() {
    if (linkUrl.trim()) editor.chain().focus().extendMarkRange('link').setLink({ href: linkUrl.trim() }).run()
    setLinkPopoverOpen(false)
  }

  const chain = () => editor.chain().focus()

  // In the order they leave the row when space runs out: lists and the
  // link first, then the rarer marks, bold last.
  const collapsible: Collapsible[] = [
    { key: 'task', icon: <TaskListIcon />, label: 'Task list', isActive: editor.isActive('taskList'), run: () => chain().toggleTaskList().run() },
    { key: 'ordered', icon: <OrderedListIcon />, label: 'Ordered list', isActive: editor.isActive('orderedList'), run: () => chain().toggleOrderedList().run() },
    { key: 'bullet', icon: <BulletListIcon />, label: 'Bullet list', isActive: editor.isActive('bulletList'), run: () => chain().toggleBulletList().run() },
    { key: 'code', icon: <InlineCodeIcon />, label: 'Inline code', isActive: editor.isActive('code'), run: () => chain().toggleCode().run() },
    { key: 'strike', icon: <span className="tb-glyph tb-strike">S</span>, label: 'Strikethrough', isActive: editor.isActive('strike'), run: () => chain().toggleStrike().run() },
    { key: 'underline', icon: <span className="tb-glyph tb-underline">U</span>, label: 'Underline', isActive: editor.isActive('underline'), run: () => chain().toggleUnderline().run() },
    { key: 'italic', icon: <span className="tb-glyph tb-italic">I</span>, label: 'Italic', isActive: editor.isActive('italic'), run: () => chain().toggleItalic().run() },
    { key: 'bold', icon: <span className="tb-glyph tb-bold">B</span>, label: 'Bold', isActive: editor.isActive('bold'), run: () => chain().toggleBold().run() },
  ]
  // The link button is never collapsed: its popover anchors to the button,
  // so a hidden button would mean a popover that cannot appear.
  // Measurement order is "first to go, first in the list"; display order is the reverse.
  const { containerRef, overflowed } = useToolbarOverflow(collapsible.map((c) => c.key))
  const byKey = new Map(collapsible.map((c) => [c.key, c]))
  const show = (key: string) => !overflowed.has(key)
  const overflowActions: OverflowAction[] = collapsible.filter((c) => overflowed.has(c.key))

  const item = (key: string) => {
    const c = byKey.get(key)!
    return (
      <span key={key} data-tb-item={key} className={show(key) ? 'tb-item' : 'tb-item tb-item--hidden'}>
        <ToolbarButton label={c.icon} isActive={c.isActive} onClick={c.run} title={c.label} />
      </span>
    )
  }

  const linkControl = (
    <div className="toolbar__link" data-tb-fixed="link">
      <ToolbarButton label={<LinkIcon />} isActive={editor.isActive('link')} onClick={openLinkPopover} title="Link" />
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
          <HeadingLinkList editor={editor} onPick={setLinkUrl} />
        </form>
      )}
    </div>
  )

  const sep = (key: string, after: string[]) =>
    after.some(show) ? <span key={key} className="toolbar__sep" /> : null

  const headingLevel = [1, 2, 3].find((l) => editor.isActive('heading', { level: l }))

  return (
    <div className="toolbar" ref={containerRef}>
      <span data-tb-fixed="style">
        <ToolbarDropdown
          title="Text style"
          showLabel
          options={[
            // No icons: the trigger reads "Normal text ⌄", as Confluence's does.
            { key: 'p', label: 'Normal text', icon: null, isActive: !headingLevel, onSelect: () => chain().setParagraph().run() },
            { key: 'h1', label: 'Heading 1', icon: null, isActive: headingLevel === 1, onSelect: () => chain().toggleHeading({ level: 1 }).run() },
            { key: 'h2', label: 'Heading 2', icon: null, isActive: headingLevel === 2, onSelect: () => chain().toggleHeading({ level: 2 }).run() },
            { key: 'h3', label: 'Heading 3', icon: null, isActive: headingLevel === 3, onSelect: () => chain().toggleHeading({ level: 3 }).run() },
          ]}
        />
      </span>
      <span className="toolbar__sep" />
      {['bold', 'italic', 'underline', 'strike', 'code'].map(item)}
      <span data-tb-fixed="highlight">
        <ToolbarPopover icon={<HighlightIcon />} title="Highlight colour" isActive={editor.isActive('highlight')}>
          {(close) => (
            <ColorPalette
              tiers={HIGHLIGHT_TIERS}
              current={editor.getAttributes('highlight').color as string | undefined}
              onPick={(color) => { chain().setHighlight({ color }).run(); close() }}
              onClear={() => { chain().unsetHighlight().run(); close() }}
              clearLabel="No highlight"
            />
          )}
        </ToolbarPopover>
      </span>
      {sep('sep-lists', ['bullet', 'ordered', 'task'])}
      {['bullet', 'ordered', 'task'].map(item)}
      <span className="toolbar__sep" />
      <span data-tb-fixed="align">
        <ToolbarDropdown
          title="Alignment"
          options={[
            { key: 'left', label: 'Align left', icon: <AlignLeftIcon />, isActive: editor.isActive({ textAlign: 'left' }) || !editor.isActive({ textAlign: 'center' }) && !editor.isActive({ textAlign: 'right' }), onSelect: () => chain().setTextAlign('left').run() },
            { key: 'center', label: 'Align center', icon: <AlignCenterIcon />, isActive: editor.isActive({ textAlign: 'center' }), onSelect: () => chain().setTextAlign('center').run() },
            { key: 'right', label: 'Align right', icon: <AlignRightIcon />, isActive: editor.isActive({ textAlign: 'right' }), onSelect: () => chain().setTextAlign('right').run() },
          ]}
        />
      </span>
      <span className="toolbar__sep" />
      {linkControl}
      <InsertMenu editor={editor} overflow={overflowActions} />
    </div>
  )
}
