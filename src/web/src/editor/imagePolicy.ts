/**
 * Whether a picture's address is one this wiki's image rule blocks (dev-plan
 * 14.3). The server's CSP is what actually blocks it; this only lets the
 * editor say why a picture does not show, so it matches hosts exactly the
 * way the server does: an entry starting with a dot also matches its
 * subdomains, on a label boundary, and any other entry must match exactly.
 *
 * Returns the blocked host, or null when the picture may show: images are
 * not restricted, or it is this instance's own file, a data: or blob: URL,
 * or from a listed host.
 */
export function blockedImageHost(src: string, hosts: string[] | null | undefined, here: string): string | null {
  if (!hosts) return null
  let url: URL
  try {
    url = new URL(src, here)
  } catch {
    return null
  }
  if (url.protocol === 'data:' || url.protocol === 'blob:') return null
  if (url.origin === new URL(here).origin) return null
  const host = url.hostname.toLowerCase().replace(/\.$/, '')
  const allowed = hosts.some((entry) => entry.startsWith('.')
    ? host === entry.slice(1) || host.endsWith(entry)
    : host === entry)
  return allowed ? null : host
}
