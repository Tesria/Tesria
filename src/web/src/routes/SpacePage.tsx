import { useCallback, useEffect, useState } from 'react'
import { NavLink, Outlet, useMatch, useOutletContext, useParams } from 'react-router-dom'
import { api, type PageTreeNode, type Space } from '../api/client'
import { OverflowMenu } from '../components/OverflowMenu'
import { PageTree } from '../components/PageTree'
import { SpaceBreadcrumb } from '../components/SpaceBreadcrumb'

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
  // "+ New" is contextual, matching Confluence: creating from an open page
  // makes a subpage of it, creating from anywhere else (the space landing,
  // Permissions, Trash, ...) makes a top-level page. Same rule for both the
  // mobile action bar below and the desktop sidebar's own "+ New page".
  const matchPageView = useMatch('/spaces/:key/pages/:pageId')
  const matchPageEdit = useMatch('/spaces/:key/pages/:pageId/edit')
  const currentPageId = matchPageView?.params.pageId ?? matchPageEdit?.params.pageId
  const newPageHref = currentPageId
    ? `/spaces/${key}/new?parent=${currentPageId}`
    : `/spaces/${key}/new`
  // On mobile, viewing/editing a page has its own breadcrumb (acting as the
  // title) and its own Edit/+New/⋮ row (PageView.tsx) — this bar would just
  // be a second, redundant header stacked above that one. Desktop is
  // unaffected: this bar is display:none there regardless (see .sidebar).
  const isPageRoute = Boolean(currentPageId)

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
      {/* Mobile only (hidden >640px) — the sidebar below is always visible
          on desktop, so this bar only needs to exist as a narrow-viewport
          substitute for it. Back-to-space-home navigation lives in the
          breadcrumb now, not here, so this is just a label. */}
      <div className={isPageRoute ? 'space-actionbar space-actionbar--hidden-on-page' : 'space-actionbar'}>
        <span className="space-actionbar__pages">{space.name}</span>
        <div className="space-actionbar__actions">
          <NavLink to={newPageHref} className="btn btn--primary btn--sm">
            + New
          </NavLink>
          <OverflowMenu label="Space actions">
            <NavLink to={`/spaces/${space.key}/permissions`} className="btn">🔒 Permissions</NavLink>
            <NavLink to={`/spaces/${space.key}/webhooks`} className="btn">🪝 Webhooks</NavLink>
            <NavLink to={`/spaces/${space.key}/trash`} className="btn">🗑 Trash</NavLink>
          </OverflowMenu>
        </div>
      </div>
      <aside className="sidebar">
        <div className="sidebar__head">
          <div>
            <div className="sidebar__key">{space.key}</div>
            <div className="sidebar__name">{space.name}</div>
          </div>
        </div>
        <NavLink to={newPageHref} className="btn btn--primary btn--block">
          + New page
        </NavLink>
        <PageTree tree={tree} spaceKey={space.key} onMoved={reloadTree} />
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
        <SpaceBreadcrumb space={space} tree={tree} />
        <Outlet context={context} />
      </section>
    </div>
  )
}
