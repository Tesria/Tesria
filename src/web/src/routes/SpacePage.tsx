import { useCallback, useEffect, useMemo, useState } from 'react'
import { NavLink, Outlet, useMatch, useOutletContext, useParams, useLocation, Link } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'
import { api, type PageTreeNode, type Space } from '../api/client'
import { OverflowMenu } from '../components/OverflowMenu'
import { PageTree } from '../components/PageTree'
import { SpaceBreadcrumb } from '../components/SpaceBreadcrumb'
import { SpaceIcon } from '../components/SpaceIcon'
import { SettingsIcon } from '../components/NavIcons'
import { usePublishSpaceNav } from '../components/spaceNav'

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
  // Any route with a page action bar puts its own breadcrumb *below* that
  // bar — the bar is the top edge of the page surface, and the breadcrumb
  // belongs with the content (Confluence does the same). Rendering it here
  // too would stack a second copy above the bar, so those routes opt out and
  // render it themselves: PageEditor for edit/new, PageView for reading. The
  // rest (settings, permissions, webhooks, trash) have no action bar, so the
  // breadcrumb is already the first thing on the page and this still owns it.
  const rendersOwnBreadcrumb = Boolean(matchPageView || matchPageEdit || matchNew)

  const reloadTree = useCallback(() => {
    if (!space) return
    api.pages.tree(space.id).then(setTree).catch(() => {})
  }, [space])

  // Hand the tree to the app shell for the phone menu (spaceNav.ts). Memoised
  // so the shell's effect runs when the space or tree changes, not on every
  // render of this route.
  usePublishSpaceNav(useMemo(
    () => (space ? { space, tree, newPageHref } : null),
    [space, tree, newPageHref],
  ))

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
              {/* Permissions, webhooks and trash are tabs of Settings now,
                  so one entry reaches all four. */}
              <NavLink to={`/spaces/${space.key}/settings`} className="btn"><SettingsIcon /> Space settings</NavLink>
            </OverflowMenu>
          </div>
        )}
      </div>
      {/* Three bands: a head and a foot that stay put, and the page tree
          scrolling between them. The sidebar is its own scroll container
          (see .sidebar in index.css) rather than part of the document's, so
          reading a long page no longer carries the space's name, its
          + New page button and its settings off the top of the screen. */}
      <aside className="sidebar">
        <div className="sidebar__top">
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
        </div>
        {/* The tree fills the space between head and foot; its own "PAGES"
            heading stays put and only the rows scroll. That split is done in
            CSS (.sidebar .tree-section / .sidebar .tree) rather than with
            props, because PageTree renders the same markup here and in the
            mobile inline copy on the space home. */}
        <PageTree tree={tree} spaceKey={space.key} onMoved={reloadTree} readOnly={!user} />
        {user && (
          <div className="sidebar__foot">
            <NavLink
              to={`/spaces/${space.key}/settings`}
              className={({ isActive }) => (isActive ? 'sidebar__trash is-active' : 'sidebar__trash')}
            >
              <SettingsIcon /> Space settings
            </NavLink>
          </div>
        )}
      </aside>
      <section className="space-content">
        {!rendersOwnBreadcrumb && <SpaceBreadcrumb space={space} tree={tree} />}
        <Outlet context={context} />
      </section>
    </div>
  )
}
