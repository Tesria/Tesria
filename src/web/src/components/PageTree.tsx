import { Fragment, useEffect, useMemo, useState } from 'react'
import { NavLink } from 'react-router-dom'
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
 *  null if it isn't in this tree — e.g. a trashed page, or the tree hasn't
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

type FlatNode = { id: string; title: string; parentId: string | null; depth: number }
type PendingMove = { pageId: string; parentPageId: string | null; index: number }

function flatten(nodes: PageTreeNode[], parentId: string | null = null, depth = 0): FlatNode[] {
  return nodes.flatMap((node) => [
    { id: node.id, title: node.title, parentId, depth },
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
 *  client-side counterpart of the backend's Move endpoint — used to keep a
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

// Horizontal drag distance (px) that shifts the projected depth by one level —
// matches the per-depth indent below so dragging "feels" like it maps 1:1.
const INDENT = 14

/** Where a dragged row would land: the id of the row it would sit right
 *  after (null = new first item), the depth that implies, and the parent
 *  that depth implies. Depth is projected from horizontal drag distance,
 *  then clamped between the previous row's depth+1 (can't skip a level) and
 *  the next row's depth (can't leave a gap) — the standard "sortable tree"
 *  projection technique. `items` must already have the dragged row (and its
 *  own former subtree) excluded — see caller. */
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

/** The recursive page list for a space — shared by the desktop sidebar and
 *  the mobile inline tree on the space landing page (SpaceHome). Reordering
 *  and reparenting only happen in "Reorder" mode (toggled via the button in
 *  the heading): outside it, rows are plain links with no drag listeners at
 *  all, so a scroll swipe that starts on a title can never be mistaken for a
 *  drag. Reorder mode is a batch edit, not one-drag-one-save: drags apply to
 *  a local draft tree (so you can reparent something and then immediately
 *  make the follow-up adjustments that reparent usually calls for, without
 *  re-entering the mode each time) and nothing reaches the server until
 *  Save. Cancel discards the draft — no request is ever sent for it. Rows
 *  aren't navigable while editing, since a stray click could otherwise
 *  discard an unsaved reorganization by navigating away from it. */
export function PageTree({
  tree,
  spaceKey,
  onNavigate,
  onMoved,
  readOnly = false,
}: {
  tree: PageTreeNode[]
  spaceKey: string
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

  // The draft only tracks the server's tree while not editing — once a
  // Reorder session starts, further prop updates (another tab, a page
  // created elsewhere) are ignored until Save or Cancel resolves the
  // session, rather than silently rebasing a half-finished reorganization.
  useEffect(() => {
    if (!editMode) setDraftTree(tree)
  }, [tree, editMode])

  const [activeId, setActiveId] = useState<string | null>(null)
  const [overId, setOverId] = useState<string | null>(null)
  const [dragOffsetX, setDragOffsetX] = useState(0)

  const flat = useMemo(() => flatten(editMode ? draftTree : tree), [editMode, draftTree, tree])

  // A row can't be dropped under itself or one of its own descendants — the
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
    // same tick, before React has re-rendered — reading state here would
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
      // server now holds — every intermediate state this produces is one
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
    return (
      <div className="tree-section">
        {heading}
        {error && <p className="alert alert--error">{error}</p>}
        <nav className="tree">
          {flat.map((node) => (
            <StaticRow key={node.id} node={node} spaceKey={spaceKey} onNavigate={onNavigate} />
          ))}
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

/** A plain navigation row — outside Reorder mode, this is all a tree row is:
 *  no drag listeners, no `touch-action` override, so scrolling through the
 *  tree behaves exactly like scrolling anything else. */
function StaticRow({
  node,
  spaceKey,
  onNavigate,
}: {
  node: FlatNode
  spaceKey: string
  onNavigate?: () => void
}) {
  return (
    <NavLink
      to={`/spaces/${spaceKey}/pages/${node.id}`}
      className={({ isActive }) => (isActive ? 'tree__link is-active' : 'tree__link')}
      style={{ paddingLeft: 8 + node.depth * INDENT }}
      onClick={onNavigate}
    >
      {node.title}
    </NavLink>
  )
}

/** A draggable row while Reorder mode is active. Not a link — mid-batch,
 *  navigating away would abandon whatever hasn't been saved yet, so rows
 *  are inert to click and only respond to drag. */
function DraggableRow({ node, isDimmed }: { node: FlatNode; isDimmed: boolean }) {
  // Only `listeners` (the pointer handlers) go on the row — not `attributes`
  // (mostly keyboard/ARIA metadata for dnd-kit's own sortable semantics),
  // which matters less here than when this was a real link, but there's
  // still no reason to relabel it as a generic draggable widget.
  const { listeners, setNodeRef, isDragging } = useSortable({ id: node.id })

  return (
    <div
      ref={setNodeRef}
      className="tree__link tree__link--draggable"
      style={{ paddingLeft: 8 + node.depth * INDENT, opacity: isDragging || isDimmed ? 0.4 : 1 }}
      title="Drag to reorder or move — Save or Cancel to browse again"
      {...listeners}
    >
      {node.title}
    </div>
  )
}

/** Same stroke-icon language as the editor toolbar (editor/icons.tsx) and
 *  the topbar bell (NotificationBell.tsx) — flat, currentColor, 1.8px
 *  stroke — instead of the platform's own emoji pencil, which rendered in
 *  full color and stood out against the rest of the app's flat icon set. */
function PencilIcon() {
  return (
    <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <path d="M16.5 3.5a2.12 2.12 0 0 1 3 3L7 19l-4 1 1-4Z" />
    </svg>
  )
}
