/**
 * Whether the pointer really moved since the last event: for a menu's
 * hover highlight. Safari (WebKit) reports the pointer entering a menu that
 * opens under a pointer standing still, and the item under it took the
 * highlight, so `/table` then Enter inserted the table of contents under the
 * mouse (t4-R04, the 0.8.3 retest). Only movement, not appearing, moves the
 * highlight. Pure, so a test can hold it to that.
 */
export function pointerMoved(
  last: { x: number; y: number } | null,
  now: { x: number; y: number },
): boolean {
  return last !== null && (last.x !== now.x || last.y !== now.y)
}
