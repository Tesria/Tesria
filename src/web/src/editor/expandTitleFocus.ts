/**
 * Where the expand this editor just inserted starts, so its node view can
 * put the cursor in the title box once it has mounted (t4-007): the docs
 * say "Type the title in the box at the top", and before this the cursor
 * stayed in the body, so a typed title became the body's first line. Kept
 * per editor and consumed once, so an expand arriving from a collaborator
 * never takes this user's focus.
 */
const titleFocusAt = new WeakMap<object, { pos: number; at: number }>()

/** Called by `setExpand` with where the expand it made starts. */
export function markNewExpand(editor: object, pos: number) {
  titleFocusAt.set(editor, { pos, at: Date.now() })
}

/** True, once, when the expand at `pos` is the one this editor just inserted. */
export function takeTitleFocus(editor: object, pos: number | undefined): boolean {
  const pending = titleFocusAt.get(editor)
  if (pos === undefined || !pending || pending.pos !== pos) return false
  titleFocusAt.delete(editor)
  // A node view mounts straight after the insert; one that turns up much
  // later at the same place (the editor redrawn) is not the new expand.
  return Date.now() - pending.at < 2000
}
