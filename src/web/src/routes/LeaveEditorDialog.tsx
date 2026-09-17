import { createPortal } from 'react-dom'

type Props = {
  /** A brand-new page: leaving discards it, because an unpublished new page is reachable from nowhere. */
  isNew: boolean
  /** Whether edits to an existing page survive leaving (they do in the live, collaborative editor). */
  keepsDraft: boolean
  busy: boolean
  error: string | null
  onStay: () => void
  onLeave: () => void
  onPublish: () => void
}

/**
 * Asked whenever something navigates away from the editor that is not the
 * editor's own Publish/Update or Close: a link in the top bar, the profile
 * avatar, the page tree, the browser's back button, an iPhone swipe-back.
 * Those used to leave instantly, and it was not obvious how to get back.
 */
export function LeaveEditorDialog({ isNew, keepsDraft, busy, error, onStay, onLeave, onPublish }: Props) {
  const leaveLabel = isNew ? 'Discard page' : 'Leave unpublished'
  const detail = isNew
    ? 'This page has not been published yet. Leaving now discards it.'
    : keepsDraft
      ? 'Your changes have not been published. They stay in the draft and will be here the next time you edit.'
      : 'Your changes have not been published. Leaving now loses them.'
  return createPortal(
    <div className="recovery-prompt leave-dialog" role="dialog" aria-modal="true" aria-labelledby="leave-dialog-title">
      <div className="recovery-prompt__card leave-dialog__card">
        <h2 id="leave-dialog-title">You are leaving the editor</h2>
        <p className="muted">{detail}</p>
        {error && <p className="alert alert--error">{error}</p>}
        <div className="leave-dialog__actions">
          <button type="button" className="btn btn--primary" onClick={onPublish} disabled={busy} autoFocus>
            {busy ? 'Publishing…' : isNew ? 'Publish and leave' : 'Update and leave'}
          </button>
          <button type="button" className="btn btn--danger" onClick={onLeave} disabled={busy}>{leaveLabel}</button>
          <button type="button" className="btn btn--ghost" onClick={onStay} disabled={busy}>Stay in the editor</button>
        </div>
      </div>
    </div>,
    document.body,
  )
}
