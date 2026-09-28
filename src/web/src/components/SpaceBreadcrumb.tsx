import { Fragment, useLayoutEffect, useRef, useState } from 'react'
import { Link, useMatch, useSearchParams } from 'react-router-dom'
import type { PageTreeNode, Space } from '../api/client'
import { findTreePath } from './PageTree'
import { SpaceIcon } from './SpaceIcon'
import { useDismissable } from '../hooks/useDismissable'

type Crumb = { label: string; to?: string }

/** The settings tabs, by their path segment. Keep in step with SpaceSettingsLayout. */
const SETTINGS_TAB_LABELS: Record<string, string> = {
  permissions: 'Permissions',
  templates: 'Templates',
  webhooks: 'Webhooks',
  trash: 'Trash',
}

/** Space Home / Page / Trash / etc. context, one level below the space
 *  action bar, not sticky, just the first thing in the scrolling content,
 *  same place a page's h1 used to be the only wayfinding on offer. */
export function SpaceBreadcrumb({ space, tree }: { space: Space; tree: PageTreeNode[] }) {
  const [searchParams] = useSearchParams()
  const matchPageView = useMatch('/spaces/:key/pages/:pageId')
  const matchPageEdit = useMatch('/spaces/:key/pages/:pageId/edit')
  const matchNew = useMatch('/spaces/:key/new')
  // One match for the whole settings section, not one per old URL: the three
  // that used to be siblings are tabs of it now, and matching them
  // individually is what left Permissions/Webhooks/Trash with no breadcrumb
  // at all while Details had one, so the content jumped a line every time
  // you changed tab.
  const matchSettings = useMatch('/spaces/:key/settings/*')

  const pageId = matchPageView?.params.pageId ?? matchPageEdit?.params.pageId
  const crumbs: Crumb[] = [{ label: space.name, to: `/spaces/${space.key}` }]

  if (pageId) {
    const path = findTreePath(tree, pageId)
    path?.forEach((node, i) => {
      const isLast = i === path.length - 1
      crumbs.push({ label: node.title, to: isLast ? undefined : `/spaces/${space.key}/pages/${node.id}` })
    })
  } else if (matchNew) {
    const parentId = searchParams.get('parent')
    const parentPath = parentId ? findTreePath(tree, parentId) : null
    parentPath?.forEach((node) => {
      crumbs.push({ label: node.title, to: `/spaces/${space.key}/pages/${node.id}` })
    })
    crumbs.push({ label: 'New Page' })
  } else if (matchSettings) {
    const tab = matchSettings.params['*'] ?? ''
    crumbs.push(tab ? { label: 'Space Settings', to: `/spaces/${space.key}/settings` } : { label: 'Space Settings' })
    if (tab) crumbs.push({ label: SETTINGS_TAB_LABELS[tab] ?? tab })
  } else {
    // Space landing: the h1 there already says where we are.
    return null
  }

  return <Crumbs crumbs={crumbs} space={space} />
}

/**
 * Viewing a page on a computer, the breadcrumb docks at the top across from
 * the page's buttons (index.css, glass.css), but only when it fits there on
 * one line; otherwise it stays inline at the top of the page (the owner,
 * 2026-09-28). A deep trail is shortened only when it has to be: to the
 * space, "...", the parent and the page, where the ... opens the levels in
 * between. In order, the first that fits wins: the whole trail docked, the
 * short one docked, the whole trail inline, and the short one inline when
 * the whole one would run past two lines.
 *
 * `data-dock` and `data-collapse` carry the answer. Both versions of the
 * trail are in the page (CSS shows one), so each is tried by setting the
 * attributes and measuring, all before anything paints.
 */
function Crumbs({ crumbs, space }: { crumbs: Crumb[]; space: Space }) {
  const navRef = useRef<HTMLElement>(null)
  const labels = crumbs.map((c) => c.label).join('/')
  const collapsible = crumbs.length >= 4
  useLayoutEffect(() => {
    const nav = navRef.current
    if (!nav) return
    const column = nav.parentElement
    const content = nav.closest('.space-content')
    const inPage = !!column?.classList.contains('page-column') && !!content
      // The editor keeps its breadcrumb inside its pane.
      && !content.querySelector(':scope > .page-actionbar--editor')
    const buttons = inPage ? content!.querySelector(':scope > .page-actionbar > .page-actionbar__secondary') : null
    const set = (dock: string | null, collapse: boolean) => {
      if (dock) nav.dataset.dock = dock
      else delete nav.dataset.dock
      if (collapse) nav.dataset.collapse = ''
      else delete nav.dataset.collapse
    }
    const dockFits = () => {
      const room = buttons ? buttons.getBoundingClientRect().left : column!.getBoundingClientRect().right
      return nav.getBoundingClientRect().right <= room - 16
    }
    const inlineLines = () => {
      const tops = [...nav.querySelectorAll('.breadcrumb__segment')]
        .flatMap((el) => [...el.getClientRects()])
        .map((r) => r.top)
        .sort((x, y) => x - y)
      return tops.filter((t, i) => i === 0 || t - tops[i - 1] > 10).length
    }
    const check = () => {
      if (inPage) {
        set('measure', false)
        if (dockFits()) return set('on', false)
        if (collapsible) {
          set('measure', true)
          if (dockFits()) return set('on', true)
        }
      }
      set(null, false)
      if (collapsible && inlineLines() > 2) set(null, true)
    }
    check()
    let live = true
    void document.fonts?.ready.then(() => { if (live) check() })
    const observer = new ResizeObserver(check)
    if (column) observer.observe(column)
    if (buttons) observer.observe(buttons)
    return () => { live = false; observer.disconnect(); set(null, false) }
  }, [labels, collapsible])

  const hidden = collapsible ? crumbs.slice(1, -2) : []

  return (
    <nav ref={navRef} className="breadcrumb" aria-label="Breadcrumb">
      {crumbs.map((c, i) => (
        <Fragment key={i}>
          {collapsible && i === 1 && (
            <span className="breadcrumb__segment breadcrumb__segment--more">
              <span className="breadcrumb__sep">/</span>
              <BreadcrumbMore crumbs={hidden} />
            </span>
          )}
          <span className={collapsible && i >= 1 && i < crumbs.length - 2 ? 'breadcrumb__segment breadcrumb__segment--middle' : 'breadcrumb__segment'}>
            {i > 0 && <span className="breadcrumb__sep">/</span>}
            {/* The space's own crumb carries its icon: the one place the icon
                appears while reading a page, so a reader always knows where
                they are without looking at the sidebar. */}
            {i === 0 && <SpaceIcon space={space} size={16} />}
            {c.to ? <Link to={c.to}>{c.label}</Link> : <span className="breadcrumb__current">{c.label}</span>}
          </span>
        </Fragment>
      ))}
    </nav>
  )
}

/** The "..." in a long breadcrumb: a menu of the levels it stands for,
 *  outermost first, each indented under the one before. */
function BreadcrumbMore({ crumbs }: { crumbs: Crumb[] }) {
  const [open, setOpen] = useState(false)
  const ref = useDismissable<HTMLSpanElement>(open, () => setOpen(false))
  const levels = crumbs.length === 1 ? '1 more level' : `${crumbs.length} more levels`
  return (
    <span className="overflow-menu breadcrumb__more" ref={ref}>
      <button
        type="button"
        className="breadcrumb__more-btn"
        aria-expanded={open}
        aria-label={`Show ${levels}`}
        title={levels}
        onClick={() => setOpen((v) => !v)}
      >
        …
      </button>
      {open && (
        <div className="overflow-menu__dropdown breadcrumb__menu" onClick={(e) => { if ((e.target as HTMLElement).closest('a')) setOpen(false) }}>
          {crumbs.map((c, i) => (
            <Link key={c.to ?? i} to={c.to ?? '#'} style={{ paddingLeft: `${0.6 + Math.min(i, 5) * 0.75}rem` }}>{c.label}</Link>
          ))}
        </div>
      )}
    </span>
  )
}
