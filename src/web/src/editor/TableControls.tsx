import { useEffect, useState } from 'react'
import type { Editor as TiptapEditor } from '@tiptap/react'
import { findTable, TableMap } from '@tiptap/pm/tables'

// How far outside the table's own box the hover zone (and the add/grip
// strips) extend — has to cover both strips plus a little slack so moving
// the mouse from the table onto a button doesn't "leave" the hover zone.
const MARGIN = 34

/**
 * Hover-triggered row/column insert (+) and delete (×) controls rendered
 * directly on the table itself — matching Confluence's table editing
 * pattern — instead of a persistent toolbar strip. Column-border dragging
 * (resize) is unrelated built-in prosemirror-tables behaviour, unaffected
 * by this component.
 */
export function TableControls({ editor }: { editor: TiptapEditor }) {
  const [table, setTable] = useState<HTMLTableElement | null>(null)
  const [, tick] = useState(0)

  useEffect(() => {
    function withinHoverZone(e: MouseEvent, rect: DOMRect) {
      // Symmetric-ish slack: the add/grip strips sit above and to the left of
      // the table (up to MARGIN out), but the trailing "add" button for the
      // last row/column also pokes out past the right/bottom edge — give
      // that a little room too rather than a tight 6px that clips its hitbox.
      return (
        e.clientX >= rect.left - MARGIN && e.clientX <= rect.right + 14 &&
        e.clientY >= rect.top - MARGIN && e.clientY <= rect.bottom + 14
      )
    }
    function onMove(e: MouseEvent) {
      const tables = Array.from(editor.view.dom.querySelectorAll('table')) as HTMLTableElement[]
      const hit = tables.find((t) => withinHoverZone(e, t.getBoundingClientRect()))
      setTable((current) => (hit ?? null) === current ? current : (hit ?? null))
    }
    const rerender = () => tick((n) => n + 1)
    document.addEventListener('mousemove', onMove)
    window.addEventListener('scroll', rerender, true)
    window.addEventListener('resize', rerender)
    return () => {
      document.removeEventListener('mousemove', onMove)
      window.removeEventListener('scroll', rerender, true)
      window.removeEventListener('resize', rerender)
    }
  }, [editor])

  if (!table || !table.isConnected) return null
  const found = findTable(editor.state.doc.resolve(editor.view.posAtDOM(table, 0)))
  if (!found) return null
  const map = TableMap.get(found.node)
  const tableRect = table.getBoundingClientRect()

  function cellSelectionChain(row: number, col: number) {
    const cellStart = found!.pos + 1 + map.positionAt(row, col, found!.node)
    return editor.chain().focus().setTextSelection(cellStart + 1)
  }
  function insertColumn(index: number) {
    const col = Math.min(index, map.width - 1)
    const chain = cellSelectionChain(0, col)
    if (index >= map.width) chain.addColumnAfter().run()
    else chain.addColumnBefore().run()
  }
  function deleteColumn(index: number) {
    cellSelectionChain(0, index).deleteColumn().run()
  }
  function insertRow(index: number) {
    const row = Math.min(index, map.height - 1)
    const chain = cellSelectionChain(row, 0)
    if (index >= map.height) chain.addRowAfter().run()
    else chain.addRowBefore().run()
  }
  function deleteRow(index: number) {
    cellSelectionChain(index, 0).deleteRow().run()
  }

  const firstRow = table.rows[0]
  const colRects = firstRow ? Array.from(firstRow.cells).map((c) => c.getBoundingClientRect()) : []
  const rowRects = Array.from(table.rows).map((r) => r.getBoundingClientRect())
  // n+1 insertion points: before each column/row, plus one after the last.
  const colBoundaries = [...colRects.map((r) => r.left), tableRect.right]
  const rowBoundaries = [...rowRects.map((r) => r.top), tableRect.bottom]

  const stop = (e: React.MouseEvent) => e.preventDefault()

  return (
    <div className="table-hover">
      {colBoundaries.map((x, i) => (
        <button
          key={`col-add-${i}`}
          type="button"
          className="table-hover__add table-hover__add--col"
          style={{ left: x - 8, top: tableRect.top - 30 }}
          onMouseDown={stop}
          onClick={() => insertColumn(i)}
          title={i === colBoundaries.length - 1 ? 'Add column' : 'Insert column before'}
        >
          +
        </button>
      ))}
      {colRects.map((r, i) => (
        <button
          key={`col-del-${i}`}
          type="button"
          className="table-hover__grip table-hover__grip--col"
          style={{ left: r.left, top: tableRect.top - 16, width: r.width }}
          onMouseDown={stop}
          onClick={() => deleteColumn(i)}
          title="Delete column"
        >
          ×
        </button>
      ))}
      {rowBoundaries.map((y, i) => (
        <button
          key={`row-add-${i}`}
          type="button"
          className="table-hover__add table-hover__add--row"
          style={{ top: y - 8, left: tableRect.left - 30 }}
          onMouseDown={stop}
          onClick={() => insertRow(i)}
          title={i === rowBoundaries.length - 1 ? 'Add row' : 'Insert row above'}
        >
          +
        </button>
      ))}
      {rowRects.map((r, i) => (
        <button
          key={`row-del-${i}`}
          type="button"
          className="table-hover__grip table-hover__grip--row"
          style={{ top: r.top, left: tableRect.left - 16, height: r.height }}
          onMouseDown={stop}
          onClick={() => deleteRow(i)}
          title="Delete row"
        >
          ×
        </button>
      ))}
    </div>
  )
}
