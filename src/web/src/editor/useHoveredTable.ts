import { useEffect, useState } from 'react'
import type { Editor as TiptapEditor } from '@tiptap/react'
import { findTable, TableMap } from '@tiptap/pm/tables'

// How far outside the table's own box the hover zone (and the add/grip/edge
// strips) extend: has to cover all three strips plus a little slack so
// moving the mouse from the table onto a button doesn't "leave" the zone.
const MARGIN = 34

// An input-capability check, not a viewport-width one: a touchscreen laptop
// at desktop width has the same "there is no hover" problem a phone does.
function isCoarsePointer() {
  return window.matchMedia('(hover: none) and (pointer: coarse)').matches
}

function tableElementFromNodeDom(dom: Node | null): HTMLTableElement | null {
  if (dom instanceof HTMLTableElement) return dom
  if (dom instanceof HTMLElement) return dom.querySelector('table')
  return null
}

/**
 * Tracks which table (if any) the controls should show for, shared by
 * TableControls (row/column insert/delete) and TableWidthControls (edge-drag
 * resize + full-width toggle): both need the same "which table, and where
 * is it" answer, just render different chrome from it.
 *
 * Two entirely different reveal mechanisms, chosen once per mount by input
 * capability: mouse/trackpad hovers near a table; touch has no hover concept
 * at all, so a tap that places the cursor inside a table (already the normal
 * behavior: no special handling needed to make that happen) is what reveals
 * the controls instead, dismissed once the selection leaves the table.
 */
/**
 * The box the table controls are positioned in: the editor's own wrapper
 * (`.editor`, position: relative). Controls are placed in its coordinates,
 * not the viewport's, so they stay on the table however the page scrolls,
 * zooms or pans: on an iPhone, position: fixed drifted off the table
 * whenever Safari moved the visual viewport.
 */
export function controlOrigin(editor: TiptapEditor): { x: number; y: number } {
  const host = editor.view.dom.closest('.editor') as HTMLElement | null
  const r = host?.getBoundingClientRect()
  return { x: r?.left ?? 0, y: r?.top ?? 0 }
}

export function useHoveredTable(editor: TiptapEditor) {
  const [table, setTable] = useState<HTMLTableElement | null>(null)
  const [, tick] = useState(0)
  const touch = isCoarsePointer()

  useEffect(() => {
    const rerender = () => tick((n) => n + 1)

    if (touch) {
      function onSelectionChange() {
        const found = findTable(editor.state.selection.$from)
        const el = found ? tableElementFromNodeDom(editor.view.nodeDOM(found.pos)) : null
        setTable((current) => (el === current ? current : el))
      }
      onSelectionChange()
      editor.on('selectionUpdate', onSelectionChange)
      editor.on('transaction', onSelectionChange)
      window.addEventListener('scroll', rerender, true)
      window.addEventListener('resize', rerender)
      return () => {
        editor.off('selectionUpdate', onSelectionChange)
        editor.off('transaction', onSelectionChange)
        window.removeEventListener('scroll', rerender, true)
        window.removeEventListener('resize', rerender)
      }
    }

    function withinHoverZone(e: MouseEvent, rect: DOMRect) {
      return (
        e.clientX >= rect.left - MARGIN && e.clientX <= rect.right + 14 &&
        e.clientY >= rect.top - MARGIN && e.clientY <= rect.bottom + 14
      )
    }
    function onMove(e: MouseEvent) {
      // A floating editor menu (a block's settings, the selection bubble) can
      // sit over a table. Pointing at the menu is not pointing at the table:
      // counting it made the table's controls draw over the menu.
      if (e.target instanceof Element && e.target.closest('.floating-menu')) {
        setTable((current) => (current === null ? current : null))
        return
      }
      const tables = Array.from(editor.view.dom.querySelectorAll('table')) as HTMLTableElement[]
      const hit = tables.find((t) => withinHoverZone(e, t.getBoundingClientRect()))
      setTable((current) => (hit ?? null) === current ? current : (hit ?? null))
    }
    document.addEventListener('mousemove', onMove)
    window.addEventListener('scroll', rerender, true)
    window.addEventListener('resize', rerender)
    return () => {
      document.removeEventListener('mousemove', onMove)
      window.removeEventListener('scroll', rerender, true)
      window.removeEventListener('resize', rerender)
    }
  }, [editor, touch])

  if (!table || !table.isConnected) return null
  const found = findTable(editor.state.doc.resolve(editor.view.posAtDOM(table, 0)))
  if (!found) return null

  return { table, found, map: TableMap.get(found.node), tableRect: table.getBoundingClientRect() }
}
