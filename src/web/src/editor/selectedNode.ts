import type { Editor } from '@tiptap/react'
import { NodeSelection } from '@tiptap/pm/state'

/**
 * Update the attributes of the selected inline atom (a status, a date) and
 * select it again. `updateAttributes` rewrites the node's markup, and a
 * NodeSelection does not survive that — it collapses to a text cursor,
 * which would close the bubble menu that is editing the node on every
 * keystroke.
 */
export function updateSelectedNode(editor: Editor, type: string, attrs: Record<string, unknown>): void {
  const { selection } = editor.state
  const at = selection instanceof NodeSelection ? selection.from : null
  const chain = editor.chain().updateAttributes(type, attrs)
  if (at !== null) chain.setNodeSelection(at)
  chain.run()
}
