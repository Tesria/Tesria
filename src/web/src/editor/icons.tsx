/**
 * Toolbar icon set — shared by Toolbar.tsx (the sticky edit toolbar) and
 * SelectionBubbleMenu.tsx (the floating selection menu), so both present the
 * same visual language. One consistent style throughout: 24x24 viewBox,
 * 1.8px stroke, round caps/joins, `currentColor` so each icon inherits its
 * button's text color (neutral by default, primary blue when active — see
 * .toolbar__btn in index.css). Deliberately not using an icon font/library:
 * this app has none installed, and a dozen-odd inline SVGs is cheap enough
 * not to warrant a new dependency.
 *
 * Bold/Italic/Underline/Strikethrough are NOT here — a literal styled
 * "B"/"I"/"U"/"S" glyph (ToolbarButton's tb-bold/tb-italic/etc. classes) is
 * the actual standard treatment for these four specifically (Google Docs,
 * Word, Notion all do the same), not a cop-out.
 */
import type { SVGProps } from 'react'

function Icon({ children, ...props }: SVGProps<SVGSVGElement>) {
  return (
    <svg
      width="18"
      height="18"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="1.8"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
      {...props}
    >
      {children}
    </svg>
  )
}

export function InlineCodeIcon() {
  return (
    <Icon>
      <path d="M8.5 8 4 12l4.5 4" />
      <path d="M15.5 8 20 12l-4.5 4" />
    </Icon>
  )
}

export function HighlightIcon() {
  return (
    <Icon>
      {/* Marker body, held at an angle, with a flat felt tip */}
      <path d="M16.5 3.5 20.5 7.5 10 18 5 19 6 14Z" />
      <path d="M9 15 13 19" />
      {/* the highlighted streak left behind */}
      <path d="M3.5 21h6" strokeWidth="2.4" />
    </Icon>
  )
}

export function BulletListIcon() {
  return (
    <Icon>
      <circle cx="4.5" cy="6" r="1.3" fill="currentColor" stroke="none" />
      <circle cx="4.5" cy="12" r="1.3" fill="currentColor" stroke="none" />
      <circle cx="4.5" cy="18" r="1.3" fill="currentColor" stroke="none" />
      <path d="M9 6h11M9 12h11M9 18h11" />
    </Icon>
  )
}

export function OrderedListIcon() {
  return (
    <Icon>
      <path d="M9 6h11M9 12h11M9 18h11" />
      <text x="1.5" y="8.2" fontSize="6.5" fontFamily="inherit" stroke="none" fill="currentColor">1</text>
      <text x="1.5" y="14.2" fontSize="6.5" fontFamily="inherit" stroke="none" fill="currentColor">2</text>
      <text x="1.5" y="20.2" fontSize="6.5" fontFamily="inherit" stroke="none" fill="currentColor">3</text>
    </Icon>
  )
}

export function TaskListIcon() {
  return (
    <Icon>
      <rect x="3" y="4" width="5" height="5" rx="1" />
      <path d="M4.3 6.5 5.3 7.5 6.8 5.8" />
      <path d="M11 6.5h9.5" />
      <rect x="3" y="14" width="5" height="5" rx="1" />
      <path d="M11 16.5h9.5" />
    </Icon>
  )
}

export function BlockquoteIcon() {
  return (
    <Icon>
      <path d="M7.5 7c-2.2 0-3.5 1.6-3.5 4s1.3 3.5 3 3.5c0 2-1 3.2-3 3.5" />
      <path d="M17 7c-2.2 0-3.5 1.6-3.5 4s1.3 3.5 3 3.5c0 2-1 3.2-3 3.5" />
    </Icon>
  )
}

export function CodeBlockIcon() {
  return (
    <Icon>
      <rect x="3" y="4.5" width="18" height="15" rx="2" />
      <path d="M8.5 10 6.5 12l2 2" />
      <path d="M12.5 15h4" />
    </Icon>
  )
}

export function TableIcon() {
  return (
    <Icon>
      <rect x="3" y="4.5" width="18" height="15" rx="1.5" />
      <path d="M3 10h18M3 15.5h18M9.5 4.5v15" />
    </Icon>
  )
}

export function ImageIcon() {
  return (
    <Icon>
      <rect x="3" y="4.5" width="18" height="15" rx="2" />
      <circle cx="8.5" cy="9.5" r="1.6" />
      <path d="M21 15.5 15.5 10 5 20" />
    </Icon>
  )
}

export function AlignLeftIcon() {
  return (
    <Icon>
      <path d="M4 6h16M4 12h10M4 18h13" />
    </Icon>
  )
}

export function AlignCenterIcon() {
  return (
    <Icon>
      <path d="M4 6h16M7 12h10M5.5 18h13" />
    </Icon>
  )
}

export function AlignRightIcon() {
  return (
    <Icon>
      <path d="M4 6h16M10 12h10M7 18h13" />
    </Icon>
  )
}

export function LinkIcon() {
  return (
    <Icon>
      <path d="M9.5 14.5 14.5 9.5" />
      <path d="M11 7l.6-.6a3.5 3.5 0 0 1 5 5l-.6.6" />
      <path d="M13 17l-.6.6a3.5 3.5 0 0 1-5-5l.6-.6" />
    </Icon>
  )
}

export function CommentIcon() {
  return (
    <Icon>
      <path d="M4 5.5h16v10.5H8.5L4 20Z" />
    </Icon>
  )
}

/** Small caret for dropdown triggers (mobile toolbar grouping) — deliberately
 * smaller than the other 18x18 icons so it reads as a suffix, not a peer. */
export function ChevronDownIcon() {
  return (
    <Icon width="11" height="11" strokeWidth="2.2">
      <path d="M5 9l6 6 6-6" />
    </Icon>
  )
}

/* ---- panel (callout) icons ----------------------------------------------
   One per PANEL_TYPES entry in panelExtension.ts, plus a generic block glyph
   for the toolbar trigger. Same 24x24 / 1.8px stroke language as everything
   above, so a panel type reads as "one of these" in the dropdown and keeps
   currentColor tinting from .toolbar__btn. */

export function PanelIcon() {
  return (
    <Icon>
      <rect x="3" y="5" width="18" height="14" rx="2" />
      <path d="M7 5v14" strokeWidth="2.6" />
    </Icon>
  )
}

export function InfoPanelIcon() {
  return (
    <Icon>
      <circle cx="12" cy="12" r="9" />
      <path d="M12 11v5" />
      <path d="M12 7.75v.5" strokeWidth="2.4" />
    </Icon>
  )
}

export function NotePanelIcon() {
  return (
    <Icon>
      <path d="M5 3.5h14v13l-4.5 4.5H5Z" />
      <path d="M19 16.5h-4.5V21" />
      <path d="M8.5 8h7M8.5 12h4" />
    </Icon>
  )
}

export function SuccessPanelIcon() {
  return (
    <Icon>
      <circle cx="12" cy="12" r="9" />
      <path d="m8 12.25 2.75 2.75L16 9.75" />
    </Icon>
  )
}

export function WarningPanelIcon() {
  return (
    <Icon>
      <path d="M12 3.75 21.5 20.25H2.5Z" />
      <path d="M12 10v4.25" />
      <path d="M12 17.25v.5" strokeWidth="2.4" />
    </Icon>
  )
}

export function ErrorPanelIcon() {
  return (
    <Icon>
      <circle cx="12" cy="12" r="9" />
      <path d="m9 9 6 6M15 9l-6 6" />
    </Icon>
  )
}

export function HeadingIcon() {
  return (
    <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <path d="M6 5v14M18 5v14M6 12h12" />
    </svg>
  )
}

export function DividerIcon() {
  return (
    <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" aria-hidden="true">
      <path d="M4 12h16" />
      <path d="M7 6h10M7 18h10" opacity="0.4" />
    </svg>
  )
}

export function PlusIcon() {
  return (
    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round" aria-hidden="true">
      <path d="M12 5v14M5 12h14" />
    </svg>
  )
}

export function TocIcon() {
  return (
    <Icon>
      <path d="M4 6h4M4 12h4M4 18h4" />
      <path d="M11 6h9M13 12h7M13 18h7" />
    </Icon>
  )
}

export function ExpandIcon() {
  return (
    <Icon>
      <path d="m8 10 4 4 4-4" />
      <rect x="3.5" y="4" width="17" height="16" rx="2" />
    </Icon>
  )
}

export function StatusIcon() {
  return (
    <Icon>
      <rect x="3" y="8" width="18" height="8" rx="2" />
      <path d="M7 12h4" />
    </Icon>
  )
}

export function DateIcon() {
  return (
    <Icon>
      <rect x="3.5" y="5" width="17" height="15" rx="2" />
      <path d="M3.5 10h17M8 3v4M16 3v4" />
    </Icon>
  )
}

export function DecisionIcon() {
  return (
    <Icon>
      <circle cx="12" cy="12" r="9" />
      <path d="m8 12.5 2.5 2.5L16 9.5" />
    </Icon>
  )
}

export function LayoutIcon() {
  return (
    <Icon>
      <rect x="3.5" y="4" width="7" height="16" rx="1.5" />
      <rect x="13.5" y="4" width="7" height="16" rx="1.5" />
    </Icon>
  )
}

/** One preset of the layout menu: columns drawn to their relative widths. */
export function LayoutPresetIcon({ widths }: { widths: readonly number[] }) {
  const total = widths.reduce((a, b) => a + b, 0)
  const gap = 1.5
  const inner = 20 - gap * (widths.length - 1)
  let x = 2
  return (
    <Icon>
      {widths.map((w, i) => {
        const width = (w / total) * inner
        const rect = <rect key={i} x={x} y="5" width={width} height="14" rx="1" />
        x += width + gap
        return rect
      })}
    </Icon>
  )
}


/** The "A" with a colour bar under it — the standard text-colour affordance. */
export function TextColorIcon() {
  return (
    <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <path d="M5 15.5 10 5l5 10.5M6.8 12h6.4" />
      <path d="M4 20h16" strokeWidth="3" className="text-color-icon__bar" />
    </svg>
  )
}

export function IndentIcon() {
  return (
    <Icon>
      <path d="M4 6h16M10 12h10M10 18h10" />
      <path d="m3 10 3 2-3 2Z" fill="currentColor" />
    </Icon>
  )
}

export function OutdentIcon() {
  return (
    <Icon>
      <path d="M4 6h16M10 12h10M10 18h10" />
      <path d="m6 10-3 2 3 2Z" fill="currentColor" />
    </Icon>
  )
}

export function ClearFormattingIcon() {
  return (
    <Icon>
      <path d="M7 6h11M13 6 9.5 18" />
      <path d="m15 14 5 5M20 14l-5 5" />
    </Icon>
  )
}

export function ChildrenBlockIcon() {
  return (
    <Icon>
      <path d="M5 5h14M8 10h11M11 15h8M14 20h5" />
      <path d="M5 5v12h9" opacity="0.5" />
    </Icon>
  )
}
