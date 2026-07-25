import { useEffect, useRef, useState, type ReactNode } from 'react'

/** A "⋮" button that reveals a dropdown of secondary actions on click. */
export function OverflowMenu({ children, label = 'More actions' }: { children: ReactNode; label?: string }) {
  const [open, setOpen] = useState(false)
  const ref = useRef<HTMLDivElement>(null)

  useEffect(() => {
    if (!open) return
    function onDocMouseDown(e: MouseEvent) {
      if (ref.current && !ref.current.contains(e.target as Node)) setOpen(false)
    }
    function onKeyDown(e: KeyboardEvent) {
      if (e.key === 'Escape') setOpen(false)
    }
    document.addEventListener('mousedown', onDocMouseDown)
    document.addEventListener('keydown', onKeyDown)
    return () => {
      document.removeEventListener('mousedown', onDocMouseDown)
      document.removeEventListener('keydown', onKeyDown)
    }
  }, [open])

  return (
    <div className="overflow-menu" ref={ref}>
      <button type="button" className="overflow-menu__trigger" title={label} onClick={() => setOpen((v) => !v)}>
        ⋮
      </button>
      {open && <div className="overflow-menu__dropdown">{children}</div>}
    </div>
  )
}
