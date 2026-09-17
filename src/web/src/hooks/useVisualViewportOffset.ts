import { useEffect } from 'react'

/**
 * Keeps the sticky bars where the eye is when a phone's keyboard is up.
 *
 * iOS Safari has two viewports. `position: sticky` and `fixed` attach to
 * the LAYOUT viewport; the on-screen keyboard shrinks the VISUAL one and,
 * to keep the caret in view, Safari scrolls the visual viewport within the
 * layout viewport. Anything pinned to the layout viewport's top then slides
 * out of the visible area — the top bar goes first, the editor toolbar
 * next. The visualViewport API reports that offset, and translating the
 * bars by it puts them back at the visible top. A class gates the
 * transform so it exists only while there is an offset: a transform, even
 * translateY(0), makes an element the containing block for its fixed
 * descendants, which would relocate the theme panel and any dialog.
 */
export function useVisualViewportOffset() {
  useEffect(() => {
    const vv = window.visualViewport
    if (!vv) return
    const root = document.documentElement
    let frame = 0
    const apply = () => {
      frame = 0
      const top = Math.max(0, Math.round(vv.offsetTop))
      if (top > 0) {
        root.style.setProperty('--vv-top', `${top}px`)
        root.classList.add('vv-offset')
      } else {
        root.style.removeProperty('--vv-top')
        root.classList.remove('vv-offset')
      }
    }
    const schedule = () => {
      if (!frame) frame = requestAnimationFrame(apply)
    }
    vv.addEventListener('resize', schedule)
    vv.addEventListener('scroll', schedule)
    apply()
    return () => {
      vv.removeEventListener('resize', schedule)
      vv.removeEventListener('scroll', schedule)
      if (frame) cancelAnimationFrame(frame)
      root.style.removeProperty('--vv-top')
      root.classList.remove('vv-offset')
    }
  }, [])
}
