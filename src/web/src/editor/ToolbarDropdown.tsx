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
 * Mobile-only collapsed form of a run of related toolbar buttons (heading
 * levels, list types, alignment). Always mounted alongside its flat
 * button-row equivalent; index.css's `.toolbar-dropdown`/`--flat` rules pick
 * one or the other via `display: none` at `--bp-mobile`, matching this
 * codebase's existing CSS-only responsive convention (see Layout.tsx's
 * hamburger nav) rather than a JS viewport check.
 */
export function ToolbarDropdown({ title, options }: { title: string; options: ToolbarDropdownOption[] }) {
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
        className={active.isActive ? 'toolbar__btn toolbar-dropdown__trigger is-active' : 'toolbar__btn toolbar-dropdown__trigger'}
        onMouseDown={(e) => e.preventDefault()}
        onClick={toggleOpen}
        title={title}
      >
        {active.icon}
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
              <span>{o.label}</span>
            </button>
          ))}
        </div>
      )}
    </div>
  )
}
