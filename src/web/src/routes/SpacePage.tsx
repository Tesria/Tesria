import { useCallback, useEffect, useState } from 'react'
import { NavLink, Outlet, useOutletContext, useParams } from 'react-router-dom'
import { api, type PageTreeNode, type Space } from '../api/client'

export type SpaceOutletContext = {
  space: Space
  tree: PageTreeNode[]
  reloadTree: () => void
}

/** Hook for child routes to reach the current space and refresh its page tree. */
export function useSpaceContext() {
  return useOutletContext<SpaceOutletContext>()
}

export function SpacePage() {
  const { key = '' } = useParams()
  const [space, setSpace] = useState<Space | null>(null)
  const [tree, setTree] = useState<PageTreeNode[]>([])
  const [error, setError] = useState<string | null>(null)

  const reloadTree = useCallback(() => {
    if (!space) return
    api.pages.tree(space.id).then(setTree).catch(() => {})
  }, [space])

  useEffect(() => {
    let cancelled = false
    setSpace(null)
    setError(null)
    api.spaces
      .get(key)
      .then((s) => {
        if (cancelled) return
        setSpace(s)
        return api.pages.tree(s.id).then((t) => !cancelled && setTree(t))
      })
      .catch((err: unknown) => !cancelled && setError(err instanceof Error ? err.message : 'Failed to load space.'))
    return () => {
      cancelled = true
    }
  }, [key])

  if (error) return <p className="alert alert--error">{error}</p>
  if (!space) return <p className="muted page-wrap">Loading…</p>

  const context: SpaceOutletContext = { space, tree, reloadTree }

  return (
    <div className="space-layout">
      <aside className="sidebar">
        <div className="sidebar__head">
          <div>
            <div className="sidebar__key">{space.key}</div>
            <div className="sidebar__name">{space.name}</div>
          </div>
        </div>
        <NavLink to={`/spaces/${space.key}/new`} className="btn btn--primary btn--block">
          + New page
        </NavLink>
        <nav className="tree">
          {tree.length === 0 && <p className="muted small">No pages yet.</p>}
          {tree.map((node) => (
            <TreeItem key={node.id} node={node} spaceKey={space.key} depth={0} />
          ))}
        </nav>
        <NavLink
          to={`/spaces/${space.key}/permissions`}
          className={({ isActive }) => (isActive ? 'sidebar__trash is-active' : 'sidebar__trash')}
        >
          🔒 Permissions
        </NavLink>
        <NavLink
          to={`/spaces/${space.key}/webhooks`}
          className={({ isActive }) => (isActive ? 'sidebar__trash is-active' : 'sidebar__trash')}
        >
          🪝 Webhooks
        </NavLink>
        <NavLink
          to={`/spaces/${space.key}/trash`}
          className={({ isActive }) => (isActive ? 'sidebar__trash is-active' : 'sidebar__trash')}
        >
          🗑 Trash
        </NavLink>
      </aside>
      <section className="space-content">
        <Outlet context={context} />
      </section>
    </div>
  )
}

function TreeItem({ node, spaceKey, depth }: { node: PageTreeNode; spaceKey: string; depth: number }) {
  return (
    <div className="tree__item">
      <NavLink
        to={`/spaces/${spaceKey}/pages/${node.id}`}
        className={({ isActive }) => (isActive ? 'tree__link is-active' : 'tree__link')}
        style={{ paddingLeft: 8 + depth * 14 }}
      >
        {node.title}
      </NavLink>
      {node.children.map((child) => (
        <TreeItem key={child.id} node={child} spaceKey={spaceKey} depth={depth + 1} />
      ))}
    </div>
  )
}
