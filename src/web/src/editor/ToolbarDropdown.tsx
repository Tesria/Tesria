import { useRef, useState, type ReactNode } from 'react'
import { useDismissable } from '../hooks/useDismissable'
import { ChevronDownIcon } from './icons'

export type ToolbarDropdownOption = {
  key: string
  icon: ReactNode
  label: string
  isActive: boolean
  onSelect: () => void
}

// Matches .toolbar-dropdown__menu's min-width in index.css — used only to
// decide left- vs right-alignment before the menu has rendered, not as a
// layout value itself.
const MENU_WIDTH_PX = 160

/**
 * A toolbar dropdown for a set of mutually exclusive choices — text style
 * and alignment. Shown at every width since the one-row toolbar rebuild:
 * a dropdown is how Confluence presents these too, and it is what keeps the
 * row short enough never to wrap. `showLabel` renders the active choice's
 * name beside its icon (the text-style control reads "Heading 2", not "H2").
 */
export function ToolbarDropdown({ title, options, showLabel = false }: { title: string; options: ToolbarDropdownOption[]; showLabel?: boolean }) {
  const [open, setOpen] = useState(false)
  const [alignRight, setAlignRight] = useState(false)
  const ref = useDismissable<HTMLDivElement>(open, () => setOpen(false))
  const wrapperRef = useRef<HTMLDivElement>(null)
  const active = options.find((o) => o.isActive) ?? options[0]

  function toggleOpen() {
    setOpen((v) => {
      const next = !v
      // A trigger positioned in the toolbar's right half would otherwise
      // open a left-anchored menu straight past the viewport edge (found on
      // the Heading/Alignment triggers, which sit near the toolbar's right
      // side on mobile) — flip to right-anchored whenever there isn't room.
      if (next && wrapperRef.current) {
        const rect = wrapperRef.current.getBoundingClientRect()
        // clientWidth, not innerWidth — see useEdgeAlign.ts for why.
        setAlignRight(rect.left + MENU_WIDTH_PX > document.documentElement.clientWidth - 16)
      }
      return next
    })
  }

  return (
    <div
      className="toolbar-dropdown"
      ref={(el) => {
        ref.current = el
        wrapperRef.current = el
      }}
    >
      <button
        type="button"
        // Never the accent "active" look: one option is always current
        // (left alignment, by default), so the trigger was permanently blue.
        className="toolbar__btn toolbar-dropdown__trigger"
        onMouseDown={(e) => e.preventDefault()}
        onClick={toggleOpen}
        title={title}
      >
        {active.icon}
        {showLabel && <span className="toolbar-dropdown__label">{active.label}</span>}
        <ChevronDownIcon />
      </button>
      {open && (
        <div className={alignRight ? 'toolbar-dropdown__menu toolbar-dropdown__menu--right' : 'toolbar-dropdown__menu'}>
          {options.map((o) => (
            <button
              key={o.key}
              type="button"
              className={o.isActive ? 'toolbar-dropdown__item is-active' : 'toolbar-dropdown__item'}
              onMouseDown={(e) => e.preventDefault()}
              onClick={() => {
                o.onSelect()
                setOpen(false)
              }}
            >
              {o.icon}
              <span className={o.key.startsWith('h') && showLabel ? `toolbar-dropdown__style toolbar-dropdown__style--${o.key}` : undefined}>{o.label}</span>
            </button>
          ))}
        </div>
      )}
    </div>
  )
}
