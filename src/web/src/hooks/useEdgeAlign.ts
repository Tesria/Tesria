import { useLayoutEffect, useRef, useState } from 'react'

const VIEWPORT_MARGIN_PX = 16

/**
 * Keeps a popover (`position: absolute; left: 0`, anchored to a small
 * toolbar button) from spilling past either edge of the viewport, by
 * measuring its actual rendered position/width once it opens and applying
 * a corrective pixel `left` offset as an inline style.
 *
 * Measures the real popover (not an estimate) via useLayoutEffect, which
 * runs after the DOM updates but before the browser paints — so the shift
 * is applied in the same frame the popover first becomes visible, with no
 * flash at the wrong position. An earlier version tried to estimate the
 * popover's width from the *anchor's* position before it ever rendered;
 * that broke for the floating selection bubble menu specifically, whose
 * own position is set asynchronously by floating-ui, so the anchor's
 * measured position at click-time didn't match where it actually ended up.
 */
export function useEdgeAlign<T extends HTMLElement>(open: boolean) {
  const ref = useRef<T>(null)
  const [offsetLeft, setOffsetLeft] = useState(0)

  useLayoutEffect(() => {
    if (!open) {
      setOffsetLeft(0)
      return
    }
    const el = ref.current
    if (!el) return
    // Runs once per open, while offsetLeft is still 0 (reset above on the
    // prior close), so this rect reflects the popover's natural, unshifted
    // position — not one already corrected by a previous open.
    // documentElement.clientWidth, not window.innerWidth: once something on
    // the page is already overflowing horizontally, some browser/automation
    // contexts report innerWidth as having grown to match the overflowing
    // content instead of the true layout viewport, which undercorrects the
    // very shift meant to fix that overflow. clientWidth stays pinned to
    // the actual viewport regardless.
    const rect = el.getBoundingClientRect()
    const viewportWidth = document.documentElement.clientWidth
    const overflowRight = rect.left + rect.width - (viewportWidth - VIEWPORT_MARGIN_PX)
    const minOffset = VIEWPORT_MARGIN_PX - rect.left
    setOffsetLeft(Math.max(minOffset, overflowRight > 0 ? -overflowRight : 0))
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open])

  return { ref, offsetLeft }
}
