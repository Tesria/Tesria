import { useEffect, useState } from 'react'
import type { Editor as TiptapEditor } from '@tiptap/react'
import { findTable, selectedRect, TableMap } from '@tiptap/pm/tables'
import { useDismissable } from '../hooks/useDismissable'
import { ChevronDownIcon } from './icons'
import { ColorPalette } from './ColorPalette'
import { CELL_BACKGROUND_TIERS } from './palette'

/** Which cells a colour applies to. Confluence gets row/column by selecting
 *  them first; offering the scope explicitly means one click either way, and
 *  a drag-selected block of cells still works as "Cell". */
type Scope = 'cell' | 'row' | 'column'

const SCOPES: { key: Scope; label: string }[] = [
  { key: 'cell', label: 'Cell' },
  { key: 'row', label: 'Row' },
  { key: 'column', label: 'Column' },
]

/**
 * The <td>/<th> the cursor is currently inside, or null.
 *
 * domAtPos throws for a position it can't resolve to rendered DOM, which
 * happens transiently while a transaction is being applied — this runs on
 * every transaction, so it has to tolerate that rather than throw out of
 * render and take the editor down with it.
 */
function currentCellElement(editor: TiptapEditor): HTMLTableCellElement | null {
  try {
    const at = editor.view.domAtPos(editor.state.selection.from)
    let node: Node | null = at.node
    while (node && !(node instanceof HTMLTableCellElement)) node = node.parentNode
    return node instanceof HTMLTableCellElement ? node : null
  } catch {
    return null
  }
}

/**
 * prosemirror-tables' selectedRect throws unless the selection really is
 * inside a cell — a NodeSelection on the table itself passes findTable but
 * fails here. Same reasoning as above: never throw out of render.
 */
function cellRect(editor: TiptapEditor): ReturnType<typeof selectedRect> | null {
  if (!findTable(editor.state.selection.$from)) return null
  try {
    return selectedRect(editor.state)
  } catch {
    return null
  }
}

/**
 * Confluence's per-cell options control: a chevron in the top-right corner of
 * the cell holding the cursor, opening a menu whose one option today is
 * "Background colour".
 *
 * Cursor-driven, unlike TableControls/TableWidthControls (which are
 * hover-driven via useHoveredTable) — matching Confluence, where the cell
 * menu belongs to the cell you're actually editing, not whichever one the
 * mouse passed over. That difference is why this doesn't share that hook.
 */
export function TableCellMenu({ editor }: { editor: TiptapEditor }) {
  const [open, setOpen] = useState(false)
  const [scope, setScope] = useState<Scope>('cell')
  const [, tick] = useState(0)
  const ref = useDismissable<HTMLDivElement>(open, () => setOpen(false))

  useEffect(() => {
    const rerender = () => tick((n) => n + 1)
    // Closing on every selection move would fight the palette itself (applying
    // a colour moves the selection); the dismissable-outside-click handler
    // owns closing instead.
    editor.on('selectionUpdate', rerender)
    editor.on('transaction', rerender)
    window.addEventListener('scroll', rerender, true)
    window.addEventListener('resize', rerender)
    return () => {
      editor.off('selectionUpdate', rerender)
      editor.off('transaction', rerender)
      window.removeEventListener('scroll', rerender, true)
      window.removeEventListener('resize', rerender)
    }
  }, [editor])

  const cell = editor.isActive('table') ? currentCellElement(editor) : null
  if (!cell || !cell.isConnected) return null
  const rect = cell.getBoundingClientRect()

  /**
   * Writes the attribute across every cell in scope in one transaction, rather
   * than moving the user's selection to a CellSelection and calling
   * setCellAttribute — the cursor should stay exactly where it was after
   * colouring a whole row or column.
   */
  function applyBackground(color: string | null) {
    const { state, view } = editor
    const r = cellRect(editor)
    if (!r) return
    const bounds = {
      left: scope === 'row' ? 0 : r.left,
      right: scope === 'row' ? r.map.width : r.right,
      top: scope === 'column' ? 0 : r.top,
      bottom: scope === 'column' ? r.map.height : r.bottom,
    }
    const tr = state.tr
    for (const offset of r.map.cellsInRect(bounds)) {
      const pos = r.tableStart + offset
      const node = tr.doc.nodeAt(pos)
      if (node) tr.setNodeMarkup(pos, undefined, { ...node.attrs, backgroundColor: color })
    }
    if (tr.docChanged) view.dispatch(tr)
    setOpen(false)
  }

  // The anchor cell's own colour, so the palette can ring the active swatch.
  let current: string | null = null
  const found = findTable(editor.state.selection.$from)
  const anchorRect = cellRect(editor)
  if (found && anchorRect) {
    const map = TableMap.get(found.node)
    const at = anchorRect.tableStart + map.positionAt(anchorRect.top, anchorRect.left, found.node)
    current = (editor.state.doc.nodeAt(at)?.attrs.backgroundColor as string | undefined) ?? null
  }

  return (
    <div className="cell-menu" ref={ref}>
      <button
        type="button"
        className="cell-menu__trigger"
        style={{ left: rect.right - 20, top: rect.top + 3 }}
        onMouseDown={(e) => e.preventDefault()}
        onClick={() => setOpen((v) => !v)}
        title="Cell options"
        aria-label="Cell options"
      >
        <ChevronDownIcon />
      </button>
      {open && (
        <div className="cell-menu__panel" style={{ left: rect.right - 20, top: rect.bottom + 4 }}>
          <p className="cell-menu__heading">Background colour</p>
          <div className="cell-menu__scopes">
            {SCOPES.map((s) => (
              <button
                key={s.key}
                type="button"
                className={scope === s.key ? 'cell-menu__scope is-active' : 'cell-menu__scope'}
                onMouseDown={(e) => e.preventDefault()}
                onClick={() => setScope(s.key)}
              >
                {s.label}
              </button>
            ))}
          </div>
          <ColorPalette
            tiers={CELL_BACKGROUND_TIERS}
            current={current}
            onPick={(v) => applyBackground(v)}
            onClear={() => applyBackground(null)}
            clearLabel="No colour"
          />
        </div>
      )}
    </div>
  )
}
