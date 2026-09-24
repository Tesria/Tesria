/**
 * How well a slash-menu item matches what was typed, lower first: an exact
 * title or keyword, then one that starts with it, then a word in the title
 * that does, then anywhere at all. Without the order, "/chart" put Diagram
 * first, because "flowchart" contains "chart" and Diagram comes earlier.
 */
export function matchRank(title: string, keywords: string[] | undefined, query: string): number | null {
  const q = query.toLowerCase()
  const t = title.toLowerCase()
  const words = [t, ...(keywords ?? [])]
  if (words.some((w) => w === q)) return 0
  if (words.some((w) => w.startsWith(q))) return 1
  if (t.split(/[^a-z0-9]+/).some((w) => w.startsWith(q))) return 2
  if (words.some((w) => w.includes(q))) return 3
  return null
}

/** The items that match, best first, keeping the catalog's order within a rank. */
export function rankMatches<T extends { title: string; keywords?: string[] }>(items: T[], query: string): T[] {
  if (!query) return items
  return items
    .map((item, i) => ({ item, i, rank: matchRank(item.title, item.keywords, query) }))
    .filter((m) => m.rank !== null)
    .sort((a, b) => a.rank! - b.rank! || a.i - b.i)
    .map((m) => m.item)
}
