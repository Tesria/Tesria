import { useCallback, useRef, useState, type KeyboardEvent, type PointerEvent } from 'react'

/**
 * The space sidebar's width, which the reader can drag (the owner,
 * 2026-09-23: a numbered tree takes room, and some trees are deep). A
 * per-device preference, like hiding the sidebar, so browser storage, which
 * can be missing or refuse: then the sidebar is simply the default width.
 */
export const SIDEBAR_DEFAULT = 260
const SIDEBAR_MIN = 200
const SIDEBAR_MAX = 560
const KEY = 'tesria-sidebar-width'
const STEP = 16

/** Never wider than half the window, so the page always keeps the larger share. */
function clamp(width: number): number {
  const max = Math.min(SIDEBAR_MAX, Math.round(window.innerWidth / 2))
  return Math.round(Math.max(SIDEBAR_MIN, Math.min(max, width)))
}

function read(): number {
  try {
    const stored = Number(localStorage.getItem(KEY))
    return stored ? clamp(stored) : SIDEBAR_DEFAULT
  } catch {
    return SIDEBAR_DEFAULT
  }
}

function write(width: number) {
  try {
    if (width === SIDEBAR_DEFAULT) localStorage.removeItem(KEY)
    else localStorage.setItem(KEY, String(width))
  } catch { /* the width lasts until the page is reloaded */ }
}

export function useSidebarWidth() {
  const [width, setWidth] = useState(read)
  const set = useCallback((next: number, save: boolean) => {
    const value = clamp(next)
    setWidth(value)
    if (save) write(value)
  }, [])
  return { width, set }
}

/**
 * The handle on the sidebar's right edge: drag it, or focus it and use the
 * arrow keys; double-click or Home puts the sidebar back to its usual width.
 */
export function SidebarResizer({ width, onResize }: { width: number; onResize: (width: number, save: boolean) => void }) {
  const drag = useRef<{ startX: number; startWidth: number } | null>(null)
  const [dragging, setDragging] = useState(false)

  function onPointerDown(e: PointerEvent<HTMLDivElement>) {
    if (e.button !== 0) return
    e.preventDefault()
    e.currentTarget.setPointerCapture(e.pointerId)
    drag.current = { startX: e.clientX, startWidth: width }
    setDragging(true)
  }

  function onPointerMove(e: PointerEvent<HTMLDivElement>) {
    if (!drag.current) return
    onResize(drag.current.startWidth + (e.clientX - drag.current.startX), false)
  }

  function onPointerUp(e: PointerEvent<HTMLDivElement>) {
    if (!drag.current) return
    onResize(drag.current.startWidth + (e.clientX - drag.current.startX), true)
    drag.current = null
    setDragging(false)
  }

  function onKeyDown(e: KeyboardEvent<HTMLDivElement>) {
    const change = e.key === 'ArrowRight' ? STEP : e.key === 'ArrowLeft' ? -STEP : 0
    if (change) { e.preventDefault(); onResize(width + change, true) }
    if (e.key === 'Home') { e.preventDefault(); onResize(SIDEBAR_DEFAULT, true) }
  }

  return (
    <div
      className={dragging ? 'sidebar__resize is-dragging' : 'sidebar__resize'}
      role="separator"
      aria-orientation="vertical"
      aria-label="Resize the sidebar"
      aria-valuemin={SIDEBAR_MIN}
      aria-valuemax={SIDEBAR_MAX}
      aria-valuenow={width}
      tabIndex={0}
      title="Drag to resize. Double-click to reset."
      onPointerDown={onPointerDown}
      onPointerMove={onPointerMove}
      onPointerUp={onPointerUp}
      onPointerCancel={onPointerUp}
      onDoubleClick={() => onResize(SIDEBAR_DEFAULT, true)}
      onKeyDown={onKeyDown}
    />
  )
}
