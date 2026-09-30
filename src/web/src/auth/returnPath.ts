/**
 * A path on this site to go to after signing in, or the fallback. Single
 * sign-on's code step carries it in the address (`/login?sso=code&returnUrl=`),
 * where anyone can write a link, so only a path on this site is followed:
 * the same rule as the server's `IsLocalPath` ("//host" and "/\host" lead
 * off the site, since browsers read a backslash as a slash).
 */
export function localPath(url: string | null | undefined, fallback = '/spaces'): string {
  if (!url || url[0] !== '/') return fallback
  if (url.length > 1 && (url[1] === '/' || url[1] === '\\')) return fallback
  // eslint-disable-next-line no-control-regex
  if (/[\u0000-\u001f\u007f]/.test(url)) return fallback
  return url
}
