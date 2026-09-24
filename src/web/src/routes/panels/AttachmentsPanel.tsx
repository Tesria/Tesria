import { type ChangeEvent, useEffect, useRef, useState } from 'react'
import { api, ApiError, type Attachment } from '../../api/client'
import { useConfirm } from '../../components/ConfirmDialog'

function formatSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`
}

export function AttachmentsPanel({ pageId }: { pageId: string }) {
  const [items, setItems] = useState<Attachment[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [uploading, setUploading] = useState(false)
  const fileInput = useRef<HTMLInputElement>(null)
  const { ask, dialog } = useConfirm()

  function reload() {
    api.attachments
      .listForPage(pageId)
      .then(setItems)
      .catch((err: unknown) => setError(err instanceof Error ? err.message : 'Failed to load attachments.'))
  }

  useEffect(() => {
    setItems(null)
    reload()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [pageId])

  async function onUpload(e: ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0]
    if (!file) return
    setUploading(true)
    setError(null)
    try {
      await api.attachments.upload(pageId, file)
      reload()
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Upload failed.')
    } finally {
      setUploading(false)
      if (fileInput.current) fileInput.current.value = ''
    }
  }

  async function remove(id: string) {
    const ok = await ask({
      title: 'Delete this attachment?',
      danger: true,
      confirmLabel: 'Delete the attachment',
      body: <p>The file goes with it. Anywhere it is embedded in this page stops rendering.</p>,
    })
    if (!ok) return
    // A refusal used to vanish without a word (found 2026-09-23).
    setError(null)
    try {
      await api.attachments.remove(id)
      reload()
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not delete the attachment.')
    }
  }

  return (
    <div className="attachments">
      {error && <p className="alert alert--error">{error}</p>}
      <label className="btn btn--ghost btn--sm upload-btn">
        {uploading ? 'Uploading…' : 'Upload file'}
        <input ref={fileInput} type="file" hidden onChange={onUpload} disabled={uploading} />
      </label>
      {items && items.length === 0 && <p className="muted small">No attachments.</p>}
      <ul className="attachment-list">
        {items?.map((a) => (
          <li key={a.id} className="attachment">
            <a href={api.attachments.downloadUrl(a.id)} target="_blank" rel="noreferrer">
              {a.filename}
            </a>
            <span className="muted small">{formatSize(a.size)}</span>
            <button type="button" className="link-btn link-btn--danger" onClick={() => remove(a.id)}>
              Delete
            </button>
          </li>
        ))}
      </ul>

      {dialog}
    </div>
  )
}
