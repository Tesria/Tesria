import { useLayoutEffect, useRef, useState } from 'react'
import { matchPath, NavLink, useLocation } from 'react-router-dom'
import { useDismissable } from '../hooks/useDismissable'

/**
 * A tab bar that never scrolls sideways (the owner, 2026-09-28): the tabs
 * that fit are shown, and the rest go into a ••• button at the end that
 * opens them as a menu. Horizontal dots, not the vertical ⋮ used for a
 * thing's actions: these are more of the same row. The button shows as
 * current when the current tab is one of those in the menu.
 *
 * Every tab is measured in an invisible copy of the bar (.tabs__ruler), as
 * the top bar measures its own contents (Layout.tsx), so what fits is known
 * before anything is hidden.
 */
export type TabItem = {
  key: string
  label: string
  /** A link tab; its current state comes from the address. */
  to?: string
  end?: boolean
  /** A button tab. */
  active?: boolean
  onClick?: () => void
}

export function OverflowTabs({ items }: { items: TabItem[] }) {
  const nav = useRef<HTMLElement>(null)
  const ruler = useRef<HTMLDivElement>(null)
  const [fit, setFit] = useState(items.length)
  const [open, setOpen] = useState(false)
  const menu = useDismissable<HTMLDivElement>(open, () => setOpen(false))
  const location = useLocation()
  const labels = items.map((i) => i.label).join('|')

  useLayoutEffect(() => {
    const n = nav.current
    const r = ruler.current
    if (!n || !r) return
    const measure = () => {
      const cs = getComputedStyle(n)
      const room = n.clientWidth - (parseFloat(cs.paddingLeft) || 0) - (parseFloat(cs.paddingRight) || 0)
      const gap = parseFloat(cs.columnGap) || 0
      const widths = [...r.querySelectorAll<HTMLElement>('[data-tab]')].map((e) => e.getBoundingClientRect().width)
      const more = r.querySelector<HTMLElement>('[data-more]')?.getBoundingClientRect().width ?? 0
      const all = widths.reduce((sum, w, i) => sum + w + (i ? gap : 0), 0)
      if (all <= room + 0.5) { setFit(widths.length); return }
      let used = more
      let k = 0
      while (k < widths.length && used + gap + widths[k] <= room + 0.5) { used += gap + widths[k]; k++ }
      setFit(k)
    }
    measure()
    const ro = new ResizeObserver(measure)
    ro.observe(n)
    ro.observe(r)
    document.fonts?.ready.then(measure).catch(() => {})
    return () => ro.disconnect()
  }, [labels])

  const current = (t: TabItem) => (t.to ? matchPath({ path: t.to, end: t.end ?? false }, location.pathname) !== null : t.active === true)
  const shown = items.slice(0, fit)
  const hidden = items.slice(fit)
  const hiddenCurrent = hidden.some(current)

  const tab = (t: TabItem, inMenu: boolean) => {
    const cls = inMenu ? 'btn' : 'tab'
    if (t.to) {
      return (
        <NavLink key={t.key} to={t.to} end={t.end}
          className={({ isActive }) => (isActive ? `${cls} is-active` : cls)}
          onClick={() => setOpen(false)}>
          {t.label}
        </NavLink>
      )
    }
    return (
      <button key={t.key} type="button" className={t.active ? `${cls} is-active` : cls} aria-pressed={t.active}
        onClick={() => { setOpen(false); t.onClick?.() }}>
        {t.label}
      </button>
    )
  }

  return (
    <nav className="tabs tabs--fit" ref={nav}>
      {shown.map((t) => tab(t, false))}
      {hidden.length > 0 && (
        <div className="tabs__more" ref={menu}>
          <button type="button" className={hiddenCurrent ? 'tab tabs__more-btn is-active' : 'tab tabs__more-btn'}
            aria-label="More tabs" aria-expanded={open} aria-haspopup="menu" onClick={() => setOpen((v) => !v)}>
            <DotsIcon />
          </button>
          {open && <div className="overflow-menu__dropdown tabs__menu" role="menu">{hidden.map((t) => tab(t, true))}</div>}
        </div>
      )}
      {/* Clipped to nothing, so it cannot widen the page; its row inside still
          lays out at full width to be measured. */}
      <div className="tabs__ruler" aria-hidden="true">
        <div className="tabs__ruler-row" ref={ruler}>
          {items.map((t) => <span key={t.key} className="tab" data-tab="">{t.label}</span>)}
          <span className="tab tabs__more-btn" data-more=""><DotsIcon /></span>
        </div>
      </div>
    </nav>
  )
}

function DotsIcon() {
  return (
    <svg width="18" height="18" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true" focusable="false">
      <circle cx="5" cy="12" r="2" /><circle cx="12" cy="12" r="2" /><circle cx="19" cy="12" r="2" />
    </svg>
  )
}
