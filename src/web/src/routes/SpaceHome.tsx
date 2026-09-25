import { Link } from 'react-router-dom'
import { api } from '../api/client'
import { PageTree } from '../components/PageTree'
import { SpaceContents } from '../components/SpaceContents'
import { WatchToggle } from '../components/WatchToggle'
import { useSpaceContext } from './SpacePage'
import { useAuth } from '../auth/AuthContext'

/** Shown when a space is open but no page is selected. */
export function SpaceHome() {
  const { space, tree, reloadTree } = useSpaceContext()
  const { user } = useAuth()
  return (
    <div className="page-wrap">
      <div className="row-between">
        <h1>{space.name}</h1>
        {user && (
          <WatchToggle
            watchKey={space.id}
            label="space"
            fetchStatus={() => api.spaceWatch.status(space.key)}
            watch={() => api.spaceWatch.watch(space.key)}
            unwatch={() => api.spaceWatch.unwatch(space.key)}
          />
        )}
      </div>
      {space.description && <p className="muted">{space.description}</p>}
      {tree.length === 0 ? (
        <p>
          This space has no pages yet.
          {user && <>{' '}<Link to={`/spaces/${space.key}/new`}>Create the first one</Link>.</>}
        </p>
      ) : (
        <>
          {/* A computer: the space's sections and their pages (the owner,
              2026-09-24: the home page looked bare), beside the sidebar's
              full tree. */}
          <div className="space-home-contents">
            <SpaceContents tree={tree} spaceKey={space.key} treeStyle={space.treeStyle} />
          </div>
          {/* A phone has no sidebar, and the menu's tree is read-only: here
              the whole tree, where pages can also be reordered. */}
          <div className="space-home-tree">
            <PageTree tree={tree} spaceKey={space.key} onMoved={reloadTree} readOnly={!user} treeStyle={space.treeStyle} />
          </div>
        </>
      )}
    </div>
  )
}
