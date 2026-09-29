import { useEffect, useRef, useState } from 'react'
import { BubbleMenu } from '@tiptap/react/menus'
import { useEditorState, type Editor as TiptapEditor } from '@tiptap/react'
import { STATUS_COLORS, STATUS_LABELS, isStatusColor, type StatusColor } from './statusExtension'
import { updateSelectedNode } from './selectedNode'
import { AppearancePicker } from './AppearancePicker'
import { continueAfterSelectedNode } from './inlineAtomTyping'

/**
 * Edits the selected status lozenge: its text and one of the six colors.
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

  // Re-seed the field whenever a (different) status is selected.
  useEffect(() => {
    setText(typeof attrs?.text === 'string' ? attrs.text : '')
  }, [attrs?.text])

  // A status just inserted opens with its label selected, so typing replaces
  // STATUS: insert, type the label, done (QA t4-010). Done as the menu is
  // shown, not when the selection changes: the menu is only put into the
  // page then, and focusing a box that is not in the page does nothing,
  // which left the status selected and the first letter typed deleted it.
  // A status that is clicked or arrowed onto keeps the focus in the text.
  const onShow = () => {
    const storage = (editor.storage as unknown as Record<string, { insertedAt?: number } | undefined>).status
    if (!storage?.insertedAt || Date.now() - storage.insertedAt > 3000) return
    storage.insertedAt = 0
    inputRef.current?.focus()
    inputRef.current?.select()
  }

  // Written on every keystroke, the lozenge updates as you type, and the
  // node is re-selected afterwards so the menu stays put (see selectedNode.ts).
  function commit(next: { text?: string; color?: StatusColor; appearance?: string }) {
    updateSelectedNode(editor, 'status', next)
  }

  return (
    <BubbleMenu
      className="floating-menu"
      editor={editor}
      pluginKey="statusMenu"
      shouldShow={({ editor }) => editor.isActive('status')}
      options={{ placement: 'bottom', onShow }}
    >
      <form
        className="chip-menu"
        onSubmit={(e) => {
          // A popover form inside the page's own save form: see the
          // architecture doc's editor gotcha. Enter returns to the text,
          // after the status: with the status still selected, the next
          // letter typed used to replace it.
          e.preventDefault()
          e.stopPropagation()
          continueAfterSelectedNode(editor)
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
            if (e.key === 'Escape') continueAfterSelectedNode(editor)
          }}
        />
        <div className="chip-menu__colors" role="group" aria-label="Color">
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
        <AppearancePicker
          className="chip-menu__appearance"
          value={attrs?.appearance}
          onChange={(appearance) => commit({ appearance })}
        />
      </form>
    </BubbleMenu>
  )
}
