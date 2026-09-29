import { createPortal } from 'react-dom'

type Props = {
  /** Other people with the page open right now, whose unpublished changes Discard would take too. */
  editingNow: string[]
  busy: boolean
  error: string | null
  onKeep: () => void
  onDiscard: () => void
  onStay: () => void
}

/** "Sam", "Sam and Mei", "Sam, Mei and Priya". */
function listNames(names: string[]): string {
  if (names.length <= 1) return names[0] ?? ''
  return `${names.slice(0, -1).join(', ')} and ${names[names.length - 1]}`
}

/**
 * Asked when Close is pressed on a page whose shared draft holds changes that
 * are not published (0.8.2, QA cal-001 and cal-002).
 *
 * Close used to leave at once and say nothing, while the changes stayed in
 * the shared draft: they came back for the next person, who could publish
 * them under their own name without knowing. Now the choice is said out loud.
 * Keep as Draft leaves them for later, where the next person to open the page
 * is told whose they are; Discard puts the draft back to the published page.
 */
export function CloseEditorDialog({ editingNow, busy, error, onKeep, onDiscard, onStay }: Props) {
  return createPortal(
    <div className="recovery-prompt leave-dialog" role="dialog" aria-modal="true" aria-labelledby="close-dialog-title">
      <div className="recovery-prompt__card leave-dialog__card">
        <h2 id="close-dialog-title">Keep Your Unpublished Changes?</h2>
        <p className="muted">
          This page has changes that are not published. Keep them in the draft to finish later, or discard them and
          go back to the page as it is published.
        </p>
        {editingNow.length > 0 && (
          <p className="muted">
            {listNames(editingNow)} {editingNow.length === 1 ? 'is' : 'are'} editing this page now. Discarding also
            removes their unpublished changes.
          </p>
        )}
        {error && <p className="alert alert--error">{error}</p>}
        <div className="leave-dialog__actions">
          <button type="button" className="btn btn--primary" onClick={onKeep} disabled={busy} autoFocus>
            Keep as Draft
          </button>
          <button type="button" className="btn btn--danger" onClick={onDiscard} disabled={busy}>
            {busy ? 'Discarding…' : 'Discard'}
          </button>
          <button type="button" className="btn btn--ghost" onClick={onStay} disabled={busy}>Stay in the Editor</button>
        </div>
      </div>
    </div>,
    document.body,
  )
}
