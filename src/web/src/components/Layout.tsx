import { type FormEvent, useState } from 'react'
import { Link, Outlet, useNavigate } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'

/** Authenticated app chrome: top bar + routed content. */
export function Layout() {
  const { user, logout } = useAuth()
  const navigate = useNavigate()
  const [query, setQuery] = useState('')

  async function onLogout() {
    await logout()
    navigate('/login')
  }

  function onSearch(e: FormEvent) {
    e.preventDefault()
    if (query.trim()) navigate(`/search?q=${encodeURIComponent(query.trim())}`)
  }

  return (
    <div className="app">
      <header className="topbar">
        <Link to="/spaces" className="brand">
          ConfluenceClone
        </Link>
        <form className="topbar__search" onSubmit={onSearch}>
          <input
            type="search"
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            placeholder="Search pages…"
            aria-label="Search pages"
          />
        </form>
        <div className="topbar__right">
          {user && <span className="muted">{user.displayName}</span>}
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
