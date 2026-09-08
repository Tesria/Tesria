import { useState, type ReactNode } from 'react'
import { useDismissable } from '../hooks/useDismissable'
import { useEdgeAlign } from '../hooks/useEdgeAlign'
import { ChevronDownIcon } from './icons'

/**
 * A toolbar button that opens a panel beneath itself — the highlight palette
 * and the panel-type list.
 *
 * Distinct from ToolbarDropdown, which looks similar but exists only as the
 * *mobile* collapsed form of a run of buttons and is `display: none` above
 * --bp-mobile. This one is visible at every width, because its contents (a
 * colour grid, a list of panel types) have no flat equivalent to collapse
 * from.
 *
 * Reuses the link popover's edge-alignment: the trigger sits in a flex-wrap
 * toolbar, so it can end up anywhere across the width and a flush-left panel
 * would spill past the viewport (see useEdgeAlign).
 */
export function ToolbarPopover({
  icon,
  title,
  isActive,
  children,
}: {
  icon: ReactNode
  title: string
  isActive: boolean
  /** Receives a `close` callback so a selection inside can dismiss the popover. */
  children: (close: () => void) => ReactNode
}) {
  const [open, setOpen] = useState(false)
  const wrapper = useDismissable<HTMLDivElement>(open, () => setOpen(false))
  const align = useEdgeAlign<HTMLDivElement>(open)

  return (
    <div className="toolbar__popover-anchor" ref={wrapper}>
      <button
        type="button"
        className={isActive ? 'toolbar__btn toolbar-dropdown__trigger is-active' : 'toolbar__btn toolbar-dropdown__trigger'}
        onMouseDown={(e) => e.preventDefault()}
        onClick={() => setOpen((v) => !v)}
        title={title}
        aria-haspopup="true"
        aria-expanded={open}
      >
        {icon}
        <ChevronDownIcon />
      </button>
      {open && (
        <div className="toolbar__popover" ref={align.ref} style={{ left: align.offsetLeft }}>
          {children(() => setOpen(false))}
        </div>
      )}
    </div>
  )
}
