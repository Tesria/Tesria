import StarterKit from '@tiptap/starter-kit'
import type { AnyExtension } from '@tiptap/core'

type SharedExtensionOptions = {
  /** Collaborative editors let Yjs own undo/redo history instead of StarterKit's. */
  collaborative?: boolean
}

/**
 * Single source of truth for the TipTap/ProseMirror schema (node/mark types),
 * shared by the plain Editor, the Yjs-backed CollaborativeEditor, and anything
 * that renders stored content read-only. Yjs requires every collaborator to
 * agree on one exact schema, so this list must never diverge between editors
 * — new node/mark extensions get added here, not inline in either component.
 */
export function getSharedExtensions({ collaborative = false }: SharedExtensionOptions = {}): AnyExtension[] {
  return [
    StarterKit.configure(collaborative ? { undoRedo: false } : {}),
  ]
}
