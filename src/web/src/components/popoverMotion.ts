import { useLayoutEffect, useRef, useState } from 'react'
import { motionReduced } from '../theme'

/**
 * The theme and notification panels open out of their round button and close
 * back into it in the glass style (the owner, 2026-09-28), as the space
 * sidebar does into its show-sidebar button (SpacePage.tsx): closing draws
 * the panel's far sides in until only a button-sized circle is left under
 * the button, which slides up onto the button as it fades; opening runs that
 * backwards and settles with a slight overshoot. The minimal style, and
 * anyone who asks for reduced motion (the system, or the switch in the
 * appearance menu), keep the instant switch.
 *
 * Returns whether the panel should be rendered (it stays for the closing
 * motion) and the ref it needs. The button is the panel's previous sibling.
 */
function animated(): boolean {
  return document.documentElement.getAttribute('data-style') === 'glass'
    && typeof Element.prototype.animate === 'function'
    && !motionReduced()
}

/** The circle under the button, as a clip, and how far up the button is. */
function toButton(panel: HTMLElement): { clip: string; lift: number } | null {
  const button = panel.previousElementSibling
  if (!(button instanceof HTMLElement)) return null
  const p = panel.getBoundingClientRect()
  const b = button.getBoundingClientRect()
  const r = b.height / 2
  const cx = Math.min(Math.max(b.left + b.width / 2 - p.left, r), p.width - r)
  const clip = `inset(0 ${p.width - cx - r}px ${p.height - 2 * r}px ${cx - r}px round ${r}px)`
  return { clip, lift: p.top - b.top }
}

const PANEL_CLIP = 'inset(0 0 0 0 round 14px)'

export function usePopoverMotion<T extends HTMLElement>(open: boolean) {
  const ref = useRef<T>(null)
  const [shown, setShown] = useState(open)
  const closing = useRef<Animation | null>(null)

  // Opening: mount at once; closing: stay mounted until the motion ends.
  if (open && !shown) setShown(true)

  useLayoutEffect(() => {
    const panel = ref.current
    if (open) {
      closing.current?.cancel()
      closing.current = null
      if (!panel || !animated()) return
      const to = toButton(panel)
      if (!to) return
      panel.animate(
        [
          { clipPath: to.clip, transform: `translateY(${-to.lift}px)`, opacity: 0.2 },
          { clipPath: to.clip, transform: 'none', opacity: 1, offset: 0.25 },
          { clipPath: PANEL_CLIP, transform: 'scale(1.01)', offset: 0.8 },
          { clipPath: PANEL_CLIP, transform: 'none', opacity: 1 },
        ],
        { duration: 380, easing: 'cubic-bezier(0.2, 0.8, 0.2, 1)' },
      )
      return
    }
    if (!shown) return
    const to = panel && animated() ? toButton(panel) : null
    if (!panel || !to) { setShown(false); return }
    const a = panel.animate(
      [
        { clipPath: PANEL_CLIP, transform: 'none', opacity: 1 },
        { clipPath: to.clip, transform: 'none', opacity: 1, offset: 0.7 },
        { clipPath: to.clip, transform: `translateY(${-to.lift}px)`, opacity: 0 },
      ],
      { duration: 300, easing: 'cubic-bezier(0.55, 0, 0.6, 1)', fill: 'forwards' },
    )
    closing.current = a
    // A browser can slow or pause animations (a background tab, a hidden
    // window), and then the panel would linger: it goes by the clock too.
    const done = () => { if (closing.current !== a) return; closing.current = null; setShown(false) }
    a.onfinish = done
    const timer = window.setTimeout(done, 450)
    return () => window.clearTimeout(timer)
  }, [open, shown])

  return { shown, ref }
}
