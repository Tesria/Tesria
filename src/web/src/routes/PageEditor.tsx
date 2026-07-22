import { type FormEvent, useEffect, useState } from 'react'
import { useNavigate, useParams, useSearchParams } from 'react-router-dom'
import { api, ApiError } from '../api/client'
import { Editor } from '../editor/Editor'
import { useSpaceContext } from './SpacePage'

const EMPTY_DOC = '{"type":"doc","content":[]}'

export function PageEditor() {
  const { key = '', pageId } = useParams()
  const [searchParams] = useSearchParams()
  const parentPageId = searchParams.get('parent')
  const navigate = useNavigate()
  const { space, reloadTree } = useSpaceContext()
  const isEdit = Boolean(pageId)

  const [title, setTitle] = useState('')
  const [content, setContent] = useState(EMPTY_DOC)
  const [changeComment, setChangeComment] = useState('')
  const [loading, setLoading] = useState(isEdit)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (!pageId) return
    let cancelled = false
    setLoading(true)
    api.pages
      .get(pageId)
      .then((p) => {
        if (cancelled) return
        setTitle(p.title)
        setContent(p.contentJson)
      })
      .catch((err: unknown) => !cancelled && setError(err instanceof Error ? err.message : 'Failed to load page.'))
      .finally(() => !cancelled && setLoading(false))
    return () => {
      cancelled = true
    }
  }, [pageId])

  async function onSubmit(e: FormEvent) {
    e.preventDefault()
    setBusy(true)
    setError(null)
    try {
      const saved = pageId
        ? await api.pages.update(pageId, { title, contentJson: content, changeComment: changeComment || null })
        : await api.pages.create({ spaceId: space.id, parentPageId, title, contentJson: content })
      reloadTree()
      navigate(`/spaces/${key}/pages/${saved.id}`)
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not save the page.')
    } finally {
      setBusy(false)
    }
  }

  if (loading) return <p className="muted page-wrap">Loading…</p>

  return (
    <form className="page-wrap editor-form" onSubmit={onSubmit}>
      {error && <p className="alert alert--error">{error}</p>}
      <input
        className="title-input"
        value={title}
        onChange={(e) => setTitle(e.target.value)}
        placeholder="Page title"
        required
        autoFocus={!isEdit}
      />
      <Editor key={pageId ?? 'new'} value={content} editable onChange={setContent} />
      {isEdit && (
        <label className="change-comment">
          What changed? (optional)
          <input value={changeComment} onChange={(e) => setChangeComment(e.target.value)} placeholder="e.g. fixed typo" />
        </label>
      )}
      <div className="editor-actions">
        <button type="submit" className="btn btn--primary" disabled={busy}>
          {busy ? 'Saving…' : isEdit ? 'Save changes' : 'Create page'}
        </button>
        <button
          type="button"
          className="btn btn--ghost"
          onClick={() => navigate(pageId ? `/spaces/${key}/pages/${pageId}` : `/spaces/${key}`)}
        >
          Cancel
        </button>
      </div>
    </form>
  )
}
