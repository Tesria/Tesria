/**
 * The file name a response asks to be saved as, from its
 * Content-Disposition: the RFC 5987 `filename*` (UTF-8, percent-encoded)
 * when there is one, which is how a non-ASCII title arrives, else the plain
 * `filename`. Null when it names none.
 */
export function downloadName(header: string | null): string | null {
  if (!header) return null
  const star = /filename\*\s*=\s*(?:UTF-8|utf-8)''([^;]+)/.exec(header)
  if (star) {
    try { return decodeURIComponent(star[1].trim()) } catch { /* fall back to the plain one */ }
  }
  const plain = /filename\s*=\s*("([^"]*)"|[^;]+)/.exec(header)
  if (!plain) return null
  return (plain[2] ?? plain[1]).trim() || null
}
