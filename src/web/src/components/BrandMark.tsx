/**
 * Tesria's logo: a stack of layers. Drawn in the same language as the rest of the
 * app's icons (24x24 viewBox, 1.8 stroke, round caps and joins) because the
 * brand mark already used exactly that: no adaptation needed.
 *
 * `currentColor`, so the color is the caller's business. In the topbar that
 * resolves to `--primary` (see `.brand__mark`), which means the mark follows
 * both the light/dark theme and the chosen accent for free.
 */
export function BrandMark({ size = 20 }: { size?: number }) {
  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="1.8"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
    >
      <path d="M12 3l8 4.5-8 4.5-8-4.5L12 3z" />
      <path d="M4 12l8 4.5 8-4.5M4 16.5L12 21l8-4.5" />
    </svg>
  )
}
