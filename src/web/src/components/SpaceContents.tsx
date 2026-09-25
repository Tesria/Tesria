import { Link } from 'react-router-dom'
import type { PageTreeNode } from '../api/client'
import { treeMarkers, type SpaceTreeStyle } from './treeMarkers'

type Row = { node: PageTreeNode; depth: number; marker: string | null }

/**
 * A space's contents on its home page (2026-09-24: the home page
 * with only a description "looks bare"): each top-level page and the pages
 * directly under it, numbered as the sidebar numbers them. The same list an
 * exported site's front page shows (SiteChrome.Contents), in the same
 * markup, so both read alike. Two levels, so a large space stays one screen
 * of sections rather than a second copy of the tree.
 */
export function SpaceContents({ tree, spaceKey, treeStyle }: {
  tree: PageTreeNode[]; spaceKey: string; treeStyle?: SpaceTreeStyle
}) {
  // Numbered over the whole tree, so "4.2" here is "4.2" in the sidebar.
  const rows: Row[] = []
  const walk = (nodes: PageTreeNode[], depth: number) => {
    for (const node of nodes) {
      rows.push({ node, depth, marker: null })
      walk(node.children, depth + 1)
    }
  }
  walk(tree, 0)
  treeMarkers(rows.map((r) => r.depth), treeStyle).forEach((m, i) => { rows[i].marker = m })
  const markerOf = new Map(rows.map((r) => [r.node.id, r.marker]))

  const link = (node: PageTreeNode, className: string) => (
    <Link className={className} to={`/spaces/${spaceKey}/pages/${node.id}`}>
      {markerOf.get(node.id) && <span className="tree__marker" aria-hidden="true">{markerOf.get(node.id)}</span>}
      {node.emoji && <span className="tree__emoji" aria-hidden="true">{node.emoji}</span>}
      <span>{node.title}</span>
    </Link>
  )

  return (
    <nav className="site-contents" aria-label="Contents">
      <h2 className="site-contents__heading">Contents</h2>
      <ul className="site-contents__sections">
        {tree.map((section) => (
          <li key={section.id} className="site-contents__section">
            {link(section, 'site-contents__title')}
            {section.children.length > 0 && (
              <ul className="site-contents__pages">
                {section.children.map((page) => <li key={page.id}>{link(page, 'site-contents__page')}</li>)}
              </ul>
            )}
          </li>
        ))}
      </ul>
    </nav>
  )
}
