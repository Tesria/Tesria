import type { Editor as TiptapEditor } from '@tiptap/react'
import { controlOrigin, useHoveredTable } from './useHoveredTable'

/**
 * Hover-triggered row/column insert (+) and delete (×) controls rendered
 * directly on the table itself, matching Confluence's table editing
 * pattern, instead of a persistent toolbar strip. Column-border dragging
 * (resize) is unrelated built-in prosemirror-tables behavior, unaffected
 * by this component.
 */
export function TableControls({ editor }: { editor: TiptapEditor }) {
  const hovered = useHoveredTable(editor)
  if (!hovered) return null
  const { table, found, map, tableRect } = hovered

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

  // Everything below is in the editor wrapper's coordinates (see controlOrigin).
  const o = controlOrigin(editor)
  const firstRow = table.rows[0]
  const colRects = firstRow ? Array.from(firstRow.cells).map((c) => c.getBoundingClientRect()) : []
  const rowRects = Array.from(table.rows).map((r) => r.getBoundingClientRect())
  // n+1 insertion points: before each column/row, plus one after the last.
  const colBoundaries = [...colRects.map((r) => r.left), tableRect.right]
  const rowBoundaries = [...rowRects.map((r) => r.top), tableRect.bottom]

  const stop = (e: React.MouseEvent) => e.preventDefault()

  // The row controls normally sit in two strips left of the table (grips at
  // -16px, add buttons at -30px). A phone's page gutter is ~24px, so the add
  // buttons went off the left edge; there they move onto the grip strip,
  // centered on each row boundary, above the grips.
  const compactRows = tableRect.left < 34
  const rowAddLeft = compactRows ? tableRect.left - 17 : tableRect.left - 30

  return (
    <div className="table-hover">
      {colBoundaries.map((x, i) => (
        <button
          key={`col-add-${i}`}
          type="button"
          className="table-hover__add table-hover__add--col"
          style={{ left: x - 8 - o.x, top: tableRect.top - 30 - o.y }}
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
          style={{ left: r.left - o.x, top: tableRect.top - 16 - o.y, width: r.width }}
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
          className={compactRows ? 'table-hover__add table-hover__add--row is-compact' : 'table-hover__add table-hover__add--row'}
          style={{ top: y - 8 - o.y, left: rowAddLeft - o.x }}
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
          style={{ top: r.top - o.y, left: tableRect.left - 16 - o.x, height: r.height }}
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
