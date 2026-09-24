/**
 * How a space's page tree marks its pages (dev-plan 15.8). The numbers are
 * worked out from the tree as it is drawn, so they follow every move and
 * every new page. The same rule as SiteChrome.TreeMarkers on the server, so
 * an exported site numbers its pages as the wiki does.
 */
export const SpaceTreeStyle = { Plain: 0, Numbered: 1, Bulleted: 2 } as const
export type SpaceTreeStyle = (typeof SpaceTreeStyle)[keyof typeof SpaceTreeStyle]

const BULLETS = ['•', '◦', '▪']

/**
 * The marker for each page, from the pages' depths in tree order: outline
 * numbers (1, 1.1, 1.2, 2), like a numbered table of contents, or a bullet
 * that changes with the level. Null for a plain tree.
 */
export function treeMarkers(depths: number[], style: SpaceTreeStyle | undefined): (string | null)[] {
  if (style !== SpaceTreeStyle.Numbered && style !== SpaceTreeStyle.Bulleted) return depths.map(() => null)
  const counters: number[] = []
  return depths.map((depth) => {
    while (counters.length <= depth) counters.push(0)
    counters[depth] += 1
    counters.length = depth + 1
    return style === SpaceTreeStyle.Numbered ? counters.join('.') : BULLETS[depth % BULLETS.length]
  })
}
