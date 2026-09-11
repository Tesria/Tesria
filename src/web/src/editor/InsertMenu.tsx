import { useState, type ReactNode } from 'react'
import type { Editor as TiptapEditor } from '@tiptap/react'
import { useDismissable } from '../hooks/useDismissable'
import { ChevronDownIcon, PlusIcon } from './icons'
import { SLASH_ITEMS, type SlashItem } from './slash/items'

export type OverflowAction = {
  key: string
  icon: ReactNode
  label: string
  isActive: boolean
  run: () => void
}

/**
 * The toolbar's "+" menu — the same idea as Confluence's: block elements
 * live here rather than as one button each, so the toolbar stays a single
 * row whatever gets added to the editor. It is a plain "+" sitting with the
 * other icons, not a labelled button pushed to the right edge — that is
 * where Confluence keeps it.
 *
 * Its contents come from the slash catalogue (`SLASH_ITEMS`), not a list of
 * their own: a block added there appears here without a second edit, and
 * the two can never disagree about what can be inserted. Formatting buttons
 * squeezed off a narrow toolbar are appended under "More", so nothing is
 * ever out of reach — only further away.
 */
export function InsertMenu({ editor, overflow }: { editor: TiptapEditor; overflow: OverflowAction[] }) {
  const [open, setOpen] = useState(false)
  const ref = useDismissable<HTMLDivElement>(open, () => setOpen(false))

  // Slash items delete the "/query" range before inserting; here the range
  // is the current selection, so an empty selection deletes nothing and a
  // real one is replaced — the same thing typing would do.
  function insert(item: SlashItem) {
    const { from, to } = editor.state.selection
    item.command(editor, { from, to })
    setOpen(false)
  }

  const blocks = SLASH_ITEMS.filter((i) => i.group === 'block')
  const panels = SLASH_ITEMS.filter((i) => i.group === 'panel')
  const dynamic = SLASH_ITEMS.filter((i) => i.group === 'dynamic')

  return (
    <div className="toolbar-dropdown toolbar-dropdown--insert" ref={ref} data-tb-fixed="insert">
      <button
        type="button"
        className="toolbar__btn toolbar-dropdown__trigger toolbar__insert"
        onMouseDown={(e) => e.preventDefault()}
        onClick={() => setOpen((v) => !v)}
        title="Insert an element"
        aria-haspopup="true"
        aria-expanded={open}
      >
        <PlusIcon />
        <ChevronDownIcon />
      </button>
      {open && (
        <div className="toolbar-dropdown__menu toolbar-dropdown__menu--insert">
          {overflow.length > 0 && (
            <>
              <p className="toolbar-dropdown__heading">Formatting</p>
              {overflow.map((a) => (
                <button key={a.key} type="button"
                  className={a.isActive ? 'toolbar-dropdown__item is-active' : 'toolbar-dropdown__item'}
                  onMouseDown={(e) => e.preventDefault()}
                  onClick={() => { a.run(); setOpen(false) }}>
                  {a.icon}<span>{a.label}</span>
                </button>
              ))}
            </>
          )}
          <p className="toolbar-dropdown__heading">Insert</p>
          {blocks.map((item) => (
            <button key={item.title} type="button" className="toolbar-dropdown__item"
              onMouseDown={(e) => e.preventDefault()} onClick={() => insert(item)} title={item.description}>
              <item.icon /><span>{item.title}</span>
            </button>
          ))}
          <p className="toolbar-dropdown__heading">Panels</p>
          {panels.map((item) => (
            <button key={item.title} type="button" className="toolbar-dropdown__item"
              onMouseDown={(e) => e.preventDefault()} onClick={() => insert(item)} title={item.description}>
              <item.icon /><span>{item.title}</span>
            </button>
          ))}
          <p className="toolbar-dropdown__heading">Live content</p>
          {dynamic.map((item) => (
            <button key={item.title} type="button" className="toolbar-dropdown__item"
              onMouseDown={(e) => e.preventDefault()} onClick={() => insert(item)} title={item.description}>
              <item.icon /><span>{item.title}</span>
            </button>
          ))}
        </div>
      )}
    </div>
  )
}
