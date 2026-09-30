import { Link } from 'react-router-dom'
import { api } from '../api/client'
import { PageTree } from '../components/PageTree'
import { SpaceContents } from '../components/SpaceContents'
import { WatchToggle } from '../components/WatchToggle'
import { SpaceIcon } from '../components/SpaceIcon'
import { useSpaceContext } from './SpacePage'
import { useAuth } from '../auth/AuthContext'

/** Shown when a space is open but no page is selected. */
export function SpaceHome() {
  const { space, tree, reloadTree } = useSpaceContext()
  const { user } = useAuth()
  return (
    <>
      {/* The heading row sits in a wrapper as wide as the page area, so the
          watch button can line up with the top bar's right edge (the avatar
          above it) rather than the centered column's (the owner, 2026-09-28). */}
      <div className="space-home__head-wrap">
        <div className="page-wrap row-between space-home__head">
          <h1 className="space-home__title">
            {/* On a phone, the space's icon (the sidebar shows it on a computer). */}
            <span className="space-home__icon"><SpaceIcon space={space} size={32} /></span>
            {space.name}
          </h1>
          {user && (
            <WatchToggle
              watchKey={space.id}
              label="Space"
              fetchStatus={() => api.spaceWatch.status(space.key)}
              watch={() => api.spaceWatch.watch(space.key)}
              unwatch={() => api.spaceWatch.unwatch(space.key)}
            />
          )}
        </div>
      </div>
      <div className="page-wrap space-home__body">
        {space.description && <p className="muted">{space.description}</p>}
        {tree.length === 0 ? (
          <p>
            This space has no pages yet.
            {user && space.canEdit !== false && <>{' '}<Link to={`/spaces/${space.key}/new`}>Create the first one</Link>.</>}
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
              <PageTree tree={tree} spaceKey={space.key} onMoved={reloadTree} readOnly={!user || space.canEdit === false} treeStyle={space.treeStyle} />
            </div>
          </>
        )}
      </div>
    </>
  )
}
