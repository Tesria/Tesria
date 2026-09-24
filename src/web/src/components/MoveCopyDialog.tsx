import { useEffect, useState } from 'react'
import { createPortal } from 'react-dom'
import { api, ApiError, type PageTreeNode, type Space } from '../api/client'

type Mode = 'move' | 'copy'

/** Every page in a tree, with its depth, minus one page and everything under it. */
function flatten(nodes: PageTreeNode[], skip: string | null, depth = 0): { id: string; title: string; depth: number }[] {
  const out: { id: string; title: string; depth: number }[] = []
  for (const n of nodes) {
    if (n.id === skip) continue
    out.push({ id: n.id, title: n.title, depth })
    out.push(...flatten(n.children, skip, depth + 1))
  }
  return out
}

/**
 * Move or copy a page, from its ⋮ menu (dev-plan 15.3): to a new place in
 * this space or another the person can see. The server decides whether they
 * may; this only offers the places.
 */
export function MoveCopyDialog({ mode, pageId, pageTitle, spaceId, onClose, onDone }: {
  mode: Mode
  pageId: string
  pageTitle: string
  spaceId: string
  onClose: () => void
  onDone: (result: { spaceKey: string; pageId: string }) => void
}) {
  const [spaces, setSpaces] = useState<Space[] | null>(null)
  const [targetSpace, setTargetSpace] = useState(spaceId)
  const [tree, setTree] = useState<PageTreeNode[] | null>(null)
  const [parent, setParent] = useState('')
  const [includeChildren, setIncludeChildren] = useState(true)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => { api.spaces.list().then(setSpaces).catch(() => setSpaces([])) }, [])
  useEffect(() => {
    setTree(null)
    setParent('')
    api.pages.tree(targetSpace).then(setTree).catch(() => setTree([]))
  }, [targetSpace])

  // Moving a page beneath itself is impossible, so it and its sub-pages are
  // not offered; a copy may go anywhere.
  const places = tree ? flatten(tree, mode === 'move' && targetSpace === spaceId ? pageId : null) : []

  async function go() {
    setBusy(true)
    setError(null)
    try {
      const key = spaces?.find((s) => s.id === targetSpace)?.key ?? ''
      if (mode === 'move') {
        await api.pages.move(pageId, { parentPageId: parent || null, index: 9999, spaceId: targetSpace })
        onDone({ spaceKey: key, pageId })
      } else {
        const made = await api.pages.copy(pageId, { spaceId: targetSpace, parentPageId: parent || null, includeChildren })
        onDone({ spaceKey: key, pageId: made.id })
      }
    } catch (err) {
      setError(err instanceof ApiError ? err.message : mode === 'move' ? 'Could not move the page.' : 'Could not copy the page.')
      setBusy(false)
    }
  }

  // Rendered at the top of the document: inside the page it sat under the
  // page bar's own stacking, and the ⋮ menu drew over it.
  return createPortal(
    <div className="recovery-prompt move-copy" role="dialog" aria-modal="true" aria-label={mode === 'move' ? `Move ${pageTitle}` : `Copy ${pageTitle}`}>
      <div className="recovery-prompt__card">
        <h2>{mode === 'move' ? `Move ${pageTitle}` : `Copy ${pageTitle}`}</h2>
        {error && <p className="alert alert--error">{error}</p>}
        <label>
          Space
          <select value={targetSpace} onChange={(e) => setTargetSpace(e.target.value)}>
            {(spaces ?? []).map((s) => <option key={s.id} value={s.id}>{s.name} ({s.key})</option>)}
          </select>
        </label>
        <label>
          Put it under
          <select value={parent} onChange={(e) => setParent(e.target.value)} disabled={!tree}>
            <option value="">The top of the space</option>
            {places.map((p) => (
              <option key={p.id} value={p.id}>{' '.repeat(p.depth)}{p.title}</option>
            ))}
          </select>
        </label>
        {mode === 'copy' ? (
          <label className="setup__check">
            <input type="checkbox" checked={includeChildren} onChange={(e) => setIncludeChildren(e.target.checked)} />
            <span>Copy the pages under it too</span>
          </label>
        ) : (
          <p className="muted small">The pages under it move with it.</p>
        )}
        {mode === 'copy' && (
          <p className="muted small">The copy gets the content, labels and attachments, not the history or comments.</p>
        )}
        <div className="row-gap">
          <button type="button" className="btn btn--primary" disabled={busy || !spaces} onClick={() => void go()}>
            {busy ? (mode === 'move' ? 'Moving…' : 'Copying…') : mode === 'move' ? 'Move' : 'Copy'}
          </button>
          <button type="button" className="btn btn--ghost" onClick={onClose}>Cancel</button>
        </div>
      </div>
    </div>,
    document.body,
  )
}
