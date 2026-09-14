import { useLayoutEffect, useRef, useState } from 'react'

/**
 * Keeps a toolbar on one line by deciding, from measurements, which of its
 * collapsible items fit and which must move into a menu.
 *
 * Every collapsible item is rendered with `data-tb-item="<key>"`. Their
 * natural widths are measured once, while all are visible, and cached — a
 * button's width does not change with the viewport, so re-measuring on every
 * resize would only risk measuring a hidden (zero-width) item. On each
 * resize the container's width, minus the room the fixed items need, is
 * filled from the *end* of `keys` — the caller lists items in the order it
 * is willing to lose them, so the last is kept longest — until the next
 * item would not fit; that item and everything before it overflow.
 *
 * The container is `flex-wrap: nowrap` (never `overflow: hidden`, which
 * would clip every dropdown), so a mis-measure shows up as a cramped row
 * rather than a second line — and the caller lists items in the order it is
 * willing to lose them.
 */
export function useToolbarOverflow(keys: string[], reserveKeys: string[] = []) {
  const containerRef = useRef<HTMLDivElement>(null)
  const widths = useRef<Map<string, number>>(new Map())
  const [overflowed, setOverflowed] = useState<Set<string>>(new Set())
  const signature = keys.join('|') + '::' + reserveKeys.join('|')

  useLayoutEffect(() => {
    const container = containerRef.current
    if (!container) return

    const measure = () => {
      // First pass (or a new item set): every item is visible, so record
      // its true width, including the gap that follows it.
      for (const el of container.querySelectorAll<HTMLElement>('[data-tb-item]')) {
        const key = el.dataset.tbItem!
        if (!widths.current.has(key) && el.offsetWidth > 0) widths.current.set(key, el.offsetWidth)
      }
    }

    const layout = () => {
      measure()
      const gap = parseFloat(getComputedStyle(container).columnGap || '0') || 0
      let available = container.clientWidth
      for (const el of container.querySelectorAll<HTMLElement>('[data-tb-fixed]')) available -= el.offsetWidth + gap
      // Separators are not items, but they take room: each is a 1px rule
      // with margins and a gap on either side. Leaving them out is why the
      // last control on the row sat a few pixels under the buttons beside
      // it on a wide screen — everything "fit" by measurement, and did not.
      for (const el of container.querySelectorAll<HTMLElement>('.toolbar__sep')) {
        const m = getComputedStyle(el)
        available -= el.offsetWidth + (parseFloat(m.marginLeft) || 0) + (parseFloat(m.marginRight) || 0) + gap
      }
      for (const key of reserveKeys) available -= (widths.current.get(key) ?? 0) + gap

      // `keys` is the order items are *lost* in, so the row is filled from
      // the other end: the last key is the one kept at all costs, and the
      // first is the first to go once room runs out.
      const next = new Set<string>()
      let used = 0
      let overflowing = false
      for (const key of [...keys].reverse()) {
        const w = (widths.current.get(key) ?? 0) + gap
        if (overflowing || used + w > available) {
          overflowing = true
          next.add(key)
        } else {
          used += w
        }
      }
      setOverflowed((prev) =>
        prev.size === next.size && [...prev].every((k) => next.has(k)) ? prev : next)
    }

    layout()
    const observer = new ResizeObserver(layout)
    observer.observe(container)
    return () => observer.disconnect()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [signature])

  return { containerRef, overflowed }
}
