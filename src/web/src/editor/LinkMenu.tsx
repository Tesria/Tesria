import { BubbleMenu } from '@tiptap/react/menus'
import { useEditorState, type Editor as TiptapEditor } from '@tiptap/react'
import { triggerLinkDialog } from './linkShortcut'

/**
 * A floating bar shown only while the cursor is inside a link: on a phone,
 * that is what tapping the link does, since links do not navigate in the
 * editor. Shows the address, opens it, edits it (in the link dialog) or
 * removes it. Editor only; a reader's tap on a link follows it.
 */
export function LinkMenu({ editor }: { editor: TiptapEditor }) {
  // Subscribed, not read once: the bubble appears on a selection change,
  // and a value captured at the previous render was the empty string.
  const href = useEditorState({
    editor,
    selector: ({ editor }) => (editor.getAttributes('link').href as string | undefined) ?? '',
  })
  return (
    <BubbleMenu
      className="floating-menu"
      editor={editor}
      pluginKey="linkMenu"
      shouldShow={({ editor }) => editor.isActive('link')}
      options={{ placement: 'bottom' }}
    >
      <div className="link-menu">
        <a className="link-menu__url" href={href || '#'} target="_blank" rel="noreferrer">{href}</a>
        <button type="button" className="link-btn" onClick={() => triggerLinkDialog(editor)}>Edit</button>
        <button
          type="button"
          className="link-btn link-btn--danger"
          onClick={() => editor.chain().focus().extendMarkRange('link').unsetLink().run()}
        >
          Remove
        </button>
      </div>
    </BubbleMenu>
  )
}
