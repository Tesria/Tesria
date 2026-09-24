/**
 * `denied`: the app no longer gives this person a token for the page (they
 * were suspended, signed out, or lost access); `gone`: the page no longer
 * exists. Both are final; `disconnected` is not, the editor keeps retrying
 * (dev-plan 14.3).
 */
export type CollabConnection = 'connecting' | 'connected' | 'disconnected' | 'denied' | 'gone'

/**
 * The collaborative session's state, as a small bordered bar. It sits above
 * the title, under the breadcrumb: between the title and the body it read
 * as the first line of the document.
 */
export function CollabStatus({ status }: { status: CollabConnection }) {
  return (
    <div className="collab-status collab-status--bar" role="status">
      <span className={`collab-dot collab-dot--${status === 'denied' || status === 'gone' ? 'disconnected' : status}`} />
      {status === 'connected'
        ? 'Live: changes are shared as you type'
        : status === 'connecting'
          ? 'Connecting to collaboration…'
          : status === 'denied'
            ? 'You are signed out or can no longer edit this page, so changes here will not be saved. Copy anything you need, then reload.'
            : status === 'gone'
              ? 'This page no longer exists, so your changes here will not be saved. Copy anything you need.'
              : 'Offline: your changes are local until reconnected'}
    </div>
  )
}
