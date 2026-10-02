/**
 * The hand-off between the API client and the re-authentication dialog
 * (dev-plan 3.5, "sudo mode").
 *
 * When the server answers a request with 403 `reauth_required`, the client
 * calls `requestReauth()` and waits. The dialog, subscribed here, opens and
 * asks for the password; on success it calls `completeReauth()` and the
 * client retries the original request. Canceling rejects it. One pending
 * prompt at a time: several requests failing together share it.
 */
type Pending = { resolve: () => void; reject: (err: unknown) => void; promise: Promise<void> }
/** `reason`: what the server says the action is, for the dialog (t2-017). */
type Listener = (open: boolean, reason?: string) => void

let pending: Pending | null = null
let pendingReason: string | undefined
const listeners = new Set<Listener>()

export function requestReauth(reason?: string): Promise<void> {
  if (pending) return pending.promise
  let resolve!: () => void
  let reject!: (err: unknown) => void
  const promise = new Promise<void>((res, rej) => {
    resolve = res
    reject = rej
  })
  pending = { resolve, reject, promise }
  pendingReason = reason
  listeners.forEach((l) => l(true, reason))
  return promise
}

export function completeReauth() {
  pending?.resolve()
  pending = null
  listeners.forEach((l) => l(false))
}

export function cancelReauth(err: unknown) {
  pending?.reject(err)
  pending = null
  listeners.forEach((l) => l(false))
}

export function subscribeReauth(listener: Listener): () => void {
  listeners.add(listener)
  // A prompt already waiting opens at once: a request can ask before the
  // dialog has mounted, and it would otherwise wait for a prompt that never
  // shows.
  if (pending) listener(true, pendingReason)
  return () => listeners.delete(listener)
}
