import { useCallback, useEffect, useRef, useState, type ReactNode } from 'react'

export type ConfirmRequest = {
  title: string
  /** Lines of explanation. Say what becomes true, not "are you sure".  */
  body: ReactNode
  /** The affirmative button's label. Name the action: "Delete the role". */
  confirmLabel?: string
  /** Colors the affirmative button as destructive. */
  danger?: boolean
}

/**
 * An in-page replacement for `window.confirm`.
 *
 * The native call is not dependable: an embedded browser (the one inside a
 * desktop app, a WebView, a preview pane) may refuse dialogs and simply
 * return false, so the guarded action never runs and nothing appears on
 * screen to explain why. That is the shape of the Resolve bug: a button that
 * does nothing, with no error to go on.
 *
 * Returns `ask`, which resolves true only when the person confirms, and the
 * `dialog` to render somewhere in the page.
 */
export function useConfirm(): { ask: (req: ConfirmRequest) => Promise<boolean>; dialog: ReactNode } {
  const [request, setRequest] = useState<ConfirmRequest | null>(null)
  const decide = useRef<((ok: boolean) => void) | null>(null)

  const close = useCallback((ok: boolean) => {
    decide.current?.(ok)
    decide.current = null
    setRequest(null)
  }, [])

  const ask = useCallback((req: ConfirmRequest) => {
    // Asking again while one is open answers the first "no" rather than
    // leaving its promise unsettled.
    decide.current?.(false)
    setRequest(req)
    return new Promise<boolean>((resolve) => {
      decide.current = resolve
    })
  }, [])

  // Escape cancels, wherever the focus happens to be.
  useEffect(() => {
    if (!request) return
    const onKey = (e: KeyboardEvent) => e.key === 'Escape' && close(false)
    document.addEventListener('keydown', onKey)
    return () => document.removeEventListener('keydown', onKey)
  }, [request, close])

  // A canceled promise is better than one that never settles, so a caller
  // unmounting mid-question gets its answer.
  useEffect(() => () => decide.current?.(false), [])

  const dialog = request ? (
    <div className="recovery-prompt" role="dialog" aria-modal="true" aria-label={request.title}>
      <div className="recovery-prompt__card">
        <h2>{request.title}</h2>
        <div className="confirm__body">{request.body}</div>
        <div className="row-gap">
          {/* Focus starts on Cancel for a destructive question, so a stray
              Return does nothing rather than everything. */}
          <button
            type="button"
            autoFocus={!request.danger}
            className={`btn ${request.danger ? 'btn--danger' : 'btn--primary'}`}
            onClick={() => close(true)}
          >
            {request.confirmLabel ?? 'Confirm'}
          </button>
          <button type="button" autoFocus={request.danger} className="btn btn--ghost" onClick={() => close(false)}>
            Cancel
          </button>
        </div>
      </div>
    </div>
  ) : null

  return { ask, dialog }
}
