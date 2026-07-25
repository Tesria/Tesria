import { useState } from 'react'
import { BubbleMenu } from '@tiptap/react/menus'
import type { Editor as TiptapEditor } from '@tiptap/react'
import { ToolbarButton } from './ToolbarButton'

/** A condensed formatting bar that floats above a non-empty text selection. */
export function SelectionBubbleMenu({ editor }: { editor: TiptapEditor }) {
  const [linkPopoverOpen, setLinkPopoverOpen] = useState(false)
  const [linkUrl, setLinkUrl] = useState('')

  function applyLink() {
    if (linkUrl.trim()) editor.chain().focus().extendMarkRange('link').setLink({ href: linkUrl.trim() }).run()
    setLinkPopoverOpen(false)
    setLinkUrl('')
  }

  return (
    <BubbleMenu
      editor={editor}
      pluginKey="selectionMenu"
      options={{ placement: 'top' }}
      shouldShow={({ editor, from, to }) =>
        // Only for a real text selection, and never while inside a code
        // block (code selections don't want inline-formatting buttons) or a
        // link (LinkMenu owns that case).
        from !== to && !editor.isActive('codeBlock') && !editor.isActive('link')
      }
    >
      <div className="toolbar toolbar--bubble">
        <ToolbarButton label={<span className="tb-glyph tb-bold">B</span>} isActive={editor.isActive('bold')} onClick={() => editor.chain().focus().toggleBold().run()} title="Bold" />
        <ToolbarButton label={<span className="tb-glyph tb-italic">I</span>} isActive={editor.isActive('italic')} onClick={() => editor.chain().focus().toggleItalic().run()} title="Italic" />
        <ToolbarButton label={<span className="tb-glyph tb-underline">U</span>} isActive={editor.isActive('underline')} onClick={() => editor.chain().focus().toggleUnderline().run()} title="Underline" />
        <ToolbarButton label={<span className="tb-glyph tb-strike">S</span>} isActive={editor.isActive('strike')} onClick={() => editor.chain().focus().toggleStrike().run()} title="Strikethrough" />
        <ToolbarButton label={<span className="tb-glyph tb-mono">{'</>'}</span>} isActive={editor.isActive('code')} onClick={() => editor.chain().focus().toggleCode().run()} title="Inline code" />
        <ToolbarButton label="Highlight" isActive={editor.isActive('highlight')} onClick={() => editor.chain().focus().toggleHighlight().run()} title="Highlight selected text" />
        <div className="toolbar__link">
          <ToolbarButton label="Link" isActive={false} onClick={() => setLinkPopoverOpen(true)} title="Add link" />
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
    </BubbleMenu>
  )
}
