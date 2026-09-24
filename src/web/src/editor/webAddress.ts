/**
 * An address typed without a scheme is a web address, as in the link dialog.
 * Shared by Smart link and Embed, which used to disagree: Embed refused
 * "youtube.com/watch?v=…" with "Enter a full web address." (2026-09-23).
 */
export function normalizeWebAddress(value: string): string {
  const v = value.trim()
  if (!v) return ''
  return /^[a-z][a-z0-9+.-]*:/i.test(v) ? v : `https://${v}`
}
