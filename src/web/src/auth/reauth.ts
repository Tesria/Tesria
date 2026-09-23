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
type Listener = (open: boolean) => void

let pending: Pending | null = null
const listeners = new Set<Listener>()

export function requestReauth(): Promise<void> {
  if (pending) return pending.promise
  let resolve!: () => void
  let reject!: (err: unknown) => void
  const promise = new Promise<void>((res, rej) => {
    resolve = res
    reject = rej
  })
  pending = { resolve, reject, promise }
  listeners.forEach((l) => l(true))
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
  return () => listeners.delete(listener)
}
