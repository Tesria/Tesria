import { NavLink, Outlet } from 'react-router-dom'
import { useSpaceContext } from './SpacePage'

/**
 * The shell for a space's own administration: details and icon, permissions,
 * templates, webhooks, trash.
 *
 * These four were four sidebar entries once, because permissions, webhooks
 * and trash existed before there was a settings page to put them in. That
 * left the sidebar carrying two unrelated jobs (browsing a space's pages,
 * and administering the space) and the second one crowding out the first
 * as the tree grew. They are one section now, reached by one sidebar entry,
 * with the same tabbed shape the admin area uses.
 *
 * The outlet context is re-published so the children still reach the space
 * through `useSpaceContext()`: a nested <Outlet> does not inherit its
 * parent's context automatically.
 */
export function SpaceSettingsLayout() {
  const context = useSpaceContext()
  const { space } = context
  const tab = ({ isActive }: { isActive: boolean }) => (isActive ? 'tab is-active' : 'tab')
  const base = `/spaces/${space.key}/settings`

  return (
    <div className="page-wrap">
      <h1>Space settings</h1>
      <nav className="tabs">
        <NavLink to={base} end className={tab}>Details</NavLink>
        <NavLink to={`${base}/permissions`} className={tab}>Permissions</NavLink>
        <NavLink to={`${base}/templates`} className={tab}>Templates</NavLink>
        <NavLink to={`${base}/webhooks`} className={tab}>Webhooks</NavLink>
        <NavLink to={`${base}/trash`} className={tab}>Trash</NavLink>
      </nav>
      <div className="tab-panel">
        <Outlet context={context} />
      </div>
    </div>
  )
}
