import { useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState, type CSSProperties } from 'react'
import { NavLink, Outlet, useMatch, useOutletContext, useParams, useLocation, Link } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'
import { api, type PageTreeNode, type Space } from '../api/client'
import { OverflowMenu } from '../components/OverflowMenu'
import { PageTree } from '../components/PageTree'
import { SpaceBreadcrumb } from '../components/SpaceBreadcrumb'
import { SpaceIcon } from '../components/SpaceIcon'
import { WatchToggle } from '../components/WatchToggle'
import { motionReduced } from '../theme'
import { SettingsIcon, SidebarIcon } from '../components/NavIcons'
import { usePublishSpaceNav } from '../components/spaceNav'
import { useTitleSpace } from '../components/DocumentTitle'
import { SidebarResizer, useSidebarWidth } from '../components/SidebarResizer'

/**
 * Whether the space sidebar is hidden (dev-plan 10.5 step 1, the owner's
 * request: more room for the page on phones in landscape, tablets and
 * desktops). A per-device preference, so browser storage, which can be
 * missing or refuse: then the sidebar is simply shown.
 */
const SIDEBAR_KEY = 'tesria-sidebar-collapsed'
function readCollapsed(): boolean {
  try { return localStorage.getItem(SIDEBAR_KEY) === '1' } catch { return false }
}
function writeCollapsed(value: boolean) {
  try {
    if (value) localStorage.setItem(SIDEBAR_KEY, '1')
    else localStorage.removeItem(SIDEBAR_KEY)
  } catch { /* the choice lasts until the page is reloaded */ }
}

/**
 * Hiding and showing the sidebar is animated in the glass style (the owner,
 * 2026-09-28): the panel's top-left corner is where the round show-sidebar
 * button appears, so hiding draws the panel's right and bottom edges in to
 * that corner, leaving a circle the button's size, which bounces; showing
 * opens the panel back out of it, settling with a slight overshoot. Minimal
 * keeps its instant switch, and so does anyone who asks for reduced motion.
 */
function sidebarMotion(): boolean {
  return document.documentElement.getAttribute('data-style') === 'glass'
    && typeof Element.prototype.animate === 'function'
    && !motionReduced()
}
/**
 * The clip that leaves only the button-sized circle at the panel's top-left
 * corner. The right and bottom edges come in while that corner keeps its
 * shape (the owner, 2026-09-28): scaling the panel instead squashed it.
 */
function clipToButton(panel: HTMLElement): string {
  const r = panel.getBoundingClientRect()
  const size = parseFloat(getComputedStyle(document.documentElement).getPropertyValue('--ctl-page')) || 38
  return `inset(0 ${r.width - size}px ${r.height - size}px 0 round ${size / 2}px)`
}
const PANEL_CLIP = 'inset(0 0 0 0 round 18px)'

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
  useTitleSpace(space?.name)
  const [tree, setTree] = useState<PageTreeNode[]>([])
  const [error, setError] = useState<string | null>(null)
  const [collapsed, setCollapsed] = useState(readCollapsed)
  const sidebarRef = useRef<HTMLElement>(null)
  const railButtonRef = useRef<HTMLButtonElement>(null)
  // Which way the toggle just went, for the element that has just appeared
  // to finish the motion; and the shrink, held at its end until the panel is
  // hidden, then let go.
  const motion = useRef<'hide' | 'show' | null>(null)
  const shrink = useRef<Animation | null>(null)
  const [layoutMoving, setLayoutMoving] = useState(false)
  // A layout effect, so a panel shown again is small before it is first painted.
  useLayoutEffect(() => {
    const m = motion.current
    motion.current = null
    shrink.current?.cancel()
    shrink.current = null
    if (m === 'hide') {
      railButtonRef.current?.animate(
        [
          // It takes over from the panel's last circle at the same size.
          { transform: 'scale(1)', opacity: 0.2 },
          { transform: 'scale(1.14)', opacity: 1, offset: 0.45 },
          { transform: 'scale(0.95)', offset: 0.75 },
          { transform: 'scale(1)' },
        ],
        { duration: 440, easing: 'ease-out' },
      )
    }
    if (m === 'show' && sidebarRef.current) {
      const panel = sidebarRef.current
      panel.animate(
        [
          { clipPath: clipToButton(panel), transform: 'none' },
          { clipPath: PANEL_CLIP, transform: 'scale(1.012)', offset: 0.78 },
          { clipPath: PANEL_CLIP, transform: 'none' },
        ],
        { duration: 420, easing: 'cubic-bezier(0.2, 0.8, 0.2, 1)' },
      )
    }
  }, [collapsed])
  const { width: sidebarWidth, set: setSidebarWidth } = useSidebarWidth()
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
  // title) and its own Edit/+New/⋮ row (PageView.tsx): this bar would just
  // be a second, redundant header stacked above that one. Desktop is
  // unaffected: this bar is display:none there regardless (see .sidebar).
  // Includes the new-page editor: every route that renders its own action
  // bar (PageView, PageEditor) hides this one, which is what lets that bar
  // be sticky under the topbar on a phone without two bars fighting for
  // the same slot.
  const isPageRoute = Boolean(currentPageId) || Boolean(matchNew)
  // Any route with a page action bar puts its own breadcrumb *below* that
  // bar: the bar is the top edge of the page surface, and the breadcrumb
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
    let canceled = false
    setSpace(null)
    setError(null)
    api.spaces
      .get(key)
      .then((s) => {
        if (canceled) return
        setSpace(s)
        return api.pages.tree(s.id).then((t) => !canceled && setTree(t))
      })
      .catch((err: unknown) => !canceled && setError(err instanceof Error ? err.message : 'Failed to load space.'))
    return () => {
      canceled = true
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
  const flipSidebar = () => {
    setCollapsed((c) => { writeCollapsed(!c); return !c })
  }
  const toggleSidebar = () => {
    const panel = sidebarRef.current
    if (!collapsed && panel && sidebarMotion()) {
      const a = panel.animate(
        [
          // The contents fade in the last stretch, so the circle hands over
          // to the button, not a sliver of the space's icon.
          { clipPath: PANEL_CLIP, opacity: 1 },
          { opacity: 1, offset: 0.55 },
          { clipPath: clipToButton(panel), opacity: 0.1 },
        ],
        { duration: 260, easing: 'cubic-bezier(0.55, 0, 0.8, 0.2)', fill: 'forwards' },
      )
      shrink.current = a
      a.onfinish = () => {
        motion.current = 'hide'
        // The page's column slides over while the button bounces in.
        setLayoutMoving(true)
        window.setTimeout(() => setLayoutMoving(false), 340)
        flipSidebar()
      }
      return
    }
    if (collapsed && sidebarMotion()) motion.current = 'show'
    flipSidebar()
  }

  return (
    <div
      className={['space-layout', collapsed && 'space-layout--collapsed', layoutMoving && 'is-moving'].filter(Boolean).join(' ')}
      style={{ '--sidebar-width': `${sidebarWidth}px` } as CSSProperties}
    >
      {/* Mobile only (hidden >640px): the sidebar below is always visible
          on desktop, so this bar only needs to exist as a narrow-viewport
          substitute for it. Back-to-space-home navigation lives in the
          breadcrumb now, not here, so this is just a label. */}
      <div className={isPageRoute ? 'space-actionbar space-actionbar--hidden-on-page' : 'space-actionbar'}>
        {/* No name here (the owner, 2026-09-28): the space home shows its
            icon beside the big title just below, and elsewhere the
            breadcrumb names the space. */}
        {user && (
          <div className="space-actionbar__actions">
            <NavLink to={newPageHref} className="btn btn--primary btn--sm">
              + New
            </NavLink>
            <OverflowMenu label="Space Actions">
              {/* Permissions, webhooks and trash are tabs of Settings now,
                  so one entry reaches all four. */}
              {/* On a phone the watch toggle lives here, not beside the title,
                  so the title and the tree sit higher. */}
              <WatchToggle
                watchKey={space.id}
                label="Space"
                fetchStatus={() => api.spaceWatch.status(space.key)}
                watch={() => api.spaceWatch.watch(space.key)}
                unwatch={() => api.spaceWatch.unwatch(space.key)}
              />
              <NavLink to={`/spaces/${space.key}/settings`} className="btn"><SettingsIcon /> Space Settings</NavLink>
            </OverflowMenu>
          </div>
        )}
      </div>
      {/* Three bands: a head and a foot that stay put, and the page tree
          scrolling between them. The sidebar is its own scroll container
          (see .sidebar in index.css) rather than part of the document's, so
          reading a long page no longer carries the space's name, its
          + New page button and its settings off the top of the screen. */}
      {collapsed && (
        <div className="sidebar-rail">
          <button type="button" className="sidebar__toggle" onClick={toggleSidebar} ref={railButtonRef}
            title="Show the Sidebar" aria-label="Show the Sidebar" aria-expanded="false">
            <SidebarIcon />
          </button>
        </div>
      )}
      <aside className="sidebar" hidden={collapsed} ref={sidebarRef}>
        <SidebarResizer width={sidebarWidth} onResize={setSidebarWidth} />
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
            <button type="button" className="sidebar__toggle sidebar__toggle--hide" onClick={toggleSidebar}
              title="Hide the Sidebar" aria-label="Hide the Sidebar" aria-expanded="true">
              <SidebarIcon />
            </button>
          </div>
          {user && (
            <NavLink to={newPageHref} className="btn btn--primary btn--block">
              + New Page
            </NavLink>
          )}
        </div>
        {/* The tree fills the space between head and foot; its own "PAGES"
            heading stays put and only the rows scroll. That split is done in
            CSS (.sidebar .tree-section / .sidebar .tree) rather than with
            props, because PageTree renders the same markup here and in the
            mobile inline copy on the space home. */}
        <PageTree tree={tree} spaceKey={space.key} onMoved={reloadTree} readOnly={!user} treeStyle={space.treeStyle} />
        {user && (
          <div className="sidebar__foot">
            <NavLink
              to={`/spaces/${space.key}/settings`}
              // The same whether or not Space Settings is open (the owner,
              // 2026-09-28): the page's own title and breadcrumb say so.
              className="sidebar__trash"
            >
              <SettingsIcon /> Space Settings
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
