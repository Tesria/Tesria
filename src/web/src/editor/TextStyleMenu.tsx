import { useRef, useState, type ReactNode } from 'react'
import { useDismissable } from '../hooks/useDismissable'
import { ChevronDownIcon } from './icons'
import { OverflowItems, type OverflowAction } from './OverflowItems'

export type TextStyleOption = {
  key: string
  label: string
  isActive: boolean
  onSelect: () => void
}

// Matches .toolbar-dropdown__menu--text's min-width in index.css: used only
// to decide left- vs right-alignment before the menu has rendered.
const MENU_WIDTH_PX = 240

/**
 * The toolbar's text menu: the first of its two menus. Its trigger reads
 * "Normal text ⌄" where there is room and "Aa ⌄" where there is not (a
 * container query on the editor's row decides, see index.css).
 *
 * It always holds the block styles. Whatever text control has left the row
 * for want of space (marks, colours, indent, alignment, lists) appears
 * beneath them, grouped, so that on a phone this one menu is the whole
 * text kit and the "+" menu stays what its name says: things to insert.
 */
export function TextStyleMenu({ title, styles, overflow, compactLabel }: {
  title: string
  styles: TextStyleOption[]
  overflow: OverflowAction[]
  compactLabel: ReactNode
}) {
  const [open, setOpen] = useState(false)
  const [alignRight, setAlignRight] = useState(false)
  const ref = useDismissable<HTMLDivElement>(open, () => setOpen(false))
  const wrapperRef = useRef<HTMLDivElement>(null)
  const active = styles.find((o) => o.isActive) ?? styles[0]

  function toggleOpen() {
    setOpen((v) => {
      const next = !v
      if (next && wrapperRef.current) {
        const rect = wrapperRef.current.getBoundingClientRect()
        setAlignRight(rect.left + MENU_WIDTH_PX > document.documentElement.clientWidth - 16)
      }
      return next
    })
  }

  return (
    <div
      className="toolbar-dropdown toolbar-dropdown--text"
      ref={(el) => {
        ref.current = el
        wrapperRef.current = el
      }}
    >
      <button
        type="button"
        className="toolbar__btn toolbar-dropdown__trigger toolbar-dropdown__trigger--menu"
        onMouseDown={(e) => e.preventDefault()}
        onClick={toggleOpen}
        title={title}
        aria-haspopup="true"
        aria-expanded={open}
      >
        <span className="toolbar-dropdown__label">{active.label}</span>
        <span className="toolbar-dropdown__compact" aria-hidden="true">{compactLabel}</span>
        <ChevronDownIcon />
      </button>
      {open && (
        <div className={alignRight ? 'toolbar-dropdown__menu toolbar-dropdown__menu--text toolbar-dropdown__menu--right' : 'toolbar-dropdown__menu toolbar-dropdown__menu--text'}>
          <p className="toolbar-dropdown__heading">Style</p>
          {styles.map((o) => (
            <button
              key={o.key}
              type="button"
              className={o.isActive ? `toolbar-dropdown__item toolbar-dropdown__style--${o.key} is-active` : `toolbar-dropdown__item toolbar-dropdown__style--${o.key}`}
              onMouseDown={(e) => e.preventDefault()}
              onClick={() => { o.onSelect(); setOpen(false) }}
            >
              <span>{o.label}</span>
            </button>
          ))}
          <OverflowItems items={overflow} onDone={() => setOpen(false)} />
        </div>
      )}
    </div>
  )
}
