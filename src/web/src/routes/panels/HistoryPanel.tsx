import { useEffect, useState } from 'react'
import { api, ApiError, type VersionContent, type VersionMeta } from '../../api/client'
import { Editor } from '../../editor/Editor'

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
    if (!confirm(`Restore version ${n}? This adds a new version with that content.`)) return
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
      <ul className="version-list">
        {versions?.map((v) => (
          <li key={v.id} className="version">
            <span className="version__num">v{v.versionNumber}</span>
            {v.versionNumber === currentVersion && <span className="badge">current</span>}
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
            <h3>Preview — version {preview.versionNumber}</h3>
            <button type="button" className="link-btn" onClick={() => setPreview(null)}>Close</button>
          </div>
          <div className="page-body">
            <Editor value={preview.contentJson} editable={false} />
          </div>
        </div>
      )}
    </div>
  )
}
