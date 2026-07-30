import { NavLink } from 'react-router-dom'
import type { PageTreeNode } from '../api/client'

/** The chain of nodes from a root page down to (and including) `pageId`, or
 *  null if it isn't in this tree — e.g. a trashed page, or the tree hasn't
 *  loaded yet. Used to build the breadcrumb: the tree is the only place
 *  parent/child relationships live on the client, so this walks it rather
 *  than asking the server for an ancestor list. */
export function findTreePath(tree: PageTreeNode[], pageId: string): PageTreeNode[] | null {
  for (const node of tree) {
    if (node.id === pageId) return [node]
    const childPath = findTreePath(node.children, pageId)
    if (childPath) return [node, ...childPath]
  }
  return null
}

/** The recursive page list for a space — shared by the desktop sidebar and
 *  the mobile inline tree on the space landing page (SpaceHome). */
export function PageTree({
  tree,
  spaceKey,
  onNavigate,
}: {
  tree: PageTreeNode[]
  spaceKey: string
  onNavigate?: () => void
}) {
  return (
    <div className="tree-section">
      <div className="tree-section__heading">📑 Pages</div>
      {tree.length === 0 ? (
        <p className="muted small">No pages yet.</p>
      ) : (
        <nav className="tree">
          {tree.map((node) => (
            <TreeItem key={node.id} node={node} spaceKey={spaceKey} depth={0} onNavigate={onNavigate} />
          ))}
        </nav>
      )}
    </div>
  )
}

function TreeItem({
  node,
  spaceKey,
  depth,
  onNavigate,
}: {
  node: PageTreeNode
  spaceKey: string
  depth: number
  onNavigate?: () => void
}) {
  return (
    <div className="tree__item">
      <NavLink
        to={`/spaces/${spaceKey}/pages/${node.id}`}
        className={({ isActive }) => (isActive ? 'tree__link is-active' : 'tree__link')}
        style={{ paddingLeft: 8 + depth * 14 }}
        onClick={onNavigate}
      >
        {node.title}
      </NavLink>
      {node.children.map((child) => (
        <TreeItem key={child.id} node={child} spaceKey={spaceKey} depth={depth + 1} onNavigate={onNavigate} />
      ))}
    </div>
  )
}
