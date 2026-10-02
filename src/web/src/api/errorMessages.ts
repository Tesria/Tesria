// What a failed request says when the server gave no reason of its own.
// Kept apart from client.ts so the wording can be tested without a browser.

/**
 * A request that never reached Tesria. The browser's own words for this are
 * "Failed to fetch" (Chromium), "Load failed" (Safari) and "NetworkError
 * when attempting to fetch resource." (Firefox), and they reached people
 * as they were (t4-024, T5-011).
 */
export const NETWORK_ERROR_MESSAGE = 'Tesria could not be reached. Check your connection and try again.'

/**
 * Whether `err`, thrown by `fetch` itself, means the request did not get an
 * answer: offline, the server down, the connection cut. `fetch` rejects with
 * a TypeError for all of those. An abort is somebody's choice, not a
 * failure, and is left alone.
 */
export function isNetworkFailure(err: unknown): boolean {
  if (err instanceof DOMException && err.name === 'AbortError') return false
  return err instanceof TypeError
}

/** "a minute", "3 minutes", "an hour": a wait in the words a person would use. */
export function waitWords(seconds: number): string {
  const minutes = Math.max(1, Math.ceil(seconds / 60))
  if (minutes === 1) return 'a minute'
  if (minutes === 60) return 'an hour'
  if (minutes > 60) {
    const hours = Math.ceil(minutes / 60)
    return `${hours} hours`
  }
  return `${minutes} minutes`
}

/**
 * The message for a refusal whose body said nothing a person can read.
 * `retryAfterSeconds` is from a 429's body, when it has one.
 */
export function fallbackMessage(status: number, retryAfterSeconds?: number): string {
  if (status === 401) return 'You need to sign in.'
  if (status === 403) return 'You do not have permission to do that.'
  if (status === 404) return 'Not found.'
  if (status === 409) return 'That clashed with another change. Reload the page and try again.'
  if (status === 413) return 'That is too large to send to Tesria.'
  if (status === 415) return 'Tesria could not read that request.'
  if (status === 429) {
    // The limiter said how long; "a minute" was the guess when the wait
    // could be an hour (t6-016).
    return retryAfterSeconds && retryAfterSeconds > 0
      ? `Too many attempts. Try again in ${waitWords(retryAfterSeconds)}.`
      : 'Too many attempts. Wait a minute and try again.'
  }
  // A proxy in front of Tesria answering for it: the app is restarting, or
  // not running.
  if (status === 502 || status === 503 || status === 504)
    return 'Tesria is not answering right now. It may be restarting: try again in a minute.'
  // "Request failed (500)." told nobody anything (T1-023, T1-025, t2-026, t6-014).
  if (status >= 500)
    return 'Something went wrong in Tesria, so that was not done. Try again; if it keeps happening, tell your administrator.'
  return 'Tesria could not do that. Check what you entered and try again.'
}
