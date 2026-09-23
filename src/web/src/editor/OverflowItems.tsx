import { useState, type ReactNode } from 'react'

/** A toolbar control that has left the row for a menu. */
export type OverflowAction = {
  key: string
  icon: ReactNode
  label: string
  isActive: boolean
  run: () => void
  /** Which heading it sits under in the menu. */
  group: 'format' | 'color' | 'paragraph'
  /** A control that is a palette rather than a click: tapping the item unfolds this in place. */
  panel?: (close: () => void) => ReactNode
}

const GROUPS: { key: OverflowAction['group']; label: string }[] = [
  { key: 'format', label: 'Format' },
  { key: 'color', label: 'Color' },
  { key: 'paragraph', label: 'Paragraph' },
]

/**
 * The overflowed text controls, grouped under headings, inside the text
 * menu. Color palettes unfold under their own item so a phone still has
 * every color; everything else is a tap.
 */
export function OverflowItems({ items, onDone }: { items: OverflowAction[]; onDone: () => void }) {
  const [openPanel, setOpenPanel] = useState<string | null>(null)
  if (items.length === 0) return null
  return (
    <>
      {GROUPS.map((g) => {
        const rows = items.filter((i) => i.group === g.key)
        if (rows.length === 0) return null
        return (
          <div key={g.key} className="toolbar-dropdown__group">
            <p className="toolbar-dropdown__heading">{g.label}</p>
            {rows.map((a) => (
              <div key={a.key}>
                <button
                  type="button"
                  className={a.isActive ? 'toolbar-dropdown__item is-active' : 'toolbar-dropdown__item'}
                  onMouseDown={(e) => e.preventDefault()}
                  onClick={() => {
                    if (a.panel) { setOpenPanel((k) => (k === a.key ? null : a.key)); return }
                    a.run()
                    onDone()
                  }}
                  aria-expanded={a.panel ? openPanel === a.key : undefined}
                >
                  {a.icon}<span>{a.label}</span>
                </button>
                {a.panel && openPanel === a.key && (
                  <div className="toolbar-dropdown__panel">
                    {a.panel(() => { setOpenPanel(null); onDone() })}
                  </div>
                )}
              </div>
            ))}
          </div>
        )
      })}
    </>
  )
}
