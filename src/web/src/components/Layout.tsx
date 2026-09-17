import { type FormEvent, useCallback, useMemo, useState } from 'react'
import { Link, NavLink, Outlet, useLocation, useNavigate } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'
import { UserRole } from '../api/client'
import { NotificationBell } from './NotificationBell'
import { ThemeToggle } from './ThemeToggle'
import { BrandMark } from './BrandMark'
import { Avatar } from './Avatar'
import { RecoveryCodesPrompt } from './RecoveryCodesPrompt'
import { ReauthDialog } from './ReauthDialog'
import { useDismissable } from '../hooks/useDismissable'
import { useVisualViewportOffset } from '../hooks/useVisualViewportOffset'
import { PageTree } from './PageTree'
import { SpaceIcon } from './SpaceIcon'
import { SettingsIcon } from './NavIcons'
import { SpaceNavContext, type SpaceNav } from './spaceNav'

const navClass = ({ isActive }: { isActive: boolean }) =>
  isActive ? 'topbar__link is-active' : 'topbar__link'

/* Everything but Spaces. Rendered twice, deliberately — once as flat links
   (desktop, and the ≤--bp-mobile column) and once inside the More menu
   (--bp-mobile..--bp-tablet) — with CSS choosing which is visible. That is
   the same trick the editor toolbar uses for its heading/list/alignment
   groups (.toolbar__flat vs .toolbar-dropdown), not an accident. */
const SECONDARY_NAV: { to: string; label: string; adminOnly?: boolean }[] = [
  // Groups and the audit log live under Admin; API tokens under the profile.
  // Server-enforced too; hiding it just spares members a page of 403s.
  { to: '/admin', label: 'Admin', adminOnly: true },
]

function ChevronIcon() {
  return (
    <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor"
         strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <path d="m6 9 6 6 6-6" />
    </svg>
  )
}

/**
 * The middle tier's overflow. Between --bp-mobile and --bp-tablet the bar
 * can't hold four links, a search box and the right-hand cluster, so the
 * secondary links live here. The trigger reads as active when the current
 * route is one of them, so "where am I" survives the collapse.
 */
function MoreMenu({ onNavigate, items }: { onNavigate: () => void; items: typeof SECONDARY_NAV }) {
  const [open, setOpen] = useState(false)
  const ref = useDismissable<HTMLDivElement>(open, () => setOpen(false))
  const { pathname } = useLocation()
  const active = items.some((item) => pathname.startsWith(item.to))
  // A member has nothing secondary to reach; no trigger for an empty menu.
  if (items.length === 0) return null
  return (
    <div className="topbar__more" ref={ref}>
      <button
        type="button"
        className={active ? 'topbar__link topbar__more-trigger is-active' : 'topbar__link topbar__more-trigger'}
        aria-haspopup="true"
        aria-expanded={open}
        onClick={() => setOpen((v) => !v)}
      >
        More <ChevronIcon />
      </button>
      {open && (
        <div className="topbar__more-menu">
          {items.map((item) => (
            <NavLink
              key={item.to}
              to={item.to}
              className={navClass}
              onClick={() => {
                setOpen(false)
                onNavigate()
              }}
            >
              {item.label}
            </NavLink>
          ))}
        </div>
      )}
    </div>
  )
}

/** Authenticated app chrome: top bar + routed content. */
export function Layout() {
  const { user, logout } = useAuth()
  const navigate = useNavigate()
  const location = useLocation()
  const [query, setQuery] = useState('')
  // Below --bp-mobile, .topbar__nav + .topbar__search collapse behind this —
  // display: contents on wider viewports keeps them laid out as direct
  // topbar flex children, so nothing changes above the breakpoint.
  const [navOpen, setNavOpen] = useState(false)
  const navRef = useDismissable<HTMLDivElement>(navOpen, () => setNavOpen(false))
  // Sticky bars follow the visual viewport while a phone keyboard is up.
  useVisualViewportOffset()
  const secondaryNav = SECONDARY_NAV.filter((i) => !i.adminOnly || user?.role === UserRole.Admin)
  // The open space's tree, published by SpacePage (see spaceNav.ts). Only the
  // phone menu renders it; wider viewports have the sidebar.
  const [spaceNav, setSpaceNavState] = useState<SpaceNav | null>(null)
  const setSpaceNav = useCallback((nav: SpaceNav | null) => setSpaceNavState(nav), [])
  const spaceNavContext = useMemo(() => ({ nav: spaceNav, setNav: setSpaceNav }), [spaceNav, setSpaceNav])
  const closeNav = () => setNavOpen(false)

  async function onLogout() {
    await logout()
    navigate('/login')
  }

  function onSearch(e: FormEvent) {
    e.preventDefault()
    if (query.trim()) {
      navigate(`/search?q=${encodeURIComponent(query.trim())}`)
      setNavOpen(false)
    }
  }

  return (
    <SpaceNavContext.Provider value={spaceNavContext}>
    <div className="app">
      <header className="topbar">
        <button
          type="button"
          className="topbar__hamburger"
          aria-label="Toggle navigation"
          aria-expanded={navOpen}
          onClick={() => setNavOpen((v) => !v)}
        >
          ☰
        </button>
        <Link to="/spaces" className="brand">
          <span className="brand__mark" aria-hidden="true"><BrandMark /></span>
          <span className="brand__word">Tesria</span>
        </Link>
        <div ref={navRef} className={navOpen ? 'topbar__collapsible is-open' : 'topbar__collapsible'}>
          {/* Phone only (the panel is a dropdown there; above the breakpoint
              this element is display: contents and the button is hidden).
              Tapping outside or Escape also closes it; a visible way out
              is for the person who does not know that. */}
          <button type="button" className="topbar__close" aria-label="Close menu" onClick={closeNav}>
            <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor"
                 strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
              <path d="M6 6l12 12M18 6L6 18" />
            </svg>
          </button>
          <nav className="topbar__nav">
            <NavLink to="/spaces" className={navClass} onClick={() => setNavOpen(false)}>Spaces</NavLink>
            {secondaryNav.map((item) => (
              <NavLink
                key={item.to}
                to={item.to}
                className={({ isActive }) => `${navClass({ isActive })} topbar__link--secondary`}
                onClick={() => setNavOpen(false)}
              >
                {item.label}
              </NavLink>
            ))}
            <MoreMenu onNavigate={() => setNavOpen(false)} items={secondaryNav} />
          </nav>
          <form className="topbar__search" onSubmit={onSearch}>
            <input
              type="search"
              value={query}
              onChange={(e) => setQuery(e.target.value)}
              placeholder="Search pages…"
              aria-label="Search pages"
            />
          </form>
          {/* Phone only (hidden by CSS above --bp-mobile): the open space's
              own navigation, so moving between two pages of one space is
              menu → page rather than menu → Spaces → space → page. */}
          {spaceNav && (
            <div className="topbar__space">
              <div className="topbar__space-head">
                <SpaceIcon space={spaceNav.space} size={22} />
                <span className="topbar__space-name">{spaceNav.space.name}</span>
              </div>
              {user && (
                <NavLink to={spaceNav.newPageHref} className="btn btn--primary btn--block" onClick={closeNav}>
                  + New page
                </NavLink>
              )}
              {/* readOnly: no reorder pencil and no dragging inside a menu
                  that closes on the first tap — navigation only. */}
              <PageTree tree={spaceNav.tree} spaceKey={spaceNav.space.key} readOnly onNavigate={closeNav} />
              {user && (
                <NavLink to={`/spaces/${spaceNav.space.key}/settings`} className="sidebar__trash" onClick={closeNav}>
                  <SettingsIcon /> Space settings
                </NavLink>
              )}
            </div>
          )}
        </div>
        <div className="topbar__right">
          <ThemeToggle />
          {user ? (
            <>
              <NotificationBell />
              <Link to="/profile" className="topbar__me" title="Your profile">
                <Avatar subject={user} size={24} />
                <span className="muted topbar__username">{user.displayName}</span>
              </Link>
              <button type="button" className="btn btn--ghost" onClick={onLogout}>
                Sign out
              </button>
            </>
          ) : (
            // Anonymous reader (dev-plan 5.3): the theme menu works without
            // a session; everything personal is replaced by a way in.
            <Link to="/login" state={{ from: location.pathname }} className="btn btn--primary">
              Sign in
            </Link>
          )}
        </div>
      </header>
      <main className="content">
        <Outlet />
      </main>
      {/* Inside the authenticated shell so it follows the user to whichever
          page they land on after signing in, rather than only the one route. */}
      <RecoveryCodesPrompt />
      <ReauthDialog />
    </div>
    </SpaceNavContext.Provider>
  )
}
