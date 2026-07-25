import { useState } from 'react'
import { BubbleMenu } from '@tiptap/react/menus'
import type { Editor as TiptapEditor } from '@tiptap/react'
import { ToolbarButton } from './ToolbarButton'
import { addInlineTextComment } from './commentAction'

type Props = {
  editor: TiptapEditor
  /** Resolves the page id a new comment should be posted against. Omit to hide the Comment button. */
  getPageId?: () => Promise<string>
  onCommentError?: (message: string) => void
}

/** A condensed formatting bar that floats above a non-empty text selection. */
export function SelectionBubbleMenu({ editor, getPageId, onCommentError }: Props) {
  const [linkPopoverOpen, setLinkPopoverOpen] = useState(false)
  const [linkUrl, setLinkUrl] = useState('')
  const [commentPopoverOpen, setCommentPopoverOpen] = useState(false)
  const [commentBody, setCommentBody] = useState('')
  const [commentRange, setCommentRange] = useState<{ from: number; to: number } | null>(null)

  function applyLink() {
    if (linkUrl.trim()) editor.chain().focus().extendMarkRange('link').setLink({ href: linkUrl.trim() }).run()
    setLinkPopoverOpen(false)
    setLinkUrl('')
  }

  function openCommentPopover() {
    const { from, to } = editor.state.selection
    setCommentRange({ from, to })
    setCommentBody('')
    setCommentPopoverOpen(true)
  }

  async function submitComment() {
    if (!commentBody.trim() || !commentRange || !getPageId) return
    try {
      await addInlineTextComment(editor, getPageId, commentBody.trim(), commentRange)
    } catch (err) {
      onCommentError?.(err instanceof Error ? err.message : 'Could not add comment.')
    } finally {
      setCommentPopoverOpen(false)
    }
  }

  return (
    <BubbleMenu
      editor={editor}
      pluginKey="selectionMenu"
      options={{ placement: 'top' }}
      shouldShow={({ editor, from, to }) =>
        // Only for a real text selection, and never while inside a code
        // block (code selections don't want inline-formatting buttons), a
        // link (LinkMenu owns that case), or an image (ImageHoverMenu does).
        from !== to && !editor.isActive('codeBlock') && !editor.isActive('link') && !editor.isActive('image')
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
                // Stop this from also submitting the page's own save <form>
                // it's nested in (React events bubble the component tree
                // regardless of BubbleMenu's DOM portal).
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
        {getPageId && (
          <div className="toolbar__link">
            <ToolbarButton label="Comment" isActive={false} onClick={openCommentPopover} title="Comment on this selection" />
            {commentPopoverOpen && (
              <form
                className="toolbar__link-popover toolbar__comment-popover"
                onSubmit={(e) => {
                  e.preventDefault()
                  e.stopPropagation()
                  submitComment()
                }}
              >
                <textarea
                  autoFocus
                  value={commentBody}
                  onChange={(e) => setCommentBody(e.target.value)}
                  placeholder="Write a comment…"
                  rows={2}
                  onKeyDown={(e) => {
                    if (e.key === 'Escape') setCommentPopoverOpen(false)
                  }}
                />
                <button type="submit" className="link-btn">Comment</button>
              </form>
            )}
          </div>
        )}
      </div>
    </BubbleMenu>
  )
}
