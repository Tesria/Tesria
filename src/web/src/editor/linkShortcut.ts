import { Extension } from '@tiptap/core'
import type { Editor } from '@tiptap/react'

/**
 * Cmd/Ctrl+K: the one Confluence shortcut the schema was missing after the
 * Wave B audit (everything else it lists is covered by StarterKit,
 * TextAlign, Highlight or `textFormatting.ts`).
 *
 * "Insert a link" is not a document edit, so it cannot be a command: it has
 * to open a popover that lives in React state, in whichever toolbar is
 * mounted. Rather than route that through a fake transaction so
 * `useEditorState` notices it, the shortcut just calls subscribers
 * directly. The registry is keyed by editor instance, so two editors on a
 * page (there never are, but the collaborative and plain editors do swap)
 * cannot trigger each other's popover, and unsubscribing on unmount keeps
 * it from leaking.
 */
type Listener = () => void

const listeners = new WeakMap<Editor, Set<Listener>>()

export function onLinkShortcut(editor: Editor, fn: Listener): () => void {
  let set = listeners.get(editor)
  if (!set) {
    set = new Set()
    listeners.set(editor, set)
  }
  set.add(fn)
  return () => {
    set.delete(fn)
  }
}

/** Open whatever link UI is mounted for this editor: the same thing Cmd/Ctrl+K does. */
export function triggerLinkDialog(editor: Editor): boolean {
  const set = listeners.get(editor)
  if (!set || set.size === 0) return false
  for (const fn of set) fn()
  return true
}

export const LinkShortcut = Extension.create({
  name: 'linkShortcut',

  addKeyboardShortcuts() {
    return {
      'Mod-k': () => triggerLinkDialog(this.editor as Editor),
    }
  },
})
