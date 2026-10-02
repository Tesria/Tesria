import { Link, useLocation } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'

/**
 * What an unknown space or page shows (T9-015): a heading, what may have
 * happened, and a way back. It was a red "Not found." box and nothing else.
 *
 * The server answers 404 both for something that does not exist and for
 * something this person may not see (so as not to say which exists), so
 * the words cover both, and someone signed out is offered the sign-in.
 */
export function NotFoundPanel({ what, spaceKey, spaceName }: {
  what: 'space' | 'page'
  /** For a page: the space to go back to, when it could be opened. */
  spaceKey?: string
  spaceName?: string
}) {
  const { user } = useAuth()
  const location = useLocation()
  return (
    <div className="page-wrap not-found">
      <h1>{what === 'space' ? 'Space Not Found' : 'Page Not Found'}</h1>
      <p>
        {what === 'space'
          ? 'There is no space at this address, or you do not have access to it.'
          : 'This page was deleted or moved, or you do not have access to it.'}
      </p>
      {!user && (
        <p className="muted">
          It may exist but not be public.{' '}
          <Link to="/login" state={{ from: location.pathname }}>Sign in</Link> to see it.
        </p>
      )}
      <p className="row-gap">
        {what === 'page' && spaceKey && (
          <Link className="btn btn--primary" to={`/spaces/${encodeURIComponent(spaceKey)}`}>
            Go to {spaceName ?? 'the Space'}
          </Link>
        )}
        <Link className={what === 'page' && spaceKey ? 'btn btn--ghost' : 'btn btn--primary'} to="/spaces">
          All Spaces
        </Link>
      </p>
    </div>
  )
}
