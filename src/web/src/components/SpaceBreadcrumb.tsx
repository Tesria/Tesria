import { Link, useMatch, useSearchParams } from 'react-router-dom'
import type { PageTreeNode, Space } from '../api/client'
import { findTreePath } from './PageTree'
import { SpaceIcon } from './SpaceIcon'

type Crumb = { label: string; to?: string }

/** Space Home / Page / Trash / etc. context, one level below the space
 *  action bar — not sticky, just the first thing in the scrolling content,
 *  same place a page's h1 used to be the only wayfinding on offer. */
export function SpaceBreadcrumb({ space, tree }: { space: Space; tree: PageTreeNode[] }) {
  const [searchParams] = useSearchParams()
  const matchPageView = useMatch('/spaces/:key/pages/:pageId')
  const matchPageEdit = useMatch('/spaces/:key/pages/:pageId/edit')
  const matchNew = useMatch('/spaces/:key/new')
  const matchSettings = useMatch('/spaces/:key/settings')
  const matchPermissions = useMatch('/spaces/:key/permissions')
  const matchWebhooks = useMatch('/spaces/:key/webhooks')
  const matchTrash = useMatch('/spaces/:key/trash')

  const pageId = matchPageView?.params.pageId ?? matchPageEdit?.params.pageId
  const crumbs: Crumb[] = [{ label: space.name, to: `/spaces/${space.key}` }]

  if (pageId) {
    const path = findTreePath(tree, pageId)
    path?.forEach((node, i) => {
      const isLast = i === path.length - 1
      crumbs.push({ label: node.title, to: isLast ? undefined : `/spaces/${space.key}/pages/${node.id}` })
    })
  } else if (matchNew) {
    const parentId = searchParams.get('parent')
    const parentPath = parentId ? findTreePath(tree, parentId) : null
    parentPath?.forEach((node) => {
      crumbs.push({ label: node.title, to: `/spaces/${space.key}/pages/${node.id}` })
    })
    crumbs.push({ label: 'New page' })
  } else if (matchSettings) {
    crumbs.push({ label: 'Settings' })
  } else if (matchPermissions) {
    crumbs.push({ label: 'Permissions' })
  } else if (matchWebhooks) {
    crumbs.push({ label: 'Webhooks' })
  } else if (matchTrash) {
    crumbs.push({ label: 'Trash' })
  } else {
    // Space landing — the h1 there already says where we are.
    return null
  }

  return (
    <nav className="breadcrumb" aria-label="Breadcrumb">
      {crumbs.map((c, i) => (
        <span key={i} className="breadcrumb__segment">
          {i > 0 && <span className="breadcrumb__sep">/</span>}
          {/* The space's own crumb carries its icon — the one place the icon
              appears while reading a page, so a reader always knows where
              they are without looking at the sidebar. */}
          {i === 0 && <SpaceIcon space={space} size={16} />}
          {c.to ? <Link to={c.to}>{c.label}</Link> : <span className="breadcrumb__current">{c.label}</span>}
        </span>
      ))}
    </nav>
  )
}
