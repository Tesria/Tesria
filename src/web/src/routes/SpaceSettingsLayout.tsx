import { Outlet } from 'react-router-dom'
import { OverflowTabs } from '../components/OverflowTabs'
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
  const base = `/spaces/${space.key}/settings`

  return (
    <div className="page-wrap page-wrap--space-settings">
      <h1>Space Settings</h1>
      <OverflowTabs items={[
        { key: 'details', label: 'Details', to: base, end: true },
        { key: 'permissions', label: 'Permissions', to: `${base}/permissions` },
        { key: 'templates', label: 'Templates', to: `${base}/templates` },
        { key: 'webhooks', label: 'Webhooks', to: `${base}/webhooks` },
        { key: 'trash', label: 'Trash', to: `${base}/trash` },
      ]} />
      <div className="tab-panel">
        <Outlet context={context} />
      </div>
    </div>
  )
}
