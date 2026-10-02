/**
 * `denied`: the app no longer gives this person a token for the page (they
 * were suspended, signed out, or lost edit rights); `gone`: the page answers
 * 404, which it does both once it is deleted and once this person may no
 * longer see it, on purpose, so as not to say which (T5-010, t7-R03). Both
 * are final; `disconnected` is not, the editor keeps retrying
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
              ? 'This page was deleted, or you no longer have access to it, so your changes here will not be saved. Copy anything you need.'
              : 'Offline: your changes are local until reconnected'}
    </div>
  )
}
