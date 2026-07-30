import { useRef } from 'react'
import type { Editor as TiptapEditor } from '@tiptap/react'
import { useHoveredTable } from './useHoveredTable'

// Matches real Confluence's own column-drag cap.
const MAX_WIDTH = 1800
const MIN_WIDTH = 120

/**
 * Table-level width chrome: a full-width toggle and a drag handle on the
 * table's own right edge — distinct from prosemirror-tables' built-in
 * per-column border dragging (unaffected, still works exactly as before).
 * Same floating-overlay convention as TableControls, not a NodeView — see
 * extensions.ts's Table extension for where `width`/`layout` actually live.
 */
export function TableWidthControls({ editor }: { editor: TiptapEditor }) {
  const hovered = useHoveredTable(editor)
  // Persists across renders without needing its own re-render on every
  // mousemove tick during a drag — only the final commit needs to touch
  // React/ProseMirror state.
  const dragRef = useRef<{ startX: number; startWidth: number } | null>(null)

  if (!hovered) return null
  const { table, found, tableRect } = hovered
  const layout = (found.node.attrs as { layout?: string }).layout ?? 'default'

  function selectThisTable() {
    return editor.chain().focus().setNodeSelection(found!.pos)
  }
  // prosemirror-tables' TableView NodeView only re-applies HTMLAttributes
  // (the style/data-layout output of extensions.ts's renderHTML) once, in
  // its constructor — its own `update(node)` (used for every subsequent
  // attribute change on an already-mounted table, i.e. every edit after the
  // first) only recalculates the colgroup and never re-touches style or
  // data-* attributes at all. So the schema alone isn't enough to keep the
  // DOM in sync live; apply the same effect directly, right after the PM
  // transaction commits. (Fresh mounts — a page load, an export — are
  // unaffected and already correct via the schema alone.)
  function applyDomEffects(px: number | null, nextLayout: string) {
    if (nextLayout === 'full-width') {
      table.setAttribute('data-layout', 'full-width')
      table.style.setProperty('--table-target-width', '100%')
    } else {
      table.removeAttribute('data-layout')
      if (px) table.style.setProperty('--table-target-width', `${px}px`)
      else table.style.removeProperty('--table-target-width')
    }
  }
  function commitWidth(px: number) {
    const rounded = Math.round(px)
    selectThisTable().updateAttributes('table', { width: rounded, layout: 'default' }).run()
    applyDomEffects(rounded, 'default')
  }
  function toggleFullWidth() {
    const next = layout === 'full-width' ? 'default' : 'full-width'
    selectThisTable().updateAttributes('table', { layout: next }).run()
    applyDomEffects(null, next)
  }

  function onEdgeMouseDown(e: React.MouseEvent) {
    e.preventDefault()
    dragRef.current = { startX: e.clientX, startWidth: table.getBoundingClientRect().width }
    function onMove(ev: MouseEvent) {
      if (!dragRef.current) return
      const next = clamp(dragRef.current.startWidth + (ev.clientX - dragRef.current.startX), MIN_WIDTH, MAX_WIDTH)
      // Live visual feedback only — no PM transaction per pixel, matching
      // how prosemirror-tables' own column-resize handle behaves. The
      // !important CSS rule (index.css) picks this custom property up
      // immediately; committed for real on mouseup.
      table.style.setProperty('--table-target-width', `${next}px`)
    }
    function onUp(ev: MouseEvent) {
      window.removeEventListener('mousemove', onMove)
      window.removeEventListener('mouseup', onUp)
      if (!dragRef.current) return
      const next = clamp(dragRef.current.startWidth + (ev.clientX - dragRef.current.startX), MIN_WIDTH, MAX_WIDTH)
      dragRef.current = null
      commitWidth(next)
    }
    window.addEventListener('mousemove', onMove)
    window.addEventListener('mouseup', onUp)
  }

  const stop = (e: React.MouseEvent) => e.preventDefault()

  return (
    <div className="table-hover">
      <button
        type="button"
        className={layout === 'full-width' ? 'table-hover__fullwidth is-active' : 'table-hover__fullwidth'}
        style={{ left: tableRect.right - 34, top: tableRect.top - 30 }}
        onMouseDown={stop}
        onClick={toggleFullWidth}
        title={layout === 'full-width' ? 'Switch to normal width' : 'Switch to full width'}
      >
        ⤢
      </button>
      <div
        className="table-hover__edge"
        style={{ left: tableRect.right - 2, top: tableRect.top, height: tableRect.height }}
        onMouseDown={onEdgeMouseDown}
        title="Drag to resize table"
      />
    </div>
  )
}

function clamp(value: number, min: number, max: number) {
  return Math.min(max, Math.max(min, value))
}
