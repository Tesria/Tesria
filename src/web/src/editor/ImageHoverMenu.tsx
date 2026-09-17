import { useState } from 'react'
import { BubbleMenu } from '@tiptap/react/menus'
import type { Editor as TiptapEditor } from '@tiptap/react'
import { ToolbarButton } from './ToolbarButton'
import { addImageComment } from './commentAction'

type Props = {
  editor: TiptapEditor
  /** Resolves the page id a new comment should be posted against. Omit to hide the Comment button. */
  getPageId?: () => Promise<string>
  onCommentError?: (message: string) => void
}

/**
 * A floating bar shown while an image is selected — display options
 * (border, drop shadow) and a comment action. Replaces the text-formatting
 * bubble (Bold/Italic/etc.), which made no sense for an image.
 */
export function ImageHoverMenu({ editor, getPageId, onCommentError }: Props) {
  const [commentOpen, setCommentOpen] = useState(false)
  const [commentBody, setCommentBody] = useState('')

  function toggleAttr(name: 'border' | 'shadow') {
    const current = Boolean(editor.getAttributes('image')[name])
    editor.chain().focus().updateAttributes('image', { [name]: !current }).run()
  }

  async function submitComment() {
    const src = editor.getAttributes('image').src as string | undefined
    if (!commentBody.trim() || !getPageId || !src) return
    try {
      await addImageComment(getPageId, commentBody.trim(), src)
    } catch (err) {
      onCommentError?.(err instanceof Error ? err.message : 'Could not add comment.')
    } finally {
      setCommentOpen(false)
      setCommentBody('')
    }
  }

  return (
    <BubbleMenu
      className="floating-menu"
      editor={editor}
      pluginKey="imageMenu"
      options={{ placement: 'top' }}
      shouldShow={({ editor }) => editor.isActive('image')}
    >
      <div className="toolbar toolbar--bubble">
        <ToolbarButton
          label="Border"
          isActive={Boolean(editor.getAttributes('image').border)}
          onClick={() => toggleAttr('border')}
          title="Toggle border"
        />
        <ToolbarButton
          label="Shadow"
          isActive={Boolean(editor.getAttributes('image').shadow)}
          onClick={() => toggleAttr('shadow')}
          title="Toggle drop shadow"
        />
        {getPageId && (
          <div className="toolbar__link">
            <ToolbarButton label="Comment" isActive={false} onClick={() => setCommentOpen(true)} title="Comment on this image" />
            {commentOpen && (
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
                    if (e.key === 'Escape') setCommentOpen(false)
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
