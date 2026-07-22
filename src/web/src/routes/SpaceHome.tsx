import { Link } from 'react-router-dom'
import { useSpaceContext } from './SpacePage'

/** Shown when a space is open but no page is selected. */
export function SpaceHome() {
  const { space, tree } = useSpaceContext()
  return (
    <div className="page-wrap">
      <h1>{space.name}</h1>
      {space.description && <p className="muted">{space.description}</p>}
      {tree.length === 0 ? (
        <p>
          This space has no pages yet.{' '}
          <Link to={`/spaces/${space.key}/new`}>Create the first one</Link>.
        </p>
      ) : (
        <p className="muted">Select a page from the tree, or create a new one.</p>
      )}
    </div>
  )
}
