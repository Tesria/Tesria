import { useEffect, useState, type ReactNode } from 'react'
import type { Editor as TiptapEditor } from '@tiptap/react'
import { ToolbarButton } from './ToolbarButton'
import { HeadingLinkList } from './HeadingLinkList'
import { ToolbarDropdown } from './ToolbarDropdown'
import { ToolbarPopover } from './ToolbarPopover'
import { InsertMenu } from './InsertMenu'
import { TextStyleMenu } from './TextStyleMenu'
import type { OverflowAction } from './OverflowItems'
import { useToolbarOverflow } from './useToolbarOverflow'
import { useEdgeAlign } from '../hooks/useEdgeAlign'
import { useMediaQuery } from '../hooks/useMediaQuery'
import { ColorPalette } from './ColorPalette'
import { HIGHLIGHT_TIERS, TEXT_COLOR_TIERS } from './palette'
import { isTextColor } from './textColorMark'
import { onLinkShortcut } from './linkShortcut'
import {
  InlineCodeIcon, HighlightIcon, BulletListIcon, OrderedListIcon, TaskListIcon,
  AlignLeftIcon, AlignCenterIcon, AlignRightIcon, LinkIcon,
  TextColorIcon, IndentIcon, OutdentIcon, ClearFormattingIcon,
} from './icons'

type Props = {
  editor: TiptapEditor
  /** Resolves the page id image attachments should be uploaded against. Omit to hide the image item. */
  getUploadPageId?: () => Promise<string>
  onUploadError?: (message: string) => void
}

/** A formatting control that can leave the row for the Insert menu's "More" section when space runs out. */
type Collapsible = { key: string; icon: ReactNode; label: string; isActive: boolean; run: () => void; group: OverflowAction['group'] }

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

  // Cmd/Ctrl+K opens this same popover — see linkShortcut.ts for why the
  // shortcut calls in rather than going through a command.
  useEffect(
    () =>
      onLinkShortcut(editor, () => {
        setLinkUrl((editor.getAttributes('link').href as string | undefined) ?? '')
        setLinkPopoverOpen(true)
      }),
    [editor],
  )

  function applyLink() {
    if (linkUrl.trim()) editor.chain().focus().extendMarkRange('link').setLink({ href: linkUrl.trim() }).run()
    setLinkPopoverOpen(false)
  }

  const chain = () => editor.chain().focus()
  // Keep in sync with --bp-mobile in index.css. Below it the row is
  // "Aa · +" beside the page buttons and nothing else — no measuring.
  const phone = useMediaQuery('(max-width: 640px)')

  // The link form, shared by the row's popover and the text menu's panel.
  const linkForm = (className: string, close: () => void) => (
    <form
      ref={className === 'toolbar__link-popover' ? linkAlign.ref : undefined}
      className={className}
      style={className === 'toolbar__link-popover' ? { left: linkAlign.offsetLeft } : undefined}
      onSubmit={(e) => {
        e.preventDefault()
        e.stopPropagation()
        applyLink()
        close()
      }}
    >
      <input
        autoFocus
        value={linkUrl}
        onChange={(e) => setLinkUrl(e.target.value)}
        placeholder="https://…"
        onKeyDown={(e) => {
          if (e.key === 'Escape') close()
        }}
      />
      <button type="submit" className="link-btn">Apply</button>
      <HeadingLinkList editor={editor} onPick={setLinkUrl} />
    </form>
  )

  // In the order they leave the row when space runs out: the rarest
  // formatting first, then lists, then the coloured and aligned things,
  // then the common marks, bold last. On a phone everything down to
  // italic goes and the row reads "Aa · B I · link · +" beside
  // Update/Close — the four things a thumb actually reaches for.
  const collapsible: Collapsible[] = [
    { key: 'superscript', group: 'format', icon: <span className="tb-glyph">x²</span>, label: 'Superscript', isActive: editor.isActive('superscript'), run: () => chain().toggleSuperscript().run() },
    { key: 'subscript', group: 'format', icon: <span className="tb-glyph">x₂</span>, label: 'Subscript', isActive: editor.isActive('subscript'), run: () => chain().toggleSubscript().run() },
    { key: 'outdent', group: 'paragraph', icon: <OutdentIcon />, label: 'Outdent', isActive: false, run: () => chain().outdent().run() },
    { key: 'indent', group: 'paragraph', icon: <IndentIcon />, label: 'Indent', isActive: false, run: () => chain().indent().run() },
    { key: 'clear', group: 'format', icon: <ClearFormattingIcon />, label: 'Clear formatting', isActive: false, run: () => chain().clearFormatting().run() },
    { key: 'task', group: 'paragraph', icon: <TaskListIcon />, label: 'Task list', isActive: editor.isActive('taskList'), run: () => chain().toggleTaskList().run() },
    { key: 'ordered', group: 'paragraph', icon: <OrderedListIcon />, label: 'Ordered list', isActive: editor.isActive('orderedList'), run: () => chain().toggleOrderedList().run() },
    { key: 'bullet', group: 'paragraph', icon: <BulletListIcon />, label: 'Bullet list', isActive: editor.isActive('bulletList'), run: () => chain().toggleBulletList().run() },
    // Alignment, colour and highlight are dropdowns on the row; in the menu
    // they become three alignment items and two inline palettes (see
    // overflowActions below). Their keys are measured like any other item.
    { key: 'align', group: 'paragraph', icon: <AlignLeftIcon />, label: 'Alignment', isActive: false, run: () => {} },
    { key: 'textcolor', group: 'colour', icon: <TextColorIcon />, label: 'Text colour', isActive: editor.isActive('textColor'), run: () => {} },
    { key: 'highlight', group: 'colour', icon: <HighlightIcon />, label: 'Highlight', isActive: editor.isActive('highlight'), run: () => {} },
    { key: 'code', group: 'format', icon: <InlineCodeIcon />, label: 'Inline code', isActive: editor.isActive('code'), run: () => chain().toggleCode().run() },
    { key: 'strike', group: 'format', icon: <span className="tb-glyph tb-strike">S</span>, label: 'Strikethrough', isActive: editor.isActive('strike'), run: () => chain().toggleStrike().run() },
    // In the menu the link is a panel holding the same form as the row's
    // popover, so a phone can add a link without the button it would
    // otherwise anchor to.
    { key: 'link', group: 'format', icon: <LinkIcon />, label: 'Link', isActive: editor.isActive('link'), run: () => {} },
    { key: 'underline', group: 'format', icon: <span className="tb-glyph tb-underline">U</span>, label: 'Underline', isActive: editor.isActive('underline'), run: () => chain().toggleUnderline().run() },
    { key: 'italic', group: 'format', icon: <span className="tb-glyph tb-italic">I</span>, label: 'Italic', isActive: editor.isActive('italic'), run: () => chain().toggleItalic().run() },
    { key: 'bold', group: 'format', icon: <span className="tb-glyph tb-bold">B</span>, label: 'Bold', isActive: editor.isActive('bold'), run: () => chain().toggleBold().run() },
  ]
  // The link button is never collapsed: its popover anchors to the button,
  // so a hidden button would mean a popover that cannot appear.
  // Measurement order is "first to go, first in the list"; display order is the reverse.
  const { containerRef, overflowed } = useToolbarOverflow(collapsible.map((c) => c.key), [], phone)
  const byKey = new Map(collapsible.map((c) => [c.key, c]))
  const show = (key: string) => !overflowed.has(key)

  const alignOptions = [
    { key: 'left', label: 'Align left', icon: <AlignLeftIcon />, isActive: editor.isActive({ textAlign: 'left' }) || !editor.isActive({ textAlign: 'center' }) && !editor.isActive({ textAlign: 'right' }), onSelect: () => chain().setTextAlign('left').run() },
    { key: 'center', label: 'Align center', icon: <AlignCenterIcon />, isActive: editor.isActive({ textAlign: 'center' }), onSelect: () => chain().setTextAlign('center').run() },
    { key: 'right', label: 'Align right', icon: <AlignRightIcon />, isActive: editor.isActive({ textAlign: 'right' }), onSelect: () => chain().setTextAlign('right').run() },
  ]
  const highlightPalette = (close: () => void) => (
    <ColorPalette
      tiers={HIGHLIGHT_TIERS}
      current={editor.getAttributes('highlight').color as string | undefined}
      onPick={(color) => { chain().setHighlight({ color }).run(); close() }}
      onClear={() => { chain().unsetHighlight().run(); close() }}
      clearLabel="No highlight"
    />
  )
  const textColorPalette = (close: () => void) => (
    <ColorPalette
      tiers={TEXT_COLOR_TIERS}
      current={isTextColor(editor.getAttributes('textColor').color) ? editor.getAttributes('textColor').color : null}
      onPick={(color) => { if (isTextColor(color)) chain().setTextColor(color).run(); close() }}
      onClear={() => { chain().unsetTextColor().run(); close() }}
      clearLabel="Default colour"
    />
  )

  // What the text menu shows beneath the block styles for whatever left the
  // row. Alignment expands to its three choices; the two colour controls
  // carry their palette with them so a phone still has every colour. The
  // "+" menu never receives any of this: it is for things to insert.
  const overflowActions: OverflowAction[] = collapsible
    .filter((c) => overflowed.has(c.key))
    // `collapsible` is in the order things are *lost*; the menu is read
    // top-down, so it lists them in the order they sat on the row —
    // italic first, superscript last.
    .reverse()
    .flatMap((c): OverflowAction[] => {
      if (c.key === 'align')
        return alignOptions.map((o) => ({ key: `align-${o.key}`, group: 'paragraph' as const, icon: o.icon, label: o.label, isActive: o.isActive, run: o.onSelect }))
      if (c.key === 'highlight') return [{ ...c, panel: highlightPalette }]
      if (c.key === 'textcolor') return [{ ...c, panel: textColorPalette }]
      if (c.key === 'link') return [{ ...c, panel: (close) => { return linkForm('toolbar__link-form', close) } }]
      return [c]
    })

  const item = (key: string) => {
    const c = byKey.get(key)!
    return (
      <span key={key} data-tb-item={key} className={show(key) ? 'tb-item' : 'tb-item tb-item--hidden'}>
        <ToolbarButton label={c.icon} isActive={c.isActive} onClick={c.run} title={c.label} />
      </span>
    )
  }

  const linkControl = (
    <div className={show('link') ? 'toolbar__link tb-item' : 'toolbar__link tb-item tb-item--hidden'} data-tb-item="link">
      <ToolbarButton label={<LinkIcon />} isActive={editor.isActive('link')} onClick={openLinkPopover} title="Link" />
      {linkPopoverOpen && linkForm('toolbar__link-popover', () => setLinkPopoverOpen(false))}
    </div>
  )

  const sep = (key: string, after: string[]) =>
    after.some(show) ? <span key={key} className="toolbar__sep" /> : null

  const headingLevel = [1, 2, 3].find((l) => editor.isActive('heading', { level: l }))

  return (
    <div className="toolbar" ref={containerRef}>
      <span data-tb-fixed="style">
        <TextStyleMenu
          title="Text"
          compactLabel="Aa"
          overflow={overflowActions}
          styles={[
            // The trigger reads "Normal text ⌄", as Confluence's does.
            { key: 'p', label: 'Normal text', isActive: !headingLevel, onSelect: () => chain().setParagraph().run() },
            { key: 'h1', label: 'Heading 1', isActive: headingLevel === 1, onSelect: () => chain().toggleHeading({ level: 1 }).run() },
            { key: 'h2', label: 'Heading 2', isActive: headingLevel === 2, onSelect: () => chain().toggleHeading({ level: 2 }).run() },
            { key: 'h3', label: 'Heading 3', isActive: headingLevel === 3, onSelect: () => chain().toggleHeading({ level: 3 }).run() },
          ]}
        />
      </span>
      {sep('sep-marks', ['bold', 'italic', 'underline', 'strike', 'code', 'highlight', 'textcolor'])}
      {['bold', 'italic', 'underline', 'strike', 'code'].map(item)}
      <span data-tb-item="highlight" className={show('highlight') ? 'tb-item' : 'tb-item tb-item--hidden'}>
        <ToolbarPopover icon={<HighlightIcon />} title="Highlight colour" isActive={editor.isActive('highlight')}>
          {highlightPalette}
        </ToolbarPopover>
      </span>
      <span data-tb-item="textcolor" className={show('textcolor') ? 'tb-item' : 'tb-item tb-item--hidden'}>
        <ToolbarPopover icon={<TextColorIcon />} title="Text colour" isActive={editor.isActive('textColor')}>
          {textColorPalette}
        </ToolbarPopover>
      </span>
      {sep('sep-lists', ['bullet', 'ordered', 'task'])}
      {['bullet', 'ordered', 'task'].map(item)}
      {sep('sep-indent', ['outdent', 'indent'])}
      {['outdent', 'indent'].map(item)}
      {sep('sep-script', ['subscript', 'superscript', 'clear'])}
      {['subscript', 'superscript', 'clear'].map(item)}
      {sep('sep-align', ['align'])}
      <span data-tb-item="align" className={show('align') ? 'tb-item' : 'tb-item tb-item--hidden'}>
        <ToolbarDropdown title="Alignment" options={alignOptions} />
      </span>
      {sep('sep-link', ['link'])}
      {linkControl}
      <InsertMenu editor={editor} />
    </div>
  )
}
