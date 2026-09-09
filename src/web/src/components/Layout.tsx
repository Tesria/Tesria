import { type FormEvent, useState } from 'react'
import { Link, NavLink, Outlet, useLocation, useNavigate } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'
import { NotificationBell } from './NotificationBell'
import { ThemeToggle } from './ThemeToggle'
import { BrandMark } from './BrandMark'
import { useDismissable } from '../hooks/useDismissable'

const navClass = ({ isActive }: { isActive: boolean }) =>
  isActive ? 'topbar__link is-active' : 'topbar__link'

/* Everything but Spaces. Rendered twice, deliberately — once as flat links
   (desktop, and the ≤--bp-mobile column) and once inside the More menu
   (--bp-mobile..--bp-tablet) — with CSS choosing which is visible. That is
   the same trick the editor toolbar uses for its heading/list/alignment
   groups (.toolbar__flat vs .toolbar-dropdown), not an accident. */
const SECONDARY_NAV: { to: string; label: string }[] = [
  { to: '/groups', label: 'Groups' },
  { to: '/audit', label: 'Audit' },
  { to: '/api-tokens', label: 'API Tokens' },
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
function MoreMenu({ onNavigate }: { onNavigate: () => void }) {
  const [open, setOpen] = useState(false)
  const ref = useDismissable<HTMLDivElement>(open, () => setOpen(false))
  const { pathname } = useLocation()
  const active = SECONDARY_NAV.some((item) => pathname.startsWith(item.to))
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
          {SECONDARY_NAV.map((item) => (
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
  const [query, setQuery] = useState('')
  // Below --bp-mobile, .topbar__nav + .topbar__search collapse behind this —
  // display: contents on wider viewports keeps them laid out as direct
  // topbar flex children, so nothing changes above the breakpoint.
  const [navOpen, setNavOpen] = useState(false)
  const navRef = useDismissable<HTMLDivElement>(navOpen, () => setNavOpen(false))

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
          <nav className="topbar__nav">
            <NavLink to="/spaces" className={navClass} onClick={() => setNavOpen(false)}>Spaces</NavLink>
            {SECONDARY_NAV.map((item) => (
              <NavLink
                key={item.to}
                to={item.to}
                className={({ isActive }) => `${navClass({ isActive })} topbar__link--secondary`}
                onClick={() => setNavOpen(false)}
              >
                {item.label}
              </NavLink>
            ))}
            <MoreMenu onNavigate={() => setNavOpen(false)} />
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
        </div>
        <div className="topbar__right">
          <ThemeToggle />
          <NotificationBell />
          {user && (
            <Link to="/profile" className="muted topbar__username" title="Your profile">
              {user.displayName}
            </Link>
          )}
          <button type="button" className="btn btn--ghost" onClick={onLogout}>
            Sign out
          </button>
        </div>
      </header>
      <main className="content">
        <Outlet />
      </main>
    </div>
  )
}
