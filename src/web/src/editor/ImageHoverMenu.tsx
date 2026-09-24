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
 * A floating bar shown while an image is selected: display options
 * (border, drop shadow) and a comment action. Replaces the text-formatting
 * bubble (Bold/Italic/etc.), which made no sense for an image.
 */
export function ImageHoverMenu({ editor, getPageId, onCommentError }: Props) {
  const [commentOpen, setCommentOpen] = useState(false)
  const [commentBody, setCommentBody] = useState('')
  // One text field at a time: the caption or the alt text (dev-plan 15.2).
  const [textField, setTextField] = useState<null | 'caption' | 'alt'>(null)
  const [textValue, setTextValue] = useState('')

  const attrs = editor.getAttributes('image') as { align?: string; width?: number | null; caption?: string | null; alt?: string | null }
  const align = attrs.align ?? 'center'

  function setAlign(value: string) {
    editor.chain().focus().updateAttributes('image', { align: value }).run()
  }

  function openText(field: 'caption' | 'alt') {
    setCommentOpen(false)
    setTextField(field)
    setTextValue((field === 'caption' ? attrs.caption : attrs.alt) ?? '')
  }

  function saveText() {
    if (!textField) return
    const value = textValue.trim()
    editor.chain().focus().updateAttributes('image', { [textField]: value || null }).run()
    setTextField(null)
  }

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
        <span className="toolbar__sep" aria-hidden="true" />
        {([['left', 'Left', 'Align left'], ['center', 'Center', 'Center'], ['right', 'Right', 'Align right'], ['full', 'Full', 'Full width']] as const).map(([value, label, title]) => (
          <ToolbarButton key={value} label={label} isActive={align === value} onClick={() => setAlign(value)} title={title} />
        ))}
        {attrs.width != null && align !== 'full' && (
          <ToolbarButton label="Original size" isActive={false}
            onClick={() => editor.chain().focus().updateAttributes('image', { width: null }).run()}
            title="Back to the picture's own size" />
        )}
        <span className="toolbar__sep" aria-hidden="true" />
        <div className="toolbar__link">
          <ToolbarButton label="Caption" isActive={Boolean(attrs.caption)} onClick={() => openText('caption')} title="A caption under the picture" />
          <ToolbarButton label="Alt text" isActive={false} onClick={() => openText('alt')} title="What the picture shows, for people who cannot see it" />
          {textField && (
            <form
              className="toolbar__link-popover"
              onSubmit={(e) => { e.preventDefault(); e.stopPropagation(); saveText() }}
            >
              <input
                autoFocus
                value={textValue}
                onChange={(e) => setTextValue(e.target.value)}
                placeholder={textField === 'caption' ? 'A caption under the picture' : 'What the picture shows'}
                maxLength={textField === 'caption' ? 300 : 500}
                aria-label={textField === 'caption' ? 'Caption' : 'Alt text'}
                onKeyDown={(e) => { if (e.key === 'Escape') setTextField(null) }}
              />
              <button type="submit" className="link-btn">Save</button>
            </form>
          )}
        </div>
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
