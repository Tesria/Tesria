import { BubbleMenu } from '@tiptap/react/menus'
import { useEditorState, type Editor as TiptapEditor } from '@tiptap/react'
import { isIsoDate } from './dateExtension'
import { updateSelectedNode } from './selectedNode'
import { continueAfterSelectedNode } from './inlineAtomTyping'

/** A date picker for the selected date chip. */
export function DateMenu({ editor }: { editor: TiptapEditor }) {
  const date = useEditorState({
    editor,
    selector: ({ editor }) => (editor.isActive('date') ? (editor.getAttributes('date').date as string | null) : null),
  })

  return (
    <BubbleMenu
      className="floating-menu"
      editor={editor}
      pluginKey="dateMenu"
      shouldShow={({ editor }) => editor.isActive('date')}
      options={{ placement: 'bottom' }}
    >
      <div className="chip-menu">
        <input
          type="date"
          autoFocus
          value={isIsoDate(date) ? date : ''}
          onChange={(e) => {
            if (isIsoDate(e.target.value)) updateSelectedNode(editor, 'date', { date: e.target.value })
          }}
          onKeyDown={(e) => {
            // Back to writing, after the date. Enter is also canceled here:
            // this box sits in the page's own form, where Enter published
            // the page (QA t4-014; the page form stops it too).
            if (e.key === 'Enter') e.preventDefault()
            if (e.key === 'Escape' || e.key === 'Enter') continueAfterSelectedNode(editor)
          }}
        />
      </div>
    </BubbleMenu>
  )
}
