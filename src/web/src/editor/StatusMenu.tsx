import { useEffect, useRef, useState } from 'react'
import { BubbleMenu } from '@tiptap/react/menus'
import { useEditorState, type Editor as TiptapEditor } from '@tiptap/react'
import { STATUS_COLORS, STATUS_LABELS, isStatusColor, type StatusColor } from './statusExtension'
import { updateSelectedNode } from './selectedNode'

/**
 * Edits the selected status lozenge: its text and one of the six colours.
 * Opens with the text field focused, so inserting a status and typing its
 * label is one motion: the same as Confluence.
 */
export function StatusMenu({ editor }: { editor: TiptapEditor }) {
  const attrs = useEditorState({
    editor,
    selector: ({ editor }) => (editor.isActive('status') ? editor.getAttributes('status') : null),
    equalityFn: (a, b) => b !== null && JSON.stringify(a) === JSON.stringify(b),
  })
  const [text, setText] = useState('')
  const inputRef = useRef<HTMLInputElement>(null)
  const color: StatusColor = isStatusColor(attrs?.color) ? attrs.color : 'grey'
  const open = attrs !== null

  // Re-seed the field whenever a (different) status is selected, and put
  // the caret in it: insert, type the label, done.
  useEffect(() => {
    setText(typeof attrs?.text === 'string' ? attrs.text : '')
  }, [attrs?.text])
  useEffect(() => {
    if (open) inputRef.current?.focus()
  }, [open])

  // Written on every keystroke, the lozenge updates as you type, and the
  // node is re-selected afterwards so the menu stays put (see selectedNode.ts).
  function commit(next: { text?: string; color?: StatusColor }) {
    updateSelectedNode(editor, 'status', next)
  }

  return (
    <BubbleMenu
      className="floating-menu"
      editor={editor}
      pluginKey="statusMenu"
      shouldShow={({ editor }) => editor.isActive('status')}
      options={{ placement: 'bottom' }}
    >
      <form
        className="chip-menu"
        onSubmit={(e) => {
          // A popover form inside the page's own save form: see the
          // architecture doc's editor gotcha. Enter returns to the text.
          e.preventDefault()
          e.stopPropagation()
          editor.commands.focus()
        }}
      >
        <input
          ref={inputRef}
          value={text}
          maxLength={40}
          placeholder="Status"
          onChange={(e) => {
            setText(e.target.value)
            commit({ text: e.target.value.trim() || 'STATUS' })
          }}
          onKeyDown={(e) => {
            if (e.key === 'Escape') editor.commands.focus()
          }}
        />
        <div className="chip-menu__colors" role="group" aria-label="Colour">
          {STATUS_COLORS.map((c) => (
            <button
              key={c}
              type="button"
              className={c === color ? `status status--${c} is-active` : `status status--${c}`}
              onMouseDown={(e) => e.preventDefault()}
              onClick={() => commit({ color: c })}
              title={STATUS_LABELS[c]}
            >
              {STATUS_LABELS[c]}
            </button>
          ))}
        </div>
      </form>
    </BubbleMenu>
  )
}
