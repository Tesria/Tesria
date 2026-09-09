import { Link } from 'react-router-dom'
import { api } from '../api/client'
import { PageTree } from '../components/PageTree'
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
          <p className="muted">Select a page from the tree, or create a new one.</p>
          {/* Desktop already shows this permanently in the sidebar — this
              copy exists only so mobile (where that sidebar is hidden) has
              somewhere to browse pages that isn't hidden behind a toggle. */}
          <div className="space-home-tree">
            <PageTree tree={tree} spaceKey={space.key} onMoved={reloadTree} readOnly={!user} />
          </div>
        </>
      )}
    </div>
  )
}
