/**
 * Shows the glass style's scrollbars (glass.css) only while the pointer is in
 * their scroller or it is scrolling (the owner, 2026-09-28), by marking those
 * scrollers with data-sb.
 *
 * Chrome does not repaint a scrollbar when only its style changes: a
 * :hover or attribute rule on ::-webkit-scrollbar-thumb takes effect at the
 * next layout of that scroller, which may be never. So each change of the mark
 * also flips the scroller's overflow for one synchronous layout (the
 * sb-refresh class), which repaints the bar. The scroll position is kept.
 */
const HIDE_AFTER_MS = 900

/**
 * Only for a mouse, in the glass style. A touch screen draws its own overlay
 * scrollbars, and the repaint below (overflow off for a moment) at the start
 * of a swipe or during one stopped iOS scrolling the phone menu's page tree
 * (the owner, 2026-09-28).
 */
const fine = window.matchMedia('(hover: hover) and (pointer: fine)')
function active(): boolean {
  return fine.matches && document.documentElement.getAttribute('data-style') === 'glass'
}
const hovered = new Set<Element>()
const scrolling = new Map<Element, number>()

function refresh(el: Element) {
  el.classList.add('sb-refresh')
  void (el as HTMLElement).offsetWidth
  el.classList.remove('sb-refresh')
}

function update(el: Element) {
  const show = hovered.has(el) || scrolling.has(el)
  if (show === el.hasAttribute('data-sb')) return
  if (show) el.setAttribute('data-sb', '')
  else el.removeAttribute('data-sb')
  refresh(el)
}

/** The scrollers the pointer is in: every scrolling ancestor (the page's own bar always shows). */
function scrollersFrom(target: EventTarget | null): Set<Element> {
  const out = new Set<Element>()
  for (let el = target instanceof Element ? target : null; el && el !== document.body; el = el.parentElement) {
    const cs = getComputedStyle(el)
    const y = /auto|scroll/.test(cs.overflowY) && el.scrollHeight > el.clientHeight
    const x = /auto|scroll/.test(cs.overflowX) && el.scrollWidth > el.clientWidth
    if (x || y) out.add(el)
  }
  return out
}

function onPointerOver(e: PointerEvent) {
  if (e.pointerType !== 'mouse' || !active()) return
  const now = scrollersFrom(e.target)
  for (const el of [...hovered]) if (!now.has(el)) { hovered.delete(el); update(el) }
  for (const el of now) if (!hovered.has(el)) { hovered.add(el); update(el) }
}

function onLeaveWindow() {
  for (const el of [...hovered]) { hovered.delete(el); update(el) }
}

function onScroll(e: Event) {
  if (!active()) return
  if (!(e.target instanceof Element) || e.target === document.documentElement) return
  const el = e.target
  const pending = scrolling.get(el)
  if (pending !== undefined) window.clearTimeout(pending)
  scrolling.set(el, window.setTimeout(() => { scrolling.delete(el); update(el) }, HIDE_AFTER_MS))
  update(el)
}

export function watchScrolling() {
  document.addEventListener('scroll', onScroll, { capture: true, passive: true })
  document.addEventListener('pointerover', onPointerOver, { passive: true })
  document.documentElement.addEventListener('pointerleave', onLeaveWindow)
}
