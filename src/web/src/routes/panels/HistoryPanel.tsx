import { useEffect, useState } from 'react'
import { api, ApiError, type VersionContent, type VersionMeta } from '../../api/client'
import { Avatar } from '../../components/Avatar'
import { useConfirm } from '../../components/ConfirmDialog'
import { Editor } from '../../editor/Editor'
import { reconcileDocument } from '../../editor/externalEdits'

export function HistoryPanel({
  pageId,
  currentVersion,
  onRestored,
}: {
  pageId: string
  currentVersion: number
  onRestored: () => void
}) {
  const [versions, setVersions] = useState<VersionMeta[] | null>(null)
  const [preview, setPreview] = useState<VersionContent | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const { ask, dialog } = useConfirm()
  // Two versions picked for comparing (dev-plan 15.3), and what they became.
  const [picked, setPicked] = useState<number[]>([])
  const [comparison, setComparison] = useState<{ older: number; newer: number; json: string } | null>(null)

  function togglePick(n: number) {
    setPicked((list) => list.includes(n) ? list.filter((x) => x !== n) : [...list, n].slice(-2))
  }

  async function compare() {
    if (picked.length !== 2) return
    const [older, newer] = [...picked].sort((a, b) => a - b)
    const [a, b] = await Promise.all([api.pages.version(pageId, older), api.pages.version(pageId, newer)])
    const who = versions?.find((v) => v.versionNumber === newer)?.authorName
    // The same block-level diff that shows outside edits (8.6): what the
    // newer one added is highlighted, what it removed is struck through.
    const merged = reconcileDocument(JSON.parse(a.contentJson), JSON.parse(b.contentJson),
      { source: 'version', actor: `version ${newer}${who ? ` by ${who}` : ''}`, at: b.createdAt })
    setPreview(null)
    setComparison({ older, newer, json: JSON.stringify(merged) })
  }

  useEffect(() => {
    setPreview(null)
    api.pages
      .versions(pageId)
      .then(setVersions)
      .catch((err: unknown) => setError(err instanceof Error ? err.message : 'Failed to load history.'))
  }, [pageId])

  async function showPreview(n: number) {
    setPreview(await api.pages.version(pageId, n))
  }

  async function restore(n: number) {
    // Not destructive, so the affirmative button is the ordinary one.
    const ok = await ask({
      title: `Restore version ${n}?`,
      confirmLabel: `Restore version ${n}`,
      body: <p>This adds a new version with that content on top. Nothing in the history is lost.</p>,
    })
    if (!ok) return
    setBusy(true)
    setError(null)
    try {
      await api.pages.restore(pageId, n)
      onRestored()
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Restore failed.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="history">
      {error && <p className="alert alert--error">{error}</p>}
      <p className="muted small">
        Tick two versions to compare them.{' '}
        {picked.length === 2 && (
          <button type="button" className="btn btn--sm" onClick={() => void compare()}>
            Compare v{Math.min(...picked)} and v{Math.max(...picked)}
          </button>
        )}
      </p>
      <ul className="version-list">
        {versions?.map((v) => (
          <li key={v.id} className="version">
            <input type="checkbox" className="version__pick" aria-label={`Compare version ${v.versionNumber}`}
              checked={picked.includes(v.versionNumber)} onChange={() => togglePick(v.versionNumber)} />
            <span className="version__num">v{v.versionNumber}</span>
            {v.versionNumber === currentVersion && <span className="badge">current</span>}
            <Avatar
              subject={{
                id: v.authorId,
                displayName: v.authorName,
                avatarHash: v.authorAvatarHash,
                avatarVariant: v.authorAvatarVariant,
              }}
              size={20}
            />
            <span className="version__author">{v.authorName}</span>
            <span className="muted small">{new Date(v.createdAt).toLocaleString()}</span>
            {v.changeComment && <span className="version__comment">“{v.changeComment}”</span>}
            <span className="version__actions">
              <button type="button" className="link-btn" onClick={() => showPreview(v.versionNumber)}>Preview</button>
              {v.versionNumber !== currentVersion && (
                <button type="button" className="link-btn" disabled={busy} onClick={() => restore(v.versionNumber)}>
                  Restore
                </button>
              )}
            </span>
          </li>
        ))}
      </ul>

      {preview && (
        <div className="version-preview paper">
          <div className="row-between">
            <h3>Preview: version {preview.versionNumber}</h3>
            <button type="button" className="link-btn" onClick={() => setPreview(null)}>Close</button>
          </div>
          <div className="page-body">
            <Editor value={preview.contentJson} editable={false} />
          </div>
        </div>
      )}

      {comparison && (
        <div className="version-preview paper">
          <div className="row-between">
            <h3>Version {comparison.older} compared with version {comparison.newer}</h3>
            <button type="button" className="link-btn" onClick={() => setComparison(null)}>Close</button>
          </div>
          <p className="muted small">Highlighted: added in version {comparison.newer}. Struck through: removed since version {comparison.older}. Paragraphs are compared whole.</p>
          <div className="page-body">
            <Editor value={comparison.json} editable={false} />
          </div>
        </div>
      )}

      {dialog}
    </div>
  )
}
