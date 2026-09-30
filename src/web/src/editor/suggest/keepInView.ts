/**
 * Scrolls a popup list (the slash, mention and emoji menus) just enough to
 * show its highlighted row, so the arrow keys never select a row the user
 * cannot see (t4-001). Only the list scrolls, never the page behind it,
 * which `Element.scrollIntoView` would do when the popup sits near the
 * window's edge.
 */
export function keepHighlightInView(list: HTMLElement | null) {
  const row = list?.querySelector<HTMLElement>('.is-selected')
  if (!list || !row) return
  const box = list.getBoundingClientRect()
  const r = row.getBoundingClientRect()
  // The list's padding stays visible around the first and last rows.
  const pad = parseFloat(getComputedStyle(list).paddingTop) || 0
  if (r.top < box.top + pad) list.scrollTop -= box.top + pad - r.top
  else if (r.bottom > box.bottom - pad) list.scrollTop += r.bottom - (box.bottom - pad)
}
