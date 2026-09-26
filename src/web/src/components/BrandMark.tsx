/**
 * Tesria's mark (the brand kit, docs/brand): a stack of four layers, one per
 * thing Tesria does, top to bottom Write, Keep, Share and Automate, each in
 * its official color. The colors are fixed: never the user's accent. They
 * come from --tesria-* in index.css, which switch shade with the page's
 * light or dark theme.
 *
 * Stroke 2 at small sizes, as the kit says, and 1.8 from 28px up.
 */
export function BrandMark({ size = 20 }: { size?: number }) {
  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill="none"
      strokeWidth={size >= 28 ? 1.8 : 2}
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
    >
      <path d="M4 17.25l8 4 8-4" stroke="var(--tesria-automate)" />
      <path d="M4 13.75l8 4 8-4" stroke="var(--tesria-share)" />
      <path d="M4 10.25l8 4 8-4" stroke="var(--tesria-keep)" />
      <path d="M12 2.75l8 4-8 4-8-4z" stroke="var(--tesria-write)" />
    </svg>
  )
}
