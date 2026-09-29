/**
 * Noticing that this tab's session ended somewhere else (t2-024): Sign Out
 * All Other Sessions on another device, or Sign Out in another tab. The
 * open app kept its signed-in shell, and a private page, now asked for
 * anonymously, answered 404 and read "Not found." as if it had been deleted.
 *
 * A 401, or a 404 (which is how a private page answers someone signed out),
 * from anything but the sign-in endpoints themselves is a reason to ask the
 * server who we are. The API client announces it; the auth provider asks,
 * and signs the tab out if the session is gone.
 */
export const SESSION_CHECK_EVENT = 'tesria:session-check'

/** Whether a failed answer to `path` may mean the session has ended. */
export function mayMeanSignedOut(status: number, path: string): boolean {
  if (status !== 401 && status !== 404) return false
  const route = path.split(/[?#]/)[0]
  // The sign-in endpoints answer 401 for a wrong password, and /me is the
  // check itself.
  return route.startsWith('/api/') && !route.startsWith('/api/auth/')
}
