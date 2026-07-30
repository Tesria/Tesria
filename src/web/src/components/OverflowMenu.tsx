import { useState, type ReactNode } from 'react'
import { useDismissable } from '../hooks/useDismissable'

/** A "⋮" button that reveals a dropdown of secondary actions on click. */
export function OverflowMenu({ children, label = 'More actions' }: { children: ReactNode; label?: string }) {
  const [open, setOpen] = useState(false)
  const ref = useDismissable<HTMLDivElement>(open, () => setOpen(false))

  return (
    <div className="overflow-menu" ref={ref}>
      <button type="button" className="overflow-menu__trigger" title={label} onClick={() => setOpen((v) => !v)}>
        ⋮
      </button>
      {open && <div className="overflow-menu__dropdown">{children}</div>}
    </div>
  )
}
