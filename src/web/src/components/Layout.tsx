import { type FormEvent, useState } from 'react'
import { Link, NavLink, Outlet, useNavigate } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'
import { NotificationBell } from './NotificationBell'
import { useDismissable } from '../hooks/useDismissable'

const navClass = ({ isActive }: { isActive: boolean }) =>
  isActive ? 'topbar__link is-active' : 'topbar__link'

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
          Tesria
        </Link>
        <div ref={navRef} className={navOpen ? 'topbar__collapsible is-open' : 'topbar__collapsible'}>
          <nav className="topbar__nav">
            <NavLink to="/spaces" className={navClass} onClick={() => setNavOpen(false)}>Spaces</NavLink>
            <NavLink to="/groups" className={navClass} onClick={() => setNavOpen(false)}>Groups</NavLink>
            <NavLink to="/audit" className={navClass} onClick={() => setNavOpen(false)}>Audit</NavLink>
            <NavLink to="/api-tokens" className={navClass} onClick={() => setNavOpen(false)}>API Tokens</NavLink>
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
          <NotificationBell />
          {user && <span className="muted topbar__username">{user.displayName}</span>}
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
