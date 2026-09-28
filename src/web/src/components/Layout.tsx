import { type FormEvent, useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState } from 'react'
import { Link, NavLink, Outlet, useLocation, useNavigate } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'
import { Permission } from '../api/client'
import { NotificationBell } from './NotificationBell'
import { ThemeToggle } from './ThemeToggle'
import { BrandLockup } from './Brand'
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

/* Everything but Spaces: shown beside it in the bar, or in the menu when
   the bar has no room (useTopbarFit below). There is no "More" menu; the
   owner removed it on 2026-09-27. */
/** Any one of these means the Administration area has something in it for you. */
const ADMIN_ENTRY_RIGHTS = [
  Permission.DashboardView, Permission.UsersView, Permission.SpacesManage, Permission.InvitesManage,
  Permission.SecurityView, Permission.BackupsView, Permission.GroupsManage, Permission.AuditView,
  Permission.PermissionsView, Permission.SettingsInstance, Permission.SettingsRegistration,
  Permission.SettingsEmail, Permission.SettingsPublicSpaces, Permission.SecuritySettings,
  Permission.PermissionsEditAdminTier,
]

const SECONDARY_NAV: { to: string; label: string; adminOnly?: boolean; nonAdminOnly?: boolean; permission?: string }[] = [
  // Groups and the audit log live under Admin; API tokens under the profile.
  // Server-enforced too; hiding it just spares members a page of 403s.
  { to: '/admin', label: 'Admin', adminOnly: true },
  // "Create invite links" can be given to people who are not administrators,
  // and the admin area turns those away, so the right has its own page.
  // Administrators reach the same form from the Invites tab, so only those
  // who cannot see the admin area get the link.
  { to: '/invite', label: 'Invite people', permission: Permission.InvitesCreate, nonAdminOnly: true },
]

/**
 * How much of the top bar fits (2026-09-27), measured rather than guessed
 * from the screen width: the brand can be any instance's name, the links
 * depend on the person's rights, and the glass style's controls are larger.
 * An invisible copy of the bar's contents (.topbar__ruler) is measured, and
 * the fullest layout that fits wins:
 *   0  everything in the bar, with your name beside your avatar
 *   1  links and search in the menu (the hamburger)
 *   2  and the theme button in the menu too
 *   3  and the notifications too
 * Nothing in the bar ever shrinks or is cut short to make room.
 */
const SEARCH_MIN = 200
const FIT_SPARE = 12

function useTopbarFit(header: React.RefObject<HTMLElement | null>, ruler: React.RefObject<HTMLElement | null>) {
  const [level, setLevel] = useState(0)
  useLayoutEffect(() => {
    const el = header.current
    const r = ruler.current
    if (!el || !r) return
    const part = (k: string) => r.querySelector<HTMLElement>(`[data-r="${k}"]`)
    const width = (k: string) => part(k)?.getBoundingClientRect().width ?? 0
    const px = (v: string) => parseFloat(v) || 0
    const measure = () => {
      const cs = getComputedStyle(el)
      const room = el.clientWidth - px(cs.paddingLeft) - px(cs.paddingRight)
      const right = part('right')
      const gap = right ? px(getComputedStyle(right).columnGap) : 0
      const navEl = part('nav')
      const nav = width('nav') + (navEl ? px(getComputedStyle(navEl).marginLeft) : 0)
      const burgerEl = part('hamburger')
      const burger = width('hamburger') + (burgerEl ? px(getComputedStyle(burgerEl).marginRight) : 0)
      const brand = width('brand')
      const meEl = part('me')
      const nameGap = meEl ? px(getComputedStyle(meEl).columnGap) : 0
      const me = width('me') || width('signin')
      const meSmall = part('name') ? me - width('name') - nameGap : me
      const theme = width('theme')
      const bell = width('bell')
      const cluster = (items: number[]) => {
        const shown = items.filter((w) => w > 0)
        return shown.reduce((a, b) => a + b, 0) + gap * Math.max(shown.length - 1, 0)
      }
      const searchEl = part('search')
      const search = SEARCH_MIN + (searchEl ? px(getComputedStyle(searchEl).marginLeft) + px(getComputedStyle(searchEl).marginRight) : 0)
      const fits = (used: number) => used + FIT_SPARE <= room
      const next =
        fits(brand + nav + search + cluster([theme, bell, me])) ? 0
        : fits(burger + brand + gap + cluster([theme, bell, meSmall])) ? 1
        : fits(burger + brand + gap + cluster([bell, meSmall])) ? 2
        : 3
      setLevel(next)
    }
    measure()
    const resize = new ResizeObserver(measure)
    resize.observe(el)
    resize.observe(r)
    // The style and theme change the controls' sizes without resizing the bar.
    const attrs = new MutationObserver(measure)
    attrs.observe(document.documentElement, { attributes: true, attributeFilter: ['data-style', 'data-theme'] })
    document.fonts?.ready.then(measure).catch(() => undefined)
    return () => { resize.disconnect(); attrs.disconnect() }
  }, [header, ruler])
  return level
}

/** Authenticated app chrome: top bar + routed content. */
export function Layout() {
  const { user, can } = useAuth()
  const navigate = useNavigate()
  const location = useLocation()
  const [query, setQuery] = useState('')
  // Below --bp-mobile, .topbar__nav + .topbar__search collapse behind this:
  // display: contents on wider viewports keeps them laid out as direct
  // topbar flex children, so nothing changes above the breakpoint.
  const [navOpen, setNavOpen] = useState(false)
  const navRef = useDismissable<HTMLDivElement>(navOpen, () => setNavOpen(false))
  // Sticky bars follow the visual viewport while a phone keyboard is up.
  useVisualViewportOffset()
  // Anyone holding an administration right has somewhere to go under /admin
  // (dev-plan 11.1), which is no longer the same as "is an administrator".
  const administers = ADMIN_ENTRY_RIGHTS.some((p) => can(p))
  const secondaryNav = SECONDARY_NAV.filter((i) =>
    (!i.adminOnly || administers) && (!i.nonAdminOnly || !administers) && (!i.permission || can(i.permission)))
  // The open space's tree, published by SpacePage (see spaceNav.ts). Only the
  // phone menu renders it; wider viewports have the sidebar.
  const [spaceNav, setSpaceNavState] = useState<SpaceNav | null>(null)
  const setSpaceNav = useCallback((nav: SpaceNav | null) => setSpaceNavState(nav), [])
  const spaceNavContext = useMemo(() => ({ nav: spaceNav, setNav: setSpaceNav }), [spaceNav, setSpaceNav])
  const closeNav = () => setNavOpen(false)
  const headerRef = useRef<HTMLElement>(null)
  const rulerRef = useRef<HTMLDivElement>(null)
  const fit = useTopbarFit(headerRef, rulerRef)
  const collapsed = fit >= 1
  // While the menu is open over the page, the page does not scroll (the
  // owner, 2026-09-28): on a phone a swipe in the menu that did not land on
  // its page tree, or reached the tree's end, scrolled the page behind it.
  const menuOver = navOpen && collapsed
  useEffect(() => {
    if (!menuOver) return
    document.documentElement.classList.add('is-menu-locked')
    return () => document.documentElement.classList.remove('is-menu-locked')
  }, [menuOver])
  // Back to a full bar: a menu left open would have nothing in it.
  useEffect(() => { if (!collapsed) setNavOpen(false) }, [collapsed])

  // The glass style's top bar floats at the top of the page and docks into
  // a frosted strip once it scrolls (0.8.1, glass.css). Flat ignores it.
  const [docked, setDocked] = useState(false)
  useEffect(() => {
    const onScroll = () => setDocked(window.scrollY > 4)
    onScroll()
    window.addEventListener('scroll', onScroll, { passive: true })
    return () => window.removeEventListener('scroll', onScroll)
  }, [])

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
      <header ref={headerRef} className={`topbar${docked ? ' is-docked' : ''}${collapsed ? ' is-collapsed' : ''}${collapsed && navOpen ? ' is-menu-open' : ''}`} data-fit={fit}>
        {/* What the bar would hold if nothing were collapsed, laid out the
            same way but invisible, for useTopbarFit to measure. */}
        <div className="topbar__ruler" ref={rulerRef} aria-hidden="true">
          <span className="topbar__hamburger" data-r="hamburger">☰</span>
          <span className="brand" data-r="brand"><BrandLockup /></span>
          <span className="topbar__nav" data-r="nav">
            <span className="topbar__link is-active">Spaces</span>
            {secondaryNav.map((item) => <span key={item.to} className="topbar__link">{item.label}</span>)}
          </span>
          <span className="topbar__search" data-r="search" />
          <span className="topbar__right" data-r="right">
            <span className="theme-toggle" data-r="theme"><svg width="19" height="19" /></span>
            {user ? (
              <>
                <span className="notif__bell" data-r="bell"><svg width="19" height="19" /></span>
                <span className="topbar__me" data-r="me">
                  <Avatar subject={user} size={24} />
                  <span className="muted topbar__username" data-r="name">{user.displayName}</span>
                </span>
              </>
            ) : (
              <span className="btn btn--primary" data-r="signin">Sign In</span>
            )}
          </span>
        </div>
        <button
          type="button"
          className="topbar__hamburger"
          aria-label="Toggle navigation"
          aria-expanded={navOpen}
          onClick={() => setNavOpen((v) => !v)}
        >
          ☰
        </button>
        {/* The instance's brand, or Tesria's until someone sets one (dev-plan 13.1). */}
        <Link to="/spaces" className="brand">
          <BrandLockup />
        </Link>
        <div ref={navRef} className={navOpen ? 'topbar__collapsible is-open' : 'topbar__collapsible'}>
          {/* In the menu only (the panel is a dropdown once the bar has
              collapsed; otherwise this element is display: contents).
              Tapping outside or Escape also closes it; a visible way out
              is for the person who does not know that. */}
          <button type="button" className="topbar__close" aria-label="Close menu" onClick={closeNav}>
            <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor"
                 strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
              <path d="M6 6l12 12M18 6L6 18" />
            </svg>
          </button>
          {fit >= 2 && (
            <div className="topbar__panel-tools">
              <ThemeToggle />
              {user && fit >= 3 && <NotificationBell />}
            </div>
          )}
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
                  + New Page
                </NavLink>
              )}
              {/* readOnly: no reorder pencil and no dragging inside a menu
                  that closes on the first tap: navigation only. */}
              <PageTree tree={spaceNav.tree} spaceKey={spaceNav.space.key} readOnly onNavigate={closeNav} treeStyle={spaceNav.space.treeStyle} />
              {user && (
                <NavLink to={`/spaces/${spaceNav.space.key}/settings`} className="sidebar__trash" onClick={closeNav}>
                  <SettingsIcon /> Space Settings
                </NavLink>
              )}
            </div>
          )}
        </div>
        <div className="topbar__right">
          {fit < 2 && <ThemeToggle />}
          {user ? (
            <>
              {fit < 3 && <NotificationBell />}
              <Link to="/profile" className="topbar__me" title="Your profile">
                <Avatar subject={user} size={24} />
                {fit === 0 && <span className="muted topbar__username">{user.displayName}</span>}
              </Link>
              {/* Sign out lives on the profile page, opposite its heading
                  (the owner, 2026-09-27), not in the top bar. */}
            </>
          ) : (
            // Anonymous reader (dev-plan 5.3): the theme menu works without
            // a session; everything personal is replaced by a way in.
            <Link to="/login" state={{ from: location.pathname }} className="btn btn--primary">
              Sign In
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
