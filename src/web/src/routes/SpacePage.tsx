import { useCallback, useEffect, useState } from 'react'
import { NavLink, Outlet, useMatch, useOutletContext, useParams, useLocation, Link } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'
import { api, type PageTreeNode, type Space } from '../api/client'
import { OverflowMenu } from '../components/OverflowMenu'
import { PageTree } from '../components/PageTree'
import { SpaceBreadcrumb } from '../components/SpaceBreadcrumb'
import { SpaceIcon } from '../components/SpaceIcon'

export type SpaceOutletContext = {
  space: Space
  tree: PageTreeNode[]
  reloadTree: () => void
  /** Settings edits the space it is shown inside, so the shell has to hear about it. */
  onSpaceChanged: (space: Space) => void
}

/** Hook for child routes to reach the current space and refresh its page tree. */
export function useSpaceContext() {
  return useOutletContext<SpaceOutletContext>()
}

export function SpacePage() {
  const { user } = useAuth()
  const location = useLocation()
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
  const matchNew = useMatch('/spaces/:key/new')
  const currentPageId = matchPageView?.params.pageId ?? matchPageEdit?.params.pageId
  const newPageHref = currentPageId
    ? `/spaces/${key}/new?parent=${currentPageId}`
    : `/spaces/${key}/new`
  // On mobile, viewing/editing a page has its own breadcrumb (acting as the
  // title) and its own Edit/+New/⋮ row (PageView.tsx) — this bar would just
  // be a second, redundant header stacked above that one. Desktop is
  // unaffected: this bar is display:none there regardless (see .sidebar).
  const isPageRoute = Boolean(currentPageId)
  // The editor puts its own breadcrumb *below* the formatting toolbar, which
  // runs edge to edge along the top of the editing surface (Confluence does
  // the same). Rendering it here too would stack a second copy above that
  // bar, so the editor routes opt out and render it themselves.
  const isEditorRoute = Boolean(matchPageEdit || matchNew)

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

  if (error) {
    // Anonymous readers get 404 for anything not public (dev-plan 5.1's
    // masking rule), so "not found" and "sign in" are the same message.
    return (
      <div className="page-wrap">
        <p className="alert alert--error">{error}</p>
        {!user && (
          <p className="muted">
            This space may exist but not be public.{' '}
            <Link to="/login" state={{ from: location.pathname }}>Sign in</Link> to see it.
          </p>
        )}
      </div>
    )
  }
  if (!space) return <p className="muted page-wrap">Loading…</p>

  const context: SpaceOutletContext = { space, tree, reloadTree, onSpaceChanged: setSpace }

  return (
    <div className="space-layout">
      {/* Mobile only (hidden >640px) — the sidebar below is always visible
          on desktop, so this bar only needs to exist as a narrow-viewport
          substitute for it. Back-to-space-home navigation lives in the
          breadcrumb now, not here, so this is just a label. */}
      <div className={isPageRoute ? 'space-actionbar space-actionbar--hidden-on-page' : 'space-actionbar'}>
        <span className="space-actionbar__pages">
          <SpaceIcon space={space} size={20} />
          {space.name}
        </span>
        {user && (
          <div className="space-actionbar__actions">
            <NavLink to={newPageHref} className="btn btn--primary btn--sm">
              + New
            </NavLink>
            <OverflowMenu label="Space actions">
              <NavLink to={`/spaces/${space.key}/settings`} className="btn">⚙ Settings</NavLink>
            <NavLink to={`/spaces/${space.key}/permissions`} className="btn">🔒 Permissions</NavLink>
              <NavLink to={`/spaces/${space.key}/webhooks`} className="btn">🪝 Webhooks</NavLink>
              <NavLink to={`/spaces/${space.key}/trash`} className="btn">🗑 Trash</NavLink>
            </OverflowMenu>
          </div>
        )}
      </div>
      <aside className="sidebar">
        <div className="sidebar__head">
          <SpaceIcon space={space} size={32} />
          <div>
            <div className="sidebar__key">
              {space.key}
              {/* Visible to everyone, so nobody edits a public page thinking it is internal. */}
              {space.isPublic && <span className="badge badge--public" title="Readable by anyone on the internet">public</span>}
            </div>
            <div className="sidebar__name">{space.name}</div>
          </div>
        </div>
        {user && (
          <NavLink to={newPageHref} className="btn btn--primary btn--block">
            + New page
          </NavLink>
        )}
        <PageTree tree={tree} spaceKey={space.key} onMoved={reloadTree} readOnly={!user} />
        {user && (<>
        <NavLink
          to={`/spaces/${space.key}/settings`}
          className={({ isActive }) => (isActive ? 'sidebar__trash is-active' : 'sidebar__trash')}
        >
          ⚙ Settings
        </NavLink>
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
        </>)}
      </aside>
      <section className="space-content">
        {!isEditorRoute && <SpaceBreadcrumb space={space} tree={tree} />}
        <Outlet context={context} />
      </section>
    </div>
  )
}
