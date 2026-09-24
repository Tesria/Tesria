/**
 * Filtering the page tree as you type (the owner, 2026-09-23). A page shows
 * when its title, or its number in a numbered tree, contains what was
 * typed; its parents show with it, so a match is never out of context.
 * Case and accents are ignored. The same rule as the script an exported
 * site carries (SiteChrome.ThemeScript), so the two filter alike.
 */
export function normalize(text: string): string {
  return text.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase()
}

type Row = { title: string; depth: number; marker?: string | null }

/** The indexes of the rows that match themselves, not counting parents shown for context. */
export function matchingRows(rows: Row[], query: string): Set<number> {
  const q = normalize(query.trim())
  const matches = new Set<number>()
  if (!q) return matches
  rows.forEach((row, i) => {
    if (normalize(row.marker ? `${row.marker} ${row.title}` : row.title).includes(q)) matches.add(i)
  })
  return matches
}

/**
 * The indexes of the rows to show, or null when there is nothing to filter by.
 * With `withChildren`, every page under a match shows too (the owner: type
 * "elements" and see Elements and all its pages).
 */
export function visibleRows(rows: Row[], query: string, withChildren = false): Set<number> | null {
  if (!normalize(query.trim())) return null
  const shown = new Set<number>()
  matchingRows(rows, query).forEach((i) => {
    const row = rows[i]
    shown.add(i)
    // Its parents: walking back, each row shallower than the last one kept.
    let depth = row.depth
    for (let j = i - 1; j >= 0 && depth > 0; j--) {
      if (rows[j].depth < depth) { shown.add(j); depth = rows[j].depth }
    }
    // Its children: the rows after it, until one is no deeper than it.
    if (withChildren) {
      for (let k = i + 1; k < rows.length && rows[k].depth > row.depth; k++) shown.add(k)
    }
  })
  return shown
}

/** The title split around the first match, for highlighting; null when the title itself does not match. */
export function splitMatch(title: string, query: string): [string, string, string] | null {
  const q = query.trim().toLowerCase()
  if (!q) return null
  const at = title.toLowerCase().indexOf(q)
  return at < 0 ? null : [title.slice(0, at), title.slice(at, at + q.length), title.slice(at + q.length)]
}
