import { useEffect, useState } from 'react'
import { BubbleMenu } from '@tiptap/react/menus'
import type { Editor as TiptapEditor } from '@tiptap/react'

/** A floating bar shown only while the cursor is inside a link: edit, remove, or open it. */
export function LinkMenu({ editor }: { editor: TiptapEditor }) {
  const [editing, setEditing] = useState(false)
  const [url, setUrl] = useState('')

  // Reset the "editing" state whenever the bubble hides (moving to a
  // different link, or leaving links entirely), so it never reopens stale.
  useEffect(() => {
    const onSelectionUpdate = () => {
      if (!editor.isActive('link')) setEditing(false)
    }
    editor.on('selectionUpdate', onSelectionUpdate)
    return () => {
      editor.off('selectionUpdate', onSelectionUpdate)
    }
  }, [editor])

  function startEditing() {
    setUrl((editor.getAttributes('link').href as string | undefined) ?? '')
    setEditing(true)
  }

  function save() {
    editor.chain().focus().extendMarkRange('link').setLink({ href: url }).run()
    setEditing(false)
  }

  function remove() {
    editor.chain().focus().extendMarkRange('link').unsetLink().run()
    setEditing(false)
  }

  return (
    <BubbleMenu
      editor={editor}
      pluginKey="linkMenu"
      shouldShow={({ editor }) => editor.isActive('link')}
      options={{ placement: 'bottom' }}
    >
      <div className="link-menu">
        {editing ? (
          <form
            className="link-menu__form"
            onSubmit={(e) => {
              e.preventDefault()
              save()
            }}
          >
            <input autoFocus value={url} onChange={(e) => setUrl(e.target.value)} placeholder="https://…" />
            <button type="submit" className="link-btn">Save</button>
          </form>
        ) : (
          <>
            <a
              className="link-menu__url"
              href={(editor.getAttributes('link').href as string | undefined) ?? '#'}
              target="_blank"
              rel="noreferrer"
            >
              {(editor.getAttributes('link').href as string | undefined) ?? ''}
            </a>
            <button type="button" className="link-btn" onClick={startEditing}>Edit</button>
            <button type="button" className="link-btn link-btn--danger" onClick={remove}>Remove</button>
          </>
        )}
      </div>
    </BubbleMenu>
  )
}
