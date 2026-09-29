import { Extension } from '@tiptap/core'
import { Plugin, PluginKey, NodeSelection, TextSelection } from '@tiptap/pm/state'

/** The inline atoms that open a menu when selected, and so are left selected after inserting. */
const CHIPS = ['status', 'date', 'math']

/**
 * Typing with a status, a date or math selected carries on after it instead
 * of replacing it (QA t4-010, t4-011).
 *
 * Inserting one of these leaves it selected, so its menu opens: pick a date,
 * choose a color. That is ProseMirror's node selection, and in a node
 * selection the first key typed replaces the node, so writing "due " then a
 * date then " to legal" deleted the date at the space, and Enter in a
 * status's label (back to the text, still selected) deleted the status at
 * the next letter. Removing one stays as easy as it was: Backspace or Delete.
 */
export const InlineAtomTyping = Extension.create({
  name: 'inlineAtomTyping',

  addProseMirrorPlugins() {
    return [
      new Plugin({
        key: new PluginKey('inlineAtomTyping'),
        props: {
          handleTextInput(view, _from, _to, text) {
            const { selection } = view.state
            if (!(selection instanceof NodeSelection)) return false
            const node = selection.node
            if (!node.isInline || !CHIPS.includes(node.type.name)) return false
            const after = selection.to
            const tr = view.state.tr.setSelection(TextSelection.create(view.state.doc, after))
            tr.insertText(text, after)
            view.dispatch(tr.scrollIntoView())
            return true
          },
        },
      }),
    ]
  },
})

/**
 * Moves the cursor to just after the selected inline atom and gives the
 * editor focus: "back to writing", from a chip's menu.
 */
export function continueAfterSelectedNode(editor: import('@tiptap/core').Editor): void {
  const { selection } = editor.state
  const after = selection instanceof NodeSelection ? selection.to : selection.from
  editor.chain().focus().setTextSelection(after).run()
}
