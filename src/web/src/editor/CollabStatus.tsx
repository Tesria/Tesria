export type CollabConnection = 'connecting' | 'connected' | 'disconnected'

/**
 * The collaborative session's state, as a small bordered bar. It sits above
 * the title, under the breadcrumb: between the title and the body it read
 * as the first line of the document.
 */
export function CollabStatus({ status }: { status: CollabConnection }) {
  return (
    <div className="collab-status collab-status--bar" role="status">
      <span className={`collab-dot collab-dot--${status}`} />
      {status === 'connected'
        ? 'Live: changes are shared as you type'
        : status === 'connecting'
          ? 'Connecting to collaboration…'
          : 'Offline: your changes are local until reconnected'}
    </div>
  )
}
