import { Fragment, useEffect, useMemo, useState } from 'react'
import { treeMarkers, type SpaceTreeStyle } from './treeMarkers'
import { matchingRows, splitMatch, visibleRows } from './treeFilter'
import { NavLink, useNavigate } from 'react-router-dom'
import {
  DndContext,
  PointerSensor,
  useSensor,
  useSensors,
  type DragEndEvent,
  type DragMoveEvent,
  type DragStartEvent,
} from '@dnd-kit/core'
import { SortableContext, arrayMove, useSortable } from '@dnd-kit/sortable'
import { api, type PageTreeNode } from '../api/client'
import { PagesIcon } from './NavIcons'

/** The chain of nodes from a root page down to (and including) `pageId`, or
 *  null if it isn't in this tree: e.g. a trashed page, or the tree hasn't
 *  loaded yet. Used to build the breadcrumb: the tree is the only place
 *  parent/child relationships live on the client, so this walks it rather
 *  than asking the server for an ancestor list. */
export function findTreePath(tree: PageTreeNode[], pageId: string): PageTreeNode[] | null {
  for (const node of tree) {
    if (node.id === pageId) return [node]
    const childPath = findTreePath(node.children, pageId)
    if (childPath) return [node, ...childPath]
  }
  return null
}

type FlatNode = { id: string; title: string; emoji?: string | null; marker?: string | null; parentId: string | null; depth: number }
type PendingMove = { pageId: string; parentPageId: string | null; index: number }

function flatten(nodes: PageTreeNode[], parentId: string | null = null, depth = 0): FlatNode[] {
  return nodes.flatMap((node) => [
    { id: node.id, title: node.title, emoji: node.emoji, parentId, depth },
    ...flatten(node.children, node.id, depth + 1),
  ])
}

function descendantIdsOf(nodes: PageTreeNode[], id: string): Set<string> {
  const collect = (list: PageTreeNode[]): string[] => list.flatMap((n) => [n.id, ...collect(n.children)])
  const find = (list: PageTreeNode[]): string[] | null => {
    for (const n of list) {
      if (n.id === id) return collect(n.children)
      const found = find(n.children)
      if (found) return found
    }
    return null
  }
  return new Set(find(nodes) ?? [])
}

/** Removes `id` (and its whole subtree, intact) from wherever it sits in
 *  the tree, then reinserts it under `parentId` at `index`. The pure,
 *  client-side counterpart of the backend's Move endpoint: used to keep a
 *  local draft tree correct across several drags in one Reorder session,
 *  without a round trip per drag. */
function applyMove(tree: PageTreeNode[], id: string, parentId: string | null, index: number): PageTreeNode[] {
  let removed: PageTreeNode | null = null
  function remove(list: PageTreeNode[]): PageTreeNode[] {
    const withoutMatch = list.filter((n) => {
      if (n.id === id) {
        removed = n
        return false
      }
      return true
    })
    if (removed) return withoutMatch
    return withoutMatch.map((n) => ({ ...n, children: remove(n.children) }))
  }
  function insert(list: PageTreeNode[], node: PageTreeNode): PageTreeNode[] {
    if (parentId === null) {
      const copy = [...list]
      copy.splice(index, 0, node)
      return copy
    }
    return list.map((n) =>
      n.id === parentId
        ? { ...n, children: [...n.children.slice(0, index), node, ...n.children.slice(index)] }
        : { ...n, children: insert(n.children, node) },
    )
  }
  const withoutNode = remove(tree)
  return removed ? insert(withoutNode, removed) : tree
}

// Horizontal drag distance (px) that shifts the projected depth by one level:
// matches the per-depth indent below so dragging "feels" like it maps 1:1.
const INDENT = 14

/** Where a dragged row would land: the id of the row it would sit right
 *  after (null = new first item), the depth that implies, and the parent
 *  that depth implies. Depth is projected from horizontal drag distance,
 *  then clamped between the previous row's depth+1 (can't skip a level) and
 *  the next row's depth (can't leave a gap): the standard "sortable tree"
 *  projection technique. `items` must already have the dragged row (and its
 *  own former subtree) excluded: see caller. */
function project(items: FlatNode[], activeId: string, overId: string, dragOffsetX: number) {
  const activeIndex = items.findIndex((i) => i.id === activeId)
  const overIndex = items.findIndex((i) => i.id === overId)
  if (activeIndex === -1 || overIndex === -1) return null
  const reordered = arrayMove(items, activeIndex, overIndex)
  const newIndex = reordered.findIndex((i) => i.id === activeId)
  const previous = reordered[newIndex - 1]
  const next = reordered[newIndex + 1]

  const desiredDepth = items[activeIndex].depth + Math.round(dragOffsetX / INDENT)
  const maxDepth = previous ? previous.depth + 1 : 0
  const minDepth = next ? next.depth : 0
  const depth = Math.min(Math.max(desiredDepth, minDepth), maxDepth)

  let parentId: string | null = null
  if (depth > 0 && previous) {
    parentId =
      depth === previous.depth
        ? previous.parentId
        : depth > previous.depth
          ? previous.id
          : (reordered
              .slice(0, newIndex)
              .reverse()
              .find((i) => i.depth === depth - 1)?.id ?? null)
  }
  return { reordered, parentId, depth, previousId: previous?.id ?? null }
}

/** The recursive page list for a space: shared by the desktop sidebar and
 *  the mobile inline tree on the space landing page (SpaceHome). Reordering
 *  and reparenting only happen in "Reorder" mode (toggled via the button in
 *  the heading): outside it, rows are plain links with no drag listeners at
 *  all, so a scroll swipe that starts on a title can never be mistaken for a
 *  drag. Reorder mode is a batch edit, not one-drag-one-save: drags apply to
 *  a local draft tree (so you can reparent something and then immediately
 *  make the follow-up adjustments that reparent usually calls for, without
 *  re-entering the mode each time) and nothing reaches the server until
 *  Save. Cancel discards the draft: no request is ever sent for it. Rows
 *  aren't navigable while editing, since a stray click could otherwise
 *  discard an unsaved reorganization by navigating away from it. */
export function PageTree({
  tree,
  spaceKey,
  onNavigate,
  onMoved,
  readOnly = false,
  treeStyle,
}: {
  tree: PageTreeNode[]
  spaceKey: string
  /** Plain, numbered or bulleted (dev-plan 15.8): the space's setting. */
  treeStyle?: SpaceTreeStyle
  onNavigate?: () => void
  onMoved?: () => void
  /** Anonymous readers (dev-plan 5.3): browse only, no reorder control. */
  readOnly?: boolean
}) {
  const [editMode, setEditMode] = useState(false)
  const [draftTree, setDraftTree] = useState(tree)
  const [pendingMoves, setPendingMoves] = useState<PendingMove[]>([])
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  // The draft only tracks the server's tree while not editing, once a
  // Reorder session starts, further prop updates (another tab, a page
  // created elsewhere) are ignored until Save or Cancel resolves the
  // session, rather than silently rebasing a half-finished reorganization.
  useEffect(() => {
    if (!editMode) setDraftTree(tree)
  }, [tree, editMode])

  const [activeId, setActiveId] = useState<string | null>(null)
  const [overId, setOverId] = useState<string | null>(null)
  const [dragOffsetX, setDragOffsetX] = useState(0)

  const flat = useMemo(() => {
    const nodes = flatten(editMode ? draftTree : tree)
    // Worked out as drawn, so the numbers follow a drag before it is saved.
    const markers = treeMarkers(nodes.map((n) => n.depth), treeStyle)
    return nodes.map((n, i) => ({ ...n, marker: markers[i] }))
  }, [editMode, draftTree, tree, treeStyle])
  // The filter stays while you move between pages (chosen over clearing
  // it, 2026-09-23), and for this browser tab, so a reload
  // or the phone menu opening again keeps it. One per space.
  const filterKey = `tesria-tree-filter:${spaceKey}`
  const [filter, setFilterState] = useState(() => readFilter(filterKey))
  const setFilter = (value: string) => { setFilterState(value); writeFilter(filterKey, value) }
  useEffect(() => { setFilterState(readFilter(filterKey)) }, [filterKey])
  const [withChildren, setWithChildren] = useState(readWithChildren)
  const navigate = useNavigate()

  // A row can't be dropped under itself or one of its own descendants: the
  // backend rejects that as a cycle regardless, but excluding the dragged
  // subtree from the working list up front means it's never even offered as
  // a drop target, and the projection math above never has to think about it.
  const hidden = activeId ? descendantIdsOf(draftTree, activeId) : null
  const visible = useMemo(() => (hidden ? flat.filter((i) => !hidden.has(i.id)) : flat), [flat, hidden])

  const projection = activeId && overId && activeId !== overId ? project(visible, activeId, overId, dragOffsetX) : null

  const sensors = useSensors(useSensor(PointerSensor, { activationConstraint: { distance: 4 } }))

  function resetDrag() {
    setActiveId(null)
    setOverId(null)
    setDragOffsetX(0)
  }

  function handleDragStart(event: DragStartEvent) {
    setActiveId(String(event.active.id))
  }

  function handleDragMove(event: DragMoveEvent) {
    setOverId(event.over ? String(event.over.id) : null)
    setDragOffsetX(event.delta.x)
  }

  function handleDragEnd(event: DragEndEvent) {
    const draggedId = String(event.active.id)
    // Computed straight from the event, not from `overId`/`dragOffsetX`
    // state: dnd-kit can fire drag-move and drag-end back to back in the
    // same tick, before React has re-rendered: reading state here would
    // risk resolving against a stale projection from an earlier move.
    const overIdNow = event.over ? String(event.over.id) : null
    const result = overIdNow && overIdNow !== draggedId ? project(visible, draggedId, overIdNow, event.delta.x) : null
    resetDrag()
    if (!result) return

    const newParentId = result.parentId
    const siblings = result.reordered.filter(
      (i) => (i.id === draggedId ? newParentId : i.parentId) === newParentId,
    )
    const index = siblings.findIndex((i) => i.id === draggedId)
    const before = flat.find((i) => i.id === draggedId)
    if (before && before.parentId === newParentId) {
      const currentSiblings = flat.filter((i) => i.parentId === newParentId)
      if (currentSiblings.findIndex((i) => i.id === draggedId) === index) return // dropped back in place
    }

    setDraftTree((prev) => applyMove(prev, draggedId, newParentId, index))
    setPendingMoves((prev) => [...prev, { pageId: draggedId, parentPageId: newParentId, index }])
  }

  function startEditing() {
    setPendingMoves([])
    setError(null)
    setEditMode(true)
  }

  function cancelEditing() {
    setDraftTree(tree)
    setPendingMoves([])
    setError(null)
    setEditMode(false)
  }

  async function save() {
    setSaving(true)
    setError(null)
    try {
      // Replayed in the order they were made, each against whatever the
      // server now holds: every intermediate state this produces is one
      // the draft itself already passed through (and validated a parent
      // choice against) while the user was dragging, so this converges to
      // the same tree without needing to diff draft-vs-original itself.
      for (const move of pendingMoves) {
        await api.pages.move(move.pageId, { parentPageId: move.parentPageId, index: move.index })
      }
      setEditMode(false)
      setPendingMoves([])
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Some changes could not be saved.')
      setEditMode(false)
      setPendingMoves([])
    } finally {
      setSaving(false)
      onMoved?.()
    }
  }

  const heading = (
    <div className="tree-section__heading">
      <span><PagesIcon /> Pages</span>
      {tree.length > 0 && (
        editMode ? (
          <span className="tree-section__actions">
            <button type="button" className="btn btn--ghost btn--sm" onClick={cancelEditing} disabled={saving}>
              Cancel
            </button>
            <button type="button" className="btn btn--primary btn--sm" onClick={save} disabled={saving || pendingMoves.length === 0}>
              {saving ? 'Saving…' : `Save${pendingMoves.length ? ` (${pendingMoves.length})` : ''}`}
            </button>
          </span>
        ) : readOnly ? null : (
          <button type="button" className="btn btn--ghost btn--sm tree-section__reorder" aria-label="Reorder pages" title="Reorder pages" onClick={startEditing}>
            <PencilIcon />
          </button>
        )
      )}
    </div>
  )

  if (tree.length === 0) {
    return (
      <div className="tree-section">
        {heading}
        <p className="muted small">No pages yet.</p>
      </div>
    )
  }

  if (!editMode) {
    const rows = flat.map((n) => ({ title: n.title, depth: n.depth, marker: n.marker }))
    const shown = visibleRows(rows, filter, withChildren)
    const matches = matchingRows(rows, filter)
    // Dimmed: the parents above a match, shown only to place it. Pages
    // shown because they are under a match are the point, so they are not.
    const parentsOnly = visibleRows(rows, filter, false)
    const firstMatch = flat.find((_, i) => matches.has(i))
    // Choosing a page keeps the filter, so the next result is a click away.
    const chosen = () => { onNavigate?.() }
    return (
      <div className="tree-section">
        {heading}
        {error && <p className="alert alert--error">{error}</p>}
        <div className="tree-filter">
          <input
            type="search"
            className="tree-filter__input"
            placeholder="Filter pages"
            aria-label="Filter pages"
            value={filter}
            onChange={(e) => setFilter(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === 'Escape') { e.preventDefault(); setFilter('') }
              if (e.key === 'Enter' && firstMatch) {
                e.preventDefault()
                navigate(`/spaces/${spaceKey}/pages/${firstMatch.id}`)
                chosen()
              }
            }}
          />
          <button
            type="button"
            className={withChildren ? 'tree-filter__children is-on' : 'tree-filter__children'}
            aria-pressed={withChildren}
            title={withChildren ? 'Showing the pages under each match' : 'Show the pages under each match'}
            aria-label="Show the pages under each match"
            onClick={() => { const next = !withChildren; setWithChildren(next); writeWithChildren(next) }}
          >
            <ChildrenIcon />
          </button>
        </div>
        <nav className="tree" aria-label="Pages">
          {flat.map((node, i) => (shown === null || shown.has(i)) && (
            <StaticRow
              key={node.id}
              node={node}
              spaceKey={spaceKey}
              onNavigate={chosen}
              query={filter}
              context={parentsOnly !== null && parentsOnly.has(i) && !matches.has(i)}
            />
          ))}
          {shown !== null && shown.size === 0 && <p className="muted small tree-filter__none">No pages match.</p>}
        </nav>
      </div>
    )
  }

  return (
    <div className="tree-section">
      {heading}
      <DndContext
        sensors={sensors}
        onDragStart={handleDragStart}
        onDragMove={handleDragMove}
        onDragEnd={handleDragEnd}
        onDragCancel={resetDrag}
      >
        <SortableContext items={visible.map((i) => i.id)}>
          <nav className="tree">
            {projection?.previousId === null && <DropLine depth={projection.depth} />}
            {visible.map((node) => (
              <Fragment key={node.id}>
                <DraggableRow node={node} isDimmed={node.id === activeId} />
                {projection?.previousId === node.id && <DropLine depth={projection.depth} />}
              </Fragment>
            ))}
          </nav>
        </SortableContext>
      </DndContext>
    </div>
  )
}

function DropLine({ depth }: { depth: number }) {
  return <div className="tree__drop-line" style={{ marginLeft: 8 + depth * INDENT }} />
}

/** A plain navigation row: outside Reorder mode, this is all a tree row is:
 *  no drag listeners, no `touch-action` override, so scrolling through the
 *  tree behaves exactly like scrolling anything else. */
function StaticRow({
  node,
  spaceKey,
  onNavigate,
  query = '',
  context = false,
}: {
  node: FlatNode
  spaceKey: string
  onNavigate?: () => void
  /** What the tree is filtered by, to highlight in the title. */
  query?: string
  /** Shown only because a page under it matched. */
  context?: boolean
}) {
  return (
    <NavLink
      to={`/spaces/${spaceKey}/pages/${node.id}`}
      className={({ isActive }) => ['tree__link', isActive && 'is-active', context && 'tree__link--context'].filter(Boolean).join(' ')}
      style={{ paddingLeft: 8 + node.depth * INDENT }}
      onClick={onNavigate}
    >
      <TreeLabel node={node} query={query} />
    </NavLink>
  )
}

/**
 * Whether the filter also shows the pages under each match (the owner,
 * 2026-09-23). On unless someone turned it off, which is remembered in
 * browser storage; storage that is missing or refuses leaves it on.
 */
const WITH_CHILDREN_KEY = 'tesria-tree-filter-children'
function readWithChildren(): boolean {
  try { return localStorage.getItem(WITH_CHILDREN_KEY) !== '0' } catch { return true }
}
function writeWithChildren(on: boolean) {
  try { if (on) localStorage.removeItem(WITH_CHILDREN_KEY); else localStorage.setItem(WITH_CHILDREN_KEY, '0') } catch { /* lasts until reload */ }
}

function readFilter(key: string): string {
  try { return sessionStorage.getItem(key) ?? '' } catch { return '' }
}
function writeFilter(key: string, value: string) {
  try { if (value) sessionStorage.setItem(key, value); else sessionStorage.removeItem(key) } catch { /* lasts until reload */ }
}

/** A parent with two pages under it: the "and its children" toggle. */
function ChildrenIcon() {
  return (
    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <path d="M5 4h9" /><path d="M8 4v12a2 2 0 0 0 2 2h1" /><path d="M8 10h3" /><path d="M14 10h5" /><path d="M14 18h5" />
    </svg>
  )
}

/** A page's title in the tree, after its emoji if it has one (dev-plan 15.7). */
function TreeLabel({ node, query = '' }: { node: FlatNode; query?: string }) {
  const parts = splitMatch(node.title, query)
  return (
    <>
      {node.marker && <span className="tree__marker" aria-hidden="true">{node.marker}</span>}
      {node.emoji && <span className="tree__emoji" aria-hidden="true">{node.emoji}</span>}
      {parts ? <span>{parts[0]}<mark className="tree__match">{parts[1]}</mark>{parts[2]}</span> : <span>{node.title}</span>}
    </>
  )
}

/** A draggable row while Reorder mode is active. Not a link: mid-batch,
 *  navigating away would abandon whatever hasn't been saved yet, so rows
 *  are inert to click and only respond to drag. */
function DraggableRow({ node, isDimmed }: { node: FlatNode; isDimmed: boolean }) {
  // Only `listeners` (the pointer handlers) go on the row, not `attributes`
  // (mostly keyboard/ARIA metadata for dnd-kit's own sortable semantics),
  // which matters less here than when this was a real link, but there's
  // still no reason to relabel it as a generic draggable widget.
  const { listeners, setNodeRef, isDragging } = useSortable({ id: node.id })

  return (
    <div
      ref={setNodeRef}
      className="tree__link tree__link--draggable"
      style={{ paddingLeft: 8 + node.depth * INDENT, opacity: isDragging || isDimmed ? 0.4 : 1 }}
      title="Drag to reorder or move, Save or Cancel to browse again"
      {...listeners}
    >
      <TreeLabel node={node} />
    </div>
  )
}

/** Same stroke-icon language as the editor toolbar (editor/icons.tsx) and
 *  the topbar bell (NotificationBell.tsx) (flat, currentColor, 1.8px
 *  stroke) instead of the platform's own emoji pencil, which rendered in
 *  full color and stood out against the rest of the app's flat icon set. */
function PencilIcon() {
  return (
    <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <path d="M16.5 3.5a2.12 2.12 0 0 1 3 3L7 19l-4 1 1-4Z" />
    </svg>
  )
}
